using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// Field work is not a queue. Grass is cut, a route is fogged, a street light
/// is repaired — often with no request behind it. Two rules written for counter
/// work made that impossible to record honestly: "completed cannot exceed
/// received" forced the office to invent requests, and the average-disposal-days
/// requirement forced a number where there is no case to dispose of.
public sealed class FieldWorkEntryRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    private static EntryInput Entry(int serviceId) =>
        new(serviceId, Today, "Ward 24", 0, 0, 0, 0, 0, 0, 0, 0, null, "");

    // Door-to-door vehicle, fogging, street dog, street lighting, road and
    // drain, grass cutting, tree pruning, water body cleaning.
    public static TheoryData<int> FieldWork => new(8, 9, 10, 13, 14, 15, 16, 17);

    // Counter work (counter and counterRevenue shapes): everything that is neither field work nor revenue (2, 5, 6).
    public static TheoryData<int> CounterShape =>
        new(1, 3, 4, 7, 11, 12, 18, 19, 20, 21, 22, 23, 24, 25);

    /// Work done with no request behind it is how field work is recorded, and it is recorded by field
    /// officers as visits now. The daily entry refuses it: see DailyEntryCollectsTests.
    [Theory]
    [MemberData(nameof(FieldWork))]
    public void WorkDoneIsNoLongerAcceptedOnTheDailyEntry(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = 5, Received = 0 };

        var error = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(entry, Today));

        Assert.Equal(new[] { "serviceId" }, error.Error.FieldErrors!.Keys);
    }

    /// Was asserted for all 17 non-field-work services; the three revenue
    /// services left it when the revenue shape stopped collecting a disposal
    /// figure — the rule's `completed` operand is not a question the office is
    /// asked there, so enforcing it only dead-ends a correction.
    [Theory]
    [MemberData(nameof(CounterShape))]
    public void CounterWorkStillCannotFinishMoreThanItReceived(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = 5, Received = 0, AvgDisposalDays = 2 };

        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(entry, Today));

        Assert.True(error.Error.FieldErrors!.ContainsKey("completed"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(24)]
    public void ACounterEntryWithNoAverageDaysIsAccepted(int serviceId)
    {
        var entry = new EntryInput(serviceId, Today, "Ward 24", 5, 3, 0, 0, 0, 0, 0, 0, null, "");

        MonitoringRules.ValidateEntry(entry, Today);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ASuppliedAverageDaysMustStillBeFiniteAndInRange(double days)
    {
        var entry = Entry(1) with { Received = 5, Completed = 3, AvgDisposalDays = days };

        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(entry, Today));

        Assert.True(error.Error.FieldErrors!.ContainsKey("avgDisposalDays"));
    }

    [Fact]
    public void ASuppliedInRangeAverageDaysIsStillAccepted()
    {
        MonitoringRules.ValidateEntry(Entry(1) with { Received = 5, Completed = 3, AvgDisposalDays = 2.5 }, Today);
    }

    [Fact]
    public void FieldWorkIsExactlyTheEightServicesTheServiceMasterCallsFieldWork()
    {
        var classified = Enumerable.Range(1, 25).Where(MonitoringRules.IsFieldWork).ToArray();

        Assert.Equal(new[] { 8, 9, 10, 13, 14, 15, 16, 17 }, classified);
    }

    /// ValidateEntry accepting the entry is not enough: Derive computes the
    /// pending balance on the same input, and a service balance that goes
    /// negative is rejected there and by ck_monitoring_closing_nonnegative in
    /// the database. Field work has no queue, so it must not move that balance
    /// at all.
    [Theory]
    [MemberData(nameof(FieldWork))]
    public void WorkDoneDoesNotMoveThePendingBalance(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = 5, Received = 0 };

        var derived = Assert.Single(MonitoringRules.Derive(new Balances(0, 0, 0), [entry]));

        Assert.Equal(new Balances(0, 0, 0), derived.Closing);
    }

    [Theory]
    [MemberData(nameof(FieldWork))]
    public void WorkDoneLeavesAnExistingBalanceUntouched(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = 5, Received = 2 };

        var derived = Assert.Single(MonitoringRules.Derive(new Balances(7, 0, 0), [entry]));

        Assert.Equal(7, derived.Closing.Service);
    }

    [Theory]
    [MemberData(nameof(CounterShape))]
    public void CounterWorkStillMovesThePendingBalance(int serviceId)
    {
        var entry = Entry(serviceId) with { Received = 5, Completed = 2, AvgDisposalDays = 1 };

        var derived = Assert.Single(MonitoringRules.Derive(new Balances(0, 0, 0), [entry]));

        Assert.Equal(3, derived.Closing.Service);
    }

    [Theory]
    [MemberData(nameof(FieldWork))]
    public void FieldWorkStillRefusesImpossibleNumbers(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = -1 };

        Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(entry, Today));
    }

    [Fact] public void GrievanceServiceTakesTheCounterShapeAndMovesTheBalance() {
        Assert.Equal(MonitoringFormShape.Counter, MonitoringRules.FormShape(12));
        Assert.True(MonitoringRules.MovesPendingBalance(12)); }

    [Fact] public void ShapesCoverAllTwentyFiveServicesWithTheExpectedCounts() {
        var byShape = Enumerable.Range(1, 25).GroupBy(MonitoringRules.FormShape)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(10, byShape[MonitoringFormShape.Counter]);
        Assert.Equal(4, byShape[MonitoringFormShape.CounterRevenue]);
        Assert.Equal(3, byShape[MonitoringFormShape.Revenue]);
        Assert.Equal(8, byShape[MonitoringFormShape.FieldWork]); }

    [Fact] public void RevenueIsExactlyTheThreePaymentServices() =>
        Assert.Equal(new[] { 2, 5, 6 },
            Enumerable.Range(1, 25).Where(x => MonitoringRules.FormShape(x) == MonitoringFormShape.Revenue));
    [Theory] [InlineData(2)] [InlineData(5)] [InlineData(6)]
    public void AMonthOfPaymentsDoesNotGrowThePendingBalance(int serviceId) {
        var entries = Enumerable.Range(1, 30).Select(d =>
            new EntryInput(serviceId, new(2026, 11, d), "Ward 24", 40, 0, 0, 500_00, 0, 0, 0, 0, null, ""));
        var derived = MonitoringRules.Derive(new Balances(0, 0, 0), entries);
        Assert.Equal(0, derived[^1].Closing.Service); }

    [Fact] public void TheGrievanceServiceStillMovesTheBalance() {
        var entry = new EntryInput(12, Today, "Ward 24", 5, 2, 0, 0, 0, 0, 0, 0, null, "");
        Assert.Equal(3, Assert.Single(MonitoringRules.Derive(new Balances(0, 0, 0), [entry])).Closing.Service); }

    // Derive runs at read time, so this change re-derives EXISTING history.
    // These two tests pin that on purpose: a revenue entry that once inflated the
    // closing balance (received 40, completed 0) now leaves it at the opening;
    // counter entries keep their old closing balance exactly.
    [Theory] [InlineData(2)] [InlineData(5)] [InlineData(6)]
    public void HistoricRevenueEntryWithReceivedAndNoCompletedKeepsClosingEqualToOpening(int serviceId) {
        var entry = new EntryInput(serviceId, Today, "Ward 24", 40, 0, 0, 0, 0, 0, 0, 0, null, "");
        var derived = Assert.Single(MonitoringRules.Derive(new Balances(7, 0, 0), [entry]));
        Assert.Equal(7, derived.Opening.Service);
        Assert.Equal(derived.Opening.Service, derived.Closing.Service); }

    [Theory] [InlineData(1)] [InlineData(12)] [InlineData(25)]
    public void HistoricCounterEntryClosingBalanceIsNotAffected(int serviceId) {
        var entry = new EntryInput(serviceId, Today, "Ward 24", 40, 15, 0, 0, 0, 0, 0, 0, null, "");
        var derived = Assert.Single(MonitoringRules.Derive(new Balances(7, 0, 0), [entry]));
        Assert.Equal(32, derived.Closing.Service); }

    [Fact] public void CounterServiceWhoseBalanceWouldGoNegativeIsStillRejected() {
        var entry = new EntryInput(1, Today, "Ward 24", 0, 5, 0, 0, 0, 0, 0, 0, null, "");
        Assert.Throws<MonitoringRuleException>(() => MonitoringRules.Derive(new Balances(0, 0, 0), [entry])); }
}
