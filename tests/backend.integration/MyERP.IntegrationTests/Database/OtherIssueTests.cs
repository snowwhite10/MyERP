using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-ISS-001～003：其他出庫草稿、庫存不足與核准交易。</summary>
public sealed class OtherIssueTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 10, 13);
    private static readonly DateTime Now = new(2026, 10, 13, 18, 0, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new OtherReceiptDbModel(), new OtherIssueDbModel()], company);
    }

    private static StockPostingService Posting(MyErpDbContext db) =>
        new(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(Now));

    private static OtherIssueService Service(MyErpDbContext db, FakeNumbers? numbers = null, ApprovalStatus status = ApprovalStatus.Approved) =>
        new(db, numbers ?? new FakeNumbers(), new InventoryReferenceGuard(db),
            new FakeWorkflow { Next = status }, Posting(db), new FixedClock(Now));

    private static async Task<(long Item, long Warehouse)> MasterAsync(MyErpDbContext db, long companyId)
    {
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        return (item.Id, warehouse.Id);
    }

    private static OtherIssueInput Input(long item, long warehouse, decimal quantity) =>
        new(Day, null, [new OtherIssueLineInput(1, item, warehouse, null, quantity, 2m)]);

    [Fact]
    public async Task InsufficientIssueRollsBackApprovalAndLeavesNoMovement()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse) = await MasterAsync(db, companyId);
        var service = Service(db);
        var draft = await service.CreateAsync(Input(item, warehouse, 1m), Ct);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.SubmitAsync(draft.Id, draft.Version, Ct));
        Assert.Equal("INV.STOCK.INSUFFICIENT", error.ErrorCode);
        db.ChangeTracker.Clear();
        Assert.Equal(InventoryDocumentStatus.Draft, (await service.GetAsync(draft.Id, Ct)).Issue.Status);
        Assert.False(await db.Set<StockMovement>().AnyAsync(Ct));
    }

    [Fact]
    public async Task ApprovedIssueDecreasesStockAndCannotBeSubmittedTwice()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var (item, warehouse) = await MasterAsync(db, companyId);
        await Posting(db).PostAsync(new InventoryPostingRequest("TEST_SEED", 1, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, null, 5m, 3m)]), Ct);
        var service = Service(db);
        var draft = await service.CreateAsync(Input(item, warehouse, 2m), Ct);

        var result = await service.SubmitAsync(draft.Id, draft.Version, Ct);

        Assert.Equal(InventoryDocumentStatus.Posted, result.Issue.Status);
        Assert.Equal(3m, await new StockBalanceQuery(db).GetOnHandAsync(item, warehouse, null, Ct));
        var movement = await db.Set<StockMovement>().SingleAsync(x => x.SourceType == OtherIssueService.SourceType, Ct);
        Assert.Equal((StockMovementDirection.Out, 2m, null), (movement.Direction, movement.Quantity, movement.UnitCost));
        Assert.Equal(OtherIssueService.NotDraft, (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.SubmitAsync(draft.Id, draft.Version, Ct))).ErrorCode);
        Assert.Equal(2, await db.Set<StockMovement>().CountAsync(Ct));
    }

    [Fact]
    public async Task ConcurrentIssuesCannotOversell()
    {
        var companyId = TestCompanies.Next();
        await using var setup = Context(companyId);
        var (item, warehouse) = await MasterAsync(setup, companyId);
        await Posting(setup).PostAsync(new InventoryPostingRequest("TEST_SEED", 2, Day,
            [new InventoryPostingLine(1, StockDirection.In, item, warehouse, null, 5m, null)]), Ct);
        var numbers = new FakeNumbers();
        var first = await Service(setup, numbers).CreateAsync(Input(item, warehouse, 4m), Ct);
        var second = await Service(setup, numbers).CreateAsync(Input(item, warehouse, 4m), Ct);
        await using var left = Context(companyId);
        await using var right = Context(companyId);

        var outcomes = await Task.WhenAll(Submit(left, first), Submit(right, second));

        Assert.Single(outcomes, x => x is null);
        Assert.Single(outcomes, x => x is BusinessRuleException { ErrorCode: "INV.STOCK.INSUFFICIENT" });
        await using var check = Context(companyId);
        Assert.Equal(1m, await new StockBalanceQuery(check).GetOnHandAsync(item, warehouse, null, Ct));
        Assert.Equal(1, await check.Set<StockMovement>().CountAsync(x => x.SourceType == OtherIssueService.SourceType, Ct));

        static async Task<Exception?> Submit(MyErpDbContext db, OtherIssue issue)
        {
            try { await Service(db).SubmitAsync(issue.Id, issue.Version, Ct); return null; }
            catch (Exception ex) { return ex; }
        }
    }

    [Fact]
    public async Task LargeEstimateIsRejectedWithoutDecimalOverflowOrNumbering()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var numbers = new FakeNumbers();
        var lines = Enumerable.Range(1, 1000).Select(i =>
            new OtherIssueLineInput(i, 1, 1, null, 9_000_000_000_000m, 9_000_000_000_000m)).ToArray();

        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            Service(db, numbers).CreateAsync(new OtherIssueInput(Day, null, lines), Ct));

        Assert.Contains("lines", error.Errors.Keys);
        Assert.Equal(0, numbers.Count);
    }

    private sealed class FakeNumbers : INumberGenerator
    {
        private int count;
        public int Count => count;
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default) =>
            Task.FromResult($"OI-{Interlocked.Increment(ref count):0000}");
    }

    private sealed class FakeWorkflow : IApprovalWorkflowService
    {
        public ApprovalStatus Next { get; init; } = ApprovalStatus.Approved;
        public Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApprovalState(1, submission.DocumentType, submission.DocumentId, Next, null, 0));
        public Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ApprovalState?>(null);
    }
}
