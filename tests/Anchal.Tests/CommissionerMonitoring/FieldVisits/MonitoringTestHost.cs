using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Anchal.Api.CommissionerMonitoring;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
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

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

internal sealed class MonitoringTestHost(WebApplication app, HttpClient client) : IAsyncDisposable
{
    public const string Root = "/v1/api/anchal";
    private const string AllowedOrigin = "https://anchal.example";
    public const string SeedPassword = "Api-contract-password-123!";

    public HttpClient Client => client;

    /// The app's root services, for a test that must plant a row the API cannot write.
    public IServiceProvider Services => app.Services;

    public Task<HttpResponseMessage> Login(string username, string password = SeedPassword) =>
        client.PostAsync(Root + "/login", Json(new { username, password }));

    /// A second client on the same app with its own cookie jar, for a test that
    /// needs two accounts signed in at once.
    public HttpClient NewClient()
    {
        var http = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() })
        {
            BaseAddress = client.BaseAddress
        };
        http.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        return http;
    }

    public static Task<HttpResponseMessage> Login(HttpClient http, string username, string password = SeedPassword) =>
        http.PostAsync(Root + "/login", Json(new { username, password }));

    public static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    /// Creates an officer as the commissioner, signs them in, and clears the forced
    /// password change so the caller lands on a usable officer session.
    public async Task<JsonElement> SignInAsOfficer(
        string username = "je.kadru", string name = "A. Kumar",
        string designation = "Junior Engineer", int homeOfficeId = 3,
        string password = "Kadru#2026je")
    {
        await Login("commissioner");
        using var created = await client.PostAsync(Root + "/officers",
            Json(new { username, name, designation, homeOfficeId }));
        Assert.True(created.IsSuccessStatusCode,
            $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var temporary = body.GetProperty("temporaryPassword").GetString()!;

        await Login(username, temporary);
        using var changed = await client.PostAsync(Root + "/password",
            Json(new { currentPassword = temporary, newPassword = password }));
        Assert.True(changed.IsSuccessStatusCode,
            $"{changed.StatusCode}: {await changed.Content.ReadAsStringAsync()}");
        using var signedIn = await Login(username, password);
        Assert.True(signedIn.IsSuccessStatusCode);
        return body.GetProperty("officer");
    }

    public static Task<MonitoringTestHost> Create() => Create(null, null);

    /// `extraConfig` adds configuration entries; `originHeader` overrides the
    /// Origin the client sends, for tests about the origin check itself.
    public static async Task<MonitoringTestHost> Create(
        Dictionary<string, string?>? extraConfig, string? originHeader)
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
        if (extraConfig is not null) builder.Configuration.AddInMemoryCollection(extraConfig);
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<CommissionerMonitoringDbContext>(x =>
            x.UseInMemoryDatabase(databaseName, databaseRoot));
        builder.Services.AddCommissionerMonitoringWorkflows();
        builder.Services.AddMonitoringAuthentication();
        builder.Services.AddAuthorization(MonitoringPolicies.AddTo);
        builder.Services.AddControllers().AddApplicationPart(typeof(CommissionerMonitoringRouteOptions).Assembly);
        builder.Services.AddCommissionerMonitoringApi(builder.Configuration);

        var app = builder.Build();
        app.UseForwardedHeaders(AnchalForwardedHeaders.CreateOptions());
        app.UseCommissionerMonitoringApi();
        app.UseCors(CommissionerMonitoringApiRegistration.CorsPolicy);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMonitoringProvisioner>().ProvisionAsync(
                Enumerable.Range(1, 6).Select(i => "anchal" + i).Append("commissioner")
                    .ToDictionary(x => x, _ => SeedPassword));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var http = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() })
        {
            BaseAddress = new Uri(address)
        };
        http.DefaultRequestHeaders.Add("Origin", originHeader ?? AllowedOrigin);
        return new(app, http);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }
}
