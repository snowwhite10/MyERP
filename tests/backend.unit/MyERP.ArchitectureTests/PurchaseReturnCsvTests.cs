using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-007c5 PUR-RET-LST-U01～U02: return CSV rows use saved values, mark estimated cost and neutralise formulas.</summary>
public sealed class PurchaseReturnCsvTests
{
    [Fact]
    public void RowsUseSavedValuesStatusAndCostSourceTextAndNeutraliseFormulaText()
    {
        var row = new PurchaseReturnCsvRow("=RT,1", new DateOnly(2028, 2, 29), "Posted", "+PO-1", 9, 2, "-GR-1", 3,
            11, 91, 2.000001m, 4.123456m, 8.2469m, CostConfirmed: true);
        Assert.Equal("\"'=RT,1\",2028-02-29,Posted,'+PO-1,9,2,'-GR-1,3,11,91,2.000001,4.123456,8.2469,Original receipt cost",
            PurchaseReturnCsv.Line(row, "en"));
        Assert.EndsWith(",已過帳,'+PO-1,9,2,'-GR-1,3,11,91,2.000001,4.123456,8.2469,入庫原成本",
            PurchaseReturnCsv.Line(row, "zh-Hant"));
        Assert.EndsWith(",入库原成本", PurchaseReturnCsv.Line(row, "zh-Hans"));
        var draft = row with { Status = "Draft", CostConfirmed = false };
        Assert.EndsWith(",Receipt unit cost (estimate)", PurchaseReturnCsv.Line(draft, "en"));
        Assert.EndsWith(",收貨單位成本（暫估）", PurchaseReturnCsv.Line(draft, "zh-Hant"));
        Assert.EndsWith(",收货单位成本（暂估）", PurchaseReturnCsv.Line(draft, "zh-Hans"));
        Assert.Contains(",草稿,", PurchaseReturnCsv.Line(draft, "zh-Hant"));
        Assert.Contains(",Pending approval,", PurchaseReturnCsv.Line(row with { Status = "PendingApproval" }, "en"));
        Assert.Contains(",待审批,", PurchaseReturnCsv.Line(row with { Status = "PendingApproval" }, "zh-Hans"));
        Assert.Contains(",已駁回,", PurchaseReturnCsv.Line(row with { Status = "Rejected" }, "zh-Hant"));
        Assert.Contains(",已过账,", PurchaseReturnCsv.Line(row, "zh-Hans"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseReturnCsv.Line(row with { Status = "Cancelled" }, "en"));
        Assert.Equal(",,Draft,,9,1,,1,11,91,1,0,0,Receipt unit cost (estimate)", PurchaseReturnCsv.Line(
            new PurchaseReturnCsvRow(null, null, "Draft", null, 9, 1, null, 1, 11, 91, 1m, 0m, 0m, false), "en"));
        Assert.Equal(14, PurchaseReturnCsv.Headers("zh-Hant").Count);
        Assert.Equal(PurchaseReturnCsv.Headers("en").Count, PurchaseReturnCsv.Headers("zh-Hans").Count);
        Assert.Equal("退货单号", PurchaseReturnCsv.Headers("zh-Hans")[0]);
        Assert.Equal("Return no.", PurchaseReturnCsv.Headers("en")[0]);
        Assert.Equal(PurchaseReturnCsv.Headers("zh-Hant"), PurchaseReturnCsv.Headers("fr"));
    }

    [Fact]
    public void AmountRoundsEachLineToFourDecimalsAwayFromZero()
    {
        Assert.Equal(0.0001m, PurchaseReturnCsv.Amount(0.000001m, 50m));
        Assert.Equal(0m, PurchaseReturnCsv.Amount(0.000001m, 49m));
        Assert.Equal(8m, PurchaseReturnCsv.Amount(2m, 4m));
    }

    [Fact]
    public void LimitIsInclusiveAndOverLimitFailsBeforeWriting()
    {
        PurchaseReturnCsv.EnsureWithinLimit(0);
        PurchaseReturnCsv.EnsureWithinLimit(10000);
        Assert.Equal("PUR.RETURN.EXPORT_LIMIT", Assert.Throws<BusinessRuleException>(() =>
            PurchaseReturnCsv.EnsureWithinLimit(10001)).ErrorCode);
    }
}
