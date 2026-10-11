using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MyERP.Shared.Errors;

namespace MyERP.Shared.Settings;

/// <summary>系統參數的值型別；API 與資料庫都以字串保存正規化後的值（ADR-026）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParameterType>))]
public enum ParameterType
{
    /// <summary><c>true</c>／<c>false</c>。</summary>
    Boolean,

    /// <summary>整數，限定在宣告的範圍內。</summary>
    WholeNumber,

    /// <summary>十進位數（不使用浮點），以不變文化格式保存並去除尾隨 0。</summary>
    Number,

    /// <summary>文字，去除前後空白，限定長度。</summary>
    Text,

    /// <summary>宣告的選項之一（大小寫不分，保存為宣告的寫法）。</summary>
    Option,
}

/// <summary>參數鍵 <c>{PREFIX}.{NAME}</c>，全大寫，最多 80 字，前綴必須屬於宣告模組。</summary>
public static partial class ParameterKey
{
    public const int MaxLength = 80;

    public static bool IsValid(string? key) => key is { Length: <= MaxLength } && Pattern().IsMatch(key);

    [GeneratedRegex(@"^[A-Z][A-Z0-9]*\.[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

/// <summary>
/// 模組宣告的公司層級系統參數（REQ_F4118.17）。<see cref="Group"/> 為畫面分組代碼（前端 i18n 用）；
/// 只有 <see cref="ParameterType.WholeNumber"/>／<see cref="ParameterType.Number"/> 使用 <see cref="Minimum"/>／<see cref="Maximum"/>，
/// 只有 <see cref="ParameterType.Text"/> 使用 <see cref="MaxLength"/>，只有 <see cref="ParameterType.Option"/> 使用 <see cref="Options"/>。
/// 以工廠方法建立，宣告錯誤（鍵格式、預設值不合規則）在啟動時拋出。
/// </summary>
public sealed class ParameterDefinition
{
    /// <summary>值欄位 <c>sys_parameter.param_value</c> 的長度上限。</summary>
    public const int MaxValueLength = 500;

    private ParameterDefinition(
        string key, string module, string group, ParameterType type, decimal? minimum, decimal? maximum, int maxLength, IReadOnlyList<string> options)
    {
        if (!ParameterKey.IsValid(key))
            throw new ArgumentException($"Invalid parameter key '{key}'. Expected {{PREFIX}}.{{NAME}} in upper case, at most {ParameterKey.MaxLength} characters.", nameof(key));
        if (string.IsNullOrWhiteSpace(module)) throw new ArgumentException("Module is required.", nameof(module));
        if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Group is required.", nameof(group));
        if (minimum > maximum) throw new ArgumentException("Minimum cannot exceed maximum.", nameof(minimum));
        if (maxLength is < 1 or > MaxValueLength) throw new ArgumentException($"Maximum length must be 1–{MaxValueLength}.", nameof(maxLength));

        Key = key;
        Module = module;
        Group = group;
        Type = type;
        Minimum = minimum;
        Maximum = maximum;
        MaxLength = maxLength;
        Options = options;
    }

    public string Key { get; }

    public string Module { get; }

    public string Group { get; }

    public ParameterType Type { get; }

    public string DefaultValue { get; private set; } = "";

    public decimal? Minimum { get; }

    public decimal? Maximum { get; }

    public int MaxLength { get; }

    public IReadOnlyList<string> Options { get; }

    public static ParameterDefinition Boolean(string key, string module, string group, bool defaultValue) =>
        new ParameterDefinition(key, module, group, ParameterType.Boolean, null, null, 5, []).WithDefault(defaultValue ? "true" : "false");

    public static ParameterDefinition WholeNumber(string key, string module, string group, int defaultValue, int minimum, int maximum) =>
        new ParameterDefinition(key, module, group, ParameterType.WholeNumber, minimum, maximum, 11, [])
            .WithDefault(defaultValue.ToString(CultureInfo.InvariantCulture));

    public static ParameterDefinition Number(string key, string module, string group, decimal defaultValue, decimal minimum, decimal maximum) =>
        new ParameterDefinition(key, module, group, ParameterType.Number, minimum, maximum, 40, [])
            .WithDefault(defaultValue.ToString(CultureInfo.InvariantCulture));

    public static ParameterDefinition Text(string key, string module, string group, string defaultValue, int maxLength) =>
        new ParameterDefinition(key, module, group, ParameterType.Text, null, null, maxLength, []).WithDefault(defaultValue);

    public static ParameterDefinition Option(string key, string module, string group, string defaultValue, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0 || options.Any(string.IsNullOrWhiteSpace) || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
            throw new ArgumentException("Options must be non-empty and unique.", nameof(options));
        return new ParameterDefinition(key, module, group, ParameterType.Option, null, null, options.Max(o => o.Length), [.. options]).WithDefault(defaultValue);
    }

    /// <summary>驗證並正規化輸入值；不合規則時拋出 400（欄位 <paramref name="field"/>）。</summary>
    public string Normalize(string? value, string field = "value")
    {
        var trimmed = value?.Trim() ?? "";
        switch (Type)
        {
            case ParameterType.Boolean:
                if (bool.TryParse(trimmed, out var flag)) return flag ? "true" : "false";
                throw Invalid(field, "Use true or false.");

            case ParameterType.WholeNumber:
                if (long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole) && InRange(whole))
                    return whole.ToString(CultureInfo.InvariantCulture);
                throw Invalid(field, $"Use a whole number between {Minimum} and {Maximum}.");

            case ParameterType.Number:
                if (decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                    && InRange(number))
                    return (number / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);
                throw Invalid(field, $"Use a number between {Minimum} and {Maximum} with '.' as the decimal point.");

            case ParameterType.Text:
                if (trimmed.Length <= MaxLength) return trimmed;
                throw Invalid(field, $"Maximum {MaxLength} characters.");

            default:
                return Options.FirstOrDefault(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase))
                    ?? throw Invalid(field, "Choose one of: " + string.Join(", ", Options) + ".");
        }
    }

    private bool InRange(decimal value) => value >= Minimum && value <= Maximum;

    private ParameterDefinition WithDefault(string value)
    {
        try
        {
            DefaultValue = Normalize(value);
        }
        catch (RequestValidationException ex)
        {
            throw new ArgumentException($"Default value of parameter '{Key}' is invalid.", nameof(value), ex);
        }

        return this;
    }

    private static RequestValidationException Invalid(string field, string message) => new(field, message);
}

/// <summary>每個模組提供本模組的參數清單；SYS 模組彙整為參數目錄，只有目錄內的鍵可以保存。</summary>
public interface IParameterDefinitionProvider
{
    IReadOnlyList<ParameterDefinition> Parameters { get; }
}

/// <summary>
/// 由 SYS 模組實作：讀取目前公司的參數值，未設定時回傳宣告的預設值。同一個 scope（請求）內只查詢資料庫一次。
/// 鍵不在目錄內時拋出 <see cref="ArgumentException"/>（程式錯誤）；沒有公司上下文時拋出 403。
/// </summary>
public interface ISystemParameters
{
    Task<string> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> GetBooleanAsync(string key, CancellationToken cancellationToken = default);

    Task<int> GetIntegerAsync(string key, CancellationToken cancellationToken = default);

    Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default);
}
