namespace MyERP.Shared.Auditing;

/// <summary>稽核日誌（sys_audit_log）的操作類型（MASTER_PROMPT 8.2）。</summary>
public static class AuditActions
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
    public const string Void = "VOID";
    public const string Approve = "APPROVE";
    public const string Post = "POST";

    public const string Login = "LOGIN";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string MasterCodeLogin = "MASTER_CODE_LOGIN";
    public const string Logout = "LOGOUT";
    public const string PermissionChange = "PERMISSION_CHANGE";
    public const string Export = "EXPORT";

    /// <summary>業務單據可由 <see cref="IAuditTrail.MarkAction"/> 指定的操作（取代自動判斷的 UPDATE）。</summary>
    public static readonly IReadOnlySet<string> BusinessActions = new HashSet<string>(StringComparer.Ordinal) { Void, Approve, Post };
}

/// <summary>稽核類別：資料變更（SaveChanges 自動記錄）或安全事件（登入、權限變更等）。</summary>
public static class AuditCategories
{
    public const string Data = "DATA";
    public const string Security = "SECURITY";
}

/// <summary>
/// 安全事件。<paramref name="CompanyId"/> 為 null 時使用目前公司（沒有則留空）；<paramref name="Detail"/> 為簡單鍵值，序列化成 JSON，
/// 不得放密碼、Token、Captcha 答案（MASTER_PROMPT 8.1）。
/// </summary>
public sealed record SecurityAuditEvent(
    string Action,
    long? UserId,
    string? Username,
    long? CompanyId = null,
    IReadOnlyDictionary<string, object?>? Detail = null);

/// <summary>
/// 稽核日誌入口（Application 層使用）。資料變更由 SaveChanges interceptor 自動寫入同一交易，業務程式不必呼叫；
/// 只有審批／作廢／過帳等需要標示操作類型，或要記錄安全事件時才使用本介面。
/// </summary>
public interface IAuditTrail
{
    /// <summary>令下一次 SaveChanges 把 <paramref name="entity"/> 的修改記為 <paramref name="action"/>（<see cref="AuditActions.BusinessActions"/>）。</summary>
    void MarkAction(object entity, string action);

    /// <summary>暫存安全事件，於下一次 SaveChanges 與其他變更同一交易寫入。</summary>
    void AddSecurityEvent(SecurityAuditEvent auditEvent);
}

/// <summary>敏感欄位（密碼雜湊、Token 等）：稽核日誌只記錄「有變更」，值以 <c>***</c> 遮蔽。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveDataAttribute : Attribute;

/// <summary>不寫入稽核日誌的實體（例如稽核表本身、純技術性的暫存表）。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotAuditedAttribute : Attribute;
