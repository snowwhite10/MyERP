using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyERP.Shared.Security;

/// <summary>
/// 資料範圍，由窄至寬：本人 → 部門 → 分店 → 全公司（MASTER_PROMPT 第 7 節）。
/// 同一權限由多個角色授予時取最寬者；數值越大越寬，不得重排。API 與資料庫皆以名稱字串表示。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DataScope>))]
public enum DataScope
{
    Self = 0,
    Department = 1,
    Branch = 2,
    Company = 3,
}

/// <summary>權限代碼 <c>{PREFIX}.{RESOURCE}.{ACTION}</c>，全大寫（api-conventions.md 第 6 節）。</summary>
public static partial class PermissionCode
{
    public static bool IsValid(string? code) => code is not null && Pattern().IsMatch(code);

    /// <summary>格式不符即拋出，用於啟動時與屬性建構時及早發現打錯的代碼。</summary>
    public static string Ensure(string? code) =>
        IsValid(code) ? code! : throw new ArgumentException($"Invalid permission code '{code}'. Expected {{PREFIX}}.{{RESOURCE}}.{{ACTION}} in upper case.", nameof(code));

    [GeneratedRegex(@"^[A-Z][A-Z0-9]*\.[A-Z][A-Z0-9_]*\.[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>模組宣告的權限；<see cref="Module"/> 為模組代碼（例如 <c>SYS</c>），代碼前綴必須與模組前綴相同。</summary>
public sealed record PermissionDefinition(string Code, string Module);

/// <summary>每個模組提供本模組的權限清單；SYS 模組彙整為權限目錄，角色只能授予目錄內的代碼。</summary>
public interface IPermissionDefinitionProvider
{
    IReadOnlyList<PermissionDefinition> Permissions { get; }
}

/// <summary>用戶在某公司的有效授權：組織歸屬（資料範圍判斷用）與每個權限的最寬資料範圍。</summary>
public sealed class UserCompanyAuthorization(
    long userId,
    long companyId,
    long? branchId,
    long? departmentId,
    bool isFullAccess,
    IReadOnlyDictionary<string, DataScope> permissions)
{
    public long UserId { get; } = userId;

    public long CompanyId { get; } = companyId;

    public long? BranchId { get; } = branchId;

    public long? DepartmentId { get; } = departmentId;

    /// <summary>具「全部權限」角色：任何權限皆為全公司範圍。</summary>
    public bool IsFullAccess { get; } = isFullAccess;

    /// <summary>明確授予的權限（全權角色時不必列出全部代碼）。</summary>
    public IReadOnlyDictionary<string, DataScope> Permissions { get; } = permissions ?? throw new ArgumentNullException(nameof(permissions));

    public bool Has(string permissionCode) => ScopeOf(permissionCode) is not null;

    public DataScope? ScopeOf(string permissionCode) =>
        IsFullAccess ? DataScope.Company : Permissions.TryGetValue(permissionCode, out var scope) ? scope : null;

    /// <summary>某權限的資料範圍篩選條件；沒有該權限時為 null（呼叫端應回 403）。</summary>
    public DataScopeFilter? FilterFor(string permissionCode) =>
        ScopeOf(permissionCode) is { } scope ? new DataScopeFilter(scope, UserId, BranchId, DepartmentId) : null;
}

/// <summary>由 SYS 模組實作：查詢用戶在公司的授權。用戶停用、未獲授權該公司或公司停用時回傳 null。</summary>
public interface IUserAuthorizationStore
{
    Task<UserCompanyAuthorization?> GetAsync(long userId, long companyId, CancellationToken cancellationToken);
}

/// <summary>
/// 目前請求（用戶 + <c>X-Company-Id</c>）的授權，同一請求只查詢一次。未登入、沒有公司上下文或無權存取該公司時為 null。
/// Application 層以此取得資料範圍（<see cref="UserCompanyAuthorization.FilterFor"/>）。
/// </summary>
public interface ICurrentAuthorization
{
    Task<UserCompanyAuthorization?> GetAsync(CancellationToken cancellationToken);
}

/// <summary>需要資料範圍控制的單據：歸屬分店、部門與負責人（通常為建立者或業務員）。</summary>
public interface IDataScopedEntity
{
    long? BranchId { get; }

    long? DepartmentId { get; }

    long OwnerUserId { get; }
}

/// <summary>
/// 資料範圍篩選。分店／部門範圍時，用戶本身沒有分店或部門歸屬即退回「本人」，避免因缺少設定而看見全公司資料（封閉失敗）。
/// </summary>
public sealed record DataScopeFilter(DataScope Scope, long UserId, long? BranchId, long? DepartmentId)
{
    /// <summary>實際生效的範圍（缺少組織歸屬時降為 <see cref="DataScope.Self"/>）。</summary>
    public DataScope EffectiveScope => Scope switch
    {
        DataScope.Branch when BranchId is null => DataScope.Self,
        DataScope.Department when DepartmentId is null => DataScope.Self,
        _ => Scope,
    };

    public IQueryable<T> Apply<T>(IQueryable<T> query)
        where T : IDataScopedEntity
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = UserId;
        var branchId = BranchId;
        var departmentId = DepartmentId;
        return EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == branchId),
            DataScope.Department => query.Where(x => x.DepartmentId == departmentId),
            _ => query.Where(x => x.OwnerUserId == userId),
        };
    }

    public bool Allows(IDataScopedEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return EffectiveScope switch
        {
            DataScope.Company => true,
            DataScope.Branch => entity.BranchId == BranchId,
            DataScope.Department => entity.DepartmentId == DepartmentId,
            _ => entity.OwnerUserId == UserId,
        };
    }
}
