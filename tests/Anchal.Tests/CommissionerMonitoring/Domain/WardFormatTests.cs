using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// A ward is filed as free text today. Ward-level accounting later is only possible
/// if the value is constrained from the first entry, so the stored string must be
/// exactly `Ward N`, one space, no leading zeros, 1 to 53. The type stays a string:
/// changing it would change the shape of a stored snapshot.
public sealed class WardFormatTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private const string Message = "Select a ward from 1 to 53";

    private static EntryInput Entry(string ward) =>
        new(1, Today, ward, 0, 0, 0, 0, 0, 0, 0, 0, null, "");

    // Break caught: off-by-one at either end of the range (1 and 53 must pass).
    [Theory]
    [InlineData("Ward 1")]
    [InlineData("Ward 53")]
    [InlineData("Ward 11")]
    // Surrounding whitespace is tolerated because MonitoringService trims the ward before it is
    // stored, so these produce the same record as "Ward 11". Only the ends: internal spacing is not.
    [InlineData(" Ward 11 ")]
    [InlineData("Ward 11 ")]
    [InlineData("\tWard 11")]
    public void AWardInTheCanonicalFormatIsAccepted(string ward)
    {
        MonitoringRules.ValidateEntry(Entry(ward), Today);
    }

    // Break caught: the daily-entry bound drifting from the field-visit bound (both read
    // MonitoringRules.WardCount; FieldVisitBoundsTests pins the other side).
    [Fact]
    public void TheLastWardIsTheOneTheBoundNames()
    {
        MonitoringRules.ValidateEntry(Entry($"Ward {MonitoringRules.WardCount}"), Today);
        var ex = Assert.Throws<MonitoringRuleException>(() =>
            MonitoringRules.ValidateEntry(Entry($"Ward {MonitoringRules.WardCount + 1}"), Today));
        Assert.Equal(Message, ex.Error.FieldErrors!["ward"]);
    }

    // Break caught: a looser rule that lets a second spelling of the same ward into the register.
    [Theory]
    [InlineData("Ward 0")]
    [InlineData("Ward 54")]
    [InlineData("ward 11")]
    [InlineData("Ward 011")]
    [InlineData("Ward 11, Konka")]
    [InlineData("Ward  11")]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("Ward  ")]
    [InlineData("11")]
    [InlineData("Ward")]
    [InlineData("")]
    public void AWardOutsideTheCanonicalFormatIsRejected(string ward)
    {
        var ex = Assert.Throws<MonitoringRuleException>(() => MonitoringRules.ValidateEntry(Entry(ward), Today));

        Assert.Equal(422, ex.Status);
        Assert.Equal(Message, ex.Error.FieldErrors!["ward"]);
    }
}
