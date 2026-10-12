using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>資金帳戶類型（ADR-041）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FundAccountType>))]
public enum FundAccountType { Cash, Bank }

/// <summary>收支類別方向（ADR-041）；建立後不可修改。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IncomeExpenseDirection>))]
public enum IncomeExpenseDirection { Income, Expense }

/// <summary>銀行欄位只有 <see cref="FundAccountType.Bank"/> 才有值；未設定對應科目時 <see cref="LedgerAccountCode"/> 為 null。</summary>
public sealed record FundAccountInfo(string Code, string Name, FundAccountType FundType, string CurrencyCode, string? BankName,
    string? BankAccountNo, string? LedgerAccountCode, bool IsActive);

public sealed record IncomeExpenseCategoryInfo(string Code, string Name, IncomeExpenseDirection Direction, string? LedgerAccountCode,
    bool IsActive);

/// <summary>其他模組讀取目前公司資金帳戶的唯一入口（不得直接讀 md_fund_account，ADR-041）。</summary>
public interface IFundAccountLookup
{
    /// <summary>依代碼找資金帳戶，含停用；不存在回 null。</summary>
    Task<FundAccountInfo?> FindAsync(string code, CancellationToken token = default);

    /// <summary>新單據選用前驗證：不存在 404 <c>MD.FUND_ACCOUNT.NOT_FOUND</c>，停用 422 <c>MD.FUND_ACCOUNT.INACTIVE</c>。</summary>
    Task<FundAccountInfo> RequireActiveAsync(string code, CancellationToken token = default);
}

/// <summary>其他模組讀取目前公司收支類別的唯一入口（不得直接讀 md_income_expense_category，ADR-041）。</summary>
public interface IIncomeExpenseCategoryLookup
{
    /// <summary>依代碼找收支類別，含停用；不存在回 null。</summary>
    Task<IncomeExpenseCategoryInfo?> FindAsync(string code, CancellationToken token = default);

    /// <summary>
    /// 新單據選用前驗證：不存在 404 <c>MD.INCOME_EXPENSE_CATEGORY.NOT_FOUND</c>，停用 422 <c>MD.INCOME_EXPENSE_CATEGORY.INACTIVE</c>，
    /// 方向與單據不符 422 <c>MD.INCOME_EXPENSE_CATEGORY.DIRECTION_MISMATCH</c>。
    /// </summary>
    Task<IncomeExpenseCategoryInfo> RequireActiveAsync(string code, IncomeExpenseDirection direction, CancellationToken token = default);
}

/// <summary>由引用資金帳戶的模組（SD、PUR、FI）實作並註冊；MD 刪除前逐一詢問，不讀其他模組的表（ADR-041）。</summary>
public interface IFundAccountUsage
{
    /// <summary>目前公司是否有資料引用此資金帳戶代碼。</summary>
    Task<bool> IsInUseAsync(string fundAccountCode, CancellationToken token = default);
}

/// <summary>由引用收支類別的模組（FI 其他收入／支出）實作並註冊；MD 刪除前逐一詢問（ADR-041）。</summary>
public interface IIncomeExpenseCategoryUsage
{
    /// <summary>目前公司是否有資料引用此收支類別代碼。</summary>
    Task<bool> IsInUseAsync(string categoryCode, CancellationToken token = default);
}
