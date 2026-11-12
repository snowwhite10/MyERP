using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftSaveCoordinatorTests
{
    private sealed class Session(PurchaseInvoiceDraftSaveLockedState state) : IPurchaseInvoiceDraftSaveSession
    {
        public List<string> Calls { get; } = [];
        public bool FailSave { get; set; }
        public int SavedRows { get; set; } = 3;
        private PurchaseInvoiceDraftEntityPlan? plan;
        public Task BeginAsync(CancellationToken ct) { Calls.Add("begin"); return Task.CompletedTask; }
        public Task<PurchaseInvoiceDraftSaveLockedState> ReadLockedAsync(CancellationToken ct)
        { Calls.Add("read"); return Task.FromResult(state); }
        public void Prepare(PurchaseInvoiceDraftEntityPlan plan, PurchaseInvoice? saved,
            IReadOnlyCollection<PurchaseInvoiceLine>? savedLines)
        {
            Calls.Add("prepare");
            this.plan = plan;
        }
        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            Calls.Add("save");
            if (FailSave) throw new InvalidOperationException("save failed");
            if (SavedRows > 0) plan!.Invoice.Id = 77; // Simulate SQL IDENTITY propagation.
            return Task.FromResult(SavedRows);
        }
        public Task CommitAsync(CancellationToken ct) { Calls.Add("commit"); return Task.CompletedTask; }
        public Task RollbackAsync(CancellationToken ct) { Calls.Add("rollback"); return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Calls.Add("dispose"); return ValueTask.CompletedTask; }
    }

    private static (PurchaseInvoiceDraftSnapshot Draft, PurchaseInvoiceDraftSaveLockedState State) Valid()
    {
        var order = new PurchaseInvoiceOrderIdentity(5, 7, 9, "TWD", "Approved", false);
        var receipt = new PurchaseInvoiceReceiptIdentity(31, 5, 7, 9, 9, 51, 51, "TWD", "Posted", false)
            { OrderLineId = 42 };
        var price = new PurchaseInvoiceOrderLinePrice(31, 5, 9, 42, 51, 2m, 20m);
        var input = new PurchaseInvoiceDraftInput(7, 9, "TWD", new DateOnly(2028, 2, 29), "INV-1",
            [new PurchaseInvoiceDraftLine(1, 31, 1m, 1m, 10m, 1m, .1m, 1.1m)]);
        var draft = PurchaseInvoiceDraftAssembly.Create(5, input, order, [receipt], [price]);
        return (draft, new(order, [receipt], [price], [], [], null, null, null));
    }

    [Fact]
    public async Task NewDraftLocksRechecksAndSavesOnceBeforeCommit()
    {
        var (draft, state) = Valid();
        var session = new Session(state);
        var id = await PurchaseInvoiceDraftSaveCoordinator.SaveAsync(draft, null, null, 30, session,
            TestContext.Current.CancellationToken);
        id.Should().Be(77);
        session.Calls.Should().Equal("begin", "read", "prepare", "save", "commit", "dispose");
    }

    [Fact]
    public async Task FailedSaveRollsBackWithoutCommit()
    {
        var (draft, state) = Valid();
        var session = new Session(state) { FailSave = true };
        Func<Task> act = () => PurchaseInvoiceDraftSaveCoordinator.SaveAsync(draft, null, null, 30, session,
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
        session.Calls.Should().Equal("begin", "read", "prepare", "save", "rollback", "dispose");
    }

    [Fact]
    public async Task ChangedAuthorityRejectsBeforePrepareAndRollsBack()
    {
        var (draft, state) = Valid();
        var session = new Session(state with { Prices = [] });
        Func<Task> act = () => PurchaseInvoiceDraftSaveCoordinator.SaveAsync(draft, null, null, 30, session,
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<BusinessRuleException>();
        session.Calls.Should().Equal("begin", "read", "rollback", "dispose");
    }

    [Fact]
    public async Task ZeroRowSaveDoesNotCommit()
    {
        var (draft, state) = Valid();
        var session = new Session(state) { SavedRows = 0 };
        Func<Task> act = () => PurchaseInvoiceDraftSaveCoordinator.SaveAsync(draft, null, null, 30, session,
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
        session.Calls.Should().Equal("begin", "read", "prepare", "save", "rollback", "dispose");
    }
}
