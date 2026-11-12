using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceNumberClaimEntityPlanTests
{
    private static PurchaseInvoiceNumberClaim Claim(long id, long company, long supplier,
        long invoice, string key, bool active) => new()
    {
        Id = id, CompanyId = company, SupplierId = supplier, InvoiceId = invoice,
        NumberKey = key, IsActive = active
    };

    [Fact]
    public void NewNumberCreatesOneClaimWithValidatedIdentity()
    {
        var plan = PurchaseInvoiceNumberClaimEntityPlan.ForNew(5, 7, 30, "INV-1",
            [Claim(1, 6, 7, 21, "INV-1", true)]);

        plan.Changes.Should().ContainSingle();
        var change = plan.Changes[0];
        change.Kind.Should().Be(PurchaseInvoiceNumberClaimWriteKind.InsertActive);
        change.Entity.Id.Should().Be(0);
        change.Entity.CompanyId.Should().Be(5);
        change.Entity.SupplierId.Should().Be(7);
        change.Entity.InvoiceId.Should().Be(30);
        change.Entity.NumberKey.Should().Be("INV-1");
        change.Entity.IsActive.Should().BeTrue();
    }

    [Fact]
    public void RenameDeactivatesTrackedOldClaimBeforeInsertingNewClaim()
    {
        var old = Claim(10, 5, 7, 20, "OLD-1", true);
        var history = Claim(11, 5, 7, 20, "HISTORY-1", false);
        var plan = PurchaseInvoiceNumberClaimEntityPlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"), [old, history]);

        plan.Changes.Select(x => x.Kind).Should().Equal(
            PurchaseInvoiceNumberClaimWriteKind.Deactivate,
            PurchaseInvoiceNumberClaimWriteKind.InsertActive);
        plan.Changes[0].Entity.Should().BeSameAs(old);
        old.IsActive.Should().BeTrue();
        plan.Changes[0].Apply();
        old.IsActive.Should().BeFalse();
        plan.Changes[1].Entity.NumberKey.Should().Be("NEW-1");
        plan.Changes[1].Entity.IsActive.Should().BeTrue();
        history.IsActive.Should().BeFalse();
    }

    [Fact]
    public void RenameBackReactivatesExistingHistoryAndRetainedKeyHasNoChanges()
    {
        var old = Claim(10, 5, 7, 20, "OLD-1", true);
        var history = Claim(11, 5, 7, 20, "HISTORY-1", false);
        var plan = PurchaseInvoiceNumberClaimEntityPlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "HISTORY-1"), [old, history]);
        plan.Changes.Select(x => x.Kind).Should().Equal(
            PurchaseInvoiceNumberClaimWriteKind.Deactivate,
            PurchaseInvoiceNumberClaimWriteKind.ActivateExisting);
        plan.Changes[1].Entity.Should().BeSameAs(history);
        plan.Changes[0].Apply();
        plan.Changes[1].Apply();
        old.IsActive.Should().BeFalse();
        history.IsActive.Should().BeTrue();

        var retained = PurchaseInvoiceNumberClaimEntityPlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Retained, "HISTORY-1", "HISTORY-1"), [old, history]);
        retained.Changes.Should().BeEmpty();
    }

    [Fact]
    public void InvalidIdentityDuplicateClaimAndOccupiedHistoricalKeyFailClosed()
    {
        var old = Claim(10, 5, 7, 20, "OLD-1", true);
        var history = Claim(11, 5, 7, 20, "HISTORY-1", false);
        Action wrongScope = () => PurchaseInvoiceNumberClaimEntityPlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"),
            [old, Claim(11, 6, 7, 20, "HISTORY-1", false)]);
        wrongScope.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");

        Action duplicateId = () => PurchaseInvoiceNumberClaimEntityPlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"),
            [old, Claim(10, 5, 7, 20, "HISTORY-1", false)]);
        duplicateId.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");

        Action occupied = () => PurchaseInvoiceNumberClaimEntityPlan.ForNew(5, 7, 30, "HISTORY-1",
            [old, history]);
        occupied.Should().Throw<ConflictException>().Which.ErrorCode.Should().Be("PUR.INVOICE.NUMBER_EXISTS");
    }

    [Fact]
    public void MutationRejectsTrackedRowThatChangesAfterPlanning()
    {
        var old = Claim(10, 5, 7, 20, "OLD-1", true);
        var plan = PurchaseInvoiceNumberClaimEntityPlan.ForRevision(5, 7, 20,
            new(PurchaseInvoiceNumberRevisionKind.Changed, "OLD-1", "NEW-1"), [old]);
        old.NumberKey = "OTHER-1";

        Action apply = () => plan.Changes[0].Apply();
        apply.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER_SNAPSHOT");
        old.IsActive.Should().BeTrue();
    }
}
