using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-007a10（REQ_F4114.2.1／2.6，PUR-RECV-DEL-001～004、PUR-RECV-LST-001～004）：收貨 Draft 軟刪、LIST 與 CSV，
/// 真實 SQL 驗資料範圍、篩選、穩定排序及刪除後零變更。
/// </summary>
public sealed class PurchaseReceiptQueryTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Delete = PurchasingPermissions.ReceiptDelete;

    [Fact] // PUR-RECV-DEL-001
    public async Task DeleteSoftDeletesDraftKeepsNumberLinesAndTrackingAndHidesItEverywhere()
    {
        var company = TestCompanies.Next();
        var source = await SeedOrderAsync(company, "PO-DEL-1");
        await using var db = Context(company);
        var created = await Drafts(db, company).CreateAsync(Save(source, 2m, new DateOnly(2028, 2, 29)), Ct);

        await Lifecycle(db, company).DeleteAsync(created.Id, created.Version, Ct);

        await using var raw = database.CreateRawContext();
        var row = await raw.Database.SqlQueryRaw<DeletedRow>(
            "SELECT is_deleted, deleted_at, document_no, status, version FROM dbo.pur_purchase_receipt WHERE id={0}",
            created.Id).SingleAsync(Ct);
        Assert.True(row.IsDeleted);
        Assert.Equal(new DateTime(2028, 2, 29), row.DeletedAt);
        Assert.Equal(created.DocumentNo, row.DocumentNo);
        Assert.Equal("Draft", row.Status);
        Assert.True(row.Version > created.Version);
        Assert.Equal(1, await raw.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.pur_purchase_receipt_line WHERE purchase_receipt_id={0}", created.Id).SingleAsync(Ct));
        Assert.Equal(0m, await raw.Database.SqlQueryRaw<decimal>(
            "SELECT pending_receipt_reserved AS [Value] FROM dbo.pur_purchase_order_line WHERE id={0}", source.LineId).SingleAsync(Ct));

        await using var fresh = Context(company);
        await Assert.ThrowsAsync<NotFoundException>(() => Drafts(fresh, company).GetAsync(created.Id, Ct));
        Assert.Equal(0, (await Queries(fresh, company).ListAsync(new PurchaseReceiptQuery(), Ct)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => Lifecycle(fresh, company).DeleteAsync(created.Id, row.Version, Ct));
    }

    [Fact] // PUR-RECV-DEL-002／003
    public async Task DeleteRejectsStaleVersionAndNonDraftStatusesWithoutChanges()
    {
        var company = TestCompanies.Next();
        var source = await SeedOrderAsync(company, "PO-DEL-2");
        await using var db = Context(company);
        var created = await Drafts(db, company).CreateAsync(Save(source, 1m, new DateOnly(2028, 3, 1)), Ct);
        await Assert.ThrowsAsync<ConflictException>(() => Lifecycle(db, company).DeleteAsync(created.Id, created.Version + 1, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => Lifecycle(db, company).DeleteAsync(created.Id, 0, Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => Lifecycle(db, company).DeleteAsync(0, 1, Ct));

        foreach (var status in new[] { "PendingApproval", "Rejected", "Posted" })
        {
            await using (var raw = database.CreateRawContext())
                await raw.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE dbo.pur_purchase_receipt SET status={status} WHERE id={created.Id}", Ct);
            await using var fresh = Context(company);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Lifecycle(fresh, company).DeleteAsync(created.Id, created.Version, Ct));
            Assert.Equal("PUR.RECEIPT.NOT_DELETABLE", error.ErrorCode);
        }
        await using var check = database.CreateRawContext();
        Assert.False(await check.Database.SqlQueryRaw<bool>(
            "SELECT is_deleted AS [Value] FROM dbo.pur_purchase_receipt WHERE id={0}", created.Id).SingleAsync(Ct));
        Assert.Equal(created.Version, await check.Database.SqlQueryRaw<int>(
            "SELECT version AS [Value] FROM dbo.pur_purchase_receipt WHERE id={0}", created.Id).SingleAsync(Ct));
    }

    [Fact] // PUR-RECV-DEL-004
    public async Task DeleteHidesOtherOwnerOtherCompanyAndRequiresDeletePermission()
    {
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        var source = await SeedOrderAsync(company, "PO-DEL-3");
        await using var db = Context(company);
        var created = await Drafts(db, company).CreateAsync(Save(source, 1m, new DateOnly(2028, 3, 1)), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Lifecycle(db, company, userId: 8).DeleteAsync(created.Id, created.Version, Ct));
        await using var foreign = Context(other);
        await Assert.ThrowsAsync<NotFoundException>(() => Lifecycle(foreign, other).DeleteAsync(created.Id, created.Version, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Lifecycle(db, company, grants: PurchasingPermissions.ReceiptEdit)
            .DeleteAsync(created.Id, created.Version, Ct));
        await using var check = database.CreateRawContext();
        Assert.False(await check.Database.SqlQueryRaw<bool>(
            "SELECT is_deleted AS [Value] FROM dbo.pur_purchase_receipt WHERE id={0}", created.Id).SingleAsync(Ct));
    }

    [Fact] // PUR-RECV-LST-001
    public async Task ListFiltersSortsAndPagesStablyAndExcludesLegacyRows()
    {
        var company = TestCompanies.Next();
        var first = await SeedOrderAsync(company, "PO-LST-A");
        var second = await SeedOrderAsync(company, "PO-LST-B");
        await using var db = Context(company);
        var drafts = Drafts(db, company);
        var a = await drafts.CreateAsync(Save(first, 1m, new DateOnly(2028, 1, 31)), Ct);
        var b = await drafts.CreateAsync(Save(first, 2m, new DateOnly(2028, 2, 29)), Ct);
        var c = await drafts.CreateAsync(Save(second, 3m, new DateOnly(2028, 2, 29)), Ct);
        await using (var raw = database.CreateRawContext())
        {
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt SET status='PendingApproval' WHERE id={c.Id}", Ct);
            await raw.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,owner_id,created_at,created_by,version)
                VALUES ({company},{first.OrderId},71,'TWD','Posted',0,7,'2026-01-01',1,1)
                """, Ct);
        }
        await using var read = Context(company);
        var queries = Queries(read, company);

        var all = await queries.ListAsync(new PurchaseReceiptQuery(), Ct);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(new[] { c.Id, b.Id, a.Id }, all.Items.Select(x => x.Id)); // -businessDate, ID descending tiebreak
        var top = all.Items[0];
        Assert.Equal(c.DocumentNo, top.DocumentNo);
        Assert.Equal("PO-LST-B", top.PurchaseOrderDocumentNo);
        Assert.Equal(second.OrderId, top.PurchaseOrderId);
        Assert.Equal("PendingApproval", top.Status);
        Assert.Equal(71, top.SupplierId);
        Assert.Equal("TWD", top.Currency);
        Assert.Equal(1, top.LineCount);
        Assert.Equal(3m, top.TotalQuantity);
        Assert.Equal(c.Version, top.Version);

        Assert.Equal(new[] { a.Id, b.Id, c.Id }, (await queries.ListAsync(new PurchaseReceiptQuery { Sort = "documentNo" }, Ct)).Items.Select(x => x.Id));
        Assert.Equal(new[] { a.Id, b.Id, c.Id }, (await queries.ListAsync(new PurchaseReceiptQuery { Sort = "businessDate" }, Ct)).Items.Select(x => x.Id));
        var page2 = await queries.ListAsync(new PurchaseReceiptQuery { Page = 2, PageSize = 2 }, Ct);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(a.Id, Assert.Single(page2.Items).Id);
        Assert.Equal(new[] { a.Id, b.Id }, (await queries.ListAsync(new PurchaseReceiptQuery { PurchaseOrderId = first.OrderId }, Ct)).Items.Select(x => x.Id).Order());
        Assert.Equal(c.Id, Assert.Single((await queries.ListAsync(new PurchaseReceiptQuery { Status = "PendingApproval" }, Ct)).Items).Id);
        Assert.Equal(c.Id, Assert.Single((await queries.ListAsync(new PurchaseReceiptQuery { Keyword = " LST-B " }, Ct)).Items).Id);
        Assert.Equal(a.Id, Assert.Single((await queries.ListAsync(new PurchaseReceiptQuery { Keyword = a.DocumentNo }, Ct)).Items).Id);
        Assert.Equal(a.Id, Assert.Single((await queries.ListAsync(new PurchaseReceiptQuery { To = new DateOnly(2028, 2, 28) }, Ct)).Items).Id);
        Assert.Equal(2, (await queries.ListAsync(new PurchaseReceiptQuery { From = new DateOnly(2028, 2, 29), To = new DateOnly(2028, 2, 29) }, Ct)).TotalCount);
        Assert.Empty((await queries.ListAsync(new PurchaseReceiptQuery { Status = "Posted" }, Ct)).Items);
    }

    [Theory] // PUR-RECV-LST-002
    [InlineData("from")]
    [InlineData("purchaseOrderId")]
    [InlineData("status")]
    [InlineData("sort")]
    [InlineData("pageSize")]
    public async Task ListRejectsInvalidParameters(string field)
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var query = field switch
        {
            "from" => new PurchaseReceiptQuery { From = new DateOnly(2028, 3, 1), To = new DateOnly(2028, 2, 29) },
            "purchaseOrderId" => new PurchaseReceiptQuery { PurchaseOrderId = 0 },
            "status" => new PurchaseReceiptQuery { Status = "Cancelled" },
            "sort" => new PurchaseReceiptQuery { Sort = "supplierId" },
            _ => new PurchaseReceiptQuery { PageSize = 0 }
        };
        await Assert.ThrowsAsync<RequestValidationException>(() => Queries(db, company).ListAsync(query, Ct));
    }

    [Fact] // PUR-RECV-LST-003
    public async Task ListAndExportApplyViewScopeAndCompany()
    {
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        var source = await SeedOrderAsync(company, "PO-LST-S");
        await using var db = Context(company);
        var mine = await Drafts(db, company).CreateAsync(Save(source, 1m, new DateOnly(2028, 3, 1)), Ct);
        await Drafts(db, company, userId: 8).CreateAsync(Save(source, 1m, new DateOnly(2028, 3, 2)), Ct);
        await using var read = Context(company);
        var own = await Queries(read, company).ListAsync(new PurchaseReceiptQuery(), Ct);
        Assert.Equal(mine.Id, Assert.Single(own.Items).Id);
        Assert.Equal(2, (await Queries(read, company, DataScope.Company).ListAsync(new PurchaseReceiptQuery(), Ct)).TotalCount);
        await using var foreign = Context(other);
        Assert.Equal(0, (await Queries(foreign, other, DataScope.Company).ListAsync(new PurchaseReceiptQuery(), Ct)).TotalCount);
        await Assert.ThrowsAsync<ForbiddenException>(() => Queries(read, company, grants: [PurchasingPermissions.ReceiptEdit])
            .ListAsync(new PurchaseReceiptQuery(), Ct));

        // EXPORT narrower than VIEW: intersection of both action scopes.
        using var narrow = new MemoryStream();
        var count = await Queries(read, company, DataScope.Company, exportScope: DataScope.Self)
            .ExportAsync(new PurchaseReceiptQuery(), "en", narrow, token: Ct);
        Assert.Equal(1, count);
        await Assert.ThrowsAsync<ForbiddenException>(() => Queries(read, company, DataScope.Company,
            grants: [PurchasingPermissions.ReceiptView]).ExportAsync(new PurchaseReceiptQuery(), "en", new MemoryStream(), token: Ct));
    }

    [Fact] // PUR-RECV-LST-004
    public async Task ExportWritesSavedLinesInListOrderAndRefusesOverLimitWithoutOutput()
    {
        var company = TestCompanies.Next();
        var source = await SeedOrderAsync(company, "=PO-CSV");
        await using var db = Context(company);
        var drafts = Drafts(db, company);
        var a = await drafts.CreateAsync(new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 1, 1),
            [new(1, source.LineId, 1m, 91), new(2, source.LineId, 0.5m, 92)]), Ct);
        var b = await drafts.CreateAsync(Save(source, 2m, new DateOnly(2028, 2, 29)), Ct);
        await using var read = Context(company);
        var queries = Queries(read, company, DataScope.Company);

        using var output = new MemoryStream();
        long? announced = null;
        var count = await queries.ExportAsync(new PurchaseReceiptQuery { Page = 9, PageSize = 1 }, "zh-Hant", output,
            rows => { announced = rows; return Task.CompletedTask; }, token: Ct);
        Assert.Equal(3, count);
        Assert.Equal(3, announced);
        var bytes = output.ToArray();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal(PurchaseReceiptCsv.HeaderLine("zh-Hant"), lines[0]);
        Assert.StartsWith($"{b.DocumentNo},2028-02-29,草稿,'=PO-CSV,71,TWD,1,81,2,91,4,0", lines[1]);
        Assert.StartsWith($"{a.DocumentNo},2028-01-01,草稿,'=PO-CSV,71,TWD,1,81,1,91,4,0", lines[2]);
        Assert.StartsWith($"{a.DocumentNo},2028-01-01,草稿,'=PO-CSV,71,TWD,2,81,0.5,92,4,0", lines[3]);
        Assert.Equal("", lines[4]);

        using var refused = new MemoryStream();
        var limit = await Assert.ThrowsAsync<BusinessRuleException>(() => queries.ExportAsync(new PurchaseReceiptQuery(),
            "en", refused, _ => throw new InvalidOperationException("must not announce"), maxRows: 2, token: Ct));
        Assert.Equal("PUR.RECEIPT.EXPORT_LIMIT", limit.ErrorCode);
        Assert.Equal(0, refused.Length);

        using var filtered = new MemoryStream();
        Assert.Equal(0, await queries.ExportAsync(new PurchaseReceiptQuery { Status = "Posted" }, "en", filtered, token: Ct));
        Assert.Equal(PurchaseReceiptCsv.HeaderLine("en") + "\r\n", Encoding.UTF8.GetString(filtered.ToArray()[3..]));
    }

    [Fact] // PUR-RECV-LST-006 (WU-PUR-007a11)
    public async Task SoftDeletedReceiptStaysHiddenFromListKeywordAndExport()
    {
        // The purchase-order-number subqueries use IgnoreQueryFilters(), which EF applies to the whole query,
        // so the receipt's own soft-delete filter must not be relied on.
        var company = TestCompanies.Next();
        var source = await SeedOrderAsync(company, "PO-SDK-1");
        await using var db = Context(company);
        var kept = await Drafts(db, company).CreateAsync(Save(source, 1m, new DateOnly(2028, 2, 1)), Ct);
        var gone = await Drafts(db, company).CreateAsync(Save(source, 2m, new DateOnly(2028, 2, 2)), Ct);
        await Lifecycle(db, company).DeleteAsync(gone.Id, gone.Version, Ct);

        await using var read = Context(company);
        var queries = Queries(read, company, DataScope.Company);
        foreach (var query in new[] { new PurchaseReceiptQuery(), new PurchaseReceiptQuery { Keyword = " PO-SDK " },
                     new PurchaseReceiptQuery { PurchaseOrderId = source.OrderId } })
        {
            var page = await queries.ListAsync(query, Ct);
            Assert.Equal(1, page.TotalCount);
            var item = Assert.Single(page.Items);
            Assert.Equal(kept.Id, item.Id);
            Assert.Equal("PO-SDK-1", item.PurchaseOrderDocumentNo);

            using var output = new MemoryStream();
            Assert.Equal(1, await queries.ExportAsync(query, "en", output, token: Ct));
            var csv = Encoding.UTF8.GetString(output.ToArray()[3..]);
            Assert.Contains(kept.DocumentNo, csv);
            Assert.DoesNotContain(gone.DocumentNo, csv);
        }
        Assert.Empty((await queries.ListAsync(new PurchaseReceiptQuery { Keyword = gone.DocumentNo }, Ct)).Items);
    }

    private static PurchaseReceiptSaveInput Save((long OrderId, long LineId) source, decimal quantity, DateOnly date) =>
        new(source.OrderId, date, [new(1, source.LineId, quantity, 91)]);

    private async Task<(long OrderId, long LineId)> SeedOrderAsync(long company, string documentNo)
    {
        await using var db = database.CreateRawContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order(company_id,document_no,supplier_id,currency,status,is_deleted,created_at,created_by,version)
            VALUES ({company},{documentNo},71,'TWD','Approved',0,'2026-01-01',1,1)
            """, Ct);
        var order = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id={0} AND document_no={1}", company, documentNo).SingleAsync(Ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order_line(company_id,purchase_order_id,line_no,material_id,quantity,unit_price,tax_rate_percent,base_net,created_at,created_by,version)
            VALUES ({company},{order},1,81,50,4,0,200,'2026-01-01',1,1)
            """, Ct);
        var line = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order_line WHERE company_id={0} AND purchase_order_id={1}", company, order).SingleAsync(Ct);
        return (order, line);
    }

    private MyErpDbContext Context(long company) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7), new FixedCompany(company)))).Options,
        [new PurchaseInvoiceSourceDbModel()], new FixedCompany(company));

    private static ICurrentAuthorization Authorization(long company, long userId, IReadOnlyDictionary<string, DataScope> grants)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCompanyAuthorization(userId, company, 10, 20, false, grants));
        return authorization.Object;
    }

    private static int number;

    private static PurchaseReceiptDraftService Drafts(MyErpDbContext db, long company, long userId = 7)
    {
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.RECEIPT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"PRQ-{company}-{Interlocked.Increment(ref number):D6}");
        var references = new Mock<IInventoryReferenceGuard>();
        return new PurchaseReceiptDraftService(db, new RequisitionCompanyAccess(new FixedCompany(company)),
            Authorization(company, userId, new Dictionary<string, DataScope>
            {
                [PurchasingPermissions.ReceiptCreate] = DataScope.Self,
                [PurchasingPermissions.ReceiptView] = DataScope.Self
            }), numbers.Object, references.Object, new PurchaseReceiptTracking(db, references.Object));
    }

    private static PurchaseReceiptLifecycleService Lifecycle(MyErpDbContext db, long company, long userId = 7,
        string grants = Delete) => new(db, new RequisitionCompanyAccess(new FixedCompany(company)),
        Authorization(company, userId, new Dictionary<string, DataScope> { [grants] = DataScope.Self }),
        new FixedClock(new DateTime(2028, 2, 29)));

    private static PurchaseReceiptQueryService Queries(MyErpDbContext db, long company,
        DataScope view = DataScope.Self, DataScope? exportScope = null, string[]? grants = null)
    {
        var map = (grants ?? [PurchasingPermissions.ReceiptView, PurchasingPermissions.ReceiptExport])
            .ToDictionary(x => x, x => x == PurchasingPermissions.ReceiptExport ? exportScope ?? view : view);
        return new(db, new RequisitionCompanyAccess(new FixedCompany(company)), Authorization(company, 7, map));
    }

    private sealed class DeletedRow
    {
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DocumentNo { get; set; }
        public string Status { get; set; } = "";
        public int Version { get; set; }
    }
}
