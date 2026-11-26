using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// PUR-RETLIM-I01～I02（WU-PUR-008b5，BR-PUR-044，ADR-209）：發票經正式 PUR→FI 過帳後，收貨行的已過帳開票量
/// 不可再被採購退貨動用。007c 退貨寫入服務尚未存在，因此以真實 SQL 收貨行驗收兩道防線：
/// PUR 公開規則 <see cref="PurchaseReturnInvoicedLimit"/>（應用層 422）與表 CHECK
/// <c>ck_pur_receipt_line_quantities</c>（資料庫最後防線）。
/// </summary>
public sealed partial class FinancePostingServiceTests
{
    private sealed record ReturnSourceRow(decimal PostedReceived, decimal PostedReturned,
        decimal PendingReturnReserved, decimal PostedInvoiced, decimal MatchedReserved);

    private async Task<PurchaseReturnSourceQuantities> ReturnSourceAsync(long receiptLineId, decimal current,
        CancellationToken token)
    {
        await using var db = database.CreateRawContext();
        var row = await db.Database.SqlQuery<ReturnSourceRow>($"""
            SELECT posted_received AS posted_received, posted_returned AS posted_returned,
                   pending_return_reserved AS pending_return_reserved, posted_invoiced AS posted_invoiced,
                   matched_reserved AS matched_reserved
            FROM dbo.pur_purchase_receipt_line WHERE id = {receiptLineId}
            """).SingleAsync(token);
        return new(row.PostedReceived, row.PostedReturned, row.PendingReturnReserved, row.PostedInvoiced,
            row.MatchedReserved, current);
    }

    private async Task SetReturnQuantitiesAsync(long receiptLineId, decimal pending, decimal returned,
        CancellationToken token)
    {
        // 以不變文化字面值寫入：EF 預設 decimal 參數為 decimal(18,2)，會把 3.000001 捨入成 3.00。
        await using var db = database.CreateRawContext();
        await db.Database.ExecuteSqlRawAsync(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"UPDATE dbo.pur_purchase_receipt_line SET pending_return_reserved={pending}, posted_returned={returned} WHERE id={receiptLineId}"),
            token);
    }

    [Fact]
    public async Task PUR_RETLIM_I01_PostedInvoiceQuantityCannotBeReturnedByRuleOrDatabase()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var provider = PostProvider(companyId, new PostActor());
        await SeedPurchaseAccountsAsync(provider, companyId, token);
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2.1m, acceptedAtVersion: 1,
            token: token);
        // 收貨 8、發票 Matched 5：過帳後已開票 5，未開票可退 3。
        await using (var db = database.CreateRawContext())
            await db.Database.ExecuteSqlAsync(
                $"UPDATE dbo.pur_purchase_receipt_line SET posted_received=8 WHERE id={arranged.ReceiptLineId}",
                token);
        var saved = await PostInvoiceAsync(provider, arranged.InvoiceId, 2, token);
        Assert.Equal("Posted", saved.Status);

        var source = await ReturnSourceAsync(arranged.ReceiptLineId, 0, token);
        Assert.Equal((8m, 5m, 0m), (source.PostedReceived, source.PostedInvoiced, source.MatchedReserved));
        Assert.Equal(3m, PurchaseReturnInvoicedLimit.Calculate(source));
        var line = arranged.ReceiptLineId;

        // 應用層：退 4 超出未開票量 → 422，訊息指出收貨行、可退量與已開票量。
        var error = Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.EnsureWithinLimit(
            [new(line, 4)], [new(line, source)]));
        Assert.Equal(PurchaseReturnInvoicedLimit.ExceedsUninvoiced, error.ErrorCode);
        Assert.Contains("returnable 3", error.Message, StringComparison.Ordinal);
        Assert.Contains("invoiced or matched 5", error.Message, StringComparison.Ordinal);
        PurchaseReturnInvoicedLimit.EnsureWithinLimit([new(line, 3)], [new(line, source)]);

        // 資料庫：預留或過帳退貨使已開票量失去收貨支撐時 CHECK 拒絕，列不變。
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            SetReturnQuantitiesAsync(line, 4, 0, token));
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            SetReturnQuantitiesAsync(line, 0, 3.000001m, token));
        Assert.Equal(source, await ReturnSourceAsync(line, 0, token));

        // SUBMIT 預留 3（上限）可寫入；之後他單再退任何量都被拒，本單核准重驗（排除本單預留）仍通過。
        await SetReturnQuantitiesAsync(line, 3, 0, token);
        var reserved = await ReturnSourceAsync(line, 0, token);
        Assert.Equal(0m, PurchaseReturnInvoicedLimit.Calculate(reserved));
        Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.EnsureWithinLimit(
            [new(line, 0.000001m)], [new(line, reserved)]));
        PurchaseReturnInvoicedLimit.EnsureWithinLimit([new(line, 3)], [new(line, reserved with
        {
            CurrentReturnReserved = 3,
        })]);

        // 核准：預留轉已過帳退貨；已開票 5 仍完整保留。
        await SetReturnQuantitiesAsync(line, 0, 3, token);
        var returned = await ReturnSourceAsync(line, 0, token);
        Assert.Equal((3m, 0m, 5m, 0m), (returned.PostedReturned, returned.PendingReturnReserved,
            returned.PostedInvoiced, PurchaseReturnInvoicedLimit.Calculate(returned)));
        Assert.Equal(("Posted", 0m, 5m), await PostedInvoiceAndReceiptAsync(arranged, token));
    }

    [Fact]
    public async Task PUR_RETLIM_I02_MatchedReservationAlsoBlocksReturnBeforePosting()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        var arranged = await ArrangePurchaseInvoiceAsync(companyId, "Matched", 2m, token: token);
        var line = arranged.ReceiptLineId;

        // 收貨 5 全數被 Matched 發票預留：尚未過帳亦不可退（BR-PUR-041），規則與 CHECK 一致。
        var source = await ReturnSourceAsync(line, 0, token);
        Assert.Equal((5m, 0m, 5m), (source.PostedReceived, source.PostedInvoiced, source.MatchedReserved));
        var decision = PurchaseReturnInvoicedLimit.Check([new(line, 1)], [new(line, source)]);
        Assert.Equal([new PurchaseReturnShortage(line, 1m, 0m, 5m)], decision.Shortages);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            SetReturnQuantitiesAsync(line, 1, 0, token));
        Assert.Equal(source, await ReturnSourceAsync(line, 0, token));
    }

    private async Task<(string Status, decimal MatchedReserved, decimal PostedInvoiced)> PostedInvoiceAndReceiptAsync(
        PurArrange arranged, CancellationToken token)
    {
        var row = await PostRowAsync(arranged, token);
        return (row.Status, row.MatchedReserved, row.PostedInvoiced);
    }
}
