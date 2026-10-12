namespace MyERP.Modules.MasterData.Domain;

/// <summary>
/// 公司內的會計科目；代碼只含數字，子科目代碼以上層代碼開頭，上層與類別建立後固定（ADR-039）。
/// 類別與方向以 Contracts 列舉名稱保存（Domain 不引用 Contracts）。
/// </summary>
public sealed class Account : MasterCategory
{
    /// <summary>同公司的上層科目；null 為根科目。</summary>
    public long? ParentId { get; set; }

    /// <summary><c>Asset</c>／<c>Liability</c>／<c>Equity</c>／<c>Cost</c>／<c>Revenue</c>／<c>Expense</c>。</summary>
    public string AccountType { get; set; } = "Asset";

    /// <summary><c>Debit</c>／<c>Credit</c>。</summary>
    public string BalanceDirection { get; set; } = "Debit";

    /// <summary>省略借貸方向時的類別慣例：資產、成本、費用為借方，其餘為貸方。</summary>
    public static string DefaultDirection(string accountType) =>
        accountType is "Asset" or "Cost" or "Expense" ? "Debit" : "Credit";

    /// <summary>子科目代碼必須以上層代碼開頭且較長。</summary>
    public static bool IsValidChildCode(string parentCode, string childCode) =>
        childCode.Length > parentCode.Length && childCode.StartsWith(parentCode, StringComparison.Ordinal);
}
