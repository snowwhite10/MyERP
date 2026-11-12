using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftClaimTrackingTests
{
    private sealed class Company : ICurrentCompany { public long? CompanyId => 5; }

    private static MyErpDbContext Db() => new(
        new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer("Server=invalid.myerp.test;Database=myerp_unit;Integrated Security=true;Connect Timeout=1")
            .Options,
        [new PurchaseInvoiceDraftDbModel(), new PurchaseInvoiceNumberDbModel()], new Company());

    private static PurchaseInvoiceDraftEntityPlan Plan(string number, string? old = null,
        params PurchaseInvoiceNumberClaim[] claims)
    {
        var order = new PurchaseInvoiceOrderIdentity(5, 7, 9, "TWD", "Approved", false);
        var receipt = new PurchaseInvoiceReceiptIdentity(31, 5, 7, 9, 9, 51, 51, "TWD", "Posted", false)
            { OrderLineId = 42 };
        var price = new PurchaseInvoiceOrderLinePrice(31, 5, 9, 42, 51, 2m, 20m);
        var input = new PurchaseInvoiceDraftInput(7, 9, "TWD", new DateOnly(2028, 2, 29), number,
            [new PurchaseInvoiceDraftLine(1, 31, 1m, 1m, 10m, 1m, .1m, 1.1m)]);
        var snapshot = PurchaseInvoiceDraftAssembly.Create(5, input, order, [receipt], [price]);
        var owners = claims.Select(c => new PurchaseInvoiceNumberOwner(5, 7, c.InvoiceId, c.NumberKey, false)).ToArray();
        var intent = old is null
            ? PurchaseInvoiceDraftSaveIntent.Create(snapshot, null, order, [receipt], [price], owners)
            : PurchaseInvoiceDraftSaveIntent.CreateRevision(snapshot,
                new PurchaseInvoiceSavedDraft(20, 5, 7, 9, "TWD", "Draft", 4, false, old), 4,
                order, [receipt], [price], owners);
        return intent.PlanDraftEntities(old is null ? 30 : 20, claims);
    }

    private static PurchaseInvoiceNumberClaim Claim(long id, string key, bool active) => new()
    {
        Id = id, CompanyId = 5, SupplierId = 7, InvoiceId = 20,
        NumberKey = key, IsActive = active
    };

    [Fact]
    public void NewDraftAddsClaim()
    {
        using var db = Db();
        var plan = Plan("INV-1");
        var result = PurchaseInvoiceDraftClaimTracking.Prepare(db, plan);
        result.ClaimsToInsert.Should().ContainSingle();
        db.Entry(result.ClaimsToInsert[0]).State.Should().Be(EntityState.Added);
    }

    [Fact]
    public void ReusingHistoryTracksBothAndMarksBothModified()
    {
        using var db = Db();
        var old = Claim(11, "OLD-1", true);
        var history = Claim(12, "HISTORY-1", false);
        var result = PurchaseInvoiceDraftClaimTracking.Prepare(db, Plan("HISTORY-1", "OLD-1", old, history));
        result.ClaimsToInsert.Should().BeEmpty();
        result.TrackedClaims.Should().Equal(old, history);
        db.Entry(old).State.Should().Be(EntityState.Modified);
        db.Entry(history).State.Should().Be(EntityState.Modified);
    }

    [Fact]
    public void RetainingNumberTracksUnchangedHistoryAndActiveClaim()
    {
        using var db = Db();
        var active = Claim(11, "INV-1", true);
        var history = Claim(12, "OLD-1", false);
        var result = PurchaseInvoiceDraftClaimTracking.Prepare(db, Plan("INV-1", "INV-1", active, history));
        result.TrackedClaims.Should().Equal(active, history);
        result.ChangedTrackedClaims.Should().BeEmpty();
        db.Entry(active).State.Should().Be(EntityState.Unchanged);
        db.Entry(history).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public void DifferentTrackedInstanceRejectsBeforeChangingClaim()
    {
        using var db = Db();
        var active = Claim(11, "OLD-1", true);
        db.Attach(Claim(11, "OLD-1", true));
        Action act = () => PurchaseInvoiceDraftClaimTracking.Prepare(db, Plan("INV-1", "OLD-1", active));
        act.Should().Throw<InvalidOperationException>();
        active.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PendingTrackedChangeRejectsBeforeApplyingNumberTransition()
    {
        using var db = Db();
        var active = Claim(11, "OLD-1", true);
        db.Attach(active);
        db.Entry(active).Property(x => x.NumberKey).IsModified = true;
        Action act = () => PurchaseInvoiceDraftClaimTracking.Prepare(db, Plan("INV-1", "OLD-1", active));
        act.Should().Throw<InvalidOperationException>();
        active.IsActive.Should().BeTrue();
    }

    [Fact]
    public void InvalidDraftPlanDoesNotAttachOrChangeClaims()
    {
        using var db = Db();
        var active = Claim(11, "OLD-1", true);
        var plan = Plan("INV-1", "OLD-1", active);
        plan.Invoice.Version++;
        Action act = () => PurchaseInvoiceDraftClaimTracking.Prepare(db, plan);
        act.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>();
        active.IsActive.Should().BeTrue();
        db.ChangeTracker.Entries<PurchaseInvoiceNumberClaim>().Should().BeEmpty();
    }
}
