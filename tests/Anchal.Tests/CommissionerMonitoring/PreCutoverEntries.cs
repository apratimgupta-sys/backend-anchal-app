using System.Text.Json;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Tests.CommissionerMonitoring;

/// Writes a daily entry the way SaveEntryAsync did before field work left it: the audit snapshot every read is
/// derived from. Needed because the rule that now refuses such an entry also stops a test from filing one.
internal static class PreCutoverEntries
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Guid Add(CommissionerMonitoringDbContext db, Guid office, Guid user, EntryInput input, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        var dto = new EntryDto(id, office, 1, input, input.ServiceDate, now, now, new(0, 0, 0), new(0, 0, 0));
        var snapshot = new { SchemaVersion = 1, RequestId = $"pre-cutover-{id}", Fingerprint = "seed", Actor = "anchal1",
            Reason = (string?)null, Value = JsonSerializer.SerializeToElement(dto, Json) };
        db.AuditEvents.Add(new MonitoringAuditEvent { Id = Guid.NewGuid(), OfficeId = office, ActorUserId = user,
            AggregateType = "entry", AggregateId = id, Action = "Entry submitted", BeforeJson = "null",
            AfterJson = JsonSerializer.Serialize(snapshot, Json), OccurredAt = now });
        return id;
    }
}
