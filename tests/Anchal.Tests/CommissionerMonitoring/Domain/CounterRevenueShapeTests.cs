using System.Text.Json;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// The fourth form shape: a queue and money together (services 1, 3, 4, 7).
/// TWIN: tests/domain/service-kind.test.ts and shape-twin.test.ts in the
/// frontend repo, which assert the same facts about the same ids.
public sealed class CounterRevenueShapeTests
{
    private static readonly DateOnly Today = new(2026, 11, 27);

    public static TheoryData<int> CounterRevenue => new(1, 3, 4, 7);

    private static EntryInput Entry(int serviceId) =>
        new(serviceId, Today, "Ward 24", 0, 0, 0, 0, 0, 0, 0, 0, null, "");

    [Theory]
    [MemberData(nameof(CounterRevenue))]
    public void TheFourServicesTakeTheCounterRevenueShape(int id)
    {
        Assert.Equal(MonitoringFormShape.CounterRevenue, MonitoringRules.FormShape(id));
        Assert.Equal(new[] { "received", "completed", "amountPaise" }, MonitoringRules.CollectedFields(id));
        Assert.True(MonitoringRules.MovesPendingBalance(id));
        Assert.True(MonitoringRules.Collects(id, "completed", "received"));
        Assert.True(MonitoringRules.Collects(id, "amountPaise"));
    }

    [Fact]
    public void EveryOtherServiceKeepsTheShapeItHad()
    {
        foreach (var id in new[] { 11, 12, 18, 19, 20, 21, 22, 23, 24, 25 })
            Assert.Equal(MonitoringFormShape.Counter, MonitoringRules.FormShape(id));
        foreach (var id in new[] { 2, 5, 6 })
            Assert.Equal(MonitoringFormShape.Revenue, MonitoringRules.FormShape(id));
        foreach (var id in new[] { 8, 9, 10, 13, 14, 15, 16, 17 })
            Assert.Equal(MonitoringFormShape.FieldWork, MonitoringRules.FormShape(id));
    }

    [Theory]
    [MemberData(nameof(CounterRevenue))]
    public void CompletedCannotExceedReceivedEvenWithAnAmount(int id)
    {
        var entry = Entry(id) with { Received = 2, Completed = 5, AmountPaise = 50_000 };

        var error = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(entry, Today));

        Assert.Equal(new[] { "completed" }, error.Error.FieldErrors!.Keys);
    }

    [Theory]
    [MemberData(nameof(CounterRevenue))]
    public void NothingToReportIsRefusedWithOnlyAnAmountTyped(int id)
    {
        var entry = Entry(id) with { NothingToReport = true, AmountPaise = 100 };

        var error = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(entry, Today));

        Assert.Equal(new[] { "nothingToReport" }, error.Error.FieldErrors!.Keys);
    }

    [Theory]
    [MemberData(nameof(CounterRevenue))]
    public void TheBalanceMovesByReceivedMinusCompletedAndIgnoresTheAmount(int id)
    {
        var derived = MonitoringRules.Derive(new Balances(7, 0, 0),
            [Entry(id) with { Received = 40, Completed = 15, AmountPaise = 123_400 }]);

        Assert.Equal(32, derived[0].Closing.Service);
    }

    // The frontend's FormShape spelling, which the golden table uses.
    private static string ShapeName(MonitoringFormShape shape) => shape switch
    {
        MonitoringFormShape.Counter => "counter",
        MonitoringFormShape.CounterRevenue => "counterRevenue",
        MonitoringFormShape.Revenue => "revenue",
        MonitoringFormShape.FieldWork => "fieldwork",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    // The twin cross-check. The same golden table is committed in the frontend
    // repo (tests/domain/shape-twin.fixture.json); the frontend test asserts its
    // own tables against it and, when this repo is checked out beside it, that
    // the two copies are byte-identical. Order of the collected fields counts.
    [Fact]
    public void TheShapeTablesMatchTheGoldenTwinFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "shape-twin.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var shapes = doc.RootElement.GetProperty("shapes");
        Assert.Contains(shapes.EnumerateObject(), p => p.Name == "1");

        foreach (var entry in shapes.EnumerateObject())
        {
            var id = int.Parse(entry.Name);
            Assert.Equal(entry.Value.GetProperty("shape").GetString(), ShapeName(MonitoringRules.FormShape(id)));
            Assert.Equal(
                entry.Value.GetProperty("collected").EnumerateArray().Select(e => e.GetString()!).ToArray(),
                MonitoringRules.CollectedFields(id));
            Assert.Equal(entry.Value.GetProperty("movesPendingBalance").GetBoolean(), MonitoringRules.MovesPendingBalance(id));
        }
    }
}
