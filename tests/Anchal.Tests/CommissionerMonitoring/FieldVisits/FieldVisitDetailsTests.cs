using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Officers;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Anchal.Tests.CommissionerMonitoring.FieldVisits.MonitoringTestHost;

namespace Anchal.Tests.CommissionerMonitoring.FieldVisits;

/// A visit's service-specific fields live in one nullable JSON column, {"v":1,...}. Writing is strict
/// (validated against the service's definition); reading never throws, whatever the stored text is.
public sealed class FieldVisitDetailsTests
{
    private const string Observation = "Fogging done along the main lane; two bylanes still pending.";

    private static string Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DateTime).ToString("yyyy-MM-dd");

    private static Dictionary<string, object?> Input(int serviceId, object? details = null, bool withDetails = true)
    {
        var input = new Dictionary<string, object?>
        {
            ["visitDate"] = Today(), ["visitTime"] = "00:01", ["officeId"] = 3, ["ward"] = 24, ["serviceId"] = serviceId,
            ["gps"] = new { latitude = 23.3441, longitude = 85.3096, accuracyMetres = 18, capturedAt = DateTimeOffset.UtcNow.ToString("O") },
            ["issueAction"] = Observation
        };
        if (withDetails) input["details"] = details;
        return input;
    }

    private static Task<HttpResponseMessage> Post(MonitoringTestHost host, object input, string? requestId = null) =>
        host.Client.PostAsync(Root + "/field-visits", Json(new { requestId = requestId ?? Guid.NewGuid().ToString("N"), input }));

    private static async Task<JsonElement> Rejected(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fieldErrors");
    }

    private static async Task<string?> StoredDetails(MonitoringTestHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        return (await db.FieldVisits.AsNoTracking().SingleAsync(x => x.Id == id)).DetailsJson;
    }

    [Fact]
    public async Task ValidDetailsAreStoredAsVersionOneAndReadBackWithEveryKey()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(8, new Dictionary<string, object>
        {
            ["vehicleLabour"] = "Tipper 4, two labourers", ["street"] = "Main lane", ["wasteType"] = "wet", ["quantityKg"] = 12.5
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        var details = visit.GetProperty("details");
        Assert.Equal("Tipper 4, two labourers", details.GetProperty("vehicleLabour").GetString());
        Assert.Equal("Main lane", details.GetProperty("street").GetString());
        Assert.Equal("wet", details.GetProperty("wasteType").GetString());
        Assert.Equal(12.5, details.GetProperty("quantityKg").GetDouble());
        Assert.False(details.TryGetProperty("v", out _));

        using var stored = JsonDocument.Parse((await StoredDetails(host, visit.GetProperty("id").GetGuid()))!);
        Assert.Equal(1, stored.RootElement.GetProperty("v").GetInt32());
        Assert.Equal(12.5, stored.RootElement.GetProperty("quantityKg").GetDouble());

        var list = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");
        Assert.Equal("wet", list.GetProperty("rows")[0].GetProperty("details").GetProperty("wasteType").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AVisitWithNoDetailsIsAcceptedAndReadsBackNull(bool sendTheKeyAsNull)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(8, null, withDetails: sendTheKeyAsNull));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, visit.GetProperty("details").ValueKind);
        Assert.Null(await StoredDetails(host, visit.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AnExplicitlyEmptyDetailsObjectIsAcceptedAndStoresNothing()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(9, new Dictionary<string, object>()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Null(await StoredDetails(host, visit.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AnEmptyOrAllBlankDetailsObjectIsTheSameAsNone()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(8, new Dictionary<string, object> { ["street"] = "   ", ["quantityKg"] = "" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, visit.GetProperty("details").ValueKind);
        Assert.Null(await StoredDetails(host, visit.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task AKeyTheServiceDoesNotDefineIsRefusedWithAFieldErrorNamingIt()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(8, new Dictionary<string, object> { ["street"] = "Main lane", ["dogsPicked"] = 3 }));

        var errors = await Rejected(response);
        Assert.Equal("This activity does not ask for this detail", errors.GetProperty("input.details.dogsPicked").GetString());
        Assert.False(errors.TryGetProperty("input.details.street", out _));
    }

    /// A visit is how field work is reported, and only field work.
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(11)] [InlineData(24)]
    public async Task ANewVisitForAServiceThatIsNotFieldWorkIsRefused(int serviceId)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(serviceId, withDetails: false));

        var errors = await Rejected(response);
        Assert.Equal("Only field-work activities are recorded as visits", errors.GetProperty("input.serviceId").GetString());
        var list = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)]
    public async Task ANewVisitForEachFieldWorkServiceIsStillAccepted(int serviceId)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await Post(host, Input(serviceId, withDetails: false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// Refusing new ones must not hide old ones: one such visit exists on staging.
    [Fact]
    public async Task AVisitAlreadyFiledForAnotherServiceIsStillListed()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        await PlantRow(host, officer.GetProperty("id").GetGuid(), null, serviceId: 1);

        var list = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");

        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("\"12kg\"")]
    // What the form sends when a typed number does not fit a double: the text as typed (M1).
    [InlineData("\"1e999\"")]
    [InlineData("\"-1e999\"")]
    [InlineData("1e999")]
    [InlineData("-1")]
    [InlineData("1000000001")]
    [InlineData("true")]
    [InlineData("[1]")]
    public async Task ANumberFieldThatIsNotANumberWithinTheEntryBoundIsRefused(string literal)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, 8, $@"{{""quantityKg"":{literal}}}");

        var errors = await Rejected(response);
        Assert.Equal("Enter a number from 0 to 1,000,000,000", errors.GetProperty("input.details.quantityKg").GetString());
    }

    [Theory]
    [InlineData(10, "dogsPicked", "2.5")]
    [InlineData(10, "dogsNeutered", "\"0.5\"")]
    [InlineData(16, "treeCount", "3.2")]
    public async Task ACountThatIsNotAWholeNumberIsRefusedOnItsOwnField(int serviceId, string key, string literal)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, serviceId, $@"{{""{key}"":{literal}}}");

        var errors = await Rejected(response);
        Assert.Equal("Enter a whole number from 0 to 1,000,000,000", errors.GetProperty("input.details." + key).GetString());
    }

    [Theory]
    [InlineData(10, "dogsPicked", "7", 7)]
    [InlineData(10, "dogsNeutered", "\"4\"", 4)]
    [InlineData(16, "treeCount", "0", 0)]
    [InlineData(16, "treeCount", "3.0", 3)]
    public async Task ACountTakesAWholeNumber(int serviceId, string key, string literal, long expected)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, serviceId, $@"{{""{key}"":{literal}}}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("details");
        Assert.Equal(expected, stored.GetProperty(key).GetInt64());
    }

    [Theory]
    [InlineData("0", 0.0)]
    [InlineData("1000000000", 1000000000.0)]
    [InlineData("\"12.5\"", 12.5)]
    public async Task ANumberFieldTakesANumberOrANumericStringWithinTheBound(string literal, double expected)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, 8, $@"{{""quantityKg"":{literal}}}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var visit = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, visit.GetProperty("details").GetProperty("quantityKg").GetDouble());
    }

    [Theory]
    [InlineData("\"liquid\"")]
    [InlineData("\"WET\"")]
    [InlineData("1")]
    public async Task ASelectFieldAcceptsOnlyItsDefinedOptions(string literal)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, 8, $@"{{""wasteType"":{literal}}}");

        var errors = await Rejected(response);
        Assert.Equal("Select one of the listed options", errors.GetProperty("input.details.wasteType").GetString());
    }

    [Fact]
    public async Task EveryDefinedOptionOfEverySelectIsAccepted()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        foreach (var serviceId in new[] { 8, 13, 14 })
            foreach (var field in MonitoringFieldDefinitions.For(serviceId).Where(x => x.Type == "select"))
                foreach (var option in field.Options!)
                {
                    using var response = await PostRaw(host, serviceId, $@"{{""{field.Key}"":""{option}""}}");
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                }
    }

    [Fact]
    public async Task ATextFieldIsBoundedAndTheBoundIsInclusive()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var max = MonitoringFieldVisitService.DetailsTextMaximumLength;

        using var atLimit = await Post(host, Input(9, new Dictionary<string, object> { ["team"] = new string('क', max) }));
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);

        using var over = await Post(host, Input(9, new Dictionary<string, object> { ["team"] = new string('क', max + 1) }));
        var errors = await Rejected(over);
        Assert.Equal($"Shorten this to {max} characters or fewer", errors.GetProperty("input.details.team").GetString());
    }

    [Fact]
    public async Task ATextFieldGivenANumberOrAnObjectIsRefused()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, 9, @"{""team"":5,""route"":{""a"":1}}");

        var errors = await Rejected(response);
        Assert.True(errors.TryGetProperty("input.details.team", out _));
        Assert.True(errors.TryGetProperty("input.details.route", out _));
    }

    [Fact]
    public async Task EveryBadFieldIsReportedAtOnce()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();

        using var response = await PostRaw(host, 8, @"{""wasteType"":""x"",""quantityKg"":""y"",""nope"":""z""}");

        var errors = await Rejected(response);
        Assert.Equal(3, errors.EnumerateObject().Count(p => p.Name.StartsWith("input.details.")));
    }

    [Fact]
    public async Task DetailsThatAreNotAnObjectAreRefusedAsAFieldError()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var body = $@"{{""requestId"":""{Guid.NewGuid():N}"",""input"":{{""visitDate"":""{Today()}"",""visitTime"":""00:01"",
            ""officeId"":3,""ward"":24,""serviceId"":8,""details"":""street=Main"",
            ""gps"":{{""latitude"":23.3,""longitude"":85.3,""accuracyMetres"":18,""capturedAt"":""{DateTimeOffset.UtcNow:O}""}},
            ""issueAction"":""{Observation}""}}}}";

        using var response = await host.Client.PostAsync(Root + "/field-visits", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
    }

    /// Idempotency is untouched: a request filed before this column existed hashed an input with no
    /// details member, and its retry after the deploy must still be recognised, not answered 409.
    [Fact]
    public async Task ARequestHashedBeforeDetailsExistedStillReplaysRatherThanConflicting()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        var officerId = officer.GetProperty("id").GetGuid();
        var captured = DateTimeOffset.UtcNow;
        var gps = new GpsReadingDto(23.3441, 85.3096, 18, captured);
        var legacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { VisitDate = DateOnly.Parse(Today()), VisitTime = "00:01", OfficeId = 3, Ward = 24, ServiceId = 9, Gps = gps, IssueAction = Observation },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var requestId = "legacy-" + Guid.NewGuid().ToString("N");
        var legacyId = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
            var office = await db.Offices.SingleAsync(x => x.Code == "anchal3");
            db.FieldVisits.Add(new MonitoringFieldVisit
            {
                Id = legacyId, RequestId = requestId, Fingerprint = legacyHash, OfficeId = office.Id, OfficerUserId = officerId,
                Ward = 24, ServiceId = 9, VisitDate = DateOnly.Parse(Today()), VisitTime = new(0, 1),
                Latitude = gps.Latitude, Longitude = gps.Longitude, AccuracyMetres = 18, GpsCapturedAt = captured,
                ReceivedAt = captured, IssueAction = Observation
            });
            await db.SaveChangesAsync();
        }
        var input = Input(9, withDetails: false);
        input["gps"] = new { latitude = gps.Latitude, longitude = gps.Longitude, accuracyMetres = 18.0, capturedAt = captured };

        using var replay = await Post(host, input, requestId);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var visit = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(legacyId, visit.GetProperty("id").GetGuid());
    }

    // ---- idempotency with details: the case this column introduced ------------------------------
    // The fingerprint is the hash of the serialised input, and Details is left out of it while null
    // (so a legacy request still replays). These pin the other side: once details are present they are
    // part of what a request id stands for.

    private static async Task<int> RowsFor(MonitoringTestHost host, string requestId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        return await db.FieldVisits.AsNoTracking().CountAsync(x => x.RequestId == requestId);
    }

    private static Dictionary<string, object> Fogging() => new() { ["vehicleLabour"] = "Tipper 4, two labourers", ["street"] = "Main lane", ["wasteType"] = "wet", ["quantityKg"] = 12.5 };

    [Fact]
    public async Task ReplayingAVisitThatCarriesDetailsReturnsTheOriginalWithTheDetailsIntactAndStoresNoSecondRow()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var requestId = Guid.NewGuid().ToString("N");
        // One input object, built once: Input() stamps the capture time when it is called.
        var input = Input(8, Fogging());

        using var first = await Post(host, input, requestId);
        using var replay = await Post(host, input, requestId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var one = await first.Content.ReadFromJsonAsync<JsonElement>();
        var two = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(one.GetProperty("id").GetGuid(), two.GetProperty("id").GetGuid());
        Assert.Equal(one.GetProperty("details").GetRawText(), two.GetProperty("details").GetRawText());
        Assert.Equal("Main lane", two.GetProperty("details").GetProperty("street").GetString());
        Assert.Equal(12.5, two.GetProperty("details").GetProperty("quantityKg").GetDouble());
        Assert.Equal(1, await RowsFor(host, requestId));
        var list = await host.Client.GetFromJsonAsync<JsonElement>(Root + $"/field-visits?from={Today()}&to={Today()}");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }

    /// The conflict path is the one every other changed field takes: the fingerprint is compared before
    /// anything is validated, so the answer is 409 CONFLICT "already used for different data" — even when
    /// the changed details would themselves be refused with a 422 — and the stored visit is not touched.
    public static TheoryData<string, Func<Dictionary<string, object?>>> ChangedDetails() => new()
    {
        { "a value changed", () => Input(8, new Dictionary<string, object> { ["vehicleLabour"] = "Tipper 4, two labourers", ["street"] = "Other lane", ["wasteType"] = "wet", ["quantityKg"] = 12.5 }) },
        { "a figure changed", () => Input(8, new Dictionary<string, object> { ["vehicleLabour"] = "Tipper 4, two labourers", ["street"] = "Main lane", ["wasteType"] = "wet", ["quantityKg"] = 13 }) },
        { "a key added", () => Input(8, new Dictionary<string, object> { ["vehicleLabour"] = "Tipper 4, two labourers", ["street"] = "Main lane", ["wasteType"] = "wet", ["quantityKg"] = 12.5, ["junk"] = "x" }) },
        { "a key removed", () => Input(8, new Dictionary<string, object> { ["street"] = "Main lane" }) },
        { "the details dropped", () => Input(8, null, withDetails: false) },
    };

    [Theory]
    [MemberData(nameof(ChangedDetails))]
    public async Task ReusingARequestIdWithDifferentDetailsIsRefusedAsConflictAndTheStoredVisitIsUntouched(
        string _, Func<Dictionary<string, object?>> changed)
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var requestId = Guid.NewGuid().ToString("N");
        var original = Input(8, Fogging());
        using var first = await Post(host, original, requestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var stored = await first.Content.ReadFromJsonAsync<JsonElement>();
        var id = stored.GetProperty("id").GetGuid();
        var storedText = await StoredDetails(host, id);

        // The retry differs only in its details: same time, same capture, same everything else.
        var retry = changed();
        retry["visitDate"] = original["visitDate"];
        retry["gps"] = original["gps"];
        using var response = await Post(host, retry, requestId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CONFLICT", body.GetProperty("code").GetString());
        Assert.Equal("This request identifier was already used for different data", body.GetProperty("message").GetString());
        Assert.Equal(1, await RowsFor(host, requestId));
        Assert.Equal(storedText, await StoredDetails(host, id));
    }

    [Fact]
    public async Task ReusingARequestIdThatFiledNoDetailsForOneThatCarriesThemIsAlsoAConflict()
    {
        await using var host = await Create();
        await host.SignInAsOfficer();
        var requestId = Guid.NewGuid().ToString("N");
        var plain = Input(8, null, withDetails: false);
        using var first = await Post(host, plain, requestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var withDetails = Input(8, Fogging());
        withDetails["gps"] = plain["gps"];
        using var retry = await Post(host, withDetails, requestId);

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(1, await RowsFor(host, requestId));
        Assert.Null(await StoredDetails(host, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid()));
    }

    // ---- the read side: whatever is stored, listing never throws -------------------------------

    private static async Task<HttpResponseMessage> PostRaw(MonitoringTestHost host, int serviceId, string detailsJson)
    {
        var body = $@"{{""requestId"":""{Guid.NewGuid():N}"",""input"":{{""visitDate"":""{Today()}"",""visitTime"":""00:01"",
            ""officeId"":3,""ward"":24,""serviceId"":{serviceId},""details"":{detailsJson},
            ""gps"":{{""latitude"":23.3,""longitude"":85.3,""accuracyMetres"":18,""capturedAt"":""{DateTimeOffset.UtcNow:O}""}},
            ""issueAction"":""{Observation}""}}}}";
        return await host.Client.PostAsync(Root + "/field-visits", new StringContent(body, Encoding.UTF8, "application/json"));
    }

    private static async Task PlantRow(MonitoringTestHost host, Guid officerId, string? detailsJson, int serviceId = 8)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommissionerMonitoringDbContext>();
        var office = await db.Offices.SingleAsync(x => x.Code == "anchal3");
        var now = DateTimeOffset.UtcNow;
        db.FieldVisits.Add(new MonitoringFieldVisit
        {
            Id = Guid.NewGuid(), RequestId = "plant-" + Guid.NewGuid().ToString("N"), Fingerprint = "plant",
            OfficeId = office.Id, OfficerUserId = officerId, Ward = 24, ServiceId = serviceId,
            VisitDate = MonitoringRules.Today(now), VisitTime = new(0, 1), Latitude = 23.3, Longitude = 85.3,
            AccuracyMetres = 18, GpsCapturedAt = now, ReceivedAt = now, IssueAction = Observation, DetailsJson = detailsJson
        });
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> ListSingle(MonitoringTestHost host)
    {
        using var response = await host.Client.GetAsync(Root + $"/field-visits?from={Today()}&to={Today()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<JsonElement>();
        return list.GetProperty("rows").EnumerateArray().Single();
    }

    // Review focus: a row filed before this column existed. It must open and list, not throw.
    [Fact]
    public async Task ARowWithNullDetailsListsWithNullDetails()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        await PlantRow(host, officer.GetProperty("id").GetGuid(), null);

        var row = await ListSingle(host);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("details").ValueKind);
    }

    // Review focus, and the anti-lesson from the entry snapshot: an unexpected version degrades, it does not throw.
    [Fact]
    public async Task AStoredVersionTwoRowIsListedWithWhatCanBeReadAndDoesNotThrow()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        await PlantRow(host, officer.GetProperty("id").GetGuid(),
            @"{""v"":2,""street"":""Main lane"",""quantityKg"":7,""wasteType"":{""kind"":""wet""},""extra"":[1,2],""flag"":true,""gone"":null}");

        var row = await ListSingle(host);

        var details = row.GetProperty("details");
        Assert.Equal("Main lane", details.GetProperty("street").GetString());
        Assert.Equal(7, details.GetProperty("quantityKg").GetInt32());
        Assert.False(details.TryGetProperty("wasteType", out _));
        Assert.False(details.TryGetProperty("extra", out _));
        Assert.False(details.TryGetProperty("flag", out _));
        Assert.False(details.TryGetProperty("gone", out _));
        Assert.False(details.TryGetProperty("v", out _));
    }

    // The list endpoint rewrites officeId/input keys anywhere in the payload; a stored details key of
    // that name must never reach it, or one odd row would turn the whole list into a 500.
    [Fact]
    public async Task AStoredKeyTheListAdapterTreatsSpeciallyIsDroppedRatherThanBreakingTheList()
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        await PlantRow(host, officer.GetProperty("id").GetGuid(),
            $@"{{""v"":2,""officeId"":""{Guid.NewGuid()}"",""officeCode"":""x"",""input"":""y"",""street"":""Main lane""}}");

        var row = await ListSingle(host);

        var details = row.GetProperty("details");
        Assert.Equal("Main lane", details.GetProperty("street").GetString());
        Assert.Single(details.EnumerateObject());
    }

    // M6: every key the adapter rewrites is reserved, and a stored details key of that name is not read.
    public static TheoryData<string> ReservedKeys() => new(MonitoringPayloadKeys.All.ToArray());

    [Theory]
    [MemberData(nameof(ReservedKeys))]
    public void ReadDetailsDropsEveryKeyTheAdapterRewrites(string key)
    {
        var read = MonitoringFieldVisitService.ReadDetails($@"{{""v"":1,""{key}"":""x"",""street"":""Main lane""}}");
        Assert.NotNull(read);
        Assert.False(read!.ContainsKey(key));
        Assert.Equal("Main lane", read["street"]);
    }

    // The set is the adapter's own constants (it rewrites by MonitoringPayloadKeys.*, not by literal),
    // so a key added there without joining All is caught here, by reflection over the class.
    [Fact]
    public void EveryNameDeclaredAsRewrittenIsInTheReservedSet()
    {
        var declared = typeof(MonitoringPayloadKeys).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(declared, MonitoringPayloadKeys.All.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "input", "officeCode", "officeId" }, declared);
    }

    // M4: a select whose option list is missing is a broken definition. It must come out as a 422 on that
    // field, as every other refusal does, not as a NullReferenceException from inside the request.
    [Fact]
    public void ASelectDefinitionWithNoOptionListRefusesTheValueRatherThanThrowing()
    {
        var input = new FieldVisitInputDto(DateOnly.Parse("2026-11-27"), "10:00", 3, 24, 8,
            new GpsReadingDto(23.3, 85.3, 10, DateTimeOffset.UtcNow), Observation)
        {
            Details = new Dictionary<string, JsonElement> { ["wasteType"] = JsonSerializer.SerializeToElement("wet") }
        };
        var broken = new[] { new VisitFieldDefinition("wasteType", "select", null) };

        var thrown = Assert.Throws<MonitoringRuleException>(() => MonitoringFieldVisitService.ValidateDetails(input, broken));

        Assert.Equal(422, thrown.Status);
        Assert.Equal("Select one of the listed options", thrown.Error.FieldErrors!["input.details.wasteType"]);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData(@"{""v"":1}")]
    [InlineData(@"{""v"":""one"",""street"":null}")]
    public async Task UnreadableStoredDetailsListAsNullWithoutThrowing(string stored)
    {
        await using var host = await Create();
        var officer = await host.SignInAsOfficer();
        await PlantRow(host, officer.GetProperty("id").GetGuid(), stored);

        var row = await ListSingle(host);

        Assert.Equal(JsonValueKind.Null, row.GetProperty("details").ValueKind);
    }

    [Theory]
    [InlineData(@"{""street"":""Main lane""}", "street", "Main lane")]
    [InlineData(@"{""v"":99,""street"":""Main lane""}", "street", "Main lane")]
    [InlineData(@"{""v"":""x"",""street"":""Main lane"",""deep"":{""a"":1}}", "street", "Main lane")]
    [InlineData(@"{""v"":1,""street"":""Main lane""} trailing", null, null)]
    public void TheReaderReadsWhatItCanForAnyVersionAndNeverThrows(string stored, string? key, string? expected)
    {
        var read = MonitoringFieldVisitService.ReadDetails(stored);

        if (key is null) { Assert.Null(read); return; }
        Assert.NotNull(read);
        Assert.Equal(expected, read![key]);
    }

    [Fact]
    public void TheReaderSurvivesAnAbsurdValueWithoutThrowing()
    {
        var read = MonitoringFieldVisitService.ReadDetails(@"{""v"":2,""quantityKg"":1e999,""big"":123456789012345678901234567890,""street"":""ok""}");

        Assert.NotNull(read);
        Assert.Equal("ok", read!["street"]);
        Assert.False(read.ContainsKey("quantityKg"));
    }
}
