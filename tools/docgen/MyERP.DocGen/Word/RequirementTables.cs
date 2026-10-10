using MyERP.DocGen.Markdown;
using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Word;

/// <summary>BlueWhale 各類表格（reference-notes.md 第 3 節的欄寬、框線與欄名；MASTER_PROMPT 15.3）。</summary>
public static class RequirementTables
{
    public static readonly int[] FunctionalWidths = [2220, 3525, 2370, 1230];
    public static readonly int[] FourEqualWidths = [2340, 2340, 2340, 2340];
    public static readonly int[] ColumnTableWidths = [2000, 2000, 2000, 2000];
    public static readonly int[] SignOffWidths = [2070, 1890, 720, 1875, 1245, 1560];
    private static readonly string[] SignOffLabels = ["Pre-Reviewed by", "Reviewed by", "Approved by"];

    private const uint OnePoint = 8;
    private const uint ThreeQuarterPoint = 6;
    private const uint HalfPoint = 4;

    /// <summary>SRS 4.1.1：每個功能需求一張表。</summary>
    public static void Functional(WordDocumentWriter writer, FunctionalRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(requirement);
        writer.Table(
            FunctionalWidths,
            OnePoint,
            [
                IdRow(requirement.Id, requirement.Version),
                Spanned("Description", requirement.Description),
                Spanned("Author", requirement.Author),
            ],
            minRowHeight: 465);
    }

    /// <summary>SRS 4.3：每張資料表一張需求表。</summary>
    public static void DataStructure(WordDocumentWriter writer, DataStructureRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(requirement);
        writer.Table(
            FourEqualWidths,
            OnePoint,
            [
                IdRow(requirement.Id, requirement.Version),
                Spanned("Table Name", requirement.TableName),
                Spanned("Description", requirement.Description),
                Spanned("Author", requirement.Author),
            ]);
    }

    /// <summary>SRS 4.4：每個介面欄位 / 按鈕一張表。</summary>
    public static void InterfaceItem(WordDocumentWriter writer, InterfaceItem item)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(item);
        writer.Table(
            FourEqualWidths,
            ThreeQuarterPoint,
            [
                IdRow(item.Id, item.Version),
                Spanned("Item", item.Item),
                Spanned("Description", item.Description),
                Spanned("Purpose", item.Purpose),
                [new WordCell("Format", Bold: true), new WordCell(item.Format), new WordCell("Valid Range", Bold: true), new WordCell(item.ValidRange)],
                Spanned("Related I/O", item.RelatedIo),
                Spanned("Author", item.Author),
            ]);
    }

    /// <summary>SDS 4.1：欄位表（由資料庫結構產生）。</summary>
    public static void Columns(WordDocumentWriter writer, TableSchema table)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(table);
        IReadOnlyList<WordCell> header =
        [
            new("Name of Attribute", Bold: true, Center: true),
            new("Data Type", Bold: true, Center: true),
            new("Size", Bold: true, Center: true),
            new("PK/FK/IS NULL/ NOT NULL", Bold: true, Center: true),
        ];
        var rows = new List<IReadOnlyList<WordCell>> { header };
        rows.AddRange(table.Columns.Select(c => (IReadOnlyList<WordCell>)
            [new WordCell(c.Name), new WordCell(c.DataType), new WordCell(c.SizeText), new WordCell(c.ConstraintText)]));
        writer.Table(ColumnTableWidths, HalfPoint, rows, minRowHeight: 530);
    }

    /// <summary>封面簽署表：Pre-Reviewed / Reviewed / Approved by，含 Date 與 Signature 欄。</summary>
    public static void SignOff(WordDocumentWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Table(
            SignOffWidths,
            OnePoint,
            SignOffLabels.Select(label => (IReadOnlyList<WordCell>)
            [
                new WordCell(label, Bold: true),
                new WordCell(""),
                new WordCell("Date", Bold: true),
                new WordCell(""),
                new WordCell("Signature", Bold: true),
                new WordCell(""),
            ]),
            minRowHeight: 600);
    }

    /// <summary>來源 Markdown 的一般表格：平均分配版面寬度，首列粗體。</summary>
    public static void Markdown(WordDocumentWriter writer, MarkdownTable table)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(table);
        var count = Math.Max(1, table.Header.Count);
        var width = BlueWhaleStyles.TextWidth / count;
        var widths = Enumerable.Repeat(width, count).ToArray();
        var rows = new List<IReadOnlyList<WordCell>>
        {
            table.Header.Select(h => new WordCell(MarkdownInline.ToPlainText(h), Bold: true)).ToList(),
        };
        rows.AddRange(table.Rows.Select(r => (IReadOnlyList<WordCell>)r.Select(c => new WordCell(MarkdownInline.ToPlainText(c))).ToList()));
        writer.Table(widths, OnePoint, rows);
    }

    private static IReadOnlyList<WordCell> IdRow(string id, string version) =>
        [new WordCell("Requirement ID", Bold: true), new WordCell(id), new WordCell("Version", Bold: true), new WordCell(version)];

    private static IReadOnlyList<WordCell> Spanned(string label, string value) =>
        [new WordCell(label, Bold: true), new WordCell(value, Span: 3)];
}
