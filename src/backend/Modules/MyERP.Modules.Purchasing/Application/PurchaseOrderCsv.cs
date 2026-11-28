using System.Globalization;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One saved order line and its saved header values; source numbers are historical labels.</summary>
public sealed record PurchaseOrderCsvRow(string? DocumentNo, DateOnly? BusinessDate, string Status,
    long SupplierId, string Currency, int LineNo, long ItemId, decimal Quantity, decimal UnitPrice,
    decimal TaxRatePercent, decimal? BaseNet, decimal? BaseTax, decimal? BaseGross,
    string? RfqDocumentNo, string? RequisitionDocumentNo, int? RequisitionLineNo);

public static class PurchaseOrderCsv
{
    public const int MaxRows = 10000;

    private static readonly string[] Hant = ["採購單號", "業務日期", "狀態", "供應商 ID", "幣別", "行號", "物料 ID",
        "數量", "單價", "稅率(%)", "本位幣未稅", "本位幣稅額", "本位幣含稅", "詢價單號", "請購單號", "請購行號"];
    private static readonly string[] Hans = ["采购单号", "业务日期", "状态", "供应商 ID", "币别", "行号", "物料 ID",
        "数量", "单价", "税率(%)", "本位币未税", "本位币税额", "本位币含税", "询价单号", "请购单号", "请购行号"];
    private static readonly string[] En = ["Purchase order no.", "Business date", "Status", "Supplier ID", "Currency",
        "Line no.", "Item ID", "Quantity", "Unit price", "Tax rate (%)", "Base net", "Base tax", "Base gross",
        "RFQ no.", "Requisition no.", "Requisition line no."];

    public static IReadOnlyList<string> Headers(string language) => language switch
    {
        "zh-Hans" => Hans,
        "en" => En,
        _ => Hant
    };

    public static string HeaderLine(string language) => string.Join(',', Headers(language).Select(PurchaseInvoiceCsv.Cell));

    public static string Line(PurchaseOrderCsvRow row, string language)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(',', PurchaseInvoiceCsv.Cell(row.DocumentNo),
            row.BusinessDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            PurchaseInvoiceCsv.Cell(StatusText(row.Status, language)), Number(row.SupplierId),
            PurchaseInvoiceCsv.Cell(row.Currency), Number(row.LineNo), Number(row.ItemId), Number(row.Quantity),
            Number(row.UnitPrice), Number(row.TaxRatePercent), Number(row.BaseNet), Number(row.BaseTax),
            Number(row.BaseGross), PurchaseInvoiceCsv.Cell(row.RfqDocumentNo),
            PurchaseInvoiceCsv.Cell(row.RequisitionDocumentNo), Number(row.RequisitionLineNo));
    }

    public static void EnsureWithinLimit(long count, int maxRows = MaxRows)
    {
        if (count > maxRows)
            throw new BusinessRuleException("PUR.ORDER.EXPORT_LIMIT",
                $"Export has {count} purchase order lines; the limit is {maxRows}. Narrow the filters.");
    }

    private static string StatusText(string value, string language) => (value, language) switch
    {
        ("Draft", "en") => "Draft", ("Draft", _) => "草稿",
        ("PendingApproval", "en") => "Pending approval", ("PendingApproval", "zh-Hans") => "待审批",
        ("PendingApproval", _) => "待審批",
        ("Approved", "en") => "Approved", ("Approved", "zh-Hans") => "已批准",
        ("Approved", _) => "已核准",
        ("Rejected", "en") => "Rejected", ("Rejected", "zh-Hans") => "已拒绝",
        ("Rejected", _) => "已駁回",
        ("Cancelled", "en") => "Cancelled", ("Cancelled", "zh-Hans") => "已取消",
        ("Cancelled", _) => "已取消",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown purchase order status.")
    };

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(decimal? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
