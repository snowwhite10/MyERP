using MyERP.Shared.Errors;

namespace MyERP.Modules.System.Application;

/// <summary>可由系統設定綁定的密碼政策；啟動時須驗證設定範圍。</summary>
public sealed class PasswordPolicy
{
    public int MinimumLength { get; init; } = 12;
    public int MaximumLength { get; init; } = 128;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public bool RequireDigit { get; init; } = true;
    public bool RequireNonAlphanumeric { get; init; } = true;
    public int ExpirationDays { get; init; } = 90;

    public void ValidateConfiguration()
    {
        if (MinimumLength is < 8 or > 128 || MaximumLength < MinimumLength || MaximumLength > 1024 || ExpirationDays is < 0 or > 3650)
        {
            throw new InvalidOperationException("Invalid password policy configuration.");
        }
    }

    /// <param name="password">待檢查的密碼。</param>
    /// <param name="field">驗證錯誤回應中的欄位名稱（本人修改密碼為 <c>newPassword</c>）。</param>
    public void ValidatePassword(string password, string field = "password")
    {
        ValidateConfiguration();
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength || password.Length > MaximumLength ||
            (RequireUppercase && !password.Any(char.IsUpper)) ||
            (RequireLowercase && !password.Any(char.IsLower)) ||
            (RequireDigit && !password.Any(char.IsDigit)) ||
            (RequireNonAlphanumeric && !password.Any(c => !char.IsLetterOrDigit(c))))
        {
            throw new RequestValidationException(field, "Password does not meet the configured policy.");
        }
    }
}
