namespace MyERP.Modules.MasterData.Domain;

/// <summary>
/// 公司內的資金帳戶（現金／銀行，BlueWhale Account）；幣別建立後固定，銀行欄位只有 <c>Bank</c> 才有值。
/// 流水、餘額與期初餘額歸 FI（ADR-041）。類型以 Contracts 列舉名稱保存。
/// </summary>
public sealed class FundAccount : MasterCategory
{
    public const int BankAccountNoMaxLength = 50;

    /// <summary><c>Cash</c>／<c>Bank</c>。</summary>
    public string FundType { get; set; } = "Cash";

    public long CurrencyId { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }

    /// <summary>對應的資產類末級科目；null 表示過帳時另選。</summary>
    public long? LedgerAccountId { get; set; }
}

/// <summary>其他收入／支出的類別（BlueWhale Income &amp; Expense Category）；方向建立後固定（ADR-041）。</summary>
public sealed class IncomeExpenseCategory : MasterCategory
{
    /// <summary><c>Income</c>／<c>Expense</c>。</summary>
    public string Direction { get; set; } = "Income";

    /// <summary>對應的收入（或費用、成本）類末級科目；null 表示過帳時另選。</summary>
    public long? LedgerAccountId { get; set; }
}
