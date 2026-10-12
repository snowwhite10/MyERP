namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內的結算方式（現金、轉帳、支票等）；收付款明細另選資金帳戶，兩者互不綁定（ADR-040）。</summary>
public sealed class SettlementMethod : MasterCategory;

/// <summary>
/// 付款條件：由單據日期推算到期日的規則（ADR-040）。<see cref="DueType"/> 決定使用哪些欄位，其餘為 null：
/// <c>NetDays</c> 用 <see cref="Days"/>；<c>ClosingDays</c> 用 <see cref="ClosingDay"/> 與 <see cref="Days"/>；
/// <c>ClosingMonthDay</c> 用 <see cref="ClosingDay"/>、<see cref="Months"/> 與 <see cref="PaymentDay"/>。
/// </summary>
public sealed class PaymentTerm : MasterCategory
{
    public const string NetDays = "NetDays";
    public const string ClosingDays = "ClosingDays";
    public const string ClosingMonthDay = "ClosingMonthDay";
    public const int MaxDays = 365;
    public const int MaxMonths = 12;

    /// <summary>結帳日或付款日為 31 表示月底；大於當月天數時一律取月底。</summary>
    public const int MonthEnd = 31;

    public string DueType { get; set; } = NetDays;
    public int? Days { get; set; } = 0;
    public int? ClosingDay { get; set; }
    public int? Months { get; set; }
    public int? PaymentDay { get; set; }

    /// <summary>單據日期對應的到期日。</summary>
    public DateOnly DueDate(DateOnly documentDate) => DueType switch
    {
        NetDays => documentDate.AddDays(Days!.Value),
        ClosingDays => ClosingDate(documentDate).AddDays(Days!.Value),
        ClosingMonthDay => DayOfMonth(ClosingDate(documentDate).AddMonths(Months!.Value), PaymentDay!.Value),
        _ => throw new InvalidOperationException($"Unknown due type '{DueType}'."),
    };

    /// <summary>單據所屬帳期的結帳日：單據日不晚於當月結帳日屬當期，否則屬下期。</summary>
    public DateOnly ClosingDate(DateOnly documentDate)
    {
        var closing = DayOfMonth(documentDate, ClosingDay!.Value);
        return documentDate <= closing ? closing : DayOfMonth(documentDate.AddMonths(1), ClosingDay.Value);
    }

    private static DateOnly DayOfMonth(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)));
}
