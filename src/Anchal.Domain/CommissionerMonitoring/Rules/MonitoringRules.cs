namespace Anchal.Domain.CommissionerMonitoring.Rules;

public sealed record Balances(long Service, long Complaints, long Field);
public sealed record EntryInput(int ServiceId, DateOnly ServiceDate, string Ward, long Received, long Completed, long SlaWithinTime, long AmountPaise, long ComplaintsReceived, long ComplaintsResolved, long FieldReceived, long FieldCompleted, double? AvgDisposalDays, string Remarks)
{ public bool IsZeroActivity => Received == 0 && Completed == 0 && SlaWithinTime == 0 && AmountPaise == 0 && ComplaintsReceived == 0 && ComplaintsResolved == 0 && FieldReceived == 0 && FieldCompleted == 0; }
public sealed record DerivedEntry(EntryInput Input, Balances Opening, Balances Closing);
public sealed record MonitoringError(string Code, string Message, IReadOnlyDictionary<string, string>? FieldErrors = null, DateOnly? ConflictDate = null, int? ServiceId = null);
public sealed class MonitoringRuleException(int status, MonitoringError error) : Exception(error.Message) { public int Status { get; } = status; public MonitoringError Error { get; } = error; }
public enum MonitoringFormShape { Counter, Revenue, FieldWork }
public static class MonitoringRules
{
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Kolkata).DateTime);
    public static void ValidateRoute(string mode, DateOnly date, DateOnly start, DateOnly today) { if (date < start || (mode != "today" && mode != "backdate") || (mode == "today" && date != today) || (mode == "backdate" && date >= today)) Fail(422, "Date is outside this reporting route"); }
    public static void ValidateReason(bool correction, string mode, string? reason) { if ((correction || mode == "backdate") && (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)) Fail(422, correction ? "A correction reason is required" : "A delayed-reporting reason is required"); }
    // Services whose Primary Monitoring Unit in the Service Master is work done
    // on the ground rather than cases in a queue: 8 door-to-door vehicle,
    // 9 fogging, 10 street dog, 13 street lighting, 14 road and drain,
    // 15 grass cutting, 16 tree pruning, 17 water body cleaning. Work here can
    // happen with no request behind it, and there is no case to dispose of, so
    // the completed-vs-received and average-days rules do not apply.
    //
    // DRIFT RISK: the source of truth is the Primary Monitoring Unit column of
    // src/shared/services.json in the frontend repo (derived there in
    // src/domain/service-kind.ts). The backend has no service master, so these
    // sets are copied by hand and must be updated together with it. Services
    // not listed are counter shape (grievance, service 12, included).
    private static readonly HashSet<int> FieldWorkServices = [8, 9, 10, 13, 14, 15, 16, 17];
    private static readonly HashSet<int> RevenueServices = [2, 5, 6];

    public static MonitoringFormShape FormShape(int serviceId) =>
        FieldWorkServices.Contains(serviceId) ? MonitoringFormShape.FieldWork
        : RevenueServices.Contains(serviceId) ? MonitoringFormShape.Revenue
        : MonitoringFormShape.Counter;

    public static bool MovesPendingBalance(int serviceId) => FormShape(serviceId) == MonitoringFormShape.Counter;

    public static bool IsFieldWork(int serviceId) => FormShape(serviceId) == MonitoringFormShape.FieldWork;

    public static void ValidateEntry(EntryInput input, DateOnly today) { var errors = new Dictionary<string, string>(); foreach (var (name, value) in new (string, long)[] { ("received", input.Received), ("completed", input.Completed), ("slaWithinTime", input.SlaWithinTime), ("amountPaise", input.AmountPaise), ("complaintsReceived", input.ComplaintsReceived), ("complaintsResolved", input.ComplaintsResolved), ("fieldReceived", input.FieldReceived), ("fieldCompleted", input.FieldCompleted) }) if (value is < 0 or > 1_000_000_000) errors[name] = "Enter a nonnegative whole number within the allowed limit"; if (input.ServiceId is < 1 or > 25) errors["serviceId"] = "Select a service"; if (input.ServiceDate == default || input.ServiceDate > today) errors["serviceDate"] = "Enter a valid date through today"; if (string.IsNullOrWhiteSpace(input.Ward) || input.Ward.Length > 200) errors["ward"] = "Enter ward number/name (maximum 200 characters)"; if (input.Remarks is null || input.Remarks.Length > 2000) errors["remarks"] = "Remarks must be at most 2000 characters"; if (input.SlaWithinTime > input.Completed) errors["slaWithinTime"] = "Cannot exceed completed cases"; if (!IsFieldWork(input.ServiceId) && input.Completed > input.Received) errors["completed"] = "Completed cannot exceed received"; if (input.ComplaintsResolved > input.ComplaintsReceived) errors["complaintsResolved"] = "Resolved complaints cannot exceed received"; if (input.FieldCompleted > input.FieldReceived) errors["fieldCompleted"] = "Completed field tasks cannot exceed received"; if ((input.AvgDisposalDays is double days && (!double.IsFinite(days) || days < 0 || days > 100000)) || (!IsFieldWork(input.ServiceId) && input.Completed > 0 && input.AvgDisposalDays is null)) errors["avgDisposalDays"] = "Enter valid average days when work is completed"; if (errors.Count > 0) throw new MonitoringRuleException(422, new("VALIDATION", "Please correct the highlighted fields", errors)); }
    public static IReadOnlyList<DerivedEntry> Derive(Balances initial, IEnumerable<EntryInput> entries) { var balance = initial; var result = new List<DerivedEntry>(); foreach (var input in entries.OrderBy(x => x.ServiceDate)) { var opening = balance; // Field work has no queue: grass is cut, a route is fogged. Counting it
            // into the service balance drove the balance negative and was rejected
            // here and by ck_monitoring_closing_nonnegative, which made honest field
            // entries impossible even after ValidateEntry allowed them.
            balance = new(balance.Service + (IsFieldWork(input.ServiceId) ? 0 : input.Received - input.Completed), balance.Complaints + input.ComplaintsReceived - input.ComplaintsResolved, balance.Field + input.FieldReceived - input.FieldCompleted); if (new[] { balance.Service, balance.Complaints, balance.Field }.Any(x => x < 0 || x > int.MaxValue)) throw new MonitoringRuleException(422, new("VALIDATION", $"Invalid pending balance on {input.ServiceDate:yyyy-MM-dd} (service {input.ServiceId})", ConflictDate: input.ServiceDate, ServiceId: input.ServiceId)); result.Add(new(input, opening, balance)); } return result; }
    public static void Fail(int status, string message, string code = "VALIDATION") => throw new MonitoringRuleException(status, new(code, message));
}
