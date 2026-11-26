using System.Globalization;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>One exported invoice line with its saved header snapshot.</summary>
public sealed record PurchaseInvoiceCsvRow(string SupplierInvoiceNumber, DateOnly BusinessDate,
    PurchaseInvoiceStatus Status, long SupplierId, long PurchaseOrderId, string Currency, int LineNo,
    long ReceiptLineId, decimal Quantity, decimal UnitPrice, decimal TaxRatePercent, decimal Net, decimal Tax,
    decimal Gross);

/// <summary>
/// Purchase invoice CSV text in zh-Hant / zh-Hans / en (MASTER_PROMPT 9.1, ADR-202). Headers and status follow
/// the request language with the 介面 4.19 wording; dates are ISO and numbers invariant so the file reads the
/// same in every locale.
/// </summary>
public static class PurchaseInvoiceCsv
{
    public const int MaxRows = 10000;
    public const string LimitErrorCode = "PUR.INVOICE.EXPORT_LIMIT";

    private static readonly string[] Hant =
    [
        "供應商發票號", "發票日期", "狀態", "供應商 ID", "採購單 ID", "幣別", "行號", "收貨行 ID", "數量", "單價",
        "稅率(%)", "未稅", "稅額", "含稅"
    ];

    private static readonly string[] Hans =
    [
        "供应商发票号", "发票日期", "状态", "供应商 ID", "采购单 ID", "币别", "行号", "收货行 ID", "数量", "单价",
        "税率(%)", "未税", "税额", "含税"
    ];

    private static readonly string[] En =
    [
        "Supplier invoice no.", "Invoice date", "Status", "Supplier ID", "Purchase order ID", "Currency",
        "Line no.", "Receipt line ID", "Quantity", "Unit price", "Tax rate (%)", "Net", "Tax", "Gross"
    ];

    public static IReadOnlyList<string> Headers(string language) => language switch
    {
        "zh-Hans" => Hans,
        "en" => En,
        _ => Hant
    };

    public static string HeaderLine(string language) => string.Join(',', Headers(language).Select(Cell));

    /// <summary>Same wording as the invoice screen's status tags (pur.invoice.state.*).</summary>
    public static string StatusText(PurchaseInvoiceStatus status, string language) => (status, language) switch
    {
        (PurchaseInvoiceStatus.Draft, "en") => "Draft",
        (PurchaseInvoiceStatus.Draft, _) => "草稿",
        (PurchaseInvoiceStatus.Matched, "en") => "Matched",
        (PurchaseInvoiceStatus.Matched, "zh-Hans") => "已比对",
        (PurchaseInvoiceStatus.Matched, _) => "已比對",
        (PurchaseInvoiceStatus.Disputed, "en") => "Disputed",
        (PurchaseInvoiceStatus.Disputed, "zh-Hans") => "有差异",
        (PurchaseInvoiceStatus.Disputed, _) => "有差異",
        (PurchaseInvoiceStatus.Rejected, "en") => "Rejected",
        (PurchaseInvoiceStatus.Rejected, "zh-Hans") => "已拒绝",
        (PurchaseInvoiceStatus.Rejected, _) => "已拒絕",
        (PurchaseInvoiceStatus.Posted, "en") => "Posted",
        (PurchaseInvoiceStatus.Posted, "zh-Hans") => "已过账",
        (PurchaseInvoiceStatus.Posted, _) => "已過帳",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown purchase invoice status.")
    };

    public static string Line(PurchaseInvoiceCsvRow row, string language)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(',',
            Cell(row.SupplierInvoiceNumber), row.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Cell(StatusText(row.Status, language)), Number(row.SupplierId), Number(row.PurchaseOrderId),
            Cell(row.Currency), Number(row.LineNo), Number(row.ReceiptLineId), Number(row.Quantity),
            Number(row.UnitPrice), Number(row.TaxRatePercent), Number(row.Net), Number(row.Tax), Number(row.Gross));
    }

    /// <summary>Rejects an over-limit export before any byte is written.</summary>
    public static void EnsureWithinLimit(long count, int maxRows = MaxRows)
    {
        if (count > maxRows)
            throw new BusinessRuleException(LimitErrorCode,
                $"Export has {count} invoice lines; the limit is {maxRows}. Narrow the filters.");
    }

    /// <summary>RFC 4180 quoting plus a leading apostrophe on text that a spreadsheet would run as a formula.</summary>
    public static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var first = value.AsSpan().TrimStart(' ');
        if (!first.IsEmpty && "=+-@\t\r".Contains(first[0])) value = "'" + value;
        return value.ContainsAny([',', '"', '\r', '\n']) ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
