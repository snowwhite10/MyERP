using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceReservationPlanTests
{
    private static PurchaseInvoiceReceiptRequest Request(long id, decimal quantity) => new(id, quantity);
    private static PurchaseInvoiceReceiptSource Source(long id, decimal received, decimal total = 0,
        decimal current = 0) => new(id, new(received, 0, 0, 0, total, current));

    [Fact]
    public void MatchCreatesSortedReservationsAndRecheckHasNoDuplicateChange()
    {
        var requests = new[] { Request(20, 1), Request(10, 1), Request(10, 0.5m) };
        var first = PurchaseInvoiceReservationPlan.ForMatch("Matched", requests,
            [Source(20, 2), Source(10, 2)]);
        first.Select(x => x.ReceiptLineId).Should().Equal(10, 20);
        first.Select(x => x.Change).Should().Equal(1.5m, 1m);

        var again = PurchaseInvoiceReservationPlan.ForMatch("Disputed", requests,
            [Source(10, 2, 1.5m, 1.5m), Source(20, 2, 1, 1)]);
        again.Should().OnlyContain(x => x.Change == 0);
    }

    [Fact]
    public void MatchAdjustsCurrentReservationWithoutDoubleCountingIt()
    {
        var plan = PurchaseInvoiceReservationPlan.ForMatch("Matched", [Request(10, 1.25m)],
            [Source(10, 2, 1.5m, 1.5m)]);
        plan.Should().ContainSingle().Which.Change.Should().Be(-0.25m);
    }

    [Fact]
    public void ResolveAcceptsWithoutChangeAndRejectsWithExactRelease()
    {
        var requests = new[] { Request(20, 1), Request(10, 1), Request(10, 0.5m) };
        var sources = new[] { Source(20, 2, 1, 1), Source(10, 2, 1.5m, 1.5m) };
        PurchaseInvoiceReservationPlan.ForResolve("Matched", requests, sources)
            .Should().OnlyContain(x => x.Change == 0);
        var release = PurchaseInvoiceReservationPlan.ForResolve("Draft", requests, sources);
        release.Select(x => x.Change).Should().Equal(-1.5m, -1m);
        release.Should().OnlyContain(x => x.TargetReserved == 0);
    }

    [Fact]
    public void RejectsShortageAndMissingOrStaleAuthorityWithoutPartialPlan()
    {
        Action shortage = () => PurchaseInvoiceReservationPlan.ForMatch("Matched",
            [Request(10, 2)], [Source(10, 1)]);
        shortage.Should().Throw<BusinessRuleException>();
        Action missing = () => PurchaseInvoiceReservationPlan.ForMatch("Matched",
            [Request(10, 1), Request(20, 1)], [Source(10, 1)]);
        missing.Should().Throw<BusinessRuleException>();
        Action stale = () => PurchaseInvoiceReservationPlan.ForResolve("Draft",
            [Request(10, 1)], [Source(10, 2, 0.5m, 0.5m)]);
        stale.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void RejectsInvalidStatusQuantityAndDuplicateAuthority()
    {
        Action status = () => PurchaseInvoiceReservationPlan.ForMatch("Shortage",
            [Request(10, 1)], [Source(10, 1)]);
        status.Should().Throw<BusinessRuleException>();
        Action quantity = () => PurchaseInvoiceReservationPlan.ForMatch("Matched",
            [Request(10, 0)], [Source(10, 1)]);
        quantity.Should().Throw<BusinessRuleException>();
        Action duplicate = () => PurchaseInvoiceReservationPlan.ForResolve("Draft",
            [Request(10, 1)], [Source(10, 2, 1, 1), Source(10, 2, 1, 1)]);
        duplicate.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void AcceptedResolutionRechecksAvailabilityButRejectedResolutionCanRelease()
    {
        var source = Source(10, 1, 1, 1) with
        {
            Quantities = new(1, 0.5m, 0, 0, 1, 1)
        };
        Action accepted = () => PurchaseInvoiceReservationPlan.ForResolve("Matched",
            [Request(10, 1)], [source]);
        accepted.Should().Throw<BusinessRuleException>();

        var released = PurchaseInvoiceReservationPlan.ForResolve("Draft",
            [Request(10, 1)], [source]);
        released.Should().ContainSingle().Which.Change.Should().Be(-1);
    }
}
