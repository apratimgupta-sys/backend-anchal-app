namespace Anchal.Domain.CommissionerMonitoring.Rules;

public sealed record Balances(long Service, long Complaints, long Field);
public sealed record EntryInput(int ServiceId, DateOnly ServiceDate, string Ward, long Received, long Completed, long SlaWithinTime, long AmountPaise, long ComplaintsReceived, long ComplaintsResolved, long FieldReceived, long FieldCompleted, double? AvgDisposalDays, string Remarks,
    // The office's deliberate statement that nothing happened on this service today. Appended last and optional so
    // snapshots written before it existed (audit SchemaVersion stays 1) still deserialize, as false. Deliberately not
    // named IsZeroActivity: the HTTP adapter strips that key, and the computed property below means something else.
    bool NothingToReport = false)
{ public bool IsZeroActivity => Received == 0 && Completed == 0 && SlaWithinTime == 0 && AmountPaise == 0 && ComplaintsReceived == 0 && ComplaintsResolved == 0 && FieldReceived == 0 && FieldCompleted == 0; }
public sealed record DerivedEntry(EntryInput Input, Balances Opening, Balances Closing);
public sealed record MonitoringError(string Code, string Message, IReadOnlyDictionary<string, string>? FieldErrors = null, DateOnly? ConflictDate = null, int? ServiceId = null);
public sealed class MonitoringRuleException(int status, MonitoringError error) : Exception(error.Message) { public int Status { get; } = status; public MonitoringError Error { get; } = error; }
public enum MonitoringFormShape { Counter, CounterRevenue, Revenue, FieldWork }
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
    // the completed-vs-received rule does not apply.
    //
    // TWIN: formShape in src/domain/service-kind.ts in the frontend repo, which
    // derives the same four sets from the Primary Monitoring Unit column of
    // src/shared/services.json. That column is the source of truth; the backend
    // has no service master, so these sets are copied by hand.
    //
    // An edit to either must be made in both, in the same change — and editing a
    // `unit` string over there is an edit to both. The failure is quiet: the
    // frontend test carrying the counter list fails, somebody fixes that test,
    // and this file keeps the old shape. The service then validates and derives
    // under one shape and renders under another, and its whole closing history
    // re-derives on the next read.
    //
    // 1 new holding, 3 trade licence, 4 water connection and 7 data correction
    // are the fourth shape, CounterRevenue: a queue and money together. They were
    // counter services, so their opening balances are already locked in and the
    // shape moves the pending balance exactly as counter does.
    //
    // Services not listed are counter shape (grievance, service 12, included).
    private static readonly HashSet<int> CounterRevenueServices = [1, 3, 4, 7];
    private static readonly HashSet<int> FieldWorkServices = [8, 9, 10, 13, 14, 15, 16, 17];
    private static readonly HashSet<int> RevenueServices = [2, 5, 6];

    // CounterRevenue is tested first: the order must never decide a shape, only
    // set membership, and the sets are disjoint.
    public static MonitoringFormShape FormShape(int serviceId) =>
        CounterRevenueServices.Contains(serviceId) ? MonitoringFormShape.CounterRevenue
        : FieldWorkServices.Contains(serviceId) ? MonitoringFormShape.FieldWork
        : RevenueServices.Contains(serviceId) ? MonitoringFormShape.Revenue
        : MonitoringFormShape.Counter;

    // Only the shapes with a queue move the pending balance.
    public static bool MovesPendingBalance(int serviceId) =>
        FormShape(serviceId) is MonitoringFormShape.Counter or MonitoringFormShape.CounterRevenue;

    public static bool IsFieldWork(int serviceId) => FormShape(serviceId) == MonitoringFormShape.FieldWork;

    // The services in the Service Master, numbered 1 to ServiceCount.
    public const int ServiceCount = 25;

    // Whether the daily entry collects this service. Field work is reported by field officers as visits, so
    // the clerk's daily entry refuses it. Derived from the form shape, never listed: the eight field-work
    // services keep their shape (visits reference them and historical entries need it to stay readable), and
    // the daily entry collects whatever is not that shape.
    public static bool DailyEntryCollects(int serviceId) => !IsFieldWork(serviceId);

    // How many services the daily entry collects: the expected-reports denominator, per day.
    public static int DailyEntryServiceCount => Enumerable.Range(1, ServiceCount).Count(DailyEntryCollects);

    // What the daily form actually asks a service for, by shape: two numbers
    // instead of nine. Everything else in the record is retired — kept for the
    // entries already filed with it, collected from nobody.
    private static readonly string[] CounterFields = ["received", "completed"];
    private static readonly string[] CounterRevenueFields = ["received", "completed", "amountPaise"];
    private static readonly string[] RevenueFields = ["received", "amountPaise"];
    private static readonly string[] FieldWorkFields = ["completed"];

    public static IReadOnlyList<string> CollectedFields(int serviceId) => FormShape(serviceId) switch
    {
        MonitoringFormShape.CounterRevenue => CounterRevenueFields,
        MonitoringFormShape.Revenue => RevenueFields,
        MonitoringFormShape.FieldWork => FieldWorkFields,
        _ => CounterFields,
    };

    // A cross-field rule can only be acted on if the office is asked for BOTH
    // of its operands. Keyed to anything else, a 422 dead-ends a correction:
    // the message names a field the form does not render, so the office sees
    // "Please correct the highlighted fields" with nothing highlighted and no
    // way to save. The retired fields keep their stored values through a
    // correction, so their consistency is preserved by construction.
    public static bool Collects(int serviceId, params string[] fields)
    {
        var collected = CollectedFields(serviceId);
        return fields.All(collected.Contains);
    }

    // The number of wards. One bound for daily entry and field visits, so they cannot drift.
    public const int WardCount = 53;
    // Canonical ward: "Ward N", one space, ASCII digits, no leading zero, 1 to WardCount. Checked by format, not against a list.
    public static bool IsCanonicalWard(string? ward) => ward is not null && ward.StartsWith("Ward ", StringComparison.Ordinal)
        && ward.Length is 6 or 7 && ward[5] is >= '1' and <= '9' && ward[5..].All(c => c is >= '0' and <= '9')
        && int.Parse(ward[5..]) <= WardCount;
    public static void ValidateEntry(EntryInput input, DateOnly today) { var errors = new Dictionary<string, string>(); var figures = new (string, long)[] { ("received", input.Received), ("completed", input.Completed), ("slaWithinTime", input.SlaWithinTime), ("amountPaise", input.AmountPaise), ("complaintsReceived", input.ComplaintsReceived), ("complaintsResolved", input.ComplaintsResolved), ("fieldReceived", input.FieldReceived), ("fieldCompleted", input.FieldCompleted) }; foreach (var (name, value) in figures) if (value is < 0 or > 1_000_000_000) errors[name] = "Enter a nonnegative whole number within the allowed limit"; if (input.ServiceId is < 1 or > ServiceCount) errors["serviceId"] = "Select a service"; else if (!DailyEntryCollects(input.ServiceId)) errors["serviceId"] = "Field work is reported by field officers, not on the daily entry"; if (input.ServiceDate == default || input.ServiceDate > today) errors["serviceDate"] = "Enter a valid date through today"; if (!IsCanonicalWard(input.Ward?.Trim())) errors["ward"] = "Select a ward from 1 to 53"; if (input.Remarks is null || input.Remarks.Length > 2000) errors["remarks"] = "Remarks must be at most 2000 characters"; if (Collects(input.ServiceId, "slaWithinTime", "completed") && input.SlaWithinTime > input.Completed) errors["slaWithinTime"] = "Cannot exceed completed cases"; if (Collects(input.ServiceId, "completed", "received") && input.Completed > input.Received) errors["completed"] = "Completed cannot exceed received"; if (Collects(input.ServiceId, "complaintsResolved", "complaintsReceived") && input.ComplaintsResolved > input.ComplaintsReceived) errors["complaintsResolved"] = "Resolved complaints cannot exceed received"; if (Collects(input.ServiceId, "fieldCompleted", "fieldReceived") && input.FieldCompleted > input.FieldReceived) errors["fieldCompleted"] = "Completed field tasks cannot exceed received"; if (input.AvgDisposalDays is double days && (!double.IsFinite(days) || days < 0 || days > 100000)) errors["avgDisposalDays"] = "Enter valid average days";
            // "Nothing to report" contradicts only the figures the form asks this service for. A retired figure
            // keeps its stored value through a correction, so it cannot be held against the flag: the message
            // would name a field with nothing to highlight. (Not EntryInput.IsZeroActivity, which is shape-blind
            // on purpose: it feeds the persisted MonitoringReport column.)
            if (input.NothingToReport && figures.Any(f => f.Item2 != 0 && Collects(input.ServiceId, f.Item1))) errors["nothingToReport"] = "Nothing to report cannot be combined with reported figures"; if (errors.Count > 0) throw new MonitoringRuleException(422, new("VALIDATION", "Please correct the highlighted fields", errors)); }
    public static IReadOnlyList<DerivedEntry> Derive(Balances initial, IEnumerable<EntryInput> entries) { var balance = initial; var result = new List<DerivedEntry>(); foreach (var input in entries.OrderBy(x => x.ServiceDate)) { var opening = balance; // Only counter-shape work has a queue. Field work (grass is cut, a route is fogged) and
            // revenue (payments land in Received, nothing is completed) have none. Counting them
            // in broke the two shapes in opposite directions: field work subtracted disposals
            // against a Received of 0, drove the balance negative, and was rejected here and by
            // ck_monitoring_closing_nonnegative, so honest field entries were impossible even
            // after ValidateEntry allowed them; revenue added every payment to the balance and
            // took nothing off, so it only ever climbed, reporting a pending figure no office
            // could work off.
            balance = new(balance.Service + (MovesPendingBalance(input.ServiceId) ? input.Received - input.Completed : 0), balance.Complaints + input.ComplaintsReceived - input.ComplaintsResolved, balance.Field + input.FieldReceived - input.FieldCompleted); if (new[] { balance.Service, balance.Complaints, balance.Field }.Any(x => x < 0 || x > int.MaxValue)) throw new MonitoringRuleException(422, new("VALIDATION", $"Invalid pending balance on {input.ServiceDate:yyyy-MM-dd} (service {input.ServiceId})", ConflictDate: input.ServiceDate, ServiceId: input.ServiceId)); result.Add(new(input, opening, balance)); } return result; }
    public static void Fail(int status, string message, string code = "VALIDATION") => throw new MonitoringRuleException(status, new(code, message));
}
