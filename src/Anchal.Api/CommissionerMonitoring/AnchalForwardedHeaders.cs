using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Anchal.Api.CommissionerMonitoring;

public static class AnchalForwardedHeaders
{
    // Nginx and Kestrel share the loopback host; never trust forwarded headers from public peers.
    public static ForwardedHeadersOptions CreateOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        return options;
    }
}
