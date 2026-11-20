namespace Anchal.Api.CommissionerMonitoring;

/// Decides whether a browser Origin may make a cookie-authenticated request.
/// This is the portal's CSRF boundary, so it is deliberately strict: an origin
/// is parsed as a URL rather than string-matched, and a suffix must begin at a
/// host-label boundary so that it cannot be made to match a domain somebody
/// else controls.
public static class MonitoringOriginPolicy
{
    public static bool IsAllowed(string? origin, IReadOnlyList<string> exact, IReadOnlyList<string> suffixes)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        if (exact.Any(x => string.Equals(x, origin, StringComparison.OrdinalIgnoreCase))) return true;
        if (suffixes.Count == 0) return false;

        // An Origin is scheme://host[:port] and nothing else. Anything carrying
        // a path, a query or credentials is not one, whatever it ends with.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var url)) return false;
        if (url.Scheme != Uri.UriSchemeHttps) return false;
        if (!url.IsDefaultPort) return false;
        if (url.AbsolutePath != "/" || url.Query.Length > 0 || url.UserInfo.Length > 0) return false;
        if (!string.Equals(url.GetLeftPart(UriPartial.Authority), origin.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase)) return false;

        var host = url.IdnHost;
        return suffixes.Any(suffix =>
        {
            // ".example.com" or "-scope.vercel.app": without a leading dot or
            // dash a suffix would admit every sibling of the parent domain.
            if (suffix.Length < 2 || (suffix[0] != '.' && suffix[0] != '-')) return false;
            if (host.Length <= suffix.Length) return false;
            return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        });
    }
}
