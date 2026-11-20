using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

public sealed class MonitoringRulesTests
{
    [Theory]
    [InlineData("2026-10-05T18:29:59Z", "2026-10-05")]
    [InlineData("2026-10-05T18:30:00Z", "2026-10-06")]
    public void ReportingDateUsesKolkataMidnight(string instant, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), MonitoringRules.Today(DateTimeOffset.Parse(instant)));

    [Theory]
    [InlineData("today", "2026-10-05")]
    [InlineData("backdate", "2026-10-06")]
    [InlineData("backdate", "2026-09-30")]
    [InlineData("other", "2026-10-06")]
    public void RejectsDatesOutsideRoute(string mode, string date) =>
        Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateRoute(mode,
            DateOnly.Parse(date), new(2026, 10, 1), new(2026, 10, 6)));

    [Fact]
    public void DerivesBalancesWithoutMutatingInitialBalances()
    {
        var initial = new Balances(10, 3, 5);
        var input = Entry(new(2026, 10, 6)) with { Received = 2, Completed = 4 };

        var derived = Assert.Single(MonitoringRules.Derive(initial, [input]));

        Assert.Equal(new Balances(8, 3, 5), derived.Closing);
        Assert.Equal(new Balances(10, 3, 5), initial);
    }

    [Fact]
    public void RejectsInvalidCounts()
    {
        foreach (var input in new[] {
            Entry(new(2026, 10, 6)) with { Received = -1 } })
            Assert.NotEmpty(Assert.Throws<MonitoringRuleException>(() =>
                MonitoringRules.ValidateEntry(input, new(2026, 10, 6))).Error.FieldErrors!);
    }

    /// The complaint and field-task tracks were dropped from this test when the
    /// form stopped collecting them: a cross-field rule whose operands the
    /// office is never asked for can only dead-end a correction (see
    /// HiddenFieldCrossRuleTests). The counter track is unchanged.
    [Fact]
    public void RejectsCompletionsAboveSameDayReceiptsOnTheCounterTrack()
    {
        var input = Entry(new(2026, 10, 6)) with { Received = 1, Completed = 2, SlaWithinTime = 0, AvgDisposalDays = 1 };

        Assert.Contains("completed", Assert.Throws<MonitoringRuleException>(() =>
            MonitoringRules.ValidateEntry(input, new(2026, 10, 6))).Error.FieldErrors!.Keys);

        MonitoringRules.ValidateEntry(Entry(new(2026, 10, 6)) with { Received = 2, Completed = 2, SlaWithinTime = 2, AvgDisposalDays = 1 }, new(2026, 10, 6));
    }

    private static EntryInput Entry(DateOnly date) => new(1, date, "1", 0, 0, 0, 0, 0, 0, 0, 0, null, "");
}
