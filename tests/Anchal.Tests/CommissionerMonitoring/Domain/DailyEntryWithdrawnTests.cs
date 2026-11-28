using System.Text.Json;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// Services 18-23 are hidden from the clerk's daily entry and 25 is removed from it. That is a decision,
/// not something derivable from a form shape, so it is a list: one in MonitoringRules, one in the frontend's
/// src/shared/daily-entry-withdrawn.json, both asserted against "dailyEntryWithdrawn" in the golden fixture.
public sealed class DailyEntryWithdrawnTests
{
    private static readonly DateOnly Today = new(2026, 11, 28);

    private static EntryInput Entry(int serviceId) =>
        new(serviceId, Today, "Ward 24", 0, 0, 0, 0, 0, 0, 0, 0, null, "");

    public static TheoryData<int> Withdrawn => new(18, 19, 20, 21, 22, 23, 25);

    [Theory]
    [MemberData(nameof(Withdrawn))]
    public void AnEntryForAWithdrawnServiceIsRefusedOnTheServiceAlone(int serviceId)
    {
        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(Entry(serviceId) with { Received = 5, Completed = 2 }, Today));

        Assert.Equal(422, error.Status);
        var only = Assert.Single(error.Error.FieldErrors!);
        Assert.Equal("serviceId", only.Key);
        Assert.Equal("This service is not reported on the daily entry", only.Value);
    }

    [Fact]
    public void AFieldWorkServiceKeepsItsOwnMessage()
    {
        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(Entry(8) with { Completed = 5 }, Today));

        Assert.Equal("Field work is reported by field officers, not on the daily entry",
            error.Error.FieldErrors!["serviceId"]);
    }

    [Fact]
    public void TheDailyEntryCollectsExactlyTheTen()
    {
        var collected = Enumerable.Range(1, MonitoringRules.ServiceCount)
            .Where(MonitoringRules.DailyEntryCollects).ToArray();

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 11, 12, 24 }, collected);
        Assert.Equal(10, MonitoringRules.DailyEntryServiceCount);
    }

    [Theory]
    [MemberData(nameof(Withdrawn))]
    public void AWithdrawnServiceKeepsItsCounterShape(int serviceId)
    {
        Assert.Equal(MonitoringFormShape.Counter, MonitoringRules.FormShape(serviceId));
        Assert.True(MonitoringRules.MovesPendingBalance(serviceId));
    }

    [Fact]
    public void TheWithdrawnSetsMatchTheGoldenTwinFixture()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "shape-twin.fixture.json")));
        var golden = doc.RootElement.GetProperty("dailyEntryWithdrawn");
        int[] Ids(string key) => golden.GetProperty(key).EnumerateArray().Select(x => x.GetInt32()).ToArray();

        Assert.Equal(Ids("hidden"), MonitoringRules.HiddenFromDailyEntry.Order().ToArray());
        Assert.Equal(Ids("removed"), MonitoringRules.RemovedFromDailyEntry.Order().ToArray());
    }
}
