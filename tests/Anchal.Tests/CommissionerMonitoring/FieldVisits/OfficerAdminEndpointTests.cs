using System.Net;
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

public sealed class OfficerAdminEndpointTests
{
    private const string Root = "/v1/api/anchal";
    private const string AllowedOrigin = "https://anchal.example";

    [Fact]
    public async Task CommissionerSeesAnEmptyRosterBeforeAnyOfficerExists()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");

        using var response = await host.Client.GetAsync(Root + "/officers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadFromJsonAsync<JsonElement[]>() ?? []);
    }

    [Fact]
    public async Task CreatingAnOfficerReturnsAPasswordThatSignsThemIn()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");

        var created = await host.CreateOfficer();
        var password = created.GetProperty("temporaryPassword").GetString();

        Assert.False(string.IsNullOrWhiteSpace(password));
        using var signIn = await host.Login("je.kadru", password!);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
    }

    [Fact]
    public async Task TheTemporaryPasswordIsNeverListedAfterwards()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        await host.CreateOfficer();

        var body = await host.Client.GetStringAsync(Root + "/officers");

        Assert.DoesNotContain("temporaryPassword", body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnOfficersHomeAnchalIsReportedAsItsNumber()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        await host.CreateOfficer(homeOfficeId: 3);

        var roster = await host.Client.GetFromJsonAsync<JsonElement[]>(Root + "/officers");

        Assert.Equal(3, roster!.Single().GetProperty("homeOfficeId").GetInt32());
    }

    [Fact]
    public async Task AnOfficeAccountCannotReadOrCreateOfficers()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("anchal1");

        using var read = await host.Client.GetAsync(Root + "/officers");
        using var write = await host.Client.PostAsync(Root + "/officers",
            Json(new { username = "x", name = "x", designation = "x", homeOfficeId = 1 }));

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task AnOfficerCannotAdministerOtherOfficers()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var password = (await host.CreateOfficer()).GetProperty("temporaryPassword").GetString()!;
        await host.Login("je.kadru", password);

        using var response = await host.Client.GetAsync(Root + "/officers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ANewOfficersSessionCarriesTheirProfileAndTheMustChangeFlag()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var password = (await host.CreateOfficer()).GetProperty("temporaryPassword").GetString()!;
        await host.Login("je.kadru", password);

        var session = await host.Client.GetFromJsonAsync<JsonElement>(Root + "/session");

        Assert.Equal("officer", session.GetProperty("role").GetString());
        Assert.True(session.GetProperty("mustChangePassword").GetBoolean());
        var officer = session.GetProperty("officer");
        Assert.Equal("A. Kumar", officer.GetProperty("name").GetString());
        Assert.Equal("Junior Engineer", officer.GetProperty("designation").GetString());
        Assert.Equal(3, officer.GetProperty("homeOfficeId").GetInt32());
    }

    [Fact]
    public async Task AnOfficerWhoMustChangeTheirPasswordIsBlockedFromOtherRoutes()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var password = (await host.CreateOfficer()).GetProperty("temporaryPassword").GetString()!;
        await host.Login("je.kadru", password);

        using var blocked = await host.Client.GetAsync(Root + "/services");
        using var allowed = await host.Client.GetAsync(Root + "/session");

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Contains("password_change_required", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task ChangingThePasswordClearsTheFlagAndTheOldPasswordStopsWorking()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var password = (await host.CreateOfficer()).GetProperty("temporaryPassword").GetString()!;
        await host.Login("je.kadru", password);

        using var change = await host.Client.PostAsync(Root + "/password",
            Json(new { currentPassword = password, newPassword = "Kadru#2026je" }));
        Assert.True(change.IsSuccessStatusCode, $"{change.StatusCode}: {await change.Content.ReadAsStringAsync()}");

        using var withOld = await host.Login("je.kadru", password);
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);

        using var withNew = await host.Login("je.kadru", "Kadru#2026je");
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
        var session = await host.Client.GetFromJsonAsync<JsonElement>(Root + "/session");
        Assert.False(session.GetProperty("mustChangePassword").GetBoolean());
        using var nowAllowed = await host.Client.GetAsync(Root + "/services");
        Assert.Equal(HttpStatusCode.OK, nowAllowed.StatusCode);
    }

    [Fact]
    public async Task ThePasswordChangeIsRefusedWhenTheCurrentOneIsWrong()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var password = (await host.CreateOfficer()).GetProperty("temporaryPassword").GetString()!;
        await host.Login("je.kadru", password);

        using var response = await host.Client.PostAsync(Root + "/password",
            Json(new { currentPassword = "not-the-password", newPassword = "Kadru#2026je" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var stillWorks = await host.Login("je.kadru", password);
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task DeactivatingAnOfficerStopsThemSigningIn()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var created = await host.CreateOfficer();
        var id = created.GetProperty("officer").GetProperty("id").GetString()!;
        var password = created.GetProperty("temporaryPassword").GetString()!;

        using var patch = await host.Client.PatchAsync(Root + "/officers/" + id, Json(new { active = false }));
        Assert.True(patch.IsSuccessStatusCode, $"{patch.StatusCode}: {await patch.Content.ReadAsStringAsync()}");

        using var signIn = await host.Login("je.kadru", password);
        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
    }

    [Fact]
    public async Task ResettingAPasswordIssuesANewOneAndForcesAChangeAgain()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        var created = await host.CreateOfficer();
        var id = created.GetProperty("officer").GetProperty("id").GetString()!;
        var first = created.GetProperty("temporaryPassword").GetString()!;
        await host.Login("je.kadru", first);
        await host.Client.PostAsync(Root + "/password", Json(new { currentPassword = first, newPassword = "Kadru#2026je" }));
        await host.Login("commissioner");

        var reset = await host.Client.PostAsync(Root + "/officers/" + id + "/password-reset", Json(new { }));
        Assert.True(reset.IsSuccessStatusCode, $"{reset.StatusCode}: {await reset.Content.ReadAsStringAsync()}");
        var issued = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString()!;

        Assert.NotEqual("Kadru#2026je", issued);
        using var signIn = await host.Login("je.kadru", issued);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var session = await host.Client.GetFromJsonAsync<JsonElement>(Root + "/session");
        Assert.True(session.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task ADuplicateUsernameIsRefused()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");
        await host.CreateOfficer();

        using var again = await host.Client.PostAsync(Root + "/officers",
            Json(new { username = "je.kadru", name = "Someone Else", designation = "AE", homeOfficeId = 2 }));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task AnImpossibleHomeAnchalIsRefused(int homeOfficeId)
    {
        await using var host = await OfficerHost.Create();
        await host.Login("commissioner");

        using var response = await host.Client.PostAsync(Root + "/officers",
            Json(new { username = "je.new", name = "A", designation = "JE", homeOfficeId }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AnOfficerCannotChangeAnotherAccountsPassword()
    {
        await using var host = await OfficerHost.Create();
        await host.Login("anchal1");

        using var response = await host.Client.PostAsync(Root + "/password",
            Json(new { currentPassword = "Api-contract-password-123!", newPassword = "Something#2026" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private sealed class OfficerHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public Task<HttpResponseMessage> Login(string username, string password = "Api-contract-password-123!") =>
            client.PostAsync(Root + "/login", Json(new { username, password }));

        public async Task<JsonElement> CreateOfficer(
            string username = "je.kadru", string name = "A. Kumar",
            string designation = "Junior Engineer", int homeOfficeId = 3)
        {
            using var response = await client.PostAsync(Root + "/officers",
                Json(new { username, name, designation, homeOfficeId }));
            Assert.True(response.IsSuccessStatusCode,
                $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        public static async Task<OfficerHost> Create()
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
            // One database name and root for the whole host: the lambda runs per scope.
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
                        .ToDictionary(x => x, _ => "Api-contract-password-123!"));
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var client = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() })
            {
                BaseAddress = new Uri(address)
            };
            client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
            return new(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
