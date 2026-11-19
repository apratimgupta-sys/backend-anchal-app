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
public sealed record SessionDto(Guid UserId, string Username, string Role, Guid? OfficeId, string? OfficeCode, long? ExpiresAt,
    OfficerProfileDto? Officer = null, bool? MustChangePassword = null);
// HomeOfficeId is the Anchal number the officer is posted to, not the office's Guid:
// the adapter only rewrites a key named officeId, and this value is already public-facing.
public sealed record OfficerProfileDto(Guid Id, string Username, string Name, string Designation, int HomeOfficeId, bool Active);
public sealed record CreateOfficerCommand(string Username, string Name, string Designation, int HomeOfficeId);
public sealed record CreatedOfficerDto(OfficerProfileDto Officer, string TemporaryPassword);
public sealed record TemporaryPasswordDto(string TemporaryPassword);
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);
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

public sealed record GpsReadingDto(double Latitude, double Longitude, double AccuracyMetres, DateTimeOffset CapturedAt);
public sealed record FieldVisitInputDto(DateOnly VisitDate, string VisitTime, int OfficeId, int Ward, int ServiceId,
    GpsReadingDto Gps, string IssueAction);
public sealed record SaveFieldVisitCommand(string RequestId, FieldVisitInputDto Input);
// OfficeId stays a Guid here so the HTTP adapter rewrites it to the Anchal number, as it
// does everywhere else; VisitTime is HH:mm, the form's own shape.
public sealed record FieldVisitDto(Guid Id, DateOnly VisitDate, string VisitTime, Guid OfficeId, int Ward,
    int ServiceId, GpsReadingDto Gps, string IssueAction, Guid OfficerId, string OfficerName,
    string OfficerDesignation, DateTimeOffset ReceivedAt, bool VerifiedOnSite,
    DateTimeOffset? VoidedAt, string? VoidReason);
public sealed record FieldVisitPageDto(IReadOnlyList<FieldVisitDto> Rows, int Total);
public sealed record FieldVisitFilter(DateOnly From, DateOnly To, int? OfficeId, int? Ward, int? ServiceId,
    Guid? OfficerId, int Page);
