using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>科目類別；子科目與上層相同，建立後不可修改（ADR-039）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountType>))]
public enum AccountType { Asset, Liability, Equity, Cost, Revenue, Expense }

/// <summary>科目餘額的正常方向。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BalanceDirection>))]
public enum BalanceDirection { Debit, Credit }

/// <summary><see cref="IsPostable"/> = 啟用且沒有子科目。</summary>
public sealed record AccountInfo(string Code, string Name, string? ParentCode, AccountType AccountType,
    BalanceDirection BalanceDirection, bool IsActive, bool IsPostable);

/// <summary>其他模組讀取目前公司會計科目的唯一入口（不得直接讀 md_account，ADR-039）。</summary>
public interface IChartOfAccounts
{
    /// <summary>依代碼找科目，含停用；不存在回 null。</summary>
    Task<AccountInfo?> FindAsync(string code, CancellationToken token = default);

    /// <summary>
    /// 過帳前驗證：不存在 404 <c>MD.ACCOUNT.NOT_FOUND</c>；停用 422 <c>MD.ACCOUNT.INACTIVE</c>；
    /// 有子科目 422 <c>MD.ACCOUNT.NOT_POSTABLE</c>。
    /// </summary>
    Task<AccountInfo> RequirePostableAsync(string code, CancellationToken token = default);
}

/// <summary>
/// 由使用科目的模組（FI 傳票、期初餘額等）實作並註冊；MD 刪除科目、修改借貸方向、新增子科目前逐一詢問，
/// 不讀其他模組的表（ADR-039）。
/// </summary>
public interface IAccountUsage
{
    /// <summary>目前公司是否有資料引用此科目代碼。</summary>
    Task<bool> IsInUseAsync(string accountCode, CancellationToken token = default);
}
