using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MyERP.Shared.Errors;

namespace MyERP.Shared.Infrastructure.Web;

/// <summary>
/// 例外 / 狀態碼 → ProblemDetails（api-conventions.md 第 4 節）。純函式，traceId 與 instance 由
/// <see cref="ErpProblemDetailsSetup"/> 在寫出回應時補上。未預期例外不回傳任何例外內容。
/// </summary>
public static class ErpProblemDetailsFactory
{
    public const string TypeBase = "https://myerp.local/errors/";
    public const string DefaultLanguage = "zh-Hant";

    private static readonly FrozenDictionary<int, string> TypeSlugs = new Dictionary<int, string>
    {
        [StatusCodes.Status400BadRequest] = "validation",
        [StatusCodes.Status401Unauthorized] = "unauthorized",
        [StatusCodes.Status403Forbidden] = "forbidden",
        [StatusCodes.Status404NotFound] = "not-found",
        [StatusCodes.Status405MethodNotAllowed] = "method-not-allowed",
        [StatusCodes.Status409Conflict] = "conflict",
        [StatusCodes.Status422UnprocessableEntity] = "business-rule",
        [StatusCodes.Status429TooManyRequests] = "rate-limited",
    }.ToFrozenDictionary();

    // 依狀態碼的標題：zh-Hant / zh-Hans / en
    private static readonly FrozenDictionary<int, (string Hant, string Hans, string En)> Titles = new Dictionary<int, (string, string, string)>
    {
        [StatusCodes.Status400BadRequest] = ("輸入資料不正確", "输入数据不正确", "Invalid input"),
        [StatusCodes.Status401Unauthorized] = ("尚未登入或登入已逾時", "尚未登录或登录已超时", "Not authenticated"),
        [StatusCodes.Status403Forbidden] = ("沒有權限執行此操作", "没有权限执行此操作", "Access denied"),
        [StatusCodes.Status404NotFound] = ("資源不存在", "资源不存在", "Resource not found"),
        [StatusCodes.Status405MethodNotAllowed] = ("不支援此操作方式", "不支持此操作方式", "Method not allowed"),
        [StatusCodes.Status409Conflict] = ("資料已被他人修改，請重新載入", "数据已被他人修改，请重新加载", "The record was modified by another user"),
        [StatusCodes.Status422UnprocessableEntity] = ("業務規則不允許此操作", "业务规则不允许此操作", "Business rule violated"),
        [StatusCodes.Status429TooManyRequests] = ("請求過於頻繁，請稍後再試", "请求过于频繁，请稍后再试", "Too many requests"),
        [StatusCodes.Status500InternalServerError] = ("系統發生錯誤，請稍後再試", "系统发生错误，请稍后再试", "An unexpected error occurred"),
    }.ToFrozenDictionary();

    public static string TypeForStatus(int status) =>
        TypeBase + (TypeSlugs.TryGetValue(status, out var slug) ? slug : "internal");

    public static string TitleFor(int status, string? language)
    {
        var titles = Titles.TryGetValue(status, out var t) ? t : Titles[status >= 500 ? StatusCodes.Status500InternalServerError : StatusCodes.Status400BadRequest];
        return NormalizeLanguage(language) switch
        {
            "zh-Hans" => titles.Hans,
            "en" => titles.En,
            _ => titles.Hant,
        };
    }

    /// <summary>由 Accept-Language 選出支援的語言（zh-Hant / zh-Hans / en），無符合時用 zh-Hant。</summary>
    public static string ResolveLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)
            || !StringWithQualityHeaderValue.TryParseList([acceptLanguage], out var values))
        {
            return DefaultLanguage;
        }

        foreach (var value in values.OrderByDescending(v => v.Quality ?? 1))
        {
            if (value.Value.Value is { } tag && TryMapLanguage(tag, out var language))
            {
                return language;
            }
        }

        return DefaultLanguage;
    }

    public static ProblemDetails FromException(Exception exception, string? language)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            RequestValidationException validation => Create(StatusCodes.Status400BadRequest, language, validation.Message, validation.ErrorCode, validation.Errors),
            ErpException erp => Create(StatusFor(erp.Kind), language, erp.Message, erp.ErrorCode),
            DbUpdateConcurrencyException => Create(StatusCodes.Status409Conflict, language, null, CommonErrorCodes.ConcurrencyConflict),
            // 框架對請求格式錯誤（例如 JSON 無法解析、Body 過大）拋出的例外，保留其 4xx 狀態而非 500
            BadHttpRequestException bad => FromStatus(bad.StatusCode, language),
            _ => Create(StatusCodes.Status500InternalServerError, language, null, CommonErrorCodes.InternalError),
        };
    }

    /// <summary>沒有例外的錯誤狀態（例如路由不存在的 404）。</summary>
    public static ProblemDetails FromStatus(int status, string? language) =>
        Create(status, language, null, status == StatusCodes.Status404NotFound ? CommonErrorCodes.NotFound : null);

    private static int StatusFor(ErpErrorKind kind) => kind switch
    {
        ErpErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErpErrorKind.Unauthenticated => StatusCodes.Status401Unauthorized,
        ErpErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErpErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErpErrorKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    private static ProblemDetails Create(int status, string? language, string? detail, string? errorCode, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = TypeForStatus(status),
            Title = TitleFor(status, language),
            Detail = detail,
        };
        if (errorCode is not null)
        {
            problem.Extensions["errorCode"] = errorCode;
        }

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }

    private static string NormalizeLanguage(string? language) =>
        language is not null && TryMapLanguage(language, out var mapped) ? mapped : DefaultLanguage;

    private static bool TryMapLanguage(string tag, out string language)
    {
        var upper = tag.ToUpperInvariant();
        language = upper switch
        {
            "ZH-HANS" or "ZH-CN" or "ZH-SG" or "ZH-HANS-CN" => "zh-Hans",
            "ZH" or "ZH-HANT" or "ZH-TW" or "ZH-HK" or "ZH-MO" or "ZH-HANT-TW" or "ZH-HANT-HK" => "zh-Hant",
            "EN" => "en",
            _ when upper.StartsWith("EN-", StringComparison.Ordinal) => "en",
            _ => "",
        };
        return language.Length > 0;
    }
}
