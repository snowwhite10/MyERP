using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// SD-RETURN-POST-001～004 (WU-SD-008a5a, ADR-190, BR-SD-043/044/047/049): sales return SUBMIT on real SQL with the
/// real SD Draft builder, SYS approval workflow, FI credit posting (credit note, AR, GL) and SD credit commitments. The
/// invoice is issued through the real SD ISSUE command. INV is a recording double of its public contract (the real
/// <c>SALES_RETURN</c> inbound is proven by INV-SRET-001～005); MD destinations use a recording guard.
/// </summary>
public sealed class SalesReturnSubmissionTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static readonly DateTime Now = new(2026, 11, 6, 9, 30, 0);
    private const long Customer = 21;
    private const long Warehouse = 51;

    [Fact]
    public async Task ZeroStepSubmitPostsInboundCreditAndReleasesUnbilledCommitmentInOneCommit()
    {
        // SD-RETURN-POST-001: SD-RETURN-009/013/014/017/018 — no SYS step: Draft→Pending→Posted in the SUBMIT
        // transaction; INV receives the saved lines with their original outbound movements; every line keeps its
        // inbound movement; FI credits only the billed portions (open AR falls by their base gross); the unbilled
        // portion releases SD commitment by its base gross; a Posted or stale return cannot be submitted again.
        var s = await SeedAsync();
        var draft = await CreateDraftAsync(s, "P-1",
            new SalesReturnLineInput(1, s.DeliveryA, Warehouse, null, 0.5m, [new(s.InvoiceLineA, 1m)]),
            new SalesReturnLineInput(2, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1.5m)]));
        var commitmentBefore = await CommitmentAsync(s);
        var openBefore = await OpenArAsync(s);

        var result = await SubmitAsync(s, draft.Id, 1);

        var posted = result.Return;
        Assert.Equal((ApprovalStatus.Approved, SalesReturnStatus.Posted, 3, Now),
            (result.Approval.Status, posted.Status, posted.Version, posted.PostedAt));
        Assert.Equal(result.Approval.InstanceId, posted.ApprovalInstanceId);
        var call = Assert.Single(s.Inventory.Calls);
        Assert.Equal((draft.Id, Day.AddDays(1)), (call.SourceId, call.Date));
        Assert.Equal(
            [
                new SalesReturnInboundLine(1, s.OutboundA, 31, Warehouse, null, 1.5m),
                new SalesReturnInboundLine(2, s.OutboundB, 32, Warehouse, null, 1.5m)
            ],
            call.Lines);
        Assert.Equal(RecordingInventory.Issued(call), posted.Lines.OrderBy(x => x.LineNo).Select(x => x.InvMovementId!.Value));

        var allocations = posted.Lines.SelectMany(x => x.InvoiceAllocations).ToArray();
        var billedBase = allocations.Sum(x => x.BaseGross);
        var unbilledBase = posted.BaseGross - billedBase;
        Assert.True(unbilledBase > 0m);
        Assert.Equal(openBefore - billedBase, await OpenArAsync(s));
        Assert.Equal(commitmentBefore - unbilledBase, await CommitmentAsync(s));
        await using (var db = s.Raw())
        {
            var source = await db.Database.SqlQuery<long>($"""
                SELECT id AS [Value] FROM dbo.ar_sales_source
                WHERE company_id = {s.CompanyId} AND source_type = 'Return' AND source_id = {draft.Id}
                """).SingleAsync(Ct);
            Assert.Equal(source, posted.FiCreditNoteId);
        }

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => SubmitAsync(s, draft.Id, 3));
        Assert.Equal(SalesReturnDraftCommandService.InvalidStatus, again.ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync(s, draft.Id, 1));
        Assert.Single(s.Inventory.Calls);
    }

    [Fact]
    public async Task SubmitWithApprovalStepOnlyPendsAndNeverCallsInventoryOrFinance()
    {
        // SD-RETURN-POST-002: SD-RETURN-009/010 — an applicable SYS step leaves the return Pending with its SYS link;
        // no INV inbound, no FI credit, no commitment release; a Pending return is not submitted twice.
        var s = await SeedAsync(withFlow: true);
        var draft = await CreateDraftAsync(s, "Q-1",
            new SalesReturnLineInput(1, s.DeliveryA, Warehouse, null, 0.5m, [new(s.InvoiceLineA, 1m)]));
        var fi = await FiCountsAsync(s);
        var commitment = await CommitmentAsync(s);

        var result = await SubmitAsync(s, draft.Id, 1);

        Assert.Equal((ApprovalStatus.Pending, SalesReturnStatus.Pending, 2),
            (result.Approval.Status, result.Return.Status, result.Return.Version));
        Assert.Equal(result.Approval.InstanceId, result.Return.ApprovalInstanceId);
        Assert.All(result.Return.Lines, x => Assert.Null(x.InvMovementId));
        Assert.Null(result.Return.FiCreditNoteId);
        Assert.Empty(s.Inventory.Calls);
        Assert.Equal(fi, await FiCountsAsync(s));
        Assert.Equal(commitment, await CommitmentAsync(s));
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => SubmitAsync(s, draft.Id, 2));
        Assert.Equal(SalesReturnDraftCommandService.InvalidStatus, again.ErrorCode);
    }

    [Fact]
    public async Task InventoryRefusalAndFinanceReceiptMismatchRollBackSysInventoryAndFinance()
    {
        // SD-RETURN-POST-003: SD-RETURN-022/023, BR-SD-047/049 — an INV refusal or an FI receipt that does not match
        // the saved return leaves the Draft unchanged with no SYS instance, no FI source/journal and no AR change.
        var s = await SeedAsync();
        var draft = await CreateDraftAsync(s, "R-1",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1m)]));
        var fi = await FiCountsAsync(s);
        var open = await OpenArAsync(s);

        s.Inventory.Failure = new BusinessRuleException("INV.SALES_RETURN.ORIGINAL_INVALID", "Original issue invalid.");
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => SubmitAsync(s, draft.Id, 1));
        Assert.Equal("INV.SALES_RETURN.ORIGINAL_INVALID", refused.ErrorCode);
        await AssertUntouchedAsync(s, draft.Id, fi, open);

        s.Inventory.Failure = null;
        s.TamperReceipt = true;
        var mismatch = await Assert.ThrowsAsync<SalesReturnPostingException>(() => SubmitAsync(s, draft.Id, 1));
        Assert.Equal(SalesReturnPosting.ReceiptMismatch, mismatch.ErrorCode);
        await AssertUntouchedAsync(s, draft.Id, fi, open);

        // INV answering for other lines (or as a replay) is a conflict, not a success.
        s.TamperReceipt = false;
        s.Inventory.Replay = true;
        var replay = await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync(s, draft.Id, 1));
        Assert.Equal(SalesReturnPosting.InventoryConflict, replay.ErrorCode);
        await AssertUntouchedAsync(s, draft.Id, fi, open);

        // The same Draft still posts once every party agrees.
        s.Inventory.Replay = false;
        Assert.Equal(SalesReturnStatus.Posted, (await SubmitAsync(s, draft.Id, 1)).Return.Status);
    }

    [Fact]
    public async Task SubmissionRechecksBalancesAndRefusesAStaleSnapshotUntilTheDraftIsSavedAgain()
    {
        // SD-RETURN-POST-004: SD-RETURN-004/011/012 — Drafts reserve nothing, so submission rechecks under the locks:
        // a second Draft of an already returned line is RETURN_EXCEEDED; a Draft whose amount snapshot was taken before
        // another unbilled return posted is SNAPSHOT_STALE (409) until an edit rebuilds it; a wholly unbilled return
        // posts without FI and releases only commitment.
        var s = await SeedAsync();
        var first = await CreateDraftAsync(s, "S-1",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1.5m)]));
        var second = await CreateDraftAsync(s, "S-2",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1.5m)]));
        Assert.Equal(SalesReturnStatus.Posted, (await SubmitAsync(s, first.Id, 1)).Return.Status);
        var exceeded = await Assert.ThrowsAsync<BusinessRuleException>(() => SubmitAsync(s, second.Id, 1));
        Assert.Equal(SalesReturnCreateService.ReturnExceeded, exceeded.ErrorCode);
        Assert.Equal(0m, exceeded.Extensions!["remaining"]);

        // Delivery A: 3 delivered, 2 invoiced → 1 unbilled left; both Drafts take half of what was left then.
        var unbilled = new SalesReturnLineInput(1, s.DeliveryA, Warehouse, null, 0.5m, null);
        var third = await CreateDraftAsync(s, "S-3", unbilled);
        var fourth = await CreateDraftAsync(s, "S-4", unbilled);
        var fi = await FiCountsAsync(s);
        var open = await OpenArAsync(s);
        var commitment = await CommitmentAsync(s);

        var wholly = (await SubmitAsync(s, third.Id, 1)).Return;
        Assert.Equal((SalesReturnStatus.Posted, (long?)null), (wholly.Status, wholly.FiCreditNoteId));
        Assert.Equal(fi, await FiCountsAsync(s));
        Assert.Equal(open, await OpenArAsync(s));
        Assert.Equal(commitment - wholly.BaseGross, await CommitmentAsync(s));

        var stale = await Assert.ThrowsAsync<ConflictException>(() => SubmitAsync(s, fourth.Id, 1));
        Assert.Equal(SalesReturnPosting.SnapshotStale, stale.ErrorCode);
        await AssertUntouchedAsync(s, fourth.Id, fi, open);

        // Saving the Draft again takes the exact remainder of the order line; then it posts and the commitment of the
        // order's uninvoiced share is fully released.
        await using (var scope = s.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SalesReturnDraftCommandService>().UpdateAsync(fourth.Id, 1,
                new SalesReturnDraftInput("S-4", Day.AddDays(1), null, [unbilled]), Ct);
        var last = (await SubmitAsync(s, fourth.Id, 2)).Return;
        Assert.Equal(SalesReturnStatus.Posted, last.Status);
        Assert.NotEqual(wholly.BaseGross, last.BaseGross);
        Assert.Equal(commitment - wholly.BaseGross - last.BaseGross, await CommitmentAsync(s));
        Assert.Equal(0m, await CommitmentAsync(s));
    }

    [Fact]
    public async Task SubmitNeedsTheSubmitGrantAndAVisibleLiveReturn()
    {
        // SD-RETURN-POST-005: BR-SD-043 permissions — no SAL.RETURN.SUBMIT is 403; a return whose order is outside the
        // caller's Self scope (another owner), an unknown ID and a soft-deleted Draft are 404 NOT_FOUND; nothing changes.
        var s = await SeedAsync();
        var draft = await CreateDraftAsync(s, "T-1",
            new SalesReturnLineInput(1, s.DeliveryB, Warehouse, null, 0m, [new(s.InvoiceLineB, 1m)]));
        var fi = await FiCountsAsync(s);
        var open = await OpenArAsync(s);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            SubmitAsAsync(s, draft.Id, new Dictionary<string, DataScope> { ["SAL.RETURN.EDIT"] = DataScope.Company }));
        var hidden = await Assert.ThrowsAsync<NotFoundException>(() =>
            SubmitAsAsync(s, draft.Id, new Dictionary<string, DataScope> { ["SAL.RETURN.SUBMIT"] = DataScope.Self }));
        Assert.Equal(SalesReturnDraftCommandService.NotFoundCode, hidden.ErrorCode);
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => SubmitAsync(s, long.MaxValue, 1));
        Assert.Equal(SalesReturnDraftCommandService.NotFoundCode, unknown.ErrorCode);
        await AssertUntouchedAsync(s, draft.Id, fi, open);

        await using (var scope = s.Provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SalesReturnDraftCommandService>().DeleteAsync(draft.Id, 1, Ct);
        var deleted = await Assert.ThrowsAsync<NotFoundException>(() => SubmitAsync(s, draft.Id, 2));
        Assert.Equal(SalesReturnDraftCommandService.NotFoundCode, deleted.ErrorCode);
        Assert.Empty(s.Inventory.Calls);
        Assert.Equal(fi, await FiCountsAsync(s));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>SUBMIT through the production service with an explicit (non-admin) grant set for the submitter.</summary>
    private static async Task<SalesReturnSubmissionResult> SubmitAsAsync(Seeded s, long id,
        IReadOnlyDictionary<string, DataScope> grants)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(s.UserId, s.CompanyId, null, null, false, grants));
        var company = sp.GetRequiredService<SalesCompanyAccess>();
        return await new SalesReturnSubmissionService(sp.GetRequiredService<MyErpDbContext>(),
                new SalesActionAccess(company, auth.Object), company, sp.GetRequiredService<SalesReturnDraftBuilder>(),
                sp.GetRequiredService<SalesReturnPosting>(), sp.GetRequiredService<ICustomerCreditService>(),
                sp.GetRequiredService<IApprovalWorkflowService>())
            .SubmitAsync(id, 1, Ct);
    }

    private sealed record Seeded(long CompanyId, long UserId, SalesDelivery Delivery, SalesInvoice Invoice,
        ServiceProvider Provider, Func<MyErpDbContext> Raw, RecordingInventory Inventory, ReceiptSwitch Switch)
    {
        public long DeliveryA => Delivery.Lines.Single(x => x.LineNo == 1).Id;
        public long DeliveryB => Delivery.Lines.Single(x => x.LineNo == 2).Id;
        public long OutboundA => Delivery.Lines.Single(x => x.LineNo == 1).InvMovementId!.Value;
        public long OutboundB => Delivery.Lines.Single(x => x.LineNo == 2).InvMovementId!.Value;
        public long InvoiceLineA => Invoice.Lines.Single(x => x.LineNo == 1).Id;
        public long InvoiceLineB => Invoice.Lines.Single(x => x.LineNo == 2).Id;

        public bool TamperReceipt
        {
            get => Switch.Tamper;
            set => Switch.Tamper = value;
        }
    }

    private sealed class ReceiptSwitch
    {
        public bool Tamper { get; set; }
    }

    private static async Task<SalesReturn> CreateDraftAsync(Seeded s, string key, params SalesReturnLineInput[] lines)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<SalesReturnCreateService>()
            .CreateAsync(new SalesReturnDraftInput(key, Day.AddDays(1), null, lines), Ct);
        return outcome.Return;
    }

    private static async Task<SalesReturnSubmissionResult> SubmitAsync(Seeded s, long id, int version)
    {
        await using var scope = s.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SalesReturnSubmissionService>()
            .SubmitAsync(id, version, Ct);
    }

    private static async Task AssertUntouchedAsync(Seeded s, long returnId, (int, int, int) fi, decimal open)
    {
        await using var db = s.Raw();
        var row = await db.Set<SalesReturn>().AsNoTracking().Include(x => x.Lines)
            .SingleAsync(x => x.Id == returnId, Ct);
        Assert.Equal((SalesReturnStatus.Draft, 1, (long?)null, (long?)null),
            (row.Status, row.Version, row.ApprovalInstanceId, row.FiCreditNoteId));
        Assert.All(row.Lines, x => Assert.Null(x.InvMovementId));
        var instances = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM dbo.sys_approval_instance
            WHERE company_id = {s.CompanyId} AND document_type = 'SAL.RETURN' AND document_id = {returnId}
            """).SingleAsync(Ct);
        Assert.Equal(0, instances);
        Assert.Equal(fi, await FiCountsAsync(s));
        Assert.Equal(open, await OpenArAsync(s));
    }

    /// <summary>SD commitment of the seeded order through the production query (FI credit lock not needed to read).</summary>
    private static async Task<decimal> CommitmentAsync(Seeded s)
    {
        await using var db = s.Raw();
        return (await SalesOrderCreditCheck.CommitmentsAsync(db, s.CompanyId, Customer, Ct)).Single().BaseGross;
    }

    private static async Task<decimal> OpenArAsync(Seeded s)
    {
        await using var db = s.Raw();
        return await db.Database.SqlQuery<decimal>(
            $"SELECT open_base_amount AS [Value] FROM dbo.ar_posted_receivable WHERE company_id = {s.CompanyId}")
            .SingleAsync(Ct);
    }

    private static async Task<(int Sources, int Allocations, int Journals)> FiCountsAsync(Seeded s)
    {
        await using var db = s.Raw();
        var sources = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_sales_source WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var allocations = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_credit_allocation WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var journals = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        return (sources, allocations, journals);
    }

    /// <summary>
    /// Company with TWD, accounts 1131/2171/4101, an open November period and the SD invoice map; an Approved USD order
    /// (rate 31) for customer 21 (A 3 units 100.00 + 5.00; B 1.5 units 15.00 + 0.75), a Posted delivery of A 3 + B 1.5
    /// with INV outbound movements, and an invoice of A 2 + B 1.5 issued through the real SD ISSUE command. Optionally
    /// an active SAL.RETURN SYS flow with one approver step.
    /// </summary>
    private async Task<Seeded> SeedAsync(bool withFlow = false)
    {
        long companyId;
        SystemUser submitter = NewUser(), approver = NewUser();
        await using (var setup = Organization(null))
        {
            var company = new SystemCompany { Code = "RS" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "Sales return submission test" };
            setup.AddRange(company, submitter, approver);
            await setup.SaveChangesAsync(Ct);
            companyId = company.Id;
            setup.AddRange(new SystemUserCompany { CompanyId = companyId, UserId = submitter.Id },
                new SystemUserCompany { CompanyId = companyId, UserId = approver.Id });
            await setup.SaveChangesAsync(Ct);
        }

        var inventory = new RecordingInventory();
        var receipts = new ReceiptSwitch();
        var provider = Provider(companyId, submitter.Id, inventory, receipts);
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<CurrencyService>().CreateAsync("TWD", "New Taiwan Dollar", "NT$", 2, true, Ct);
            var accounts = sp.GetRequiredService<AccountService>();
            var assets = await accounts.CreateAsync("1", "Assets", null, AccountType.Asset, null, Ct);
            await accounts.CreateAsync("1131", "Receivable", assets.Id, null, null, Ct);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, Ct);
            await accounts.CreateAsync("2171", "Output tax", liabilities.Id, null, null, Ct);
            var revenue = await accounts.CreateAsync("4", "Revenue", null, AccountType.Revenue, null, Ct);
            await accounts.CreateAsync("4101", "Sales", revenue.Id, null, null, Ct);
            await sp.GetRequiredService<GlPeriodWriteService>().CreateAsync(companyId,
                new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), true, Ct);
            var db = sp.GetRequiredService<MyErpDbContext>();
            db.Add(new SalesInvoiceAccountMapping
            {
                ReceivableAccountCode = "1131", RevenueAccountCode = "4101", OutputTaxAccountCode = "2171"
            });
            await db.SaveChangesAsync(Ct);
            if (withFlow)
                await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("SAL.RETURN", "Return", false,
                    [new ApprovalStepInput("Approver", ApproverType.User, null, approver.Id, null)]), Ct);
        }

        Func<MyErpDbContext> raw = () => Raw(companyId);
        var order = await SeedOrderAsync(companyId);
        SalesDelivery delivery;
        await using (var db = raw())
        {
            delivery = new SalesDelivery { CompanyId = companyId, DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10],
                OrderId = order.Id, BusinessDate = Day, Status = SalesDeliveryStatus.Posted, PostedAt = Now };
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 1, OrderLineId = order.Lines[0].Id,
                ItemId = order.Lines[0].ItemId, WarehouseId = Warehouse, Quantity = 3m, InvMovementId = 8_100_001 });
            delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 2, OrderLineId = order.Lines[1].Id,
                ItemId = order.Lines[1].ItemId, WarehouseId = Warehouse, Quantity = 1.5m, InvMovementId = 8_100_002 });
            db.Add(delivery);
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<SalesInvoiceIssueService>().IssueAsync(
                new SalesInvoiceIssueInput("INV-1", Day,
                    [new SalesInvoiceLineInput(1, delivery.Lines[0].Id, 2m), new SalesInvoiceLineInput(2, delivery.Lines[1].Id, 1.5m)]),
                Ct);
        SalesInvoice invoice;
        await using (var db = raw())
            invoice = await db.Set<SalesInvoice>().AsNoTracking().Include(x => x.Lines).SingleAsync(Ct);
        Assert.Equal(SalesInvoiceStatus.Posted, invoice.Status);
        return new Seeded(companyId, submitter.Id, delivery, invoice, provider, raw, inventory, receipts);
    }

    private async Task<SalesOrder> SeedOrderAsync(long companyId)
    {
        await using var db = Raw(companyId);
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10], CustomerId = Customer, CustomerCode = "C21",
            CustomerName = "Customer", BusinessDate = Day, CurrencyCode = "USD", BaseCurrencyCode = "TWD",
            ExchangeRate = 31m, OriginalNet = 115m, OriginalTax = 5.75m, OriginalGross = 120.75m, BaseNet = 3565m,
            BaseTax = 178m, BaseGross = 3743m };
        order.Lines.Add(OrderLine(companyId, 1, 31, 3m, 100m, 5m, 3100m, 155m));
        order.Lines.Add(OrderLine(companyId, 2, 32, 1.5m, 15m, 0.75m, 465m, 23m));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-11-06' WHERE id = {0}", [order.Id], Ct);
        return order;
    }

    private static SalesOrderLine OrderLine(long companyId, int lineNo, long itemId, decimal quantity, decimal net,
        decimal tax, decimal baseNet, decimal baseTax) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = itemId, ItemCode = "I" + itemId, ItemName = "Item",
        Quantity = quantity, UnitPrice = decimal.Round(net / quantity, 6), TaxRateId = 41, TaxRatePercent = 5m,
        OriginalNet = net, OriginalTax = tax, OriginalGross = net + tax, BaseNet = baseNet, BaseTax = baseTax,
        BaseGross = baseNet + baseTax
    };

    private static SystemUser NewUser()
    {
        var name = "srs" + Guid.NewGuid().ToString("N")[..16];
        return new SystemUser { Username = name, NormalizedUsername = name.ToUpperInvariant(), PasswordHash = "x" };
    }

    /// <summary>Records SD's INV calls and answers with fresh movement IDs; can refuse or answer as a replay.</summary>
    private sealed class RecordingInventory : IExternalInventoryPostingService
    {
        private static long next = 9_000_000;
        public Exception? Failure { get; set; }
        public bool Replay { get; set; }
        public List<(long SourceId, DateOnly Date, SalesReturnInboundLine[] Lines, long[] Ids)> Calls { get; } = [];

        public static long[] Issued((long SourceId, DateOnly Date, SalesReturnInboundLine[] Lines, long[] Ids) call) =>
            call.Ids;

        public Task<InventoryPostingResult> PostPurchaseInboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<PurchaseInboundLine> lines, CancellationToken token = default) =>
            throw new NotSupportedException();

        public Task<InventoryPostingResult> PostSalesOutboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<SalesOutboundLine> lines, CancellationToken token = default) =>
            throw new NotSupportedException();

        public Task<SalesReturnInboundResult> PostSalesReturnInboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<SalesReturnInboundLine> lines, CancellationToken token = default)
        {
            if (Failure is { } failure)
                throw failure;
            var ids = lines.Select(_ => Interlocked.Increment(ref next)).ToArray();
            if (!Replay)
                Calls.Add((sourceId, businessDate, [.. lines], ids));
            return Task.FromResult(new SalesReturnInboundResult(Replay,
                lines.Select((x, i) => new SalesReturnInboundMovement(x.LineNo, ids[i], x.OriginalMovementId, 3m))
                    .ToArray()));
        }
    }

    /// <summary>Real FI credit posting whose receipt can be altered to prove SD verifies it before commit.</summary>
    private sealed class TamperingCredit(ISalesCreditPostingService inner, ReceiptSwitch receipts)
        : ISalesCreditPostingService
    {
        public async Task<SalesCreditPostingResult> PostInCurrentTransactionAsync(SalesCreditPostingCommand? command,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.PostInCurrentTransactionAsync(command, cancellationToken);
            return receipts.Tamper && result.Receipt is { } receipt
                ? result with { Receipt = receipt with { GrossAmount = receipt.GrossAmount + 0.01m } }
                : result;
        }
    }

    private sealed class CountingNumbers : INumberGenerator
    {
        private int next;

        public Task<string> NextAsync(string documentType, DateOnly documentDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(documentType[4..6] + Guid.NewGuid().ToString("N")[..8] + Interlocked.Increment(ref next));
    }

    private sealed class PassGuard : IInventoryReferenceGuard
    {
        public Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId, Func<Task<T>> action,
            CancellationToken token = default) => action();

        public Task<T> RunWithCodesAsync<T>(long itemId, long warehouseId, long? locationId,
            Func<InventoryReferenceCodes, Task<T>> action, CancellationToken token = default) =>
            action(new InventoryReferenceCodes("I" + itemId, "W" + warehouseId, null, ItemTrackingMode.None));

        public Task RequireWarehousesAsync(IReadOnlyCollection<long> warehouseIds, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    private ServiceProvider Provider(long companyId, long userId, RecordingInventory inventory, ReceiptSwitch receipts)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new MasterDataModule().AddServices(services, configuration);
        new FinanceModule().AddServices(services, configuration);
        new SalesModule().AddServices(services, configuration);
        services.AddSingleton<IModuleDbModel, SystemOrganizationDbModel>();
        var company = new FixedCompany(companyId);
        services.AddSingleton<ICurrentCompany>(company);
        services.AddSingleton<ICurrentUser>(new FixedUser(userId));
        services.AddSingleton<IClock>(new FixedClock(Now));
        services.AddSingleton<INumberGenerator>(new CountingNumbers());
        services.AddSingleton<IInventoryReferenceGuard>(new PassGuard());
        services.AddSingleton<IExternalInventoryPostingService>(inventory);
        services.AddScoped<IApprovalWorkflowService>(sp => new ApprovalWorkflowService(
            sp.GetRequiredService<MyErpDbContext>(), sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ICurrentUser>(), new AuditTrail(), []));
        var real = services.Last(x => x.ServiceType == typeof(ISalesCreditPostingService));
        services.Remove(real);
        services.AddScoped(real.ImplementationType!);
        services.AddScoped<ISalesCreditPostingService>(sp => new TamperingCredit(
            (ISalesCreditPostingService)sp.GetRequiredService(real.ImplementationType!), receipts));
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, companyId, null, null, true, new Dictionary<string, DataScope>()));
        services.AddSingleton(auth.Object);
        var customers = new Mock<ICustomerLookup>();
        customers.Setup(x => x.RequireApprovedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken _) => new CustomerIdentity(id, "C" + id, "Customer"));
        services.AddSingleton(customers.Object);
        services.AddScoped(sp => new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<ICurrentUser>(), company))).Options,
            sp.GetServices<IModuleDbModel>(), company));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private MyErpDbContext Raw(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new SalesModule().AddServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Now), new FixedUser(7),
                    company))).Options,
            provider.GetServices<IModuleDbModel>(), company);
    }

    private MyErpDbContext Organization(long? companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Now), new FixedUser(7),
                new FixedCompany(companyId)))).Options,
        [new SystemOrganizationDbModel()], new FixedCompany(companyId));
}
