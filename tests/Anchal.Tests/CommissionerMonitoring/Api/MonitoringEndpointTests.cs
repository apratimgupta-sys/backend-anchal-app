using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Seeding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Api;

public sealed class MonitoringEndpointTests
{
    private const string Root = "/v1/api/anchal";
    private const string AllowedOrigin = "https://anchal.example";

    // Break caught: a permissive or missing credentialed CORS policy exposes session login to arbitrary origins.
    [Fact]
    public async Task AllowedConfiguredOriginReceivesCredentialedCorsOnLogin()
    {
        await using var host = await Host.Create();
        Assert.NotNull(await host.SignInDirectly());
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = new StringContent("{\"username\":\"anchal1\",\"password\":\"Api-contract-password-123!\"}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", AllowedOrigin);

        using var response = await host.Client.SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.True(response.Headers.GetValues("Access-Control-Allow-Credentials").Single().Equals("true", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("path=/v1/api/anchal", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
    }

    // Break caught: an origin not listed in Cors:AllowedOrigins must never receive a credentialed CORS grant.
    [Fact]
    public async Task UnlistedOriginCannotReceiveCredentialedCorsGrantOnLogin()
    {
        await using var host = await Host.Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = new StringContent("{\"username\":\"anchal1\",\"password\":\"Api-contract-password-123!\"}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "https://attacker.example");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task MalformedLoginJsonUsesValidationEnvelopeAndNoStore()
    {
        await using var host = await Host.Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", AllowedOrigin);
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VALIDATION", error.GetProperty("code").GetString());
        Assert.True(error.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task LoginRateLimitIncludesRetryAfterAndMonitoringEnvelope()
    {
        await using var host = await Host.Create();
        for (var i = 0; i < 10; i++)
            await host.Login("missing", "wrong");
        using var response = await host.Login();

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("RATE_LIMITED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ForwardedHttpsFromTrustedLoopbackProxySetsSecureCookie()
    {
        await using var host = await Host.Create();
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = new StringContent("{\"username\":\"anchal1\",\"password\":\"Api-contract-password-123!\"}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
        var options = AnchalForwardedHeaders.CreateOptions();
        Assert.Contains(System.Net.IPAddress.Loopback, options.KnownProxies);
    }

    [Fact]
    public async Task ForwardedHttpsPreservesSameOriginLoginBehavior()
    {
        await using var host = await Host.Create();
        var origin = "https://" + host.Client.BaseAddress!.Authority;
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
        {
            Content = new StringContent("{\"username\":\"anchal1\",\"password\":\"Api-contract-password-123!\"}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("secure", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonAsciiBodyOverFiftyKilobytesIsRejectedByByteLimit()
    {
        await using var host = await Host.Create();
        var oversized = "{\"username\":\"anchal1\",\"password\":\"" + new string('अ', 20_000) + "\"}";
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login") { Content = new StringContent(oversized, System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add("Origin", AllowedOrigin);
        using var response = await host.Client.SendAsync(request);

        Assert.Equal((HttpStatusCode)413, response.StatusCode);
        Assert.Equal("VALIDATION", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task HistoryDoesNotInventRevisionHeaderAndDashboardRejectsInvalidFilters()
    {
        await using var host = await Host.Create();
        using (var login = await host.Login("commissioner")) Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var history = await host.Client.GetAsync(Root + "/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.False(history.Headers.Contains("X-Monitoring-Revision"));
        using var invalid = await host.Client.GetAsync(Root + "/dashboard?officeId=99&serviceId=26");
        Assert.Equal((HttpStatusCode)422, invalid.StatusCode);
    }

    [Fact]
    public async Task UnexpectedMonitoringFailureUsesInternalErrorEnvelope()
    {
        await using var host = await Host.Create(throwOnProbe: true);
        using var response = await host.Client.GetAsync(Root + "/internal-probe");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("INTERNAL", error.GetProperty("code").GetString());
        Assert.True(error.TryGetProperty("message", out _));
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public Task<HttpResponseMessage> Login(string username = "anchal1", string password = "Api-contract-password-123!")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Root + "/login")
            {
                Content = new StringContent($"{{\"username\":\"{username}\",\"password\":\"{password}\"}}", System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Origin", AllowedOrigin);
            return client.SendAsync(request);
        }
        public async Task<string?> SignInDirectly()
        {
            using var scope = app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>()
                .SignInAsync("anchal1", "Api-contract-password-123!");
        }

        public static async Task<Host> Create(bool throwOnProbe = false)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CommissionerMonitoring:RoutesEnabled"] = "true",
                ["Anchal:Jwt:Secret"] = "monitoring-contract-test-secret-at-least-thirty-two-bytes",
                ["Anchal:Jwt:Issuer"] = "anchal-contract",
                ["Anchal:Jwt:Audience"] = "anchal-client",
                ["Anchal:Jwt:ExpiryMinutes"] = "60",
                ["Cors:AllowedOrigins:0"] = AllowedOrigin
            });
            var databaseRoot = new InMemoryDatabaseRoot();
            var databaseName = Guid.NewGuid().ToString();
            builder.Services.AddDbContext<Anchal.Infrastructure.CommissionerMonitoring.Persistence.CommissionerMonitoringDbContext>(x =>
                x.UseInMemoryDatabase(databaseName, databaseRoot));
            builder.Services.AddCommissionerMonitoringWorkflows();
            builder.Services.AddMonitoringAuthentication();
            builder.Services.AddAuthorization(Anchal.Application.CommissionerMonitoring.Auth.MonitoringPolicies.AddTo);
            builder.Services.AddControllers().AddApplicationPart(typeof(CommissionerMonitoringRouteOptions).Assembly);
            builder.Services.AddCommissionerMonitoringApi(builder.Configuration);

            var app = builder.Build();
            app.UseForwardedHeaders(AnchalForwardedHeaders.CreateOptions());
            app.UseCommissionerMonitoringApi();
            app.UseCors(CommissionerMonitoringApiRegistration.CorsPolicy);
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();
            if (throwOnProbe)
                app.MapGet(Root + "/internal-probe", (Microsoft.AspNetCore.Http.HttpContext _) => throw new InvalidOperationException("test-only failure"));
            app.MapControllers();
            using (var scope = app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>().ProvisionAsync(
                    Enumerable.Range(1, 6).Select(i => "anchal" + i).Append("commissioner")
                        .ToDictionary(x => x, _ => "Api-contract-password-123!"));
                Assert.NotNull(await scope.ServiceProvider.GetRequiredService<IMonitoringAuthenticator>()
                    .SignInAsync("anchal1", "Api-contract-password-123!"));
            }
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new(app, new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() }) { BaseAddress = new Uri(address) });
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
