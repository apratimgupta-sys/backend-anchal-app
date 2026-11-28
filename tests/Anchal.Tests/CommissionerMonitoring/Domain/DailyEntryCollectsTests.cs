using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// Field work leaves the daily entry. The rule is derived from the form shape, never listed:
/// "the daily entry collects every service that is not field work".
public sealed class DailyEntryCollectsTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    private static EntryInput Entry(int serviceId) =>
        new(serviceId, Today, "Ward 24", 0, 0, 0, 0, 0, 0, 0, 0, null, "");

    public static TheoryData<int> FieldWork => new(8, 9, 10, 13, 14, 15, 16, 17);
    public static TheoryData<int> Collected => new(1, 2, 3, 4, 5, 6, 7, 11, 12, 18, 19, 20, 21, 22, 23, 24, 25);

    [Theory]
    [MemberData(nameof(FieldWork))]
    public void AnEntryForAFieldWorkServiceIsRefusedWithAFieldErrorOnTheService(int serviceId)
    {
        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(Entry(serviceId) with { Completed = 5 }, Today));

        Assert.Equal(422, error.Status);
        Assert.True(error.Error.FieldErrors!.ContainsKey("serviceId"));
    }

    [Theory]
    [MemberData(nameof(FieldWork))]
    public void ARefusedFieldWorkEntryIsRefusedEvenWhenItIsAllZeroes(int serviceId) =>
        Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(Entry(serviceId), Today));

    [Theory]
    [MemberData(nameof(Collected))]
    public void AnEntryForACollectedServiceIsStillAccepted(int serviceId) =>
        MonitoringRules.ValidateEntry(Entry(serviceId), Today);

    [Fact]
    public void ARangeErrorOnTheServiceIsStillTheOldMessage()
    {
        var error = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(Entry(26), Today));

        Assert.Equal("Select a service", error.Error.FieldErrors!["serviceId"]);
    }

    [Fact]
    public void TheDailyEntryCollectsExactlyTheServicesThatAreNotFieldWork()
    {
        var collected = Enumerable.Range(1, MonitoringRules.ServiceCount)
            .Where(MonitoringRules.DailyEntryCollects).ToArray();

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 11, 12, 18, 19, 20, 21, 22, 23, 24, 25 }, collected);
        Assert.All(collected, id => Assert.NotEqual(MonitoringFormShape.FieldWork, MonitoringRules.FormShape(id)));
    }

    [Fact]
    public void TheCountIsSeventeenAndIsTheCountOfThePredicate()
    {
        Assert.Equal(17, MonitoringRules.DailyEntryServiceCount);
        Assert.Equal(Enumerable.Range(1, MonitoringRules.ServiceCount).Count(MonitoringRules.DailyEntryCollects),
            MonitoringRules.DailyEntryServiceCount);
    }

    /// The point of deriving it: no source file carries a second copy of the eight ids as a list.
    /// Pins the one list that exists (MonitoringRules' own shape set) and fails if another appears.
    [Fact]
    public void NoOtherSourceFileSpellsOutTheFieldWorkIds()
    {
        var root = SourceRoot();
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.EndsWith("MonitoringRules.cs"))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"8\s*,\s*9\s*,\s*10\s*,\s*13\s*,\s*14"))
            .Select(Path.GetFileName).ToArray();

        Assert.Empty(offenders);
    }

    internal static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Anchal.Application"))) return Path.Combine(dir.FullName, "src");
        throw new InvalidOperationException("Cannot locate the backend src directory from the test output");
    }
}
