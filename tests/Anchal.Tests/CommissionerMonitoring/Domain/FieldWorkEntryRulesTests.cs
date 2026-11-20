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

    [Theory]
    [MemberData(nameof(FieldWork))]
    public void FieldWorkStillRefusesImpossibleNumbers(int serviceId)
    {
        var entry = Entry(serviceId) with { Completed = -1 };

        Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(entry, Today));
    }
}
