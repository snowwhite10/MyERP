using System.Text.RegularExpressions;

namespace MyERP.Shared.Errors;

/// <summary>錯誤類別，由全域例外處理器對應 HTTP 狀態（api-conventions.md 第 4 節）。</summary>
public enum ErpErrorKind
{
    Validation,
    Forbidden,
    NotFound,
    Conflict,
    BusinessRule,
}

/// <summary>
/// 可預期的業務錯誤基底。<see cref="ErrorCode"/> 格式 <c>{PREFIX}.{RESOURCE}.{REASON}</c>（大寫、底線，至少三段），
/// 同時是前端 i18n key。Controller 不寫 try/catch，由全域處理器轉為 ProblemDetails。
/// </summary>
public abstract partial class ErpException : Exception
{
    protected ErpException(string errorCode, string message)
        : base(message)
    {
        if (!ErrorCodePattern().IsMatch(errorCode ?? ""))
        {
            throw new ArgumentException($"Invalid error code '{errorCode}'. Expected {{PREFIX}}.{{RESOURCE}}.{{REASON}} in upper case.", nameof(errorCode));
        }

        ErrorCode = errorCode!;
    }

    public string ErrorCode { get; }

    public abstract ErpErrorKind Kind { get; }

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*(\.[A-Z0-9_]+){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCodePattern();
}

/// <summary>業務規則違反（422），例如信用額度不足、庫存不足、狀態不允許此動作。</summary>
public sealed class BusinessRuleException(string errorCode, string message) : ErpException(errorCode, message)
{
    public override ErpErrorKind Kind => ErpErrorKind.BusinessRule;
}

/// <summary>資源不存在或不屬於目前公司（404）。</summary>
public sealed class NotFoundException(string errorCode, string resource, object key)
    : ErpException(errorCode, $"{resource} '{key}' was not found.")
{
    public override ErpErrorKind Kind => ErpErrorKind.NotFound;
}

/// <summary>樂觀鎖衝突或唯一值重複（409）。</summary>
public sealed class ConflictException(string errorCode, string message) : ErpException(errorCode, message)
{
    public ConflictException()
        : this(CommonErrorCodes.ConcurrencyConflict, "The record has been modified by another user. Please reload.")
    {
    }

    public override ErpErrorKind Kind => ErpErrorKind.Conflict;
}

/// <summary>無權限、IP 白名單拒絕或資料範圍外（403）。</summary>
public sealed class ForbiddenException(string errorCode, string message) : ErpException(errorCode, message)
{
    public override ErpErrorKind Kind => ErpErrorKind.Forbidden;
}
