namespace Anchal.Domain.CommissionerMonitoring.Rules;

/// One field a field visit asks a service for beyond the commons (date, ward, geo-location, description).
/// Type is one of text, textarea, number, select; Options holds the allowed values of a select, in order.
/// Integer marks a number that is a count (dogs, trees) and takes a whole number only; a measurement does not.
/// There is no Required: every details field is optional, and a flag nothing honoured was carried here once.
public sealed record VisitFieldDefinition(string Key, string Type, IReadOnlyList<string>? Options, bool Integer = false);

/// What a field visit asks each of the eight field-work services for. This holds the definitions only;
/// validating a submitted details object against them is the visit service's job.
///
/// TWIN: src/shared/field-visit-fields.json in the frontend repo, which also carries the English and Hindi
/// labels (presentation, not held here). The keys, types, whole-number marker and option values, in order, are
/// asserted on both sides against the golden fixture shape-twin.fixture.json ("fieldVisitFields"), which is
/// committed identically in both repos. An edit to either side must be made in the other, the fixture and
/// this table together; the side edited alone goes red.
public static class MonitoringFieldDefinitions
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<VisitFieldDefinition>> ByService =
        new Dictionary<int, IReadOnlyList<VisitFieldDefinition>>
    {
        [8] = [
            new("vehicleLabour", "text", null),
            new("street", "text", null),
            new("wasteType", "select", ["wet", "dry", "mixed"]),
            new("quantityKg", "number", null),
        ],
        [9] = [
            new("team", "text", null),
            new("vehicleNumber", "text", null),
            new("street", "text", null),
            new("route", "text", null),
        ],
        [10] = [
            new("teamLabour", "text", null),
            new("dogsPicked", "number", null, Integer: true),
            new("dogsNeutered", "number", null, Integer: true),
            new("actionLocation", "text", null),
        ],
        [13] = [
            new("technician", "text", null),
            new("street", "text", null),
            new("poleNumber", "text", null),
            new("workType", "select", ["repair", "replace", "shift"]),
        ],
        [14] = [
            new("street", "text", null),
            new("structureType", "select", ["road", "drain"]),
            new("lengthMetres", "number", null),
        ],
        [15] = [
            new("reportingOfficer", "text", null),
            new("areaCutSqMetres", "number", null),
        ],
        [16] = [
            new("vehicle", "text", null),
            new("team", "text", null),
            new("street", "text", null),
            new("treeCount", "number", null, Integer: true),
        ],
        [17] = [
            new("team", "text", null),
        ]
    };

    /// The service's fields in form order; empty for a service with none, and for an unknown id.
    public static IReadOnlyList<VisitFieldDefinition> For(int serviceId) =>
        ByService.TryGetValue(serviceId, out var fields) ? fields : [];
}
