using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseOrderCsvTests
{
    [Fact]
    public void RowsUseSavedValuesAndNeutraliseFormulaText()
    {
        var row = new PurchaseOrderCsvRow("=PO,1", new DateOnly(2028, 2, 29), "Approved",
            9, "TWD", 1, 11, 2.000001m, 3.5m, 5m, 7m, 0.35m, 7.35m,
            "RFQ-1", "\tREQ-1", 2);
        var csv = PurchaseOrderCsv.Line(row, "en");
        Assert.StartsWith("\"'=PO,1\",2028-02-29,Approved,9,TWD,1,11,2.000001,3.5,5,7,0.35,7.35", csv);
        Assert.EndsWith(",RFQ-1,'\tREQ-1,2", csv);
        Assert.Equal(PurchaseOrderCsv.Headers("en").Count, PurchaseOrderCsv.Headers("zh-Hans").Count);
        Assert.Equal("采购单号", PurchaseOrderCsv.Headers("zh-Hans")[0]);
        Assert.Contains(",已核准,", PurchaseOrderCsv.Line(row, "zh-Hant"));
        Assert.Contains(",已批准,", PurchaseOrderCsv.Line(row, "zh-Hans"));
        Assert.Equal(PurchaseOrderCsv.Headers("zh-Hant"), PurchaseOrderCsv.Headers("fr"));
    }

    [Fact]
    public void LimitIsInclusiveAndOverLimitFailsBeforeWriting()
    {
        PurchaseOrderCsv.EnsureWithinLimit(0);
        PurchaseOrderCsv.EnsureWithinLimit(10000);
        Assert.Equal("PUR.ORDER.EXPORT_LIMIT", Assert.Throws<BusinessRuleException>(() =>
            PurchaseOrderCsv.EnsureWithinLimit(10001)).ErrorCode);
    }
}
