using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftSaveServiceTests
{
    private sealed class Factory(PurchaseInvoiceDraftSaveLockedState state)
        : IPurchaseInvoiceDraftSessionFactory
    {
        public (long Company, long Supplier, long Order, long[] Receipts, long? Invoice)? Scope { get; private set; }
        public Session Session { get; } = new(state);

        public IPurchaseInvoiceDraftSaveSession Create(long companyId, long supplierId,
            long orderId, IReadOnlyList<long> receiptLineIds, long? invoiceId)
        {
            Scope = (companyId, supplierId, orderId, receiptLineIds.ToArray(), invoiceId);
            return Session;
        }
    }

    private sealed class Session(PurchaseInvoiceDraftSaveLockedState state)
        : IPurchaseInvoiceDraftSaveSession
    {
        public List<string> Calls { get; } = [];
        public int SavedRows { get; set; } = 3;
        private PurchaseInvoiceDraftEntityPlan? plan;
        public Task BeginAsync(CancellationToken ct) { Calls.Add("begin"); return Task.CompletedTask; }
        public Task<PurchaseInvoiceDraftSaveLockedState> ReadLockedAsync(CancellationToken ct)
        { Calls.Add("read"); return Task.FromResult(state); }
        public void Prepare(PurchaseInvoiceDraftEntityPlan plan, PurchaseInvoice? saved,
            IReadOnlyCollection<PurchaseInvoiceLine>? lines)
        { Calls.Add("prepare"); this.plan = plan; }
        public Task<int> SaveChangesAsync(CancellationToken ct)
        {
            Calls.Add("save");
            if (SavedRows > 0) plan!.Invoice.Id = 77;
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
            [new PurchaseInvoiceDraftLine(1, 31, 1m, 1m, 10m, 1m, .1m, 1.1m),
             new PurchaseInvoiceDraftLine(2, 31, 1m, 1m, 10m, 1m, .1m, 1.1m)]);
        var draft = PurchaseInvoiceDraftAssembly.Create(5, input, order, [receipt], [price]);
        return (draft, new(order, [receipt], [price], [], [], null, null, null));
    }

    [Fact]
    public async Task PassesServerScopeAndDistinctReceiptIdsToSession()
    {
        var (draft, state) = Valid();
        var factory = new Factory(state);
        var service = new PurchaseInvoiceDraftSaveService(factory);
        var id = await service.SaveAsync(draft, null, null, 30,
            TestContext.Current.CancellationToken);
        id.Should().Be(77);
        factory.Scope.Should().NotBeNull();
        factory.Scope!.Value.Company.Should().Be(5);
        factory.Scope.Value.Supplier.Should().Be(7);
        factory.Scope.Value.Order.Should().Be(9);
        factory.Scope.Value.Receipts.Should().Equal(31);
        factory.Scope.Value.Invoice.Should().BeNull();
        factory.Session.Calls.Should().Equal("begin", "read", "prepare", "save", "commit", "dispose");
    }

    [Fact]
    public async Task ChangedAuthorityRollsBackBeforePrepare()
    {
        var (draft, state) = Valid();
        var factory = new Factory(state with { Prices = [] });
        var service = new PurchaseInvoiceDraftSaveService(factory);
        Func<Task> act = () => service.SaveAsync(draft, null, null, 30,
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<BusinessRuleException>();
        factory.Session.Calls.Should().Equal("begin", "read", "rollback", "dispose");
    }

    [Fact]
    public async Task InvalidScopeDoesNotCreateSession()
    {
        var (draft, state) = Valid();
        var factory = new Factory(state);
        var service = new PurchaseInvoiceDraftSaveService(factory);
        Func<Task> act = () => service.SaveAsync(draft with
        { Identity = draft.Identity with { CompanyId = 0 } }, null, null, 30,
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ArgumentException>();
        factory.Scope.Should().BeNull();
    }

    [Fact]
    public async Task ZeroRowSaveRollsBackAndDisposes()
    {
        var (draft, state) = Valid();
        var factory = new Factory(state);
        factory.Session.SavedRows = 0;
        var service = new PurchaseInvoiceDraftSaveService(factory);
        Func<Task> act = () => service.SaveAsync(draft, null, null, 30,
            TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<InvalidOperationException>();
        factory.Session.Calls.Should().Equal("begin", "read", "prepare", "save", "rollback", "dispose");
    }
}
