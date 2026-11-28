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

    private static readonly int[] FieldWorkIds = [8, 9, 10, 13, 14, 15, 16, 17];

    /// The two statements that may spell the field-work ids out, each named by file and by what it declares:
    /// the shape set the daily entry's rule is derived from, and the table of what a visit asks each of them.
    private static readonly (string File, string Anchor)[] Carriers =
    [
        ("MonitoringRules.cs", "FieldWorkServices ="),
        ("MonitoringFieldDefinitions.cs", "ByService ="),
    ];

    /// A statement that names this many of the eight ids as bare integers is spelling out the field-work set,
    /// in whatever order, with whatever separators ("[8, 9, ...]", "is 8 or 9 or ...", "case 8: case 9:"), or
    /// is half of it. Four, not eight: no statement in src has more than two of them by accident today.
    private const int SpellingThreshold = 4;

    /// The statements of the source (comments removed) that name SpellingThreshold or more of the eight ids.
    internal static string[] FieldWorkIdSpellings(string source)
    {
        var code = System.Text.RegularExpressions.Regex.Replace(source, @"//[^\n]*|/\*.*?\*/", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return code.Split(';').Where(statement =>
                System.Text.RegularExpressions.Regex.Matches(statement, @"(?<![\w.])\d+(?![\w.])")
                    .Select(m => int.Parse(m.Value)).Where(FieldWorkIds.Contains).Distinct().Count() >= SpellingThreshold)
            .Select(statement => statement.Trim()).ToArray();
    }

    private static string[] Offenders(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => FieldWorkIdSpellings(File.ReadAllText(f))
                .Where(statement => !Carriers.Any(c => Path.GetFileName(f) == c.File && statement.Contains(c.Anchor)))
                .Select(statement => $"{Path.GetFileName(f)}: {statement[..Math.Min(80, statement.Length)]}"))
            .ToArray();

    /// The point of deriving it: no source file carries a second copy of the eight ids. Every file is scanned,
    /// MonitoringRules.cs included, minus the two named carrier statements.
    [Fact]
    public void NoOtherSourceStatementSpellsOutTheFieldWorkIds() => Assert.Empty(Offenders(SourceRoot()));

    /// The allowance is not a hole: each carrier still exists and still holds the whole set, so the exemption
    /// cannot outlive the statement it names, and the shape set is the one the rule is derived from.
    [Fact]
    public void TheTwoCarriersExistAndTheShapeSetIsExactlyTheEightIds()
    {
        var rules = FieldWorkIdSpellings(File.ReadAllText(Path.Combine(SourceRoot(),
            "Anchal.Domain", "CommissionerMonitoring", "Rules", "MonitoringRules.cs")));
        var definitions = FieldWorkIdSpellings(File.ReadAllText(Path.Combine(SourceRoot(),
            "Anchal.Domain", "CommissionerMonitoring", "Rules", "MonitoringFieldDefinitions.cs")));

        var shapeSet = Assert.Single(rules);
        Assert.Contains("FieldWorkServices =", shapeSet);
        Assert.Contains("[8, 9, 10, 13, 14, 15, 16, 17]", shapeSet);
        Assert.Contains("ByService =", Assert.Single(definitions));
    }

    [Theory]
    [InlineData("var a = new[] { 8, 9, 10, 13, 14, 15, 16, 17 };")]
    [InlineData("var a = new[] { 17, 9, 14, 8, 16, 10, 15, 13 };")]
    [InlineData("var a = id is 8 or 9 or 10 or 13 or 14 or 15 or 16 or 17;")]
    [InlineData("var a = new HashSet<int> { 8,9,10,13,14,15,16,17 };")]
    [InlineData("switch (id) { case 17: case 16: case 15: case 14: case 13: case 10: case 9: case 8: return true; }")]
    [InlineData("var a = new[] { 8, 9, 10, 13 }; var b = new[] { 14, 15, 16, 17 };")]
    [InlineData("var a = ids.Where(x => x is 8 or 9 or 10 or 13)")]
    public void TheScanSeesTheIdsInAnyOrderAndWithAnySeparator(string source) =>
        Assert.NotEmpty(FieldWorkIdSpellings(source));

    [Theory]
    [InlineData("var a = new[] { 8, 9, 10 };")]
    [InlineData("// the field-work services are 8, 9, 10, 13, 14, 15, 16, 17\nvar a = 1;")]
    [InlineData("var a = new[] { 1, 3, 4, 7, 11, 12, 18, 19 };")]
    [InlineData("var a = 18.9 + 1_000_000_000 + 8 + 9 + x10;")]
    public void TheScanIgnoresCommentsOtherNumbersAndAFewIncidentalIds(string source) =>
        Assert.Empty(FieldWorkIdSpellings(source));

    internal static string SourceRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Anchal.Application"))) return Path.Combine(dir.FullName, "src");
        throw new InvalidOperationException("Cannot locate the backend src directory from the test output");
    }
}
