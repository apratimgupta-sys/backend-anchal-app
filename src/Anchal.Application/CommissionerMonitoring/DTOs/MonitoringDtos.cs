using Anchal.Domain.CommissionerMonitoring.Rules;

namespace Anchal.Application.CommissionerMonitoring.DTOs;

public sealed record MonitoringResult<T>(T? Value, MonitoringError? Error, int Status)
{
    public bool IsSuccess => Error is null;
    public static MonitoringResult<T> Success(T value) => new(value, null, 200);
    public static MonitoringResult<T> Failure(MonitoringRuleException exception) => new(default, exception.Error, exception.Status);
}
// LastUpdated is unknown when the underlying data has no persisted modification timestamp.
// Revision identifies that response's content independently of the retrieval time.
public sealed record MonitoringRead<T>(T Data, DateTimeOffset RetrievedAt, DateTimeOffset? LastUpdated, string? Revision = null);
public sealed record SessionDto(Guid UserId, string Username, string Role, Guid? OfficeId, string? OfficeCode, long? ExpiresAt);
public sealed record ServiceDto(int Id, string Name);
public sealed record OpeningBalanceDto(int ServiceId, Balances Opening);
public sealed record SetupDto(Guid OfficeId, DateOnly StartDate, int Version, IReadOnlyList<OpeningBalanceDto> Balances);
public sealed record SetupCommand(string RequestId, int? ExpectedVersion, DateOnly StartDate,
    IReadOnlyList<OpeningBalanceDto> Balances, string? Reason = null);
public sealed record SaveEntryCommand(string RequestId, int? ExpectedVersion, string Mode, EntryInput Input, string? Reason = null);
// Input is intentionally nested in the application DTO. The HTTP adapter can flatten it to the Cloudflare shape.
public sealed record EntryDto(Guid Id, Guid OfficeId, int Version, EntryInput Input, DateOnly ReportingDate,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Balances Opening, Balances Closing);
public sealed record EntriesDto(SetupDto Setup, IReadOnlyList<EntryDto> Entries, IReadOnlyList<OpeningBalanceDto> Openings);
public sealed record PeriodFilter(DateOnly From, DateOnly To, Guid? OfficeId = null, int? ServiceId = null);
public sealed record DashboardDto(IReadOnlyList<MonitoringRowDto> Rows);
public sealed record MonitoringRowDto(Guid OfficeId, string OfficeCode, long Received, long Completed,
    long SlaWithinTime, long Pending, double? DisposalPct, double? SlaPct, long AmountPaise,
    long ComplaintsPending, long FieldPending, int Submitted, long Expected, bool Configured,
    IReadOnlyList<string> ReviewReasons);
public sealed record AuditDto(Guid Id, Guid OfficeId, string Actor, DateTimeOffset At, string Action,
    string? Reason, string? Before, string After);
