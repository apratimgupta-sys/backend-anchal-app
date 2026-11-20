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
        new(serviceId, Today, "24", 0, 0, 0, 0, 0, 0, 0, 0, null, "");

    // Door-to-door vehicle, fogging, street dog, street lighting, road and
    // drain, grass cutting, tree pruning, water body cleaning.
    public static TheoryData<int> FieldWork => new(8, 9, 10, 13, 14, 15, 16, 17);

    // Applications, licences, registrations, beneficiaries, bookings, revenue.
    public static TheoryData<int> NotFieldWork =>
        new(1, 2, 3, 4, 5, 6, 7, 11, 12, 18, 19, 20, 21, 22, 23, 24, 25);

    [Theory]
    [MemberData(nameof(FieldWork))]
    public void WorkDoneWithNoRequestBehindItIsAccepted(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = 5, Received = 0 };

        MonitoringRules.ValidateEntry(entry, Today);
    }

    [Theory]
    [MemberData(nameof(NotFieldWork))]
    public void CounterWorkStillCannotFinishMoreThanItReceived(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = 5, Received = 0, AvgDisposalDays = 2 };

        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(entry, Today));

        Assert.True(error.Error.FieldErrors!.ContainsKey("completed"));
    }

    [Theory]
    [MemberData(nameof(NotFieldWork))]
    public void CounterWorkStillNeedsAverageDaysWhenSomethingWasFinished(int serviceId)
    {
        var entry = Entry(serviceId) with { Received = 5, Completed = 5, AvgDisposalDays = null };

        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(entry, Today));

        Assert.True(error.Error.FieldErrors!.ContainsKey("avgDisposalDays"));
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
    [MemberData(nameof(NotFieldWork))]
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
}
