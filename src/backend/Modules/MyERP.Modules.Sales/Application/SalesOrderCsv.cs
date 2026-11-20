using System.Globalization;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>One exported order line with its saved header snapshot.</summary>
public sealed record SalesOrderCsvRow(string DocumentNo, DateOnly BusinessDate, SalesOrderStatus Status,
    string CustomerCode, string CustomerName, string CurrencyCode, decimal ExchangeRate, int LineNo,
    string ItemCode, string ItemName, decimal Quantity, decimal UnitPrice, decimal DiscountRate,
    decimal TaxRatePercent, decimal OriginalNet, decimal OriginalTax, decimal OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross, string? Remark);

/// <summary>
/// Sales order CSV text in zh-Hant / zh-Hans / en (ADR-141). Headers and status follow the request
/// language; dates are ISO and numbers invariant so the file re-imports identically in every locale.
/// </summary>
public static class SalesOrderCsv
{
    public const int MaxRows = 10000;
    public const string LimitErrorCode = "SAL.ORDER.EXPORT_LIMIT";

    private static readonly string[] Hant =
    [
        "單號", "訂單日期", "狀態", "客戶代碼", "客戶名稱", "幣別", "匯率", "行號", "物料代碼", "物料名稱",
        "數量", "單價", "折扣率", "稅率(%)", "原幣未稅", "原幣稅額", "原幣含稅", "本位幣未稅", "本位幣稅額",
        "本位幣含稅", "備註"
    ];

    private static readonly string[] Hans =
    [
        "单号", "订单日期", "状态", "客户代码", "客户名称", "币别", "汇率", "行号", "物料代码", "物料名称",
        "数量", "单价", "折扣率", "税率(%)", "原币未税", "原币税额", "原币含税", "本位币未税", "本位币税额",
        "本位币含税", "备注"
    ];

    private static readonly string[] En =
    [
        "Order no.", "Order date", "Status", "Customer code", "Customer name", "Currency", "Exchange rate",
        "Line no.", "Item code", "Item name", "Quantity", "Unit price", "Discount rate", "Tax rate (%)",
        "Net (document)", "Tax (document)", "Gross (document)", "Net (base)", "Tax (base)", "Gross (base)",
        "Remark"
    ];

    public static IReadOnlyList<string> Headers(string language) => language switch
    {
        "zh-Hans" => Hans,
        "en" => En,
        _ => Hant
    };

    public static string HeaderLine(string language) => string.Join(',', Headers(language).Select(Cell));

    /// <summary>Same wording as the order screens' status tags.</summary>
    public static string StatusText(SalesOrderStatus status, string language) => (status, language) switch
    {
        (SalesOrderStatus.Draft, "en") => "Draft",
        (SalesOrderStatus.Draft, _) => "草稿",
        (SalesOrderStatus.Pending, "en") => "Pending approval",
        (SalesOrderStatus.Pending, "zh-Hans") => "审批中",
        (SalesOrderStatus.Pending, _) => "審批中",
        (SalesOrderStatus.Rejected, "en") => "Rejected",
        (SalesOrderStatus.Rejected, "zh-Hans") => "已驳回",
        (SalesOrderStatus.Rejected, _) => "已駁回",
        (SalesOrderStatus.Approved, "en") => "Approved",
        (SalesOrderStatus.Approved, "zh-Hans") => "已批准",
        (SalesOrderStatus.Approved, _) => "已核准",
        (SalesOrderStatus.Cancelled, "en") => "Cancelled",
        (SalesOrderStatus.Cancelled, _) => "已取消",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown sales order status.")
    };

    public static string Line(SalesOrderCsvRow row, string language)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Join(',',
            Cell(row.DocumentNo), row.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Cell(StatusText(row.Status, language)), Cell(row.CustomerCode), Cell(row.CustomerName),
            Cell(row.CurrencyCode), Number(row.ExchangeRate), row.LineNo.ToString(CultureInfo.InvariantCulture),
            Cell(row.ItemCode), Cell(row.ItemName), Number(row.Quantity), Number(row.UnitPrice),
            Number(row.DiscountRate), Number(row.TaxRatePercent), Number(row.OriginalNet),
            Number(row.OriginalTax), Number(row.OriginalGross), Number(row.BaseNet), Number(row.BaseTax),
            Number(row.BaseGross), Cell(row.Remark));
    }

    /// <summary>Rejects an over-limit export before any byte is written.</summary>
    public static void EnsureWithinLimit(long count, int maxRows = MaxRows)
    {
        if (count > maxRows)
            throw new BusinessRuleException(LimitErrorCode,
                $"Export has {count} order lines; the limit is {maxRows}. Narrow the filters.");
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
}
