using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anchal.Infrastructure.CommissionerMonitoring.Officers;

public sealed class MonitoringFieldVisitService(
    CommissionerMonitoringDbContext db,
    IMonitoringService monitoring,
    TimeProvider clock) : IMonitoringFieldVisitService
{
    public const int IssueActionMinimumLength = 20;
    // Matches the IssueAction column width in CommissionerMonitoringDbContext.
    public const int IssueActionMaximumLength = 4000;
    public const int PageSize = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<MonitoringResult<FieldVisitDto>> CreateAsync(ClaimsPrincipal principal, SaveFieldVisitCommand command, CancellationToken ct = default) =>
        Run(async () =>
        {
            var session = await Session(principal, ct, "officer");
            if (command.Input is null) throw Validation("input", "Enter a visit");
            var input = command.Input;
            var now = clock.GetUtcNow();
            var today = MonitoringRules.Today(now);

            if (command.RequestId is null || command.RequestId.Length is < 8 or > 100)
                throw Validation("requestId", "Invalid request identifier");

            var fingerprint = Fingerprint(input);
            var existing = await Replayed(session.UserId, command.RequestId, ct);
            if (existing is not null)
            {
                if (existing.Fingerprint != fingerprint)
                    throw new MonitoringRuleException(409,
                        new("CONFLICT", "This request identifier was already used for different data"));
                return await Dto(existing, ct);
            }

            Validate(input, today, now);
            var office = await db.Offices.SingleOrDefaultAsync(
                x => x.Code == "anchal" + input.OfficeId.ToString(CultureInfo.InvariantCulture) && x.IsActive, ct)
                ?? throw Validation("officeId", "Select an active Anchal");

            var visit = new MonitoringFieldVisit
            {
                Id = Guid.NewGuid(),
                RequestId = command.RequestId,
                Fingerprint = fingerprint,
                OfficeId = office.Id,
                OfficerUserId = session.UserId,
                Ward = input.Ward,
                ServiceId = input.ServiceId,
                VisitDate = input.VisitDate,
                VisitTime = TimeOnly.ParseExact(input.VisitTime, "HH:mm", CultureInfo.InvariantCulture),
                Latitude = input.Gps.Latitude,
                Longitude = input.Gps.Longitude,
                AccuracyMetres = input.Gps.AccuracyMetres,
                GpsCapturedAt = input.Gps.CapturedAt,
                ReceivedAt = now,
                IssueAction = input.IssueAction.Trim()
            };
            db.FieldVisits.Add(visit);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another submission under the same request id committed between
                // the check above and this insert — a double-tapped Submit. Give
                // back what was stored rather than a failure the officer would
                // answer by filing the visit a second time.
                db.Entry(visit).State = EntityState.Detached;
                var raced = await Replayed(session.UserId, command.RequestId, ct);
                if (raced is null) throw;
                if (raced.Fingerprint != fingerprint)
                    throw new MonitoringRuleException(409,
                        new("CONFLICT", "This request identifier was already used for different data"));
                return await Dto(raced, ct);
            }
            return await Dto(visit, ct);
        });

    public Task<MonitoringResult<FieldVisitPageDto>> ListAsync(ClaimsPrincipal principal, FieldVisitFilter filter, CancellationToken ct = default) =>
        Run(async () =>
        {
            var session = await Session(principal, ct);
            if (session.Role is not ("officer" or "commissioner"))
                throw new MonitoringRuleException(403, new("FORBIDDEN", "officer access required"));
            if (filter.From > filter.To) throw Validation("from", "Enter a valid date range");

            var query = Visits().Where(x => x.VisitDate >= filter.From && x.VisitDate <= filter.To);
            // An officer's list is their own, whatever they ask for.
            if (session.Role == "officer") query = query.Where(x => x.OfficerUserId == session.UserId);
            else if (filter.OfficerId is { } officerId) query = query.Where(x => x.OfficerUserId == officerId);
            if (filter.Ward is { } ward) query = query.Where(x => x.Ward == ward);
            if (filter.ServiceId is { } serviceId) query = query.Where(x => x.ServiceId == serviceId);
            if (filter.OfficeId is { } officeNumber)
            {
                var code = "anchal" + officeNumber.ToString(CultureInfo.InvariantCulture);
                query = query.Where(x => x.Office.Code == code);
            }

            var total = await query.CountAsync(ct);
            var page = Math.Max(1, filter.Page);
            var rows = await query
                .OrderByDescending(x => x.VisitDate).ThenByDescending(x => x.ReceivedAt)
                .Skip((page - 1) * PageSize).Take(PageSize)
                .ToListAsync(ct);
            var mapped = new List<FieldVisitDto>(rows.Count);
            foreach (var row in rows) mapped.Add(await Dto(row, ct));
            return new FieldVisitPageDto(mapped, total);
        });

    public Task<MonitoringResult<FieldVisitDto>> VoidAsync(ClaimsPrincipal principal, Guid visitId, string reason, CancellationToken ct = default) =>
        Run(async () =>
        {
            var session = await Session(principal, ct, "commissioner");
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
                throw Validation("reason", "Give a reason for voiding this visit");

            var visit = await Visits().SingleOrDefaultAsync(x => x.Id == visitId, ct)
                ?? throw new MonitoringRuleException(404, new("NOT_FOUND", "Visit not found"));
            if (visit.VoidedAt is not null)
                throw new MonitoringRuleException(409, new("CONFLICT", "This visit has already been voided"));

            visit.VoidedAt = clock.GetUtcNow();
            visit.VoidReason = reason.Trim();
            visit.VoidedByUserId = session.UserId;
            await db.SaveChangesAsync(ct);
            return await Dto(visit, ct);
        });

    private Task<MonitoringFieldVisit?> Replayed(Guid officerUserId, string requestId, CancellationToken ct) =>
        Visits().AsNoTracking().SingleOrDefaultAsync(
            x => x.OfficerUserId == officerUserId && x.RequestId == requestId, ct);

    private IQueryable<MonitoringFieldVisit> Visits() => db.FieldVisits
        .Include(x => x.Office).Include(x => x.OfficerUser);

    private static void Validate(FieldVisitInputDto input, DateOnly today, DateTimeOffset now)
    {
        if (input.VisitDate != today)
            throw Validation("visitDate", "A visit can only be recorded on the day it was made");
        if (!TimeOnly.TryParseExact(input.VisitTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var visitTime))
            throw Validation("visitTime", "Enter a valid time");
        // The record is evidence of a visit already made, so a time the server
        // has not reached yet contradicts its own receipt timestamp.
        if (visitTime > TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now, "Asia/Kolkata").DateTime))
            throw Validation("visitTime", "A visit cannot be recorded for a time later than now");
        if (input.OfficeId is < 1 or > 6) throw Validation("officeId", "Select an Anchal");
        if (input.Ward is < 1 or > MonitoringRules.WardCount) throw Validation("ward", "Select a ward from 1 to 53");
        if (input.ServiceId is < 1 or > 25) throw Validation("serviceId", "Select an activity");
        var issueAction = (input.IssueAction ?? string.Empty).Trim();
        if (issueAction.Length < IssueActionMinimumLength)
            throw Validation("issueAction",
                $"Describe what was seen and what is to be done (at least {IssueActionMinimumLength} characters)");
        if (issueAction.Length > IssueActionMaximumLength)
            throw Validation("issueAction",
                $"Shorten this to {IssueActionMaximumLength} characters or fewer");
        // A coarse or late fix is stored as it came; the product passes no
        // verdict on it. An impossible one is refused here, because the database refuses it too
        // and that arrives as an unrecoverable 500.
        if (input.Gps is null || !double.IsFinite(input.Gps.Latitude) || !double.IsFinite(input.Gps.Longitude) ||
            !double.IsFinite(input.Gps.AccuracyMetres) || input.Gps.AccuracyMetres < 0 ||
            input.Gps.Latitude is < -90 or > 90 || input.Gps.Longitude is < -180 or > 180)
            throw Validation("gps", "Capture the location at the site");
    }

    private async Task<FieldVisitDto> Dto(MonitoringFieldVisit visit, CancellationToken ct)
    {
        var officer = visit.OfficerUser ?? await db.Users.SingleAsync(x => x.Id == visit.OfficerUserId, ct);
        return new FieldVisitDto(visit.Id, visit.VisitDate, visit.VisitTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            visit.OfficeId, visit.Ward, visit.ServiceId,
            new GpsReadingDto(visit.Latitude, visit.Longitude, visit.AccuracyMetres, visit.GpsCapturedAt),
            visit.IssueAction, visit.OfficerUserId, officer.DisplayName, officer.Designation ?? string.Empty,
            visit.ReceivedAt, visit.VoidedAt, visit.VoidReason);
    }

    private async Task<SessionDto> Session(ClaimsPrincipal principal, CancellationToken ct, string? requiredRole = null)
    {
        var session = await monitoring.GetSessionAsync(principal, ct);
        if (!session.IsSuccess) throw new MonitoringRuleException(session.Status, session.Error!);
        if (requiredRole is not null && session.Value!.Data.Role != requiredRole)
            throw new MonitoringRuleException(403, new("FORBIDDEN", $"{requiredRole} access required"));
        return session.Value!.Data;
    }

    private static string Fingerprint(FieldVisitInputDto input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, Json))));

    private static MonitoringRuleException Validation(string field, string message) =>
        new(422, new("VALIDATION", "Please correct the highlighted fields",
            new Dictionary<string, string> { ["input." + field] = message }));

    private static async Task<MonitoringResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return MonitoringResult<T>.Success(await action()); }
        catch (MonitoringRuleException exception) { return MonitoringResult<T>.Failure(exception); }
    }
}
