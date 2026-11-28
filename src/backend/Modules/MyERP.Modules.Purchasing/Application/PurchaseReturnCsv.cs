using System.Globalization;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// One saved return line with its saved header values (WU-PUR-007c5, ADR-226). <paramref name="UnitCost"/> is the original
/// inventory receipt cost once Posted (<paramref name="CostConfirmed"/>), otherwise the receipt line's base unit cost.
/// </summary>
public sealed record PurchaseReturnCsvRow(string? DocumentNo, DateOnly? BusinessDate, string Status,
    string? PurchaseOrderDocumentNo, long SupplierId, int LineNo, string? ReceiptDocumentNo, int ReceiptLineNo,
    long ItemId, long WarehouseId, decimal Quantity, decimal UnitCost, decimal Amount, bool CostConfirmed);

public static class PurchaseReturnCsv
{
    public const int MaxRows = 10000;

    private static readonly string[] Hant = ["退貨單號", "業務日期", "狀態", "採購單號", "供應商 ID", "行號", "收貨單號",
        "收貨行號", "物料 ID", "倉庫 ID", "退貨數量", "本位幣單位成本", "本位幣退貨金額", "成本來源"];
    private static readonly string[] Hans = ["退货单号", "业务日期", "状态", "采购单号", "供应商 ID", "行号", "收货单号",
        "收货行号", "物料 ID", "仓库 ID", "退货数量", "本位币单位成本", "本位币退货金额", "成本来源"];
    private static readonly string[] En = ["Return no.", "Business date", "Status", "Purchase order no.", "Supplier ID",
        "Line no.", "Receipt no.", "Receipt line no.", "Item ID", "Warehouse ID", "Return quantity", "Base unit cost",
        "Base return amount", "Cost source"];

    public static IReadOnlyList<string> Headers(string language) => language switch
    {
        "zh-Hans" => Hans,
        "en" => En,
        _ => Hant
    };

    public static string HeaderLine(string language) =>
        string.Join(',', Headers(language).Select(PurchaseInvoiceCsv.Cell));

    /// <summary>Line value in base currency, rounded to 4 decimals away from zero like SUBMIT's approval amount.</summary>
    public static decimal Amount(decimal quantity, decimal unitCost) =>
        decimal.Round(quantity * unitCost, 4, MidpointRounding.AwayFromZero);

    public static string Line(PurchaseReturnCsvRow row, string language)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(',', PurchaseInvoiceCsv.Cell(row.DocumentNo),
            row.BusinessDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PurchaseInvoiceCsv.Cell(StatusText(row.Status, language)),
            PurchaseInvoiceCsv.Cell(row.PurchaseOrderDocumentNo), Number(row.SupplierId), Number(row.LineNo),
            PurchaseInvoiceCsv.Cell(row.ReceiptDocumentNo), Number(row.ReceiptLineNo), Number(row.ItemId),
            Number(row.WarehouseId), Number(row.Quantity), Number(row.UnitCost), Number(row.Amount),
            PurchaseInvoiceCsv.Cell(CostSourceText(row.CostConfirmed, language)));
    }

    public static void EnsureWithinLimit(long count, int maxRows = MaxRows)
    {
        if (count > maxRows)
            throw new BusinessRuleException("PUR.RETURN.EXPORT_LIMIT",
                $"Export has {count} return lines; the limit is {maxRows}. Narrow the filters.");
    }

    private static string CostSourceText(bool confirmed, string language) => (confirmed, language) switch
    {
        (true, "en") => "Original receipt cost", (true, "zh-Hans") => "入库原成本", (true, _) => "入庫原成本",
        (false, "en") => "Receipt unit cost (estimate)", (false, "zh-Hans") => "收货单位成本（暂估）",
        _ => "收貨單位成本（暫估）"
    };

    private static string StatusText(string value, string language) => (value, language) switch
    {
        ("Draft", "en") => "Draft", ("Draft", _) => "草稿",
        ("PendingApproval", "en") => "Pending approval", ("PendingApproval", "zh-Hans") => "待审批",
        ("PendingApproval", _) => "待審批",
        ("Rejected", "en") => "Rejected", ("Rejected", "zh-Hans") => "已拒绝",
        ("Rejected", _) => "已駁回",
        ("Posted", "en") => "Posted", ("Posted", "zh-Hans") => "已过账",
        ("Posted", _) => "已過帳",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown purchase return status.")
    };

    // SQL decimal(19,6) reads back with six decimals; trailing zeros are dropped so 2.000000 exports as 2.
    private static string Number(decimal value) =>
        (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
