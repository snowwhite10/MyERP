using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftRecheckTests
{
    private static readonly PurchaseInvoiceOrderIdentity Order = new(5, 7, 9, "TWD", "Approved", false);
    private static readonly PurchaseInvoiceReceiptIdentity Receipt =
        new(31, 5, 7, 9, 9, 51, 51, "TWD", "Posted", false) { OrderLineId = 42 };
    private static readonly PurchaseInvoiceOrderLinePrice Price = new(31, 5, 9, 42, 51, 1m, 10m);
    private static readonly PurchaseInvoiceDraftLine Line = new(1, 31, 1m, 1m, 10m, 1m, .1m, 1.1m);

    private static PurchaseInvoiceDraftSnapshot Snapshot() => PurchaseInvoiceDraftAssembly.Create(5,
        new PurchaseInvoiceDraftInput(7, 9, "TWD", new DateOnly(2028, 2, 29), "INV-1", [Line]),
        Order, [Receipt], [Price]);

    [Fact]
    public void SameLockedAuthorityReturnsFreshValidatedSnapshot()
    {
        var original = Snapshot();
        var result = PurchaseInvoiceDraftRecheck.Check(original, Order, [Receipt], [Price]);
        result.Should().NotBeSameAs(original);
        result.Lines.Should().Equal(original.Lines);
        result.AmountEvidence.CanMatch.Should().BeTrue();
    }

    [Fact]
    public void ChangedOrderPriceOrTaxRejectsOldEvidence()
    {
        AssertChanged(Snapshot(), [Price with { UnitPrice = 2m }]);
        AssertChanged(Snapshot(), [Price with { TaxRatePercent = 20m }]);
    }

    [Fact]
    public void ChangedSavedAmountsOrSourceRejectsEvenWhenSnapshotStillHasOldEvidence()
    {
        var snapshot = Snapshot();
        AssertChanged(snapshot with { Lines = [Line with { ReceiptLineId = 32 }] }, [Price]);
        AssertChanged(snapshot with { Lines = [Line with { UnitPrice = 2m, Net = 2m, Tax = .2m, Gross = 2.2m }] }, [Price]);
        AssertChanged(snapshot with { Totals = new(2m, .2m, 2.2m) }, [Price]);
        AssertChanged(snapshot with { Identity = snapshot.Identity with { CompanyId = 6 } }, [Price]);
    }

    [Fact]
    public void MissingOrMisboundAuthorityAndTamperedEvidenceRejectWholeSnapshot()
    {
        var snapshot = Snapshot();
        AssertChanged(snapshot, []);
        AssertChanged(snapshot, [Price with { OrderLineId = 99 }]);
        AssertChanged(snapshot with { AmountEvidence = snapshot.AmountEvidence with { CanMatch = false } }, [Price]);
        Action deleted = () => PurchaseInvoiceDraftRecheck.Check(snapshot, Order, [Receipt with { IsDeleted = true }], [Price]);
        deleted.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED");
    }

    [Fact]
    public void LegitimateDifferenceSurvivesRecheckButChangedDifferenceDetailDoesNot()
    {
        var authority = Price with { UnitPrice = 2m, TaxRatePercent = 20m };
        var snapshot = PurchaseInvoiceDraftAssembly.Create(5,
            new PurchaseInvoiceDraftInput(7, 9, "TWD", new DateOnly(2028, 2, 29), "INV-1", [Line]),
            Order, [Receipt], [authority]);

        var valid = PurchaseInvoiceDraftRecheck.Check(snapshot, Order, [Receipt], [authority]);
        valid.AmountEvidence.CanMatch.Should().BeFalse();
        valid.AmountEvidence.Lines[0].Differences.Should().HaveCount(5);

        var changedLine = snapshot.AmountEvidence.Lines[0] with { Differences = [] };
        AssertChanged(snapshot with
        {
            AmountEvidence = snapshot.AmountEvidence with { Lines = [changedLine] }
        }, [authority]);
    }

    [Fact]
    public void EmptyOrChangedHeaderCannotProduceSaveSnapshot()
    {
        Action empty = () => PurchaseInvoiceDraftRecheck.Check(null, Order, [Receipt], [Price]);
        empty.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED");

        var snapshot = Snapshot();
        AssertChanged(snapshot with { Number = snapshot.Number with { Key = "OTHER" } }, [Price]);
        AssertChanged(snapshot with { BusinessDate = default }, [Price]);
    }

    private static void AssertChanged(PurchaseInvoiceDraftSnapshot snapshot,
        IReadOnlyList<PurchaseInvoiceOrderLinePrice> prices)
    {
        Action act = () => PurchaseInvoiceDraftRecheck.Check(snapshot, Order, [Receipt], prices);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED");
    }
}
