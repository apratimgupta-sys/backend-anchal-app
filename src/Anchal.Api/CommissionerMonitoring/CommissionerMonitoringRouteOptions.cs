namespace Anchal.Api.CommissionerMonitoring;

public sealed class CommissionerMonitoringRouteOptions
{
    public const string Section = "CommissionerMonitoring";
    public const string Prefix = "/v1/api/anchal";
    public const string CookieName = "rmc_monitoring_session";
    public bool RoutesEnabled { get; set; } = true;
}
