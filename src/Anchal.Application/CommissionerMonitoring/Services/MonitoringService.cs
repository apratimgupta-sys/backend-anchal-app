using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using static Anchal.Domain.CommissionerMonitoring.Rules.MonitoringRules;

namespace Anchal.Application.CommissionerMonitoring.Services;

/// <summary>
/// Office-scoped application operations. Count rows are projections of immutable entry snapshots.
/// Audit snapshots retain the Cloudflare metadata and mutation receipts absent from the count schema.
/// All writers must use MutateOfficeAsync so validation and projection updates are serialized.
/// </summary>
public sealed class MonitoringService(IMonitoringContext db, TimeProvider clock) : IMonitoringService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Versioned storage envelope, deliberately separate from the public history DTO.
    private sealed record AuditSnapshot(int SchemaVersion, string RequestId, string Fingerprint,
        string Actor, string? Reason, JsonElement Value);

    public Task<MonitoringResult<MonitoringRead<SessionDto>>> GetSessionAsync(ClaimsPrincipal principal, CancellationToken ct = default) =>
        Run(async () => ReadContent(await Authorize(principal, ct)));

    public Task<MonitoringResult<MonitoringRead<IReadOnlyList<ServiceDto>>>> GetServicesAsync(ClaimsPrincipal principal, CancellationToken ct = default) =>
        Run(async () =>
        {
            await Authorize(principal, ct);
            var catalog = await db.CatalogItems.AsNoTracking().Where(x => x.Kind == MonitoringActivityKind.Service && x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Key).ToListAsync(ct);
            return ReadContent<IReadOnlyList<ServiceDto>>(catalog.Select(x => new ServiceDto(int.Parse(x.Key), x.Name)).ToArray());
        });

    public Task<MonitoringResult<MonitoringRead<SetupDto?>>> GetSetupAsync(ClaimsPrincipal principal, CancellationToken ct = default) =>
        Run(async () =>
        {
            var session = await Authorize(principal, ct, "office");
            var setup = await LoadSetup(session.OfficeId!.Value, ct);
            return Read(ToDto(setup), setup?.LockedAt);
        });

    public Task<MonitoringResult<SetupDto>> SaveSetupAsync(ClaimsPrincipal principal, SetupCommand command, CancellationToken ct = default) =>
        Run(async () =>
        {
            var session = await Authorize(principal, ct, "office");
            var office = session.OfficeId!.Value;
            return await db.MutateOfficeAsync(office, async () =>
            {
                await Authorize(principal, ct, "office");
                ValidateRequest(command.RequestId);
                var fingerprint = Fingerprint(new { Kind = "setup", Command = command });
                var audits = await LoadAudits(office, ct);
                if (Replay<SetupDto>(audits, command.RequestId, fingerprint) is { } replay) return replay;
                if (await LoadSetup(office, ct) is not null)
                    Fail(409, "Opening balances have already been set and cannot be changed");
                var now = clock.GetUtcNow();
                if (command.StartDate == default || command.StartDate > Today(now))
                    Fail(422, "Enter a valid reporting start date through today");
                if (command.ExpectedVersion is not null) Fail(409, "Initial balances changed. Reload before correcting.", "CONFLICT");
                if (command.Balances is null || command.Balances.Count != 25 ||
                    command.Balances.Select(x => x.ServiceId).Distinct().Count() != 25 ||
                    command.Balances.Any(x => x.ServiceId is < 1 or > 25 || x.Opening is null ||
                        new[] { x.Opening.Service, x.Opening.Complaints, x.Opening.Field }.Any(v => v is < 0 or > 1_000_000_000)))
                    Fail(422, "Confirm nonnegative opening balances for all 25 services");
                if (await db.Reports.AnyAsync(x => x.OfficeId == office, ct))
                    Fail(409, "Reports already exist without opening setup");
                var setup = new MonitoringOfficeSetup { Id = Guid.NewGuid(), OfficeId = office,
                    LockedByUserId = session.UserId, EffectiveDate = command.StartDate, LockedAt = now };
                foreach (var balance in command.Balances!)
                    foreach (var (kind, count) in Counts(balance.Opening))
                        setup.OpeningBalances.Add(new MonitoringOpeningBalance { Id = Guid.NewGuid(), OfficeSetupId = setup.Id,
                            Kind = kind, ItemKey = balance.ServiceId.ToString(), Count = checked((int)count) });
                db.OfficeSetups.Add(setup);
                var result = ToDto(setup)!;
                AddAudit(session, "setup", setup.Id, "Initial balances configured", command.RequestId,
                    fingerprint, command.Reason, null, result, now);
                await db.SaveChangesAsync(ct);
                return result;
            }, ct);
        });

    public Task<MonitoringResult<EntryDto>> SaveEntryAsync(ClaimsPrincipal principal, SaveEntryCommand command,
        Guid? id = null, CancellationToken ct = default) => Run(async () =>
    {
        var session = await Authorize(principal, ct, "office");
        var office = session.OfficeId!.Value;
        return await db.MutateOfficeAsync(office, async () =>
        {
            await Authorize(principal, ct, "office");
            ValidateRequest(command.RequestId);
            if (command.Input is null) Fail(422, "Entry required");
            var now = clock.GetUtcNow();
            ValidateEntry(command.Input!, Today(now));
            var fingerprint = Fingerprint(new { Kind = "entry", Id = id, Command = command });
            var audits = await LoadAudits(office, ct);
            if (Replay<EntryDto>(audits, command.RequestId, fingerprint) is { } replay) return replay;
            var setup = ToDto(await LoadSetup(office, ct));
            if (setup is null) Fail(422, "Configure opening balances before reporting");
            ValidateRoute(command.Mode, command.Input!.ServiceDate, setup!.StartDate, Today(now));
            var entries = CurrentEntries(audits);
            var old = id.HasValue ? entries.SingleOrDefault(x => x.Id == id)
                : entries.SingleOrDefault(x => x.Input.ServiceId == command.Input.ServiceId && x.Input.ServiceDate == command.Input.ServiceDate);
            if (id.HasValue && old is null) Fail(404, "Entry not found");
            if (old is not null && (old.Input.ServiceId != command.Input.ServiceId || old.Input.ServiceDate != command.Input.ServiceDate))
                Fail(422, "Service/date identity cannot be changed by correction");
            if (old?.Version != command.ExpectedVersion) Fail(409, "Entry changed. Reload before correcting.", "CONFLICT");
            ValidateReason(old is not null, command.Mode, command.Reason);
            var input = command.Input with { Ward = command.Input.Ward.Trim(), Remarks = command.Input.Remarks.Trim(),
                AvgDisposalDays = command.Input.Completed > 0 ? command.Input.AvgDisposalDays : null };
            // Entry snapshots retain the balances from when they were written. A correction to an
            // earlier date can change a later entry's effective ledger position without mutating
            // that historical snapshot, so audit the current derived view rather than the stale
            // snapshot when recording a correction.
            var before = old is null ? null : Views(setup, entries).Single(x => x.Id == old.Id);
            var opening = setup.Balances.Single(x => x.ServiceId == input.ServiceId).Opening;
            var ledger = Derive(opening, entries.Where(x => x.Input.ServiceId == input.ServiceId && x.Id != old?.Id)
                .Select(x => x.Input).Append(input));
            var view = ledger.Single(x => x.Input.ServiceDate == input.ServiceDate);
            var result = new EntryDto(old?.Id ?? Guid.NewGuid(), office, (old?.Version ?? 0) + 1, input,
                old?.ReportingDate ?? Today(now), old?.CreatedAt ?? now, now, view.Opening, view.Closing);

            // No tracked mutation occurs until the complete historical ledger has passed validation.
            var reports = await db.Reports.Include(x => x.Entries).Where(x => x.OfficeId == office).ToListAsync(ct);
            var report = reports.SingleOrDefault(x => x.ReportingDate == input.ServiceDate);
            if (report is null)
            {
                report = new MonitoringReport { Id = Guid.NewGuid(), OfficeId = office, ReportingDate = input.ServiceDate,
                    SubmittedByUserId = session.UserId, SubmittedAt = now, UpdatedAt = now };
                db.Reports.Add(report);
                reports.Add(report);
            }
            foreach (var item in ledger)
            {
                var dated = reports.Single(x => x.ReportingDate == item.Input.ServiceDate);
                foreach (var (kind, count) in Counts(item.Closing))
                {
                    var key = input.ServiceId.ToString();
                    var row = dated.Entries.SingleOrDefault(x => x.Kind == kind && x.ItemKey == key);
                    if (row is null)
                    {
                        row = new MonitoringReportEntry { Id = kind == MonitoringActivityKind.Service ? result.Id : Guid.NewGuid(),
                            ReportId = dated.Id, Kind = kind, ItemKey = key };
                        dated.Entries.Add(row);
                        db.ReportEntries.Add(row);
                    }
                    (row.ReceivedCount, row.DisposedCount) = Flows(item.Input, kind);
                    row.ClosingBalance = checked((int)count);
                }
                if (dated.ReportingDate >= input.ServiceDate) dated.UpdatedAt = now;
            }
            report.IsZeroActivity = report.Entries.All(x => x.ReceivedCount == 0 && x.DisposedCount == 0) &&
                entries.Where(x => x.Input.ServiceDate == input.ServiceDate && x.Id != result.Id).All(x => x.Input.IsZeroActivity) && input.IsZeroActivity;
            if (old is not null)
                db.Corrections.Add(new MonitoringCorrection { Id = Guid.NewGuid(), ReportId = report.Id, EntryId = result.Id,
                    CorrectedByUserId = session.UserId, Reason = command.Reason!.Trim(),
                    BeforeJson = Serialize(before), AfterJson = Serialize(result), CorrectedAt = now });
            AddAudit(session, "entry", result.Id, old is null ? "Entry submitted" : "Entry corrected", command.RequestId,
                fingerprint, command.Reason, before, result, now);
            await db.SaveChangesAsync(ct);
            return result;
        }, ct);
    });

    public Task<MonitoringResult<MonitoringRead<EntriesDto>>> GetEntriesAsync(ClaimsPrincipal principal,
        string mode, DateOnly date, CancellationToken ct = default) => Run(async () =>
    {
        var session = await Authorize(principal, ct, "office");
        var setup = ToDto(await LoadSetup(session.OfficeId!.Value, ct));
        if (setup is null) Fail(422, "Configure opening balances before reporting");
        ValidateRoute(mode, date, setup!.StartDate, Today(clock.GetUtcNow()));
        var audits = await LoadAudits(session.OfficeId, ct);
        var entries = Views(setup, CurrentEntries(audits));
        var openings = setup.Balances.Select(b => new OpeningBalanceDto(b.ServiceId,
            entries.Where(x => x.Input.ServiceId == b.ServiceId && x.Input.ServiceDate < date)
                .OrderBy(x => x.Input.ServiceDate).LastOrDefault()?.Closing ?? b.Opening)).ToArray();
        return Read(new EntriesDto(setup, entries.Where(x => x.Input.ServiceDate == date).ToArray(), openings), LastUpdated(audits));
    });

    public Task<MonitoringResult<MonitoringRead<IReadOnlyList<AuditDto>>>> GetHistoryAsync(ClaimsPrincipal principal,
        CancellationToken ct = default) => Run(async () =>
    {
        var session = await Authorize(principal, ct);
        var audits = await LoadAudits(session.OfficeId, ct);
        var history = audits.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(200).Select(x =>
        {
            var snapshot = Snapshot(x);
            return new AuditDto(x.Id, x.OfficeId, snapshot.Actor, x.OccurredAt, x.Action, snapshot.Reason,
                x.BeforeJson == "null" ? null : x.BeforeJson, snapshot.Value.GetRawText());
        }).ToArray();
        return Read<IReadOnlyList<AuditDto>>(history, LastUpdated(audits));
    });

    public Task<MonitoringResult<MonitoringRead<DashboardDto>>> GetDashboardAsync(ClaimsPrincipal principal,
        PeriodFilter filter, CancellationToken ct = default) => Run(async () =>
    {
        await Authorize(principal, ct, "commissioner");
        await ValidateFilter(filter, ct);
        var offices = await ActiveOffices(filter)
            .OrderBy(x => x.Code).ToListAsync(ct);
        var audits = await LoadAudits(filter.OfficeId, ct);
        var all = CurrentEntries(audits);
        var rows = new List<MonitoringRowDto>();
        foreach (var office in offices)
        {
            var setup = ToDto(await LoadSetup(office.Id, ct));
            if (setup is null)
            {
                rows.Add(new(office.Id, office.Code, 0, 0, 0, 0, null, null, 0, 0, 0, 0, 0, false,
                    ["Initial balances not configured"]));
                continue;
            }
            var entries = Views(setup, all.Where(x => x.OfficeId == office.Id).ToArray());
            var period = Filter(entries, filter);
            var received = period.Sum(x => x.Input.Received);
            var completed = period.Sum(x => x.Input.Completed);
            var sla = period.Sum(x => x.Input.SlaWithinTime);
            long pending = 0, complaints = 0, field = 0;
            foreach (var balance in setup.Balances.Where(x => !filter.ServiceId.HasValue || x.ServiceId == filter.ServiceId))
            {
                var closing = filter.To < setup.StartDate ? new Balances(0, 0, 0)
                    : entries.Where(x => x.Input.ServiceId == balance.ServiceId && x.Input.ServiceDate <= filter.To)
                        .OrderBy(x => x.Input.ServiceDate).LastOrDefault()?.Closing ?? balance.Opening;
                pending += closing.Service; complaints += closing.Complaints; field += closing.Field;
            }
            double? disposalPct = received == 0 ? null : (double)completed / received * 100;
            double? slaPct = completed == 0 ? null : (double)sla / completed * 100;
            var start = filter.From > setup.StartDate ? filter.From : setup.StartDate;
            var expected = Math.Max(0L, (long)filter.To.DayNumber - start.DayNumber + 1) * (filter.ServiceId.HasValue ? 1 : 25);
            var reasons = new List<string>();
            if (pending > 10) reasons.Add("Pending > 10");
            if (disposalPct < 90) reasons.Add("Disposal < 90%");
            if (slaPct < 90) reasons.Add("SLA < 90%");
            if (complaints > 5) reasons.Add("Complaints pending > 5");
            if (field > 10) reasons.Add("Field tasks pending > 10");
            rows.Add(new(office.Id, office.Code, received, completed, sla, pending, disposalPct, slaPct,
                period.Sum(x => x.Input.AmountPaise), complaints, field, period.Count, expected, true, reasons));
        }
        return Read(new DashboardDto(rows), LastUpdated(audits));
    });

    public Task<MonitoringResult<MonitoringRead<IReadOnlyList<EntryDto>>>> GetDashboardEntriesAsync(ClaimsPrincipal principal,
        PeriodFilter filter, CancellationToken ct = default) => Run(async () =>
    {
        await Authorize(principal, ct, "commissioner");
        await ValidateFilter(filter, ct);
        var audits = await LoadAudits(filter.OfficeId, ct);
        var all = CurrentEntries(audits);
        var activeOfficeIds = ActiveOffices(filter).Select(x => x.Id);
        var setups = await db.OfficeSetups.AsNoTracking().Include(x => x.OpeningBalances)
            .Where(x => activeOfficeIds.Contains(x.OfficeId)).ToListAsync(ct);
        var entries = setups.SelectMany(setup => Views(ToDto(setup)!, all.Where(x => x.OfficeId == setup.OfficeId).ToArray())).ToArray();
        return Read<IReadOnlyList<EntryDto>>(Filter(entries, filter), LastUpdated(audits));
    });

    private async Task<SessionDto> Authorize(ClaimsPrincipal principal, CancellationToken ct, string? requiredRole = null)
    {
        // Use one authenticated identity; never assemble authority from claims on different identities.
        var identities = principal.Identities.Where(x => x.IsAuthenticated &&
            x.FindFirst(MonitoringPolicies.ScopeClaim)?.Value == MonitoringPolicies.ScopeValue).ToArray();
        if (identities.Length != 1) Fail(401, "Monitoring authentication required", "UNAUTHORIZED");
        var identity = identities.Single();
        string? Single(string type) => identity.FindAll(type).Count() == 1 ? identity.FindFirst(type)?.Value : null;
        if (Single(MonitoringPolicies.ScopeClaim) != MonitoringPolicies.ScopeValue ||
            !Guid.TryParse(Single(MonitoringPolicies.UserIdClaim), out var userId) ||
            Single(ClaimTypes.NameIdentifier) != userId.ToString())
            Fail(403, "Invalid monitoring identity", "FORBIDDEN");
        Guid.TryParse(Single(MonitoringPolicies.UserIdClaim), out userId);
        var user = await db.Users.AsNoTracking().Include(x => x.OfficeAssignments).ThenInclude(x => x.Office)
            .SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null || !user.IsActive || Single(MonitoringPolicies.RoleClaim) != user.Role)
            Fail(403, "Monitoring account unavailable", "FORBIDDEN");
        Guid? officeId = null;
        string? officeCode = null;
        string role;
        if (user!.Role == MonitoringPolicies.OfficeRole)
        {
            var assignment = user.OfficeAssignments.Count == 1 ? user.OfficeAssignments.Single() : null;
            if (assignment is null || !assignment.Office.IsActive ||
                Single(MonitoringPolicies.OfficeIdClaim) != assignment.OfficeId.ToString())
                Fail(403, "Office assignment unavailable", "FORBIDDEN");
            officeId = assignment!.OfficeId; officeCode = assignment.Office.Code; role = "office";
        }
        else if (user.Role == MonitoringPolicies.CommissionerRole && user.OfficeAssignments.Count == 0 &&
                 !identity.HasClaim(x => x.Type == MonitoringPolicies.OfficeIdClaim)) role = "commissioner";
        else { Fail(403, "Monitoring role unavailable", "FORBIDDEN"); return null!; }
        if (requiredRole is not null && role != requiredRole) Fail(403, $"{requiredRole} access required", "FORBIDDEN");
        long? expiresAt = long.TryParse(Single("exp"), out var seconds) && seconds >= 0 && seconds <= long.MaxValue / 1000
            ? seconds * 1000 : null;
        return new(user.Id, user.LoginName, role, officeId, officeCode, expiresAt);
    }

    private IQueryable<MonitoringOffice> ActiveOffices(PeriodFilter filter) =>
        db.Offices.AsNoTracking().Where(x => x.IsActive && (!filter.OfficeId.HasValue || x.Id == filter.OfficeId));

    private async Task ValidateFilter(PeriodFilter filter, CancellationToken ct)
    {
        if (filter.From == default || filter.To == default || filter.From > filter.To || filter.To > Today(clock.GetUtcNow()) ||
            filter.ServiceId is < 1 or > 25 || (filter.OfficeId.HasValue &&
            !await db.Offices.AnyAsync(x => x.Id == filter.OfficeId && x.IsActive, ct)))
            Fail(422, "Invalid reporting filters");
    }

    private Task<MonitoringOfficeSetup?> LoadSetup(Guid office, CancellationToken ct) =>
        db.OfficeSetups.AsNoTracking().Include(x => x.OpeningBalances).SingleOrDefaultAsync(x => x.OfficeId == office, ct);

    private Task<List<MonitoringAuditEvent>> LoadAudits(Guid? office, CancellationToken ct) =>
        db.AuditEvents.AsNoTracking().Where(x => !office.HasValue || x.OfficeId == office).ToListAsync(ct);

    private static SetupDto? ToDto(MonitoringOfficeSetup? setup) => setup is null ? null : new(setup.OfficeId,
        setup.EffectiveDate, 1, setup.OpeningBalances.GroupBy(x => int.Parse(x.ItemKey)).OrderBy(x => x.Key)
            .Select(group => new OpeningBalanceDto(group.Key, new(
                group.Single(x => x.Kind == MonitoringActivityKind.Service).Count,
                group.Single(x => x.Kind == MonitoringActivityKind.Complaint).Count,
                group.Single(x => x.Kind == MonitoringActivityKind.FieldTask).Count))).ToArray());

    private static IReadOnlyList<EntryDto> CurrentEntries(IEnumerable<MonitoringAuditEvent> audits) =>
        audits.Where(x => x.AggregateType == "entry").Select(x => Snapshot(x).Value.Deserialize<EntryDto>(Json)!)
            .GroupBy(x => x.Id).Select(x => x.MaxBy(e => e.Version)!).ToArray();

    private static IReadOnlyList<EntryDto> Views(SetupDto setup, IReadOnlyList<EntryDto> entries) =>
        setup.Balances.SelectMany(balance =>
        {
            var group = entries.Where(x => x.Input.ServiceId == balance.ServiceId).ToDictionary(x => x.Input.ServiceDate);
            return Derive(balance.Opening, group.Values.Select(x => x.Input))
                .Select(x => group[x.Input.ServiceDate] with { Opening = x.Opening, Closing = x.Closing });
        }).OrderBy(x => x.Input.ServiceDate).ThenBy(x => x.Input.ServiceId).ToArray();

    private static IReadOnlyList<EntryDto> Filter(IEnumerable<EntryDto> entries, PeriodFilter filter) => entries.Where(x =>
        x.Input.ServiceDate >= filter.From && x.Input.ServiceDate <= filter.To &&
        (!filter.OfficeId.HasValue || x.OfficeId == filter.OfficeId) &&
        (!filter.ServiceId.HasValue || x.Input.ServiceId == filter.ServiceId)).ToArray();

    private static IEnumerable<(MonitoringActivityKind Kind, long Count)> Counts(Balances balances) =>
        [(MonitoringActivityKind.Service, balances.Service), (MonitoringActivityKind.Complaint, balances.Complaints),
            (MonitoringActivityKind.FieldTask, balances.Field)];

    private static (int Received, int Disposed) Flows(EntryInput input, MonitoringActivityKind kind) => kind switch
    {
        MonitoringActivityKind.Service => (checked((int)input.Received), checked((int)input.Completed)),
        MonitoringActivityKind.Complaint => (checked((int)input.ComplaintsReceived), checked((int)input.ComplaintsResolved)),
        _ => (checked((int)input.FieldReceived), checked((int)input.FieldCompleted))
    };

    private void AddAudit<T>(SessionDto session, string type, Guid aggregate, string action, string requestId,
        string fingerprint, string? reason, object? before, T after, DateTimeOffset now) =>
        db.AuditEvents.Add(new MonitoringAuditEvent { Id = Guid.NewGuid(), OfficeId = session.OfficeId!.Value,
            ActorUserId = session.UserId, AggregateType = type, AggregateId = aggregate, Action = action,
            BeforeJson = Serialize(before), AfterJson = Serialize(new AuditSnapshot(1, requestId, fingerprint,
                session.Username, reason, JsonSerializer.SerializeToElement(after, Json))), OccurredAt = now });

    private static AuditSnapshot Snapshot(MonitoringAuditEvent audit)
    {
        var snapshot = JsonSerializer.Deserialize<AuditSnapshot>(audit.AfterJson, Json)
            ?? throw new InvalidOperationException("Missing monitoring audit snapshot");
        if (snapshot.SchemaVersion != 1) throw new InvalidOperationException("Unsupported monitoring audit snapshot version");
        return snapshot;
    }
    private static T? Replay<T>(IEnumerable<MonitoringAuditEvent> audits, string requestId, string fingerprint) where T : class
    {
        var receipt = audits.Select(Snapshot).SingleOrDefault(x => x.RequestId == requestId);
        if (receipt is null) return null;
        if (receipt.Fingerprint != fingerprint) Fail(409, "This request identifier was already used for different data", "CONFLICT");
        return receipt.Value.Deserialize<T>(Json)!;
    }
    private static void ValidateRequest(string requestId)
    {
        if (requestId is null || !Regex.IsMatch(requestId, "\\A[a-zA-Z0-9_-]{8,100}\\z")) Fail(422, "Invalid request identifier");
    }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Fingerprint<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
    private static DateTimeOffset? LastUpdated(IEnumerable<MonitoringAuditEvent> audits) =>
        audits.Select(x => (DateTimeOffset?)x.OccurredAt).Max();
    private MonitoringRead<T> Read<T>(T data, DateTimeOffset? lastUpdated) => new(data, clock.GetUtcNow(), lastUpdated);
    private MonitoringRead<T> ReadContent<T>(T data) => new(data, clock.GetUtcNow(), null, Fingerprint(data));
    private static async Task<MonitoringResult<T>> Run<T>(Func<Task<T>> operation)
    {
        try { return MonitoringResult<T>.Success(await operation()); }
        catch (MonitoringRuleException error) { return MonitoringResult<T>.Failure(error); }
    }
}
