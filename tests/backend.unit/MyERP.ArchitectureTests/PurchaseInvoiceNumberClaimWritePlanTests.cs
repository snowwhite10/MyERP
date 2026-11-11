using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceNumberClaimWritePlanTests
{
    private static readonly PurchaseInvoiceNumberClaimState[] Claims =
    [
        new(5, 7, 20, "HISTORY-1", false),
        new(5, 7, 20, "OLD-1", true)
    ];

    [Fact]
    public void NewClaimAcceptsSameKeyOutsideSupplierScopeAndRejectsOccupiedOrInvalidSnapshots()
    {
        var plan = PurchaseInvoiceNumberClaimWritePlan.ForNew(5, 7, 30, "INV-1",
            [new(5, 8, 21, "INV-1", true), new(6, 7, 22, "INV-1", false)]);
        plan.Steps.Should().Equal(new PurchaseInvoiceNumberClaimWriteStep(
            PurchaseInvoiceNumberClaimWriteKind.InsertActive, "INV-1"));
        plan.RetainedKeys.Should().Equal("INV-1");

        Action occupiedHistory = () => PurchaseInvoiceNumberClaimWritePlan.ForNew(5, 7, 30, "INV-1",
            [new(5, 7, 21, "INV-1", false)]);
        occupiedHistory.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");

        PurchaseInvoiceNumberClaimState[][] invalid =
        [
            [new(5, 7, 30, "OLD-1", false)],
            [new(5, 7, 21, "OTHER-1", false), new(5, 7, 22, "OTHER-1", false)],
            [new(5, 7, 21, "other-1", false)]
        ];
        foreach (var rows in invalid)
        {
            Action act = () => PurchaseInvoiceNumberClaimWritePlan.ForNew(5, 7, 30, "INV-1", rows);
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
        }
        Action missing = () => PurchaseInvoiceNumberClaimWritePlan.ForNew(5, 7, 30, "INV-1", null);
        missing.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void RenameDeactivatesOldBeforeInsertingNewAndRetainsEveryHistoryKey()
    {
        var plan = PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"), Claims);

        plan.Steps.Should().Equal(
            new PurchaseInvoiceNumberClaimWriteStep(PurchaseInvoiceNumberClaimWriteKind.Deactivate, "OLD-1"),
            new PurchaseInvoiceNumberClaimWriteStep(PurchaseInvoiceNumberClaimWriteKind.InsertActive, "NEW-1"));
        plan.RetainedKeys.Should().BeEquivalentTo("HISTORY-1", "OLD-1", "NEW-1");
    }

    [Fact]
    public void RenamingBackToOwnedHistoryActivatesExistingRowAfterDeactivation()
    {
        var plan = PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "HISTORY-1"), Claims);
        plan.Steps.Should().Equal(
            new PurchaseInvoiceNumberClaimWriteStep(PurchaseInvoiceNumberClaimWriteKind.Deactivate, "OLD-1"),
            new PurchaseInvoiceNumberClaimWriteStep(PurchaseInvoiceNumberClaimWriteKind.ActivateExisting, "HISTORY-1"));
        plan.RetainedKeys.Should().BeEquivalentTo("HISTORY-1", "OLD-1");
    }

    [Fact]
    public void SameCanonicalKeyHasNoClaimMutation()
    {
        var plan = PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Retained, "OLD-1", "OLD-1"), Claims);
        plan.Steps.Should().BeEmpty();
        plan.RetainedKeys.Should().BeEquivalentTo("HISTORY-1", "OLD-1");
    }

    [Fact]
    public void ReturnedStepsAndRetainedKeysAreIndependentReadOnlySnapshots()
    {
        var rows = Claims.ToList();
        var plan = PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"), rows);
        rows.Clear();
        plan.Steps.Should().HaveCount(2);
        plan.RetainedKeys.Should().HaveCount(3);
        plan.Steps.Should().NotBeOfType<PurchaseInvoiceNumberClaimWriteStep[]>();
        plan.RetainedKeys.Should().NotBeOfType<string[]>();
    }

    [Fact]
    public void MismatchedRevisionKindAndKeysFailClosed()
    {
        PurchaseInvoiceNumberRevision[] revisions =
        [
            new(PurchaseInvoiceNumberRevisionKind.Retained, "OLD-1", "NEW-1"),
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "OLD-1"),
            new((PurchaseInvoiceNumberRevisionKind)999, "OLD-1", "NEW-1")
        ];
        foreach (var revision in revisions)
        {
            Action act = () => PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20, revision, Claims);
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
        }
    }

    [Fact]
    public void InvalidActiveOwnershipAndAnotherInvoiceConflictFailClosed()
    {
        PurchaseInvoiceNumberClaimState[][] invalid =
        [
            [new(5, 7, 20, "OLD-1", false)],
            [.. Claims, new(5, 7, 20, "OTHER-1", true)],
            [.. Claims, new(6, 7, 20, "OTHER-1", false)],
            [.. Claims, new(5, 7, 20, "old-1", false)]
        ];
        foreach (var rows in invalid)
        {
            Action act = () => PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20,
                new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"), rows);
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
        }

        Action conflict = () => PurchaseInvoiceNumberClaimWritePlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"),
            [.. Claims, new(5, 7, 21, "NEW-1", false)]);
        conflict.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Theory]
    [InlineData(2601, "Cannot insert duplicate key row in object 'dbo.pur_purchase_invoice_number_claim' with unique index 'ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key'.", true)]
    [InlineData(2627, "Violation of UNIQUE KEY constraint 'ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key'.", true)]
    [InlineData(2601, "duplicate key in ix_unrelated", false)]
    [InlineData(547, "ux_pur_purchase_invoice_number_claim_company_id_supplier_id_number_key", false)]
    public void OnlyNumberScopeUniqueCollisionMapsToNumberExists(int number, string message, bool expected)
    {
        PurchaseInvoiceNumberClaimWritePlan.IsNumberConflict(number, message).Should().Be(expected);
    }
}
