using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MyERP.Shared.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.System.Domain;

/// <summary>單號的日期部分（以單據業務日期格式化）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NumberDateFormat>))]
public enum NumberDateFormat
{
    /// <summary>不含日期。</summary>
    None,

    /// <summary><c>yyyy</c>，例如 2026。</summary>
    Year,

    /// <summary><c>yyyyMM</c>，例如 202610。</summary>
    YearMonth,

    /// <summary><c>yyyyMMdd</c>，例如 20261011。</summary>
    YearMonthDay,

    /// <summary><c>yyMM</c>，例如 2610。</summary>
    ShortYearMonth,

    /// <summary><c>yyMMdd</c>，例如 261011。</summary>
    ShortYearMonthDay,
}

/// <summary>流水號重設週期。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NumberResetPeriod>))]
public enum NumberResetPeriod
{
    Never,
    Yearly,
    Monthly,
    Daily,
}

/// <summary>
/// 單號規則（sys_number_rule，REQ_DS43.95）：每間公司每個單據類型一條。單號 = 前綴 + 日期 + 補零流水號，
/// 例如 <c>SO</c> + <c>202610</c> + <c>0001</c>。流水號存於 <see cref="NumberSequence"/>，按重設週期分開計數。
/// </summary>
public sealed partial class NumberRule : BusinessEntity
{
    public const int MinSequenceLength = 3;
    public const int MaxSequenceLength = 10;
    public const int MaxPrefixLength = 10;

    /// <summary>單號最長字數：前綴 10 + 日期 8 + 流水號 10。各模組單號欄位使用 VARCHAR(30)。</summary>
    public const int MaxNumberLength = 28;

    public required string DocumentType { get; set; }
    public required string Name { get; set; }
    public required string Prefix { get; set; }
    public NumberDateFormat DateFormat { get; set; }
    public int SequenceLength { get; set; }
    public NumberResetPeriod ResetPeriod { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>流水號上限，例如長度 4 為 9999。</summary>
    public long MaxSequence
    {
        get
        {
            var limit = 1L;
            for (var i = 0; i < SequenceLength; i++) limit *= 10;
            return limit - 1;
        }
    }

    /// <summary>單據類型代碼 <c>{PREFIX}.{DOCUMENT}</c>，大寫；不符即拋出 400。</summary>
    public static string NormalizeDocumentType(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length > 50 || !DocumentTypePattern().IsMatch(normalized))
            throw new RequestValidationException("documentType", "Use {PREFIX}.{DOCUMENT} in letters, digits or underscore; maximum 50 characters.");
        return normalized;
    }

    /// <summary>前綴可為空，最多 10 字，只可用英數字、<c>-</c>、<c>_</c>，保存為大寫。</summary>
    public static string NormalizePrefix(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length > MaxPrefixLength || !PrefixPattern().IsMatch(normalized))
            throw new RequestValidationException("prefix", "Use at most 10 ASCII letters, digits, hyphen or underscore.");
        return normalized;
    }

    /// <summary>
    /// 驗證長度與週期組合：重設週期必須包含在日期部分內（每年重設要有年、每月要有月、每日要有日），
    /// 否則不同週期會產生相同單號。
    /// </summary>
    public static void ValidateSettings(NumberDateFormat dateFormat, int sequenceLength, NumberResetPeriod resetPeriod)
    {
        if (!Enum.IsDefined(dateFormat))
            throw new RequestValidationException("dateFormat", "Unknown date format.");
        if (!Enum.IsDefined(resetPeriod))
            throw new RequestValidationException("resetPeriod", "Unknown reset period.");
        if (sequenceLength is < MinSequenceLength or > MaxSequenceLength)
            throw new RequestValidationException("sequenceLength", $"Sequence length must be between {MinSequenceLength} and {MaxSequenceLength}.");
        var covered = resetPeriod switch
        {
            NumberResetPeriod.Yearly => dateFormat != NumberDateFormat.None,
            NumberResetPeriod.Monthly => dateFormat is not (NumberDateFormat.None or NumberDateFormat.Year),
            NumberResetPeriod.Daily => dateFormat is NumberDateFormat.YearMonthDay or NumberDateFormat.ShortYearMonthDay,
            _ => true,
        };
        if (!covered)
            throw new RequestValidationException("resetPeriod", "The date format must contain the reset period so that numbers stay unique.");
    }

    /// <summary>計數分組鍵：不重設為 <c>-</c>，其餘為 yyyy／yyyyMM／yyyyMMdd。</summary>
    public string PeriodKey(DateOnly documentDate) => ResetPeriod switch
    {
        NumberResetPeriod.Yearly => documentDate.ToString("yyyy", CultureInfo.InvariantCulture),
        NumberResetPeriod.Monthly => documentDate.ToString("yyyyMM", CultureInfo.InvariantCulture),
        NumberResetPeriod.Daily => documentDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        _ => "-",
    };

    /// <summary>組合單號；流水號超過上限時拋出 SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED（422）。</summary>
    public string Format(DateOnly documentDate, long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sequence, 1);
        if (sequence > MaxSequence)
            throw new BusinessRuleException("SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED",
                $"Number rule '{DocumentType}' has used all {MaxSequence} numbers of this period.");
        var datePart = DateFormat switch
        {
            NumberDateFormat.Year => "yyyy",
            NumberDateFormat.YearMonth => "yyyyMM",
            NumberDateFormat.YearMonthDay => "yyyyMMdd",
            NumberDateFormat.ShortYearMonth => "yyMM",
            NumberDateFormat.ShortYearMonthDay => "yyMMdd",
            _ => null,
        };
        var date = datePart is null ? "" : documentDate.ToString(datePart, CultureInfo.InvariantCulture);
        return Prefix + date + sequence.ToString(CultureInfo.InvariantCulture).PadLeft(SequenceLength, '0');
    }

    [GeneratedRegex(@"^[A-Z][A-Z0-9]*\.[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentTypePattern();

    [GeneratedRegex(@"^[A-Z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}

/// <summary>
/// 流水號計數（sys_number_sequence，REQ_DS43.96）：每條規則每個週期一列，只由單號產生服務以單一 MERGE 原子更新
/// （不經 EF 追蹤，故不寫稽核記錄）。
/// </summary>
public sealed class NumberSequence : BusinessEntity
{
    public long RuleId { get; set; }
    public required string PeriodKey { get; set; }
    public long CurrentValue { get; set; }
}
