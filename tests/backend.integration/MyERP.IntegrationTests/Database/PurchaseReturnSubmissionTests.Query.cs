using System.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// WU-PUR-007c5（REQ_F4114.7.1、ADR-226，PUR-RET-LST-001～004）：退貨 LIST 與 CSV 以真實 SQL、真實 SYS 核准與真實 INV
/// 過帳驗收——VIEW 資料範圍、篩選、穩定排序分頁、軟刪不出現；退貨金額 Posted 用原入庫成本、未過帳用收貨單位成本並標示暫估；
/// CSV 取 VIEW ∩ EXPORT、快照、上限 422 不輸出。
/// </summary>
public sealed partial class PurchaseReturnSubmissionTests
{
    private sealed record ListSeed(Seeded S, PurchaseReturnReadDetail Draft, PurchaseReturnReadDetail Posted,
        string OrderANo, string OrderBNo, string ReceiptANo, string ReceiptBNo);

    /// <summary>
    /// Draft d1 (order A, lines 2 + 1.5, Day), Posted d2 (order B, line 1, Day+1, cost 4 from INV) and a soft-deleted
    /// Draft on order A; the deleted one must never appear.
    /// </summary>
    private async Task<ListSeed> SeedListAsync()
    {
        var s = await SeedAsync(withFlow: true);
        var orderA = await SeedOrderAsync(s, (s.ItemA, 5m), (s.ItemB, 3m));
        var orderB = await SeedOrderAsync(s, (s.ItemA, 4m));
        var receiptA = await SeedPostedReceiptAsync(s, orderA, (0, 5m, null), (1, 3m, null));
        var receiptB = await SeedPostedReceiptAsync(s, orderB, (0, 4m, null));
        var d1 = await CreateDraftAsync(s, orderA, L(1, receiptA.LineIds[0], 2m), L(2, receiptA.LineIds[1], 1.5m));
        var d2 = await CreateDraftAsync(s, orderB, L(1, receiptB.LineIds[0], 1m));
        var deleted = await CreateDraftAsync(s, orderA, L(1, receiptA.LineIds[0], 1m));
        await using (var db = NewDb(s))
            await Drafts(db, s).DeleteAsync(deleted.Id, deleted.Version, Ct);
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_return SET business_date={Day.AddDays(1)} WHERE id={d2.Id}", Ct);
        await SubmitAsync(s, d2, "PendingApproval"); // date set while Draft: Posted returns are immutable
        await DecideAsync(s, d2.Id, approve: true);
        // Diverge the receipt cost from the posted INV cost so Posted values must come from OriginalUnitCost (4), not 5.
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt_line SET unit_cost=5 WHERE id={receiptB.LineIds[0]}", Ct);
        await using var check = NewDb(s);
        var posted = await Drafts(check, s).GetAsync(d2.Id, Ct);
        var draft = await Drafts(check, s).GetAsync(d1.Id, Ct);
        var orders = await check.Set<PurchaseOrderSource>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.Id == orderA.Id || x.Id == orderB.Id).ToDictionaryAsync(x => x.Id, x => x.DocumentNo!, Ct);
        var receipts = await check.Set<PurchaseReceiptSource>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.Id == receiptA.Id || x.Id == receiptB.Id).ToDictionaryAsync(x => x.Id, x => x.DocumentNo!, Ct);
        return new ListSeed(s, draft, posted, orders[orderA.Id], orders[orderB.Id], receipts[receiptA.Id],
            receipts[receiptB.Id]);
    }

    [Fact] // PUR-RET-LST-001
    public async Task ReturnListFiltersSortsPagesAndValuesAtOriginalOrEstimatedCost()
    {
        var x = await SeedListAsync();
        var (s, d1, d2) = (x.S, x.Draft, x.Posted);
        await using var db = NewDb(s);
        var queries = Queries(db, s);
        async Task<long[]> Ids(PurchaseReturnQuery q) => (await queries.ListAsync(q, Ct)).Items.Select(r => r.Id).ToArray();

        var all = await queries.ListAsync(new PurchaseReturnQuery(), Ct);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal([d2.Id, d1.Id], all.Items.Select(r => r.Id).ToArray()); // -businessDate
        var draft = all.Items[1];
        Assert.Equal((d1.DocumentNo, Day, d1.PurchaseOrderId, (string?)x.OrderANo, 101L, "TWD", "Draft"),
            (draft.DocumentNo, draft.BusinessDate, draft.PurchaseOrderId, draft.PurchaseOrderDocumentNo, draft.SupplierId,
                draft.Currency, draft.Status));
        Assert.Equal((2, 3.5m, 14m, true, d1.Version),
            (draft.LineCount, draft.TotalQuantity, draft.ReturnAmount, draft.AmountEstimated, draft.Version));
        var posted = all.Items[0];
        Assert.Equal(("Posted", Day.AddDays(1), (string?)x.OrderBNo, 1, 1m, 4m, false),
            (posted.Status, posted.BusinessDate, posted.PurchaseOrderDocumentNo, posted.LineCount, posted.TotalQuantity,
                posted.ReturnAmount, posted.AmountEstimated));

        Assert.Equal([d1.Id, d2.Id], await Ids(new PurchaseReturnQuery { Sort = "businessDate" }));
        var byNo = new[] { d1, d2 }.OrderBy(r => r.DocumentNo, StringComparer.Ordinal).Select(r => r.Id).ToArray();
        Assert.Equal(byNo, await Ids(new PurchaseReturnQuery { Sort = "documentNo" }));
        Assert.Equal(byNo.Reverse().ToArray(), await Ids(new PurchaseReturnQuery { Sort = "-documentNo" }));
        var page2 = await queries.ListAsync(new PurchaseReturnQuery { Page = 2, PageSize = 1 }, Ct);
        Assert.Equal(2L, page2.TotalCount);
        Assert.Equal([d1.Id], page2.Items.Select(r => r.Id).ToArray());
        Assert.Equal([d2.Id], await Ids(new PurchaseReturnQuery { Status = "Posted" }));
        Assert.Empty(await Ids(new PurchaseReturnQuery { Status = "Rejected" }));
        Assert.Equal([d2.Id], await Ids(new PurchaseReturnQuery { PurchaseOrderId = d2.PurchaseOrderId }));
        Assert.Equal([d1.Id], await Ids(new PurchaseReturnQuery { Keyword = "  " + x.OrderANo[3..] + " " }));
        Assert.Equal([d2.Id], await Ids(new PurchaseReturnQuery { Keyword = d2.DocumentNo }));
        Assert.Equal([d2.Id], await Ids(new PurchaseReturnQuery { From = Day.AddDays(1) }));
        Assert.Equal([d1.Id], await Ids(new PurchaseReturnQuery { To = Day }));
        Assert.Equal([d1.Id], await Ids(new PurchaseReturnQuery { From = Day, To = Day }));
        Assert.Empty(await Ids(new PurchaseReturnQuery { Keyword = "NO-SUCH-RETURN" }));
    }

    [Fact] // PUR-RET-LST-002／003
    public async Task ReturnListRejectsBadFiltersAndAppliesViewAndExportScopes()
    {
        var x = await SeedListAsync();
        var s = x.S;
        await using var db = NewDb(s);
        foreach (var bad in new[]
                 {
                     new PurchaseReturnQuery { From = Day, To = Day.AddDays(-1) },
                     new PurchaseReturnQuery { PurchaseOrderId = 0 },
                     new PurchaseReturnQuery { Status = "Cancelled" },
                     new PurchaseReturnQuery { Sort = "supplierId" },
                     new PurchaseReturnQuery { Sort = "documentNo,businessDate" },
                     new PurchaseReturnQuery { PageSize = 0 }
                 })
            await Assert.ThrowsAsync<RequestValidationException>(() => Queries(db, s).ListAsync(bad, Ct));

        // Self scope: the approver owns nothing, the creator owns both; another company sees nothing; EDIT is not VIEW.
        Assert.Equal(0, (await Queries(db, s, userId: s.ApproverId, view: DataScope.Self)
            .ListAsync(new PurchaseReturnQuery(), Ct)).TotalCount);
        Assert.Equal(2, (await Queries(db, s, view: DataScope.Self).ListAsync(new PurchaseReturnQuery(), Ct)).TotalCount);
        Assert.Equal(0, (await Queries(db, s, companyId: s.CompanyId + 1_000_000)
            .ListAsync(new PurchaseReturnQuery(), Ct)).TotalCount);
        Assert.Equal("COMMON.PERMISSION.DENIED", (await Assert.ThrowsAsync<ForbiddenException>(() =>
            Queries(db, s, grants: [PurchasingPermissions.ReturnEdit]).ListAsync(new PurchaseReturnQuery(), Ct))).ErrorCode);

        // EXPORT narrower than VIEW → intersection (Self export scope of a user who owns nothing); missing either 403.
        using var narrow = new MemoryStream();
        Assert.Equal(0, await Queries(db, s, userId: s.ApproverId, exportSelf: true)
            .ExportAsync(new PurchaseReturnQuery(), "en", narrow, token: Ct));
        Assert.Equal(2, (await Queries(db, s, userId: s.ApproverId, exportSelf: true)
            .ListAsync(new PurchaseReturnQuery(), Ct)).TotalCount);
        await Assert.ThrowsAsync<ForbiddenException>(() => Queries(db, s, grants: [PurchasingPermissions.ReturnView])
            .ExportAsync(new PurchaseReturnQuery(), "en", new MemoryStream(), token: Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => Queries(db, s, grants: [PurchasingPermissions.ReturnExport])
            .ExportAsync(new PurchaseReturnQuery(), "en", new MemoryStream(), token: Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => Queries(db, s)
            .ExportAsync(new PurchaseReturnQuery { Status = "Cancelled" }, "en", new MemoryStream(), token: Ct));
    }

    [Fact] // PUR-RET-LST-004
    public async Task ReturnCsvWritesOneRowPerSavedLineInListOrderAndRefusesOverLimitBeforeWriting()
    {
        var x = await SeedListAsync();
        var (s, d1, d2) = (x.S, x.Draft, x.Posted);
        await using var db = NewDb(s);
        using var output = new MemoryStream();
        long? announced = null;
        var count = await Queries(db, s).ExportAsync(new PurchaseReturnQuery { Page = 2, PageSize = 1 }, "zh-Hant",
            output, n => { announced = n; return Task.CompletedTask; }, token: Ct);
        Assert.Equal((3L, (long?)3L), (count, announced)); // paging ignored
        var bytes = output.ToArray();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.Equal(5, lines.Length); // header + 3 rows + trailing empty
        Assert.Equal("", lines[4]);
        Assert.Equal(string.Join(',', PurchaseReturnCsv.Headers("zh-Hant")), lines[0]);
        var day = Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var day2 = Day.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"{d2.DocumentNo},{day2},已過帳,{x.OrderBNo},101,1,{x.ReceiptBNo},1," +
            $"{s.ItemA},{s.Warehouse},1,4,4,入庫原成本", lines[1]);
        Assert.Equal($"{d1.DocumentNo},{day},草稿,{x.OrderANo},101,1,{x.ReceiptANo},1,{s.ItemA},{s.Warehouse},2,4,8," +
            "收貨單位成本（暫估）", lines[2]);
        Assert.Equal($"{d1.DocumentNo},{day},草稿,{x.OrderANo},101,2,{x.ReceiptANo},2,{s.ItemB},{s.Warehouse},1.5,4,6," +
            "收貨單位成本（暫估）", lines[3]);

        using var filtered = new MemoryStream();
        Assert.Equal(1, await Queries(db, s).ExportAsync(new PurchaseReturnQuery { Status = "Posted" }, "en", filtered,
            token: Ct));
        Assert.StartsWith("Return no.,", Encoding.UTF8.GetString(filtered.ToArray()[3..]));

        using var refused = new MemoryStream();
        var ready = false;
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Queries(db, s).ExportAsync(
            new PurchaseReturnQuery(), "en", refused, _ => { ready = true; return Task.CompletedTask; }, maxRows: 2,
            token: Ct));
        Assert.Equal("PUR.RETURN.EXPORT_LIMIT", error.ErrorCode);
        Assert.Equal((0L, false), (refused.Length, ready));

        using var empty = new MemoryStream();
        Assert.Equal(0, await Queries(db, s).ExportAsync(new PurchaseReturnQuery { Status = "Rejected" }, "en", empty,
            token: Ct));
        Assert.Equal(string.Join(',', PurchaseReturnCsv.Headers("en")) + "\r\n",
            Encoding.UTF8.GetString(empty.ToArray()[3..]));
    }

    /// <summary>VIEW (and EXPORT) default to <paramref name="view"/>; <paramref name="exportSelf"/> narrows EXPORT to Self.</summary>
    private static PurchaseReturnQueryService Queries(MyErpDbContext db, Seeded s, long? userId = null,
        DataScope view = DataScope.Company, long? companyId = null, string[]? grants = null, bool exportSelf = false)
    {
        var company = companyId ?? s.CompanyId;
        var map = (grants ?? [PurchasingPermissions.ReturnView, PurchasingPermissions.ReturnExport])
            .ToDictionary(g => g, g => g == PurchasingPermissions.ReturnExport && exportSelf ? DataScope.Self : view);
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(a => a.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserCompanyAuthorization(userId ?? s.UserId, company, 10, 20, false, map));
        return new PurchaseReturnQueryService(db, new RequisitionCompanyAccess(new FixedCompany(company)),
            authorization.Object);
    }
}
