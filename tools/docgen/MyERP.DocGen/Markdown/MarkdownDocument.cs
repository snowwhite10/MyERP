using System.Text;
using System.Text.RegularExpressions;

namespace MyERP.DocGen.Markdown;

/// <summary>Markdown 區塊（docgen 只需要標題、段落、清單、表格、程式碼區塊）。</summary>
public abstract record MarkdownBlock(int Line);

public sealed record MarkdownHeading(int Line, int Level, string Text) : MarkdownBlock(Line);

public sealed record MarkdownParagraph(int Line, string Text) : MarkdownBlock(Line);

public sealed record MarkdownList(int Line, bool Ordered, IReadOnlyList<string> Items) : MarkdownBlock(Line);

public sealed record MarkdownCode(int Line, string Language, string Text) : MarkdownBlock(Line);

/// <summary>GFM pipe 表格。<see cref="Rows"/> 每列欄數已補齊或截斷為表頭欄數。</summary>
public sealed record MarkdownTable(int Line, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock(Line)
{
    /// <summary>表頭欄位索引（不分大小寫）；找不到回傳 -1。</summary>
    public int ColumnIndex(string name)
    {
        for (var i = 0; i < Header.Count; i++)
        {
            if (string.Equals(Header[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public bool HasColumns(params string[] names) => names.All(n => ColumnIndex(n) >= 0);
}

/// <summary>
/// 最小化的 Markdown 區塊解析器：只解析 docgen 來源檔用得到的語法，
/// 不支援巢狀清單與 HTML。引言（<c>&gt;</c>）視為來源檔的維護說明，不輸出。
/// </summary>
public sealed partial class MarkdownDocument
{
    private MarkdownDocument(string path, IReadOnlyList<MarkdownBlock> blocks)
    {
        Path = path;
        Blocks = blocks;
    }

    public string Path { get; }

    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    public IEnumerable<MarkdownTable> Tables => Blocks.OfType<MarkdownTable>();

    public static MarkdownDocument Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8), path);

    public static MarkdownDocument Parse(string text, string path = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<MarkdownBlock>();
        var paragraph = new List<string>();
        var paragraphLine = 0;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
            {
                blocks.Add(new MarkdownParagraph(paragraphLine, string.Join(' ', paragraph)));
                paragraph.Clear();
            }
        }

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            var lineNo = i + 1;

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }

                blocks.Add(new MarkdownCode(lineNo, language, string.Join('\n', code)));
                i++;
                continue;
            }

            var heading = HeadingPattern().Match(trimmed);
            if (heading.Success)
            {
                FlushParagraph();
                blocks.Add(new MarkdownHeading(lineNo, heading.Groups[1].Length, heading.Groups[2].Value.Trim()));
                i++;
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                i++;
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && SeparatorPattern().IsMatch(lines[i + 1].Trim()))
            {
                FlushParagraph();
                var header = SplitRow(trimmed);
                var rows = new List<IReadOnlyList<string>>();
                i += 2;
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    rows.Add(Normalize(SplitRow(lines[i].Trim()), header.Count));
                    i++;
                }

                blocks.Add(new MarkdownTable(lineNo, header, rows));
                continue;
            }

            var bullet = BulletPattern().Match(trimmed);
            var ordered = OrderedPattern().Match(trimmed);
            if (bullet.Success || ordered.Success)
            {
                FlushParagraph();
                var isOrdered = ordered.Success;
                var items = new List<string>();
                while (i < lines.Length)
                {
                    var current = lines[i].Trim();
                    var match = isOrdered ? OrderedPattern().Match(current) : BulletPattern().Match(current);
                    if (!match.Success)
                    {
                        break;
                    }

                    items.Add(match.Groups[1].Value.Trim());
                    i++;
                }

                blocks.Add(new MarkdownList(lineNo, isOrdered, items));
                continue;
            }

            if (paragraph.Count == 0)
            {
                paragraphLine = lineNo;
            }

            paragraph.Add(trimmed);
            i++;
        }

        FlushParagraph();
        return new MarkdownDocument(path, blocks);
    }

    /// <summary>拆開表格列；支援 <c>\|</c> 跳脫。</summary>
    internal static List<string> SplitRow(string row)
    {
        var body = row.Trim();
        if (body.StartsWith('|'))
        {
            body = body[1..];
        }

        if (body.EndsWith('|') && !body.EndsWith("\\|", StringComparison.Ordinal))
        {
            body = body[..^1];
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (body[i] == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(body[i]);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static List<string> Normalize(List<string> cells, int count)
    {
        while (cells.Count < count)
        {
            cells.Add("");
        }

        return cells.Count > count ? cells.GetRange(0, count) : cells;
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?$")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"^[-*+]\s+(.+)$")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^\d+[.)]\s+(.+)$")]
    private static partial Regex OrderedPattern();
}
