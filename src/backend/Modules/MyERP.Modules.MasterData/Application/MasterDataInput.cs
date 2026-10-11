using MyERP.Shared.Errors;

namespace MyERP.Modules.MasterData.Application;

/// <summary>主檔共用的輸入正規化：代碼轉大寫、名稱必填、選填文字空白轉 null。</summary>
internal static class MasterDataInput
{
    public static string Code(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || code.Length > 30 ||
            code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not '_' and not '-'))
            throw new RequestValidationException("code", "Use 1–30 ASCII letters, digits, hyphen or underscore.");
        return code;
    }

    public static string Name(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            throw new RequestValidationException("name", "Required; maximum 200 characters.");
        return name;
    }

    public static string? OptionalText(string? value, string field, int maxLength)
    {
        var text = value?.Trim();
        if (text?.Length > maxLength) throw new RequestValidationException(field, $"Maximum {maxLength} characters.");
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
