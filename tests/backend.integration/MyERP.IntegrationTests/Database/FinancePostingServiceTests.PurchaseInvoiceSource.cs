using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// PUR-FISRC-007～011（WU-PUR-008b3，ADR-207）：PUR 正式實作的 <see cref="IFinancePurchaseInvoiceSource"/> 經
/// PurchasingModule 註冊，與 FI 正式 <see cref="IPurchaseInvoicePostingService"/> 在同一真實 SQL 交易內組合。
/// PUR 來源列以 SQL 安排（採購單／收貨尚無寫入 API，同 ADR-204）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private ServiceProvider RealPurchaseProvider(long companyId) =>
        Provider(companyId, mapping: null,
            services => new PurchasingModule().AddServices(services, new ConfigurationBuilder().Build()));

    private sealed record PurArrange(long OrderId, long ReceiptLineId, long InvoiceId);

    /// <summary>
    /// 採購單（原幣 <paramref name="currency"/>，匯率快照可空）＋1 行 5×2.000000 稅率 20%、已過帳收貨 5、
    /// 發票 1 行 5×<paramref name="invoicePrice"/>（業務日 2026-11-06）。<paramref name="acceptedAtVersion"/> 有值時寫入一列
    /// 以該版本 Accept 的 RESOLVE 決定，發票版本為其 +1。
    /// </summary>
    private async Task<PurArrange> ArrangePurchaseInvoiceAsync(long companyId, string status, decimal invoicePrice,
        string currency = "USD", string? baseCurrency = "TWD", string? rate = "31.5", int? acceptedAtVersion = null,
        bool deleted = false, CancellationToken token = default)
    {
        await using var db = database.CreateRawContext();
        async Task Exec(FormattableString sql) =>
            await db.Database.ExecuteSqlRawAsync(sql.ToString(CultureInfo.InvariantCulture), token);
        async Task<long> Last(string table) => await db.Database.SqlQuery<long>(table switch
        {
            "pur_purchase_order" => $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_order WHERE company_id={companyId}",
            "pur_purchase_order_line" => $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_order_line WHERE company_id={companyId}",
            "pur_purchase_receipt" => $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_receipt WHERE company_id={companyId}",
            "pur_purchase_receipt_line" => $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_receipt_line WHERE company_id={companyId}",
            _ => $"SELECT MAX(id) AS [Value] FROM dbo.pur_purchase_invoice WHERE company_id={companyId}",
        }).SingleAsync(token);
        string Sql(string? value) => value is null ? "NULL" : $"'{value}'";

        var net = 5m * invoicePrice;
        var tax = net * 0.2m;
        var version = acceptedAtVersion + 1 ?? 1;
        await Exec($"INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,base_currency_code,exchange_rate,status,is_deleted,created_at,created_by,version) VALUES ({companyId},{PurchaseSupplierId},'{currency}',{Sql(baseCurrency)},{rate ?? "NULL"},'Approved',0,'2026-01-01',1,1)");
        var orderId = await Last("pur_purchase_order");
        await Exec($"INSERT dbo.pur_purchase_order_line(company_id,purchase_order_id,line_no,material_id,quantity,unit_price,tax_rate_percent,created_at,created_by,version) VALUES ({companyId},{orderId},1,51,5,2,20,'2026-01-01',1,1)");
        var orderLineId = await Last("pur_purchase_order_line");
        await Exec($"INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,created_at,created_by,version) VALUES ({companyId},{orderId},{PurchaseSupplierId},'{currency}','Posted',0,'2026-01-01',1,1)");
        var receiptId = await Last("pur_purchase_receipt");
        await Exec($"INSERT dbo.pur_purchase_receipt_line(company_id,purchase_receipt_id,purchase_order_line_id,line_no,material_id,posted_received,posted_returned,pending_return_reserved,posted_invoiced,matched_reserved,created_at,created_by,version) VALUES ({companyId},{receiptId},{orderLineId},1,51,5,0,0,0,5,'2026-01-01',1,1)");
        var receiptLineId = await Last("pur_purchase_receipt_line");
        await Exec($"INSERT dbo.pur_purchase_invoice(company_id,supplier_id,purchase_order_id,currency,business_date,supplier_invoice_number,number_key,status,total_net,total_tax,total_gross,is_deleted,created_at,created_by,version) VALUES ({companyId},{PurchaseSupplierId},{orderId},'{currency}','2026-11-06','INV-{orderId}','INV-{orderId}','{status}',{net},{tax},{net + tax},{(deleted ? 1 : 0)},'2026-01-01',1,{version})");
        var invoiceId = await Last("pur_purchase_invoice");
        await Exec($"INSERT dbo.pur_purchase_invoice_line(company_id,invoice_id,line_no,receipt_line_id,quantity,unit_price,tax_rate_percent,net,tax,gross,created_at,created_by,version) VALUES ({companyId},{invoiceId},1,{receiptLineId},5,{invoicePrice},20,{net},{tax},{net + tax},'2026-01-01',1,1)");
        if (acceptedAtVersion is { } resolved)
            await Exec($"INSERT dbo.pur_purchase_invoice_resolution(company_id,invoice_id,invoice_version,line_no,action,reason,net_difference,tax_difference,gross_difference,created_at,created_by,version) VALUES ({companyId},{invoiceId},{resolved},1,'Accept',N'供應商調價',{net - 10m},{tax - 2m},{net + tax - 12m},'2026-01-01',1,1)");
        return new(orderId, receiptLineId, invoiceId);
    }

    /// <summary>以測試端獨立組出的保存行與接受決定計算 008a10 指紋（不經 PUR 來源的 SQL 讀取）。</summary>
    private static string ExpectedPurchaseFingerprint(long companyId, PurArrange arranged, decimal invoicePrice,
        string currency = "USD", bool accepted = true)
    {
        var net = 5m * invoicePrice;
        var line = new PurchaseInvoiceAmountLine(1, 5m, 2m, 20m, invoicePrice, 20m, net, net * 0.2m, net * 1.2m);
        var amount = PurchaseInvoiceAmountMatch.Check([line]);
        IReadOnlyList<PurchaseInvoiceResolvedLine> decisions = accepted
            ? [new(1, "Accept", "供應商調價", amount.Lines[0].Differences)]
            : [];
        return PurchaseInvoiceSnapshotFingerprint.Create(companyId, arranged.InvoiceId, PurchaseSupplierId, currency,
            BusinessDate, [new PurchaseInvoiceMatchLine(arranged.ReceiptLineId, line)], decisions);
    }

    [Fact]
    public async Task PUR_FISRC_007_RealPurSourcePostsMatchedForeignInvoiceWithAcceptedDifferenceThroughFi()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = RealPurchaseProvider(companyId);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);
        var fingerprint = ExpectedPurchaseFingerprint(companyId, arranged, 2.1m);

        // 直接讀：交易內按保存值帶出原幣、以採購單快照 31.5 換算本位幣，版本與指紋＝保存值。
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var source = Assert.Single(scope.ServiceProvider.GetServices<IFinancePurchaseInvoiceSource>());
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            var s = Assert.IsType<FinancePurchaseInvoiceSnapshot>(
                await source.ReadInvoiceAsync(companyId, arranged.InvoiceId, tx.GetDbTransaction(), token));
            Assert.Equal((FinancePurchaseSourceState.Matched, 2, PurchaseSupplierId, fingerprint, "USD", "TWD", 31.5m),
                (s.State, s.SourceVersion, s.SupplierId, s.Fingerprint, s.Currency, s.BaseCurrency, s.ExchangeRate));
            Assert.Equal((10.5m, 2.1m, 12.6m, 330.75m, 66.15m, 396.90m),
                (s.NetAmount, s.TaxAmount, s.GrossAmount, s.BaseNetAmount, s.BaseTaxAmount, s.BaseGrossAmount));
            Assert.Equal(new FinancePurchaseInvoiceLine(1, 10.5m, 2.1m, 12.6m, 330.75m, 66.15m, 396.90m),
                Assert.Single(s.Lines));
            Assert.NotNull(db.Database.CurrentTransaction);
            await tx.RollbackAsync(token);
        }

        var command = new PurchaseInvoicePostingCommand(companyId, arranged.InvoiceId, PurchaseSupplierId, 2,
            fingerprint);
        var rolledBack = await PostPurchaseAsync(provider, command, commit: false, token);
        Assert.Equal((PurchasePostingOutcomeKind.Posted, true, 200), (rolledBack.Kind, rolledBack.Changed,
            rolledBack.HttpStatus));
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));

        var posted = await PostPurchaseAsync(provider, command, commit: true, token);
        Assert.Equal((PurchasePostingOutcomeKind.Posted, true, 200, (string?)null),
            (posted.Kind, posted.Changed, posted.HttpStatus, posted.ErrorCode));
        var receipt = Assert.IsType<PurchaseInvoicePostingReceipt>(posted.Receipt);
        Assert.Equal((arranged.InvoiceId, 2, fingerprint, "USD", 12.6m, "TWD", 396.90m),
            (receipt.InvoiceId, receipt.SourceVersion, receipt.Fingerprint, receipt.Currency, receipt.GrossAmount,
                receipt.BaseCurrency, receipt.BaseGrossAmount));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));

        // 重放：FI 先查到自己已提交的來源，不重讀、不重寫。
        var replay = await PostPurchaseAsync(provider, command, commit: true, token);
        Assert.Equal((PurchasePostingOutcomeKind.Unchanged, false), (replay.Kind, replay.Changed));
        Assert.Equal((1, 1, 1, 3), await PurchaseCountsAsync(companyId, token));
    }

    [Fact]
    public async Task PUR_FISRC_008_NonMatchedDeletedForeignOrMissingInvoicesAreNotPostedByFi()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = RealPurchaseProvider(companyId);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        // Disputed（差異未決、無 RESOLVE 列）、Draft、軟刪除的 Matched 都只報狀態 → 422；不需匯率快照。
        var disputed = await ArrangePurchaseInvoiceAsync(companyId, "Disputed", 2.1m, rate: null, baseCurrency: null,
            token: token);
        var draft = await ArrangePurchaseInvoiceAsync(companyId, "Draft", 2m, token: token);
        var deleted = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2m, deleted: true, token: token);
        var otherCompany = NewCompany();
        var foreign = await ArrangePurchaseInvoiceAsync(otherCompany, "Matched", 2m, token: token);

        foreach (var invoiceId in new[] { disputed.InvoiceId, draft.InvoiceId, deleted.InvoiceId })
            AssertPurchaseRejected(await PostPurchaseAsync(provider,
                    new(companyId, invoiceId, PurchaseSupplierId, 1, "PUR-INVOICE-POST-v1:x"), commit: true, token),
                422, PurchasePostingErrors.SourceNotPostable);
        foreach (var invoiceId in new[] { foreign.InvoiceId, long.MaxValue })
            AssertPurchaseRejected(await PostPurchaseAsync(provider,
                    new(companyId, invoiceId, PurchaseSupplierId, 1, "PUR-INVOICE-POST-v1:x"), commit: true, token),
                404, PurchasePostingErrors.SourceNotFound);
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<IFinancePurchaseInvoiceSource>();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var snapshot = await source.ReadInvoiceAsync(companyId, deleted.InvoiceId, tx.GetDbTransaction(), token);
        Assert.Equal(FinancePurchaseSourceState.Deleted, snapshot!.State);
        // 公司參數與目前公司不符也看不到（他公司的發票 ID 以他公司身分查詢）。
        Assert.Null(await source.ReadInvoiceAsync(otherCompany, foreign.InvoiceId, tx.GetDbTransaction(), token));
        await tx.RollbackAsync(token);
    }

    [Fact]
    public async Task PUR_FISRC_009_FingerprintOrVersionMismatchIsRejected409WithoutWriting()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = RealPurchaseProvider(companyId);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2m, currency: "TWD",
            baseCurrency: "TWD", rate: "1", token: token);
        var fingerprint = ExpectedPurchaseFingerprint(companyId, arranged, 2m, "TWD", accepted: false);

        AssertPurchaseRejected(await PostPurchaseAsync(provider,
                new(companyId, arranged.InvoiceId, PurchaseSupplierId, 1, fingerprint + "0"), commit: true, token),
            409, PurchasePostingErrors.SourceMismatch);
        AssertPurchaseRejected(await PostPurchaseAsync(provider,
                new(companyId, arranged.InvoiceId, PurchaseSupplierId, 2, fingerprint), commit: true, token),
            409, PurchasePostingErrors.SourceMismatch);

        // 保存行在指紋計算後被改（例如另一交易已提交的修改）→ 同一命令 409。
        await using (var db = database.CreateRawContext())
            await db.Database.ExecuteSqlAsync(
                $"UPDATE dbo.pur_purchase_invoice SET business_date='2026-11-07' WHERE id={arranged.InvoiceId}", token);
        AssertPurchaseRejected(await PostPurchaseAsync(provider,
                new(companyId, arranged.InvoiceId, PurchaseSupplierId, 1, fingerprint), commit: true, token),
            409, PurchasePostingErrors.SourceMismatch);
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));

        // 改回後同一命令可過帳（本位幣發票：本位幣＝原幣、匯率 1）。
        await using (var db = database.CreateRawContext())
            await db.Database.ExecuteSqlAsync(
                $"UPDATE dbo.pur_purchase_invoice SET business_date='2026-11-06' WHERE id={arranged.InvoiceId}", token);
        var posted = await PostPurchaseAsync(provider,
            new(companyId, arranged.InvoiceId, PurchaseSupplierId, 1, fingerprint), commit: true, token);
        Assert.Equal((PurchasePostingOutcomeKind.Posted, "TWD", 12m, "TWD", 12m),
            (posted.Kind, posted.Receipt!.Currency, posted.Receipt.GrossAmount, posted.Receipt.BaseCurrency,
                posted.Receipt.BaseGrossAmount));
    }

    [Fact]
    public async Task PUR_FISRC_010_UnusableSavedEvidenceThrowsPurErrorAndFiWritesNothing()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = RealPurchaseProvider(companyId);
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var noRate = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2m, baseCurrency: null, rate: null,
            token: token);
        // Matched 但有差異而沒有 RESOLVE 決定：證據不一致。
        var unresolved = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, token: token);

        foreach (var (invoiceId, code) in new[]
                 {
                     (noRate.InvoiceId, "PUR.INVOICE.RATE_SNAPSHOT_MISSING"),
                     (unresolved.InvoiceId, "PUR.INVOICE.INVALID_POSTING_DIFFERENCE")
                 })
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => PostPurchaseAsync(provider,
                new(companyId, invoiceId, PurchaseSupplierId, 1, "PUR-INVOICE-POST-v1:x"), commit: true, token));
            Assert.Equal(code, error.ErrorCode);
        }
        Assert.Equal((0, 0, 0, 0), await PurchaseCountsAsync(companyId, token));

        // 不在呼叫者交易內讀取是程式錯誤。
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<IFinancePurchaseInvoiceSource>();
        await using var other = database.CreateRawContext();
        await using var foreignTx = await other.Database.BeginTransactionAsync(token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ReadInvoiceAsync(companyId, noRate.InvoiceId, foreignTx.GetDbTransaction(), token));
        await foreignTx.RollbackAsync(token);
        Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public void PUR_FISRC_011_PurchasingModuleRegistersExactlyOneRealFinanceSource()
    {
        var companyId = NewCompany();
        using var provider = RealPurchaseProvider(companyId);
        using var scope = provider.CreateScope();
        var source = Assert.Single(scope.ServiceProvider.GetServices<IFinancePurchaseInvoiceSource>());
        Assert.Equal("PurchaseInvoiceFinanceSource", source.GetType().Name);
    }
}
