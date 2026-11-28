using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-007a10 PUR-RECV-LST-U01～U02: receipt CSV rows use saved values only and neutralise formulas.</summary>
public sealed class PurchaseReceiptCsvTests
{
    [Fact]
    public void RowsUseSavedValuesStatusTextAndNeutraliseFormulaText()
    {
        var row = new PurchaseReceiptCsvRow("=PR,1", new DateOnly(2028, 2, 29), "Posted", "+PO-1",
            9, "USD", 2, 11, 2.000001m, 91, 4.123456m, 2.000001m);
        var csv = PurchaseReceiptCsv.Line(row, "en");
        Assert.Equal("\"'=PR,1\",2028-02-29,Posted,'+PO-1,9,USD,2,11,2.000001,91,4.123456,2.000001", csv);
        Assert.Contains(",已過帳,", PurchaseReceiptCsv.Line(row, "zh-Hant"));
        Assert.Contains(",已过账,", PurchaseReceiptCsv.Line(row, "zh-Hans"));
        Assert.Contains(",草稿,", PurchaseReceiptCsv.Line(row with { Status = "Draft" }, "zh-Hant"));
        Assert.Contains(",Pending approval,", PurchaseReceiptCsv.Line(row with { Status = "PendingApproval" }, "en"));
        Assert.Contains(",已駁回,", PurchaseReceiptCsv.Line(row with { Status = "Rejected" }, "zh-Hant"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseReceiptCsv.Line(row with { Status = "Cancelled" }, "en"));
        Assert.Equal(",,Draft,,9,USD,1,11,1,91,0,0", PurchaseReceiptCsv.Line(
            new PurchaseReceiptCsvRow(null, null, "Draft", null, 9, "USD", 1, 11, 1m, 91, 0m, 0m), "en"));
        Assert.Equal(PurchaseReceiptCsv.Headers("en").Count, PurchaseReceiptCsv.Headers("zh-Hans").Count);
        Assert.Equal(12, PurchaseReceiptCsv.Headers("zh-Hant").Count);
        Assert.Equal("收货单号", PurchaseReceiptCsv.Headers("zh-Hans")[0]);
        Assert.Equal(PurchaseReceiptCsv.Headers("zh-Hant"), PurchaseReceiptCsv.Headers("fr"));
    }

    [Fact]
    public void LimitIsInclusiveAndOverLimitFailsBeforeWriting()
    {
        PurchaseReceiptCsv.EnsureWithinLimit(0);
        PurchaseReceiptCsv.EnsureWithinLimit(10000);
        Assert.Equal("PUR.RECEIPT.EXPORT_LIMIT", Assert.Throws<BusinessRuleException>(() =>
            PurchaseReceiptCsv.EnsureWithinLimit(10001)).ErrorCode);
    }
}
