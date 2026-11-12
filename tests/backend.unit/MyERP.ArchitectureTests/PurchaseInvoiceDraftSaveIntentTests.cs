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
        intent.NumberRevision.Should().BeNull();
    }

    [Fact]
    public void ValidatedNewIntentBindsClaimEntityToAssignedInvoiceId()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        var changes = intent.PlanNumberClaimEntities(30, []).Changes;
        changes.Should().ContainSingle();
        changes[0].Entity.CompanyId.Should().Be(5);
        changes[0].Entity.SupplierId.Should().Be(7);
        changes[0].Entity.InvoiceId.Should().Be(30);
        changes[0].Entity.NumberKey.Should().Be(intent.Number.Key);

        Action wrongId = () => intent.PlanNumberClaimEntities(0, []);
        wrongId.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void CombinedEntityPlanUsesOneInvoiceIdentityAndNumberKey()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);

        var plan = intent.PlanDraftEntities(30, []);

        plan.Invoice.Id.Should().Be(30);
        plan.Invoice.Lines.Should().ContainSingle().Which.InvoiceId.Should().Be(30);
        plan.NumberClaims.Changes.Should().ContainSingle().Which.Entity.InvoiceId.Should().Be(30);
        plan.NumberClaims.Changes[0].Entity.NumberKey.Should().Be(plan.Invoice.NumberKey);
        plan.Invoice.SupplierInvoiceNumber.Should().Be(intent.Number.Display);

        Action invalidId = () => intent.PlanDraftEntities(0, []);
        invalidId.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_SAVED_IDENTITY");
    }

    [Fact]
    public void CombinedPlanRejectsHeaderAndLineDriftBeforeSave()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        var plan = intent.PlanDraftEntities(30, []);
        plan.ValidateBeforeSave();
        plan.Invoice.Version = 2;
        Action staleVersion = plan.ValidateBeforeSave;
        staleVersion.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_DRAFT_ROWS");

        plan = intent.PlanDraftEntities(30, []);
        plan.Invoice.Lines[0].ReceiptLineId = 32;
        Action staleLine = plan.ValidateBeforeSave;
        staleLine.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_DRAFT_ROWS");
    }

    [Fact]
    public void CombinedPlanRejectsUnchangedClaimDriftBeforeSave()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var owners = new[] { new PurchaseInvoiceNumberOwner(5, 7, 20, "INV-1", false) };
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], owners);
        var claim = new MyERP.Modules.Purchasing.Domain.PurchaseInvoiceNumberClaim
        {
            Id = 11, CompanyId = 5, SupplierId = 7, InvoiceId = 20,
            NumberKey = "INV-1", IsActive = true
        };
        var plan = intent.PlanDraftEntities(20, [claim]);
        plan.ValidateBeforeSave();
        claim.IsActive = false;
        Action staleClaim = plan.ValidateBeforeSave;
        staleClaim.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void CombinedPlanRejectsNewClaimKeyDriftBeforeSave()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        var plan = intent.PlanDraftEntities(30, []);
        plan.NumberClaims.Changes[0].Entity.NumberKey = "OTHER";
        Action staleClaim = plan.ValidateBeforeSave;
        staleClaim.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void CombinedRevisionKeepsVersionAndRejectsDuplicateClaimSnapshot()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var owners = new[] { new PurchaseInvoiceNumberOwner(5, 7, 20, "INV-1", false) };
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], owners);
        var claim = new MyERP.Modules.Purchasing.Domain.PurchaseInvoiceNumberClaim
        {
            Id = 11, CompanyId = 5, SupplierId = 7, InvoiceId = 20,
            NumberKey = "INV-1", IsActive = true
        };

        var plan = intent.PlanDraftEntities(20, [claim]);
        plan.Invoice.Version.Should().Be(4);
        plan.NumberClaims.Changes.Should().BeEmpty();

        Action wrongId = () => intent.PlanDraftEntities(21, [claim]);
        wrongId.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_SAVED_IDENTITY");
        Action occupied = () => intent.PlanDraftEntities(20, [claim,
            new MyERP.Modules.Purchasing.Domain.PurchaseInvoiceNumberClaim
            {
                Id = 12, CompanyId = 5, SupplierId = 7, InvoiceId = 21,
                NumberKey = "INV-1", IsActive = false
            }]);
        occupied.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
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

    [Fact]
    public void RevisionUsesSavedDraftIdentityAndVersionForNumberExemption()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var owners = new[] { new PurchaseInvoiceNumberOwner(5, 7, 20, "INV-1", false) };
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], owners);
        intent.InvoiceId.Should().Be(20);
        intent.ExpectedVersion.Should().Be(4);
        intent.Identity.CompanyId.Should().Be(saved.CompanyId);
        intent.NumberRevision.Should().Be(new PurchaseInvoiceNumberRevision(
            PurchaseInvoiceNumberRevisionKind.Retained, "INV-1", "INV-1"));
    }

    [Theory]
    [InlineData("PendingApproval", 4, false, "PUR.INVOICE.NOT_DRAFT")]
    [InlineData("Posted", 4, false, "PUR.INVOICE.NOT_DRAFT")]
    [InlineData("Draft", 3, false, "COMMON.CONCURRENCY.CONFLICT")]
    [InlineData("Draft", 4, true, "PUR.INVOICE.NOT_DRAFT")]
    public void RevisionRejectsInvalidStateVersionOrDeletedDocument(string status, int version,
        bool deleted, string expectedCode)
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", status, version, deleted, "INV-1");
        Action act = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], []);
        act.Should().Throw<ErpException>().Which.ErrorCode.Should().Be(expectedCode);
    }

    [Fact]
    public void RevisionRejectsMissingOrMismatchedSavedIdentityBeforeNumberExemption()
    {
        PurchaseInvoiceSavedDraft?[] invalid = [null,
            new(0, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1"),
            new(20, 6, 7, 9, "TWD", "Draft", 4, false, "INV-1"),
            new(20, 5, 8, 9, "TWD", "Draft", 4, false, "INV-1"),
            new(20, 5, 7, 10, "TWD", "Draft", 4, false, "INV-1"),
            new(20, 5, 7, 9, "USD", "Draft", 4, false, "INV-1")];
        foreach (var saved in invalid)
        {
            Action act = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
                Order, [Receipt], [Price], []);
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_SAVED_IDENTITY");
        }
    }

    [Fact]
    public void RevisionRejectsZeroVersionAndAnotherOwnerEvenWhenSavedIdMatches()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        Action zero = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 0,
            Order, [Receipt], [Price], [new(5, 7, 20, "INV-1", false)]);
        zero.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("COMMON.CONCURRENCY.CONFLICT");

        Action otherOwner = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "INV-1", false), new(5, 7, 21, "INV-1", true)]);
        otherOwner.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void RevisionCanChangeNumberWhenSavedKeyIsOwnedAndNewKeyIsFree()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "OLD-1");
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "OLD-1", false)]);
        intent.Number.Key.Should().Be("INV-1");
        intent.InvoiceId.Should().Be(20);
        intent.NumberRevision.Should().Be(new PurchaseInvoiceNumberRevision(
            PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "INV-1"));
        intent.NumberPersistence.Should().Be(new PurchaseInvoiceNumberPersistencePlan(
            "INV-1", "OLD-1"));
    }

    [Fact]
    public void NewDraftBindsNumberClaimInsertionToValidatedIdentityAndAssignedId()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        var plan = intent.PlanNumberClaimWrite(30, [new(5, 7, 21, "OTHER-1", false)]);
        plan.Steps.Should().Equal(new PurchaseInvoiceNumberClaimWriteStep(
            PurchaseInvoiceNumberClaimWriteKind.InsertActive, "INV-1"));
        plan.RetainedKeys.Should().Equal("INV-1");

        Action duplicate = () => intent.PlanNumberClaimWrite(30, [new(5, 7, 21, "INV-1", false)]);
        duplicate.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
        Action reusedId = () => intent.PlanNumberClaimWrite(30, [new(6, 7, 30, "HISTORY-1", false)]);
        reusedId.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void RevisionBindsClaimPlanToSavedInvoiceAndActiveHistory()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "OLD-1");
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "OLD-1", false)]);
        var plan = intent.PlanNumberClaimWrite(20,
            [new(5, 7, 20, "OLD-1", true), new(5, 7, 20, "HISTORY-1", false)]);
        plan.Steps.Select(step => step.Kind).Should().Equal(
            PurchaseInvoiceNumberClaimWriteKind.Deactivate,
            PurchaseInvoiceNumberClaimWriteKind.InsertActive);
        plan.RetainedKeys.Should().BeEquivalentTo("OLD-1", "HISTORY-1", "INV-1");

        Action wrongId = () => intent.PlanNumberClaimWrite(21, []);
        wrongId.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
        Action missingHistory = () => intent.PlanNumberClaimWrite(20, [new(5, 7, 20, "OLD-1", false)]);
        missingHistory.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void RevisionAcceptsItsRetainedHistoryAlongsideCurrentKey()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var owners = new[]
        {
            new PurchaseInvoiceNumberOwner(5, 7, 20, "OLD-1", false),
            new PurchaseInvoiceNumberOwner(5, 7, 20, "INV-1", false)
        };
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], owners);
        intent.NumberPersistence.Should().Be(new PurchaseInvoiceNumberPersistencePlan("INV-1", null));
    }

    [Fact]
    public void RevisionUsesCanonicalKeysEvenWhenDisplayNumberChangesOnlyInCase()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "INV-1");
        var intent = PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "INV-1", false)]);
        intent.Number.Display.Should().Be("inv-1");
        intent.NumberRevision.Should().Be(new PurchaseInvoiceNumberRevision(
            PurchaseInvoiceNumberRevisionKind.Retained, "INV-1", "INV-1"));
        intent.NumberPersistence.Should().Be(new PurchaseInvoiceNumberPersistencePlan("INV-1", null));
    }

    [Fact]
    public void NewDraftReservesOnlyItsNewKey()
    {
        var intent = PurchaseInvoiceDraftSaveIntent.Create(Snapshot(), null, Order,
            [Receipt], [Price], []);
        intent.NumberPersistence.Should().Be(new PurchaseInvoiceNumberPersistencePlan("INV-1", null));
    }

    [Theory]
    [InlineData("INV-1", "OTHER-1")]
    [InlineData("OLD-1", "OTHER-1")]
    [InlineData("old-1", "OLD-1")]
    public void RevisionRejectsSavedNumberKeyNotProvenByOwner(string savedKey, string ownerKey)
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, savedKey);
        Action act = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, ownerKey, false)]);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
    }

    [Fact]
    public void RevisionChangingNumberRejectsAnotherOwnerIncludingSoftDeleted()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "OLD-1");
        Action act = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
            Order, [Receipt], [Price], [new(5, 7, 20, "OLD-1", false), new(5, 7, 21, "INV-1", true)]);
        act.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void RevisionRejectsMissingDeletedOrWrongScopeSavedNumberOwner()
    {
        var saved = new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, "OLD-1");
        PurchaseInvoiceNumberOwner[][] invalidOwners =
        [
            [],
            [new(5, 7, 20, "OLD-1", true)],
            [new(6, 7, 20, "OLD-1", false)],
            [new(5, 8, 20, "OLD-1", false)]
        ];
        foreach (var owners in invalidOwners)
        {
            Action act = () => PurchaseInvoiceDraftSaveIntent.CreateRevision(Snapshot(), saved, 4,
                Order, [Receipt], [Price], owners);
            act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
        }
    }
}
