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

    private static EntryInput Figure(int serviceId, string figure, long value = 1) =>
        Entry(x => (x with { ServiceId = serviceId }) switch
        {
            var y when figure == "received" => y with { Received = value },
            var y when figure == "completed" => y with { Completed = value },
            var y when figure == "slaWithinTime" => y with { SlaWithinTime = value },
            var y when figure == "amountPaise" => y with { AmountPaise = value },
            var y when figure == "complaintsReceived" => y with { ComplaintsReceived = value },
            var y when figure == "complaintsResolved" => y with { ComplaintsResolved = value },
            var y when figure == "fieldReceived" => y with { FieldReceived = value },
            var y => y with { FieldCompleted = value },
        });

    // Break caught: "the office said nothing happened" persisted next to a figure the office was
    // asked for, leaving downstream consumers to guess which side is true. Each row names the
    // service it applies to, because which figures count depends on the service's shape:
    // 1 and 12 counter (12 is grievance by kind but counter by shape), 2 revenue, 8 field work.
    [Theory]
    [InlineData(1, "received")]
    [InlineData(1, "completed")]
    [InlineData(12, "received")]
    [InlineData(12, "completed")]
    [InlineData(2, "received")]
    [InlineData(2, "amountPaise")]
    [InlineData(8, "completed")]
    public void NothingToReportWithACollectedFigureIsRejectedOnTheFlagField(int serviceId, string figure)
    {
        var ex = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(Figure(serviceId, figure), Today));
        Assert.Equal(422, ex.Status);
        Assert.True(ex.Error.FieldErrors!.ContainsKey("nothingToReport"));
    }

    // Break caught: the flag rejected because of a retired figure the form no longer shows. Those
    // keep their stored values through a correction, so "Set zero activity" on an old entry sends
    // them non-zero, and a 422 here names a field with nothing to highlight.
    [Theory]
    [InlineData(1, "slaWithinTime")]
    [InlineData(1, "complaintsReceived")]
    [InlineData(1, "amountPaise")]
    [InlineData(1, "fieldReceived")]
    [InlineData(12, "complaintsReceived")]
    [InlineData(12, "complaintsResolved")]
    [InlineData(12, "slaWithinTime")]
    [InlineData(2, "completed")]
    [InlineData(2, "slaWithinTime")]
    [InlineData(8, "received")]
    [InlineData(8, "slaWithinTime")]
    [InlineData(8, "amountPaise")]
    public void NothingToReportWithARetiredFigureIsAccepted(int serviceId, string figure) =>
        MonitoringRules.ValidateEntry(Figure(serviceId, figure), Today);

    [Fact]
    public void NothingToReportWithAllZerosIsAccepted() => MonitoringRules.ValidateEntry(Entry(), Today);

    [Fact]
    public void FiguresWithoutTheFlagAreStillAccepted() =>
        MonitoringRules.ValidateEntry(Entry(x => x with { NothingToReport = false, Received = 3 }), Today);
}
