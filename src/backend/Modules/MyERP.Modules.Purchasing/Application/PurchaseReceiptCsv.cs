using System.Globalization;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One saved receipt line with its saved header values (WU-PUR-007a10); unit cost is the base-currency snapshot.</summary>
public sealed record PurchaseReceiptCsvRow(string? DocumentNo, DateOnly? BusinessDate, string Status,
    string? PurchaseOrderDocumentNo, long SupplierId, string Currency, int LineNo, long ItemId,
    decimal Quantity, long WarehouseId, decimal UnitCost, decimal PostedReceived);

public static class PurchaseReceiptCsv
{
    public const int MaxRows = 10000;

    private static readonly string[] Hant = ["收貨單號", "業務日期", "狀態", "採購單號", "供應商 ID", "採購幣別", "行號",
        "物料 ID", "收貨數量", "倉庫 ID", "本位幣單位成本", "已入庫數量"];
    private static readonly string[] Hans = ["收货单号", "业务日期", "状态", "采购单号", "供应商 ID", "采购币别", "行号",
        "物料 ID", "收货数量", "仓库 ID", "本位币单位成本", "已入库数量"];
    private static readonly string[] En = ["Receipt no.", "Business date", "Status", "Purchase order no.", "Supplier ID",
        "Order currency", "Line no.", "Item ID", "Received quantity", "Warehouse ID", "Base unit cost", "Posted quantity"];

    public static IReadOnlyList<string> Headers(string language) => language switch
    {
        "zh-Hans" => Hans,
        "en" => En,
        _ => Hant
    };

    public static string HeaderLine(string language) =>
        string.Join(',', Headers(language).Select(PurchaseInvoiceCsv.Cell));

    public static string Line(PurchaseReceiptCsvRow row, string language)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(',', PurchaseInvoiceCsv.Cell(row.DocumentNo),
            row.BusinessDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PurchaseInvoiceCsv.Cell(StatusText(row.Status, language)),
            PurchaseInvoiceCsv.Cell(row.PurchaseOrderDocumentNo), Number(row.SupplierId),
            PurchaseInvoiceCsv.Cell(row.Currency), Number(row.LineNo), Number(row.ItemId), Number(row.Quantity),
            Number(row.WarehouseId), Number(row.UnitCost), Number(row.PostedReceived));
    }

    public static void EnsureWithinLimit(long count, int maxRows = MaxRows)
    {
        if (count > maxRows)
            throw new BusinessRuleException("PUR.RECEIPT.EXPORT_LIMIT",
                $"Export has {count} receipt lines; the limit is {maxRows}. Narrow the filters.");
    }

    private static string StatusText(string value, string language) => (value, language) switch
    {
        ("Draft", "en") => "Draft", ("Draft", _) => "草稿",
        ("PendingApproval", "en") => "Pending approval", ("PendingApproval", "zh-Hans") => "待审批",
        ("PendingApproval", _) => "待審批",
        ("Rejected", "en") => "Rejected", ("Rejected", "zh-Hans") => "已拒绝",
        ("Rejected", _) => "已駁回",
        ("Posted", "en") => "Posted", ("Posted", "zh-Hans") => "已过账",
        ("Posted", _) => "已過帳",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown purchase receipt status.")
    };

    // SQL decimal(19,6) reads back with six decimals; trailing zeros are dropped so 2.000000 exports as 2.
    private static string Number(decimal value) =>
        (value / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
