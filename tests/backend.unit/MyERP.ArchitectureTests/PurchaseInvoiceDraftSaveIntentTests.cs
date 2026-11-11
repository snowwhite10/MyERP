using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftSaveIntentTests
{
    private static readonly PurchaseInvoiceOrderIdentity Order = new(5, 7, 9, "TWD", "Approved", false);
    private static readonly PurchaseInvoiceReceiptIdentity Receipt =
        new(31, 5, 7, 9, 9, 51, 51, "TWD", "Posted", false) { OrderLineId = 42 };
    private static readonly PurchaseInvoiceOrderLinePrice Price = new(31, 5, 9, 42, 51, 2m, 20m);
    private static readonly PurchaseInvoiceDraftLine Line = new(1, 31, 1m, 1m, 10m, 1m, .1m, 1.1m);

    private static PurchaseInvoiceDraftSnapshot Snapshot() => PurchaseInvoiceDraftAssembly.Create(5,
        new PurchaseInvoiceDraftInput(7, 9, "TWD", new DateOnly(2028, 2, 29), " inv-1 ", [Line]),
        Order, [Receipt], [Price]);

    [Fact]
    public void BindsWriteFieldsAndDifferenceEvidenceToOneFreshRecheckedSnapshot()
    {
        var saved = Snapshot();
        var intent = PurchaseInvoiceDraftSaveIntent.Create(saved, null, Order, [Receipt], [Price], []);

        intent.Snapshot.Should().NotBeSameAs(saved);
        intent.Identity.Should().Be(intent.Snapshot.Identity);
        intent.Number.Should().Be(intent.Snapshot.Number);
        intent.BusinessDate.Should().Be(intent.Snapshot.BusinessDate);
        intent.Lines.Should().BeSameAs(intent.Snapshot.Lines);
        intent.Totals.Should().Be(intent.Snapshot.Totals);
        intent.AmountEvidence.Should().BeSameAs(intent.Snapshot.AmountEvidence);
        intent.AmountEvidence.CanMatch.Should().BeFalse();
        intent.AmountEvidence.Lines[0].Differences.Should().HaveCount(5);
        intent.Number.Key.Should().Be("INV-1");
    }

    [Fact]
    public void NumberConflictIncludingSoftDeletedOwnerRejectsWholeIntent()
    {
        var owners = new[] { new PurchaseInvoiceNumberOwner(5, 7, 20, "INV-1", true) };
        Action act = () => PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], owners);
        act.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void EditingSameInvoiceMayRetainNumberButOtherOwnerCannot()
    {
        var owners = new[] { new PurchaseInvoiceNumberOwner(5, 7, 20, "INV-1", false) };
        PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), 20, Order, [Receipt], [Price], owners);
        Action act = () => PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), 21, Order,
            [Receipt], [Price], owners);
        act.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void StaleAuthorityAndInvalidOwnershipSnapshotFailClosed()
    {
        Action stale = () => PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price with { UnitPrice = 3m }], []);
        stale.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED");

        Action missingOwners = () => PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], null);
        missingOwners.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void WriteIntentDoesNotExposeMutableAmountEvidenceArrays()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        intent.AmountEvidence.Lines.Should().NotBeOfType<PurchaseInvoiceAmountLineMatch[]>();
        intent.AmountEvidence.Lines[0].Differences.Should().NotBeOfType<PurchaseInvoiceAmountDifference[]>();
    }
}
