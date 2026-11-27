namespace Anchal.Domain.CommissionerMonitoring.Rules;

/// The JSON member names the HTTP adapter rewrites wherever it finds them in a response payload (Transform in
/// MonitoringHttpAdapter): officeId is replaced by the Anchal number, officeCode removed, and the members of
/// input lifted onto their parent. The adapter reads them from here, not from literals, and the visit service
/// refuses to read a stored details key of any of these names (a details object is user-shaped data inside
/// that payload, and one such key would be rewritten or would fail the whole list). A name Transform starts to
/// treat specially must be declared here and added to All; a test checks that every name declared is in All.
public static class MonitoringPayloadKeys
{
    public const string OfficeId = "officeId";
    public const string OfficeCode = "officeCode";
    public const string Input = "input";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { OfficeId, OfficeCode, Input };
}
