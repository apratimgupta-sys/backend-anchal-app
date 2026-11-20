using Anchal.Api.CommissionerMonitoring;
using Anchal.Tests.CommissionerMonitoring.FieldVisits;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Api;

/// Vercel preview hostnames carry a per-deployment hash, so no fixed list can
/// cover them. A suffix can — but this check is the CSRF protection for
/// cookie-authenticated POSTs, so the suffix must be anchored at a real host
/// boundary and must not be coaxed into matching somebody else's domain.
public sealed class AllowedOriginSuffixTests
{
    private static readonly string[] Exact = ["https://anchal.example"];
    private static readonly string[] Suffixes = ["-apratim1.vercel.app"];

    private static bool Permitted(string origin) =>
        MonitoringOriginPolicy.IsAllowed(origin, Exact, Suffixes);

    [Theory]
    [InlineData("https://anchal-ffqdtedfc-apratim1.vercel.app")]
    [InlineData("https://anchal-9f2c1b-apratim1.vercel.app")]
    [InlineData("https://ANCHAL-ABC-APRATIM1.VERCEL.APP")]
    public void APreviewUnderTheConfiguredScopeIsAllowed(string origin) =>
        Assert.True(Permitted(origin));

    [Fact]
    public void AnExactlyListedOriginIsStillAllowed() =>
        Assert.True(Permitted("https://anchal.example"));

    [Theory]
    // Someone else's Vercel account: the scope segment differs.
    [InlineData("https://anchal-ffqdtedfc-attacker.vercel.app")]
    // The suffix as a prefix of a domain the attacker controls.
    [InlineData("https://-apratim1.vercel.app.evil.example")]
    // The suffix in a path or query, which an Origin header cannot carry
    // anyway, but a naive EndsWith over a URL would accept.
    [InlineData("https://evil.example/-apratim1.vercel.app")]
    [InlineData("https://evil.example/?x=-apratim1.vercel.app")]
    // Credentials section pointing at another host.
    [InlineData("https://x-apratim1.vercel.app@evil.example")]
    // Plaintext, and a non-default port, neither of which Vercel serves.
    [InlineData("http://anchal-abc-apratim1.vercel.app")]
    [InlineData("https://anchal-abc-apratim1.vercel.app:8443")]
    // The bare suffix is a hostname nobody should be trusted on.
    [InlineData("https://-apratim1.vercel.app")]
    [InlineData("null")]
    [InlineData("")]
    public void AnythingElseIsRefused(string origin) =>
        Assert.False(Permitted(origin), origin);

    [Fact]
    public void ASuffixThatDoesNotStartAtAHostBoundaryIsIgnoredRatherThanTrusted()
    {
        // "vercel.app" would admit every Vercel account on earth; a suffix has
        // to begin with a dot or a dash to be usable at all.
        Assert.False(MonitoringOriginPolicy.IsAllowed(
            "https://anchal-abc-attacker.vercel.app", [], ["vercel.app"]));
    }

    [Fact]
    public void NoSuffixesConfiguredMeansExactMatchingOnly()
    {
        Assert.False(MonitoringOriginPolicy.IsAllowed(
            "https://anchal-abc-apratim1.vercel.app", Exact, []));
        Assert.True(MonitoringOriginPolicy.IsAllowed("https://anchal.example", Exact, []));
    }
}

/// The suffix has to be honoured in both places that look at an Origin: the
/// middleware that returns 403, and the CORS policy. Fixing only the first
/// leaves the browser discarding a response the server allowed.
public sealed class SuffixOriginEndToEndTests
{
    private const string Root = "/v1/api/anchal";
    private const string Preview = "https://anchal-ffqdtedfc-apratim1.vercel.app";

    private static readonly Dictionary<string, string?> Suffix =
        new() { ["Cors:AllowedOriginSuffixes:0"] = "-apratim1.vercel.app" };

    [Fact]
    public async Task APreviewOriginCanLogInAndReceivesACredentialedCorsGrant()
    {
        await using var host = await MonitoringTestHost.Create(Suffix, Preview);
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = MonitoringTestHost.Json(
                new { username = "anchal1", password = MonitoringTestHost.SeedPassword })
        };

        using var response = await host.Client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(Preview, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single(),
            ignoreCase: true);
    }

    [Fact]
    public async Task AnotherAccountsVercelOriginIsStillRefused()
    {
        await using var host = await MonitoringTestHost.Create(
            Suffix, "https://anchal-ffqdtedfc-attacker.vercel.app");
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = MonitoringTestHost.Json(
                new { username = "anchal1", password = MonitoringTestHost.SeedPassword })
        };

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Same-origin request required", await response.Content.ReadAsStringAsync());
    }
}
