using System.Text.Json;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// "Nothing to report" is a deliberate, per-service statement by the office. It
/// is stored inside the entry JSON of the audit snapshot, whose SchemaVersion
/// stays 1, so the field must be purely additive: every snapshot written before
/// it existed has to keep reading, as false.
public sealed class NothingToReportTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Shape of an entry snapshot as the code before this field wrote it: the
    // application DTO nests the input, and there is no nothingToReport key.
    private const string OldSnapshot = """
        {"id":"11111111-1111-1111-1111-111111111111","officeId":"22222222-2222-2222-2222-222222222222","version":1,
         "input":{"serviceId":1,"serviceDate":"2026-11-01","ward":"24","received":0,"completed":0,
           "slaWithinTime":0,"amountPaise":0,"complaintsReceived":0,"complaintsResolved":0,
           "fieldReceived":0,"fieldCompleted":0,"avgDisposalDays":null,"remarks":"","isZeroActivity":true},
         "reportingDate":"2026-11-01","createdAt":"2026-11-01T10:00:00+00:00","updatedAt":"2026-11-01T10:00:00+00:00",
         "opening":{"service":0,"complaints":0,"field":0},"closing":{"service":0,"complaints":0,"field":0}}
        """;

    // Break caught: a snapshot with no nothingToReport property failing to read, or reading as true,
    // would break every historical entry because all reads go through Snapshot().
    [Fact]
    public void AnEntryFiledBeforeThisFieldExistedReadsAsFalse()
    {
        var dto = JsonSerializer.Deserialize<EntryDto>(OldSnapshot, Web)!;
        Assert.NotNull(dto.Input);
        Assert.Equal(1, dto.Input.ServiceId);
        Assert.False(dto.Input.NothingToReport);
    }

    // Break caught: the same property sitting directly on the input, not only inside an entry.
    [Fact]
    public void AnInputWithoutTheFieldReadsAsFalse()
    {
        var json = """
            {"serviceId":1,"serviceDate":"2026-11-01","ward":"24","received":0,"completed":0,
            "slaWithinTime":0,"amountPaise":0,"complaintsReceived":0,"complaintsResolved":0,
            "fieldReceived":0,"fieldCompleted":0,"avgDisposalDays":null,"remarks":""}
            """;
        Assert.False(JsonSerializer.Deserialize<EntryInput>(json, Web)!.NothingToReport);
    }

    // Break caught: the flag being dropped or renamed on the way out and back in.
    [Fact]
    public void TheFlagSurvivesASerializeDeserializeRoundTrip()
    {
        var input = new EntryInput(1, new(2026, 11, 1), "24", 0, 0, 0, 0, 0, 0, 0, 0, null, "", NothingToReport: true);
        var json = JsonSerializer.Serialize(input, Web);
        Assert.Contains("\"nothingToReport\":true", json);
        Assert.True(JsonSerializer.Deserialize<EntryInput>(json, Web)!.NothingToReport);
    }

    // Break caught: the field being named like the key the HTTP adapter strips (isZeroActivity).
    [Fact]
    public void TheFieldIsNotNamedLikeTheKeyTheHttpAdapterStrips()
    {
        var names = typeof(EntryInput).GetConstructors().Single().GetParameters().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("IsZeroActivity", names, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("NothingToReport", names[^1]);
    }

    private static readonly DateOnly Today = new(2026, 11, 20);
    private static EntryInput Entry(Func<EntryInput, EntryInput>? change = null) =>
        (change ?? (x => x))(new EntryInput(1, Today, "24", 0, 0, 0, 0, 0, 0, 0, 0, null, "", NothingToReport: true));

    // Break caught: "the office said nothing happened" persisted next to figures, leaving downstream
    // consumers to guess which side is true.
    [Theory]
    [InlineData("received")]
    [InlineData("completed")]
    [InlineData("slaWithinTime")]
    [InlineData("amountPaise")]
    [InlineData("complaintsReceived")]
    [InlineData("complaintsResolved")]
    [InlineData("fieldReceived")]
    [InlineData("fieldCompleted")]
    public void NothingToReportWithAnyFigureIsRejectedOnTheFlagField(string figure)
    {
        var input = Entry(x => figure switch
        {
            "received" => x with { Received = 1 },
            "completed" => x with { Completed = 1 },
            "slaWithinTime" => x with { SlaWithinTime = 1 },
            "amountPaise" => x with { AmountPaise = 1 },
            "complaintsReceived" => x with { ComplaintsReceived = 1 },
            "complaintsResolved" => x with { ComplaintsResolved = 1 },
            "fieldReceived" => x with { FieldReceived = 1 },
            _ => x with { FieldCompleted = 1 },
        });
        var ex = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(input, Today));
        Assert.Equal(422, ex.Status);
        Assert.True(ex.Error.FieldErrors!.ContainsKey("nothingToReport"));
    }

    [Fact]
    public void NothingToReportWithAllZerosIsAccepted() => MonitoringRules.ValidateEntry(Entry(), Today);

    [Fact]
    public void FiguresWithoutTheFlagAreStillAccepted() =>
        MonitoringRules.ValidateEntry(Entry(x => x with { NothingToReport = false, Received = 3 }), Today);
}
