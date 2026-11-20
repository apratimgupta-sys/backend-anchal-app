using Anchal.Domain.CommissionerMonitoring.Rules;
using Xunit;

namespace Anchal.Tests.CommissionerMonitoring.Domain;

/// A cross-field rule can only be acted on if the office is asked for both of
/// its operands. The daily form now collects two numbers per service — counter
/// Received and Disposed, revenue Payments and the amount, field work Work done
/// — so a rule keyed to anything else dead-ends a correction: the 422 names a
/// field the form does not render, the office sees "Please correct the
/// highlighted fields" with nothing highlighted, and the entry cannot be saved.
///
/// The fields those rules guarded are retired. A correction leaves their stored
/// values untouched, so their internal consistency is preserved by
/// construction, and nothing reads them once the dashboard drops them.
public sealed class HiddenFieldCrossRuleTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    // A pre-cutover entry: filed under the nine-field form, with figures in the
    // six fields the shapes no longer collect.
    private static EntryInput PreCutover(int serviceId) =>
        new(serviceId, Today, "24", 7, 5, 4, 1234, 2, 1, 3, 3, 2.5, "filed before the cutover");

    public static TheoryData<int> EveryService => new([.. Enumerable.Range(1, 25)]);

    [Fact]
    public void CollectedFieldsAreTheOnesTheShapeAsksFor()
    {
        Assert.Equal(new[] { "received", "completed" }, MonitoringRules.CollectedFields(1));
        Assert.Equal(new[] { "received", "amountPaise" }, MonitoringRules.CollectedFields(2));
        Assert.Equal(new[] { "completed" }, MonitoringRules.CollectedFields(15));
    }

    /// Counter shape, the scenario the review enumerated: stored slaWithinTime
    /// is 4 and the office lowers Disposed to 3. slaWithinTime is hidden on
    /// every shape, so a rejection on it cannot be acted on.
    [Fact]
    public void LoweringDisposedBelowAStoredWithinSlaFigureIsAccepted()
    {
        var corrected = PreCutover(1) with { Completed = 3 };

        MonitoringRules.ValidateEntry(corrected, Today);
    }

    /// Field work shape, same scenario through Work done.
    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(15)]
    public void LoweringWorkDoneBelowAStoredWithinSlaFigureIsAccepted(int serviceId)
    {
        var corrected = PreCutover(serviceId) with { Completed = 2 };

        MonitoringRules.ValidateEntry(corrected, Today);
    }

    /// Revenue shape: the form has no disposal figure, so lowering Payments
    /// below a stored Completed must not be rejected on `completed`.
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(6)]
    public void LoweringPaymentsBelowAStoredCompletedFigureIsAccepted(int serviceId)
    {
        var corrected = PreCutover(serviceId) with { Received = 3 };

        MonitoringRules.ValidateEntry(corrected, Today);
    }

    /// The invariant behind all three: no entry can be refused on a field its
    /// shape does not collect, whatever the retired fields hold.
    [Theory]
    [MemberData(nameof(EveryService))]
    public void NoShapeIsRefusedOnAFieldItDoesNotCollect(int serviceId)
    {
        var messy = PreCutover(serviceId) with
        {
            Received = 7,
            Completed = 1,
            SlaWithinTime = 9,
            ComplaintsReceived = 2,
            ComplaintsResolved = 5,
            FieldReceived = 1,
            FieldCompleted = 4,
        };

        MonitoringRules.ValidateEntry(messy, Today);
    }

    /// The one cross-field rule that survives, and must: a counter service
    /// cannot dispose of more cases than it ever received.
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(25)]
    public void ACounterServiceStillCannotDisposeMoreThanItReceived(int serviceId)
    {
        var entry = PreCutover(serviceId) with { Received = 2, Completed = 5 };

        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(entry, Today));

        Assert.Equal(new[] { "completed" }, error.Error.FieldErrors!.Keys);
    }

    /// Range and presence checks are not cross-field rules and still apply to
    /// every field, visible or not, so a corrupt payload is still refused.
    [Fact]
    public void AnOutOfRangeHiddenFigureIsStillRefused()
    {
        var entry = PreCutover(15) with { ComplaintsReceived = -1 };

        var error = Assert.Throws<MonitoringRuleException>(
            () => MonitoringRules.ValidateEntry(entry, Today));

        Assert.Contains("complaintsReceived", error.Error.FieldErrors!.Keys);
    }
}
