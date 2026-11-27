namespace Anchal.Domain.CommissionerMonitoring.Rules;

/// One field a field visit asks a service for beyond the commons (date, ward, geo-location, description).
/// Type is one of text, textarea, number, select; Options holds the allowed values of a select, in order.
/// Integer marks a number that is a count (dogs, trees) and takes a whole number only; a measurement does not.
public sealed record VisitFieldDefinition(string Key, string Type, bool Required, IReadOnlyList<string>? Options, bool Integer = false);

/// What a field visit asks each of the eight field-work services for. This holds the definitions only;
/// validating a submitted details object against them is the visit service's job.
///
/// TWIN: src/shared/field-visit-fields.json in the frontend repo, which also carries the English and Hindi
/// labels (presentation, not held here). The keys, types, requiredness, whole-number marker and option values, in order, are
/// asserted on both sides against the golden fixture shape-twin.fixture.json ("fieldVisitFields"), which is
/// committed identically in both repos. An edit to either side must be made in the other, the fixture and
/// this table together; the side edited alone goes red.
public static class MonitoringFieldDefinitions
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<VisitFieldDefinition>> ByService =
        new Dictionary<int, IReadOnlyList<VisitFieldDefinition>>
    {
        [8] = [
            new("vehicleLabour", "text", false, null),
            new("street", "text", false, null),
            new("wasteType", "select", false, ["wet", "dry", "mixed"]),
            new("quantity", "number", false, null),
        ],
        [9] = [
            new("team", "text", false, null),
            new("vehicleNumber", "text", false, null),
            new("street", "text", false, null),
            new("route", "text", false, null),
        ],
        [10] = [
            new("teamLabour", "text", false, null),
            new("dogsPicked", "number", false, null, Integer: true),
            new("dogsNeutered", "number", false, null, Integer: true),
            new("actionLocation", "text", false, null),
        ],
        [13] = [
            new("technician", "text", false, null),
            new("street", "text", false, null),
            new("poleNumber", "text", false, null),
            new("workType", "select", false, ["repair", "replace", "shift"]),
        ],
        [14] = [
            new("street", "text", false, null),
            new("structureType", "select", false, ["road", "drain"]),
            new("length", "number", false, null),
        ],
        [15] = [
            new("reportingOfficer", "text", false, null),
            new("areaCut", "number", false, null),
        ],
        [16] = [
            new("vehicle", "text", false, null),
            new("team", "text", false, null),
            new("street", "text", false, null),
            new("treeCount", "number", false, null, Integer: true),
        ],
        [17] = [
            new("team", "text", false, null),
        ]
    };

    /// The service's fields in form order; empty for a service with none, and for an unknown id.
    public static IReadOnlyList<VisitFieldDefinition> For(int serviceId) =>
        ByService.TryGetValue(serviceId, out var fields) ? fields : [];
}
