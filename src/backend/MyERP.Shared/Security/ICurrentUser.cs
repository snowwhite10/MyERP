namespace MyERP.Shared.Security;

/// <summary>目前登入用戶（Infrastructure 由 HTTP 請求的 JWT 取得）。</summary>
public interface ICurrentUser
{
    /// <summary>用戶 Id；未登入或背景作業為 null。</summary>
    long? UserId { get; }

    bool IsAuthenticated { get; }
}

/// <summary>目前操作公司（請求標頭 <c>X-Company-Id</c>）。用戶是否可存取該公司由 SYS 模組（WU-SYS-004）驗證。</summary>
public interface ICurrentCompany
{
    /// <summary>公司 Id；沒有公司上下文時為 null，此時公司篩選不回傳任何資料（封閉失敗）。</summary>
    long? CompanyId { get; }
}

/// <summary>系統（背景作業、種子程序）身分。</summary>
public static class SystemUser
{
    /// <summary>沒有登入用戶時，<c>created_by</c> / <c>updated_by</c> 填入的值。</summary>
    public const long Id = 0;
}
