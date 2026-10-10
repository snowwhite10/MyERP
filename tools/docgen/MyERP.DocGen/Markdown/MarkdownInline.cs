using System.Text.RegularExpressions;

namespace MyERP.DocGen.Markdown;

/// <summary>行內語法轉純文字：Word / Excel 輸出不保留 Markdown 標記。</summary>
public static partial class MarkdownInline
{
    public static string ToPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var result = LinkPattern().Replace(text, "$1");
        result = BoldPattern().Replace(result, "$1");
        result = ItalicPattern().Replace(result, "$1");
        result = CodePattern().Replace(result, "$1");
        result = result.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
        return result.Trim();
    }

    /// <summary>以逗號、頓號或空白分隔的 ID 清單；<c>-</c>、<c>None</c> 視為空。</summary>
    public static IReadOnlyList<string> SplitIds(string? text)
    {
        var plain = ToPlainText(text);
        if (plain.Length == 0 || plain == "-" || plain.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return plain.Split([',', '、', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])")]
    private static partial Regex ItalicPattern();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodePattern();
}
