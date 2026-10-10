using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MyERP.DocGen.Word;

/// <summary>產出文件中的一個標題，供一致性檢查比對章節結構與介面次序。</summary>
public sealed record OutlineEntry(int Level, string Text);

/// <summary>表格儲存格：<see cref="Span"/> 為合併欄數（gridSpan）。</summary>
public sealed record WordCell(string Text, bool Bold = false, int Span = 1, bool Center = false);

/// <summary>
/// 以 BlueWhale 版面寫 Word 文件的低階工具（OpenXML SDK）。
/// 只負責版面；章節內容由 SrsGenerator / SdsGenerator 決定。
/// </summary>
public sealed class WordDocumentWriter : IDisposable
{
    private readonly WordprocessingDocument _document;
    private readonly Body _body;
    private readonly string _footerId;
    private readonly List<OutlineEntry> _outline = [];
    private bool _disposed;

    public WordDocumentWriter(Stream stream, string title)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document);
        _document.PackageProperties.Title = title;
        _document.PackageProperties.Creator = "Developer";

        var main = _document.AddMainDocumentPart();
        _body = new Body();
        main.Document = new Document(_body);

        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = BlueWhaleStyles.Create();

        // 開啟時更新欄位（目錄、頁碼總數）。
        var settings = main.AddNewPart<DocumentSettingsPart>();
        settings.Settings = new Settings(new UpdateFieldsOnOpen { Val = true });

        var footer = main.AddNewPart<FooterPart>();
        footer.Footer = new Footer(PageNumberParagraph());
        _footerId = main.GetIdOfPart(footer);
    }

    public IReadOnlyList<OutlineEntry> Outline => _outline;

    public void Heading(int level, string text)
    {
        _outline.Add(new OutlineEntry(level, text));
        _body.Append(new Paragraph(
            new ParagraphProperties { ParagraphStyleId = new ParagraphStyleId { Val = BlueWhaleStyles.HeadingStyleId(level) } },
            TextRun(text)));
    }

    public void Paragraph(string text, bool bold = false, bool italic = false, bool center = false, string? styleId = null)
    {
        var properties = new ParagraphProperties();
        if (styleId is not null)
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = styleId };
        }

        if (center)
        {
            properties.Justification = new Justification { Val = JustificationValues.Center };
        }

        _body.Append(new Paragraph(properties, TextRun(text, bold, italic)));
    }

    /// <summary>空段落（BlueWhale 在表與表之間留一個空段）。</summary>
    public void Blank() => _body.Append(new Paragraph());

    public void Bullets(IEnumerable<string> items, bool ordered = false)
    {
        var number = 0;
        foreach (var item in items)
        {
            number++;
            var prefix = ordered ? number.ToString(CultureInfo.InvariantCulture) + ". " : "• ";
            _body.Append(new Paragraph(
                new ParagraphProperties { Indentation = new Indentation { Left = "720", Hanging = "360" } },
                TextRun(prefix + item)));
        }
    }

    public void Caption(string text) => Paragraph(text, styleId: BlueWhaleStyles.CaptionStyleId);

    public void PageBreak() => _body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

    /// <summary>目錄欄位（Heading 1–6，與 BlueWhale SRS 相同），開啟文件時由 Word 更新。</summary>
    public void TableOfContents()
    {
        Paragraph("Table of Contents", bold: true);
        _body.Append(new Paragraph(new SimpleField(TextRun("Right-click and choose Update Field to refresh the table of contents."))
        {
            Instruction = " TOC \\o \"1-6\" \\h \\z \\u ",
        }));
    }

    /// <summary>
    /// BlueWhale 表格：固定版面、靠左、黑色單線框、無底色、儲存格內距 100。
    /// <paramref name="borderSize"/> 單位為 1/8 pt（8 = 1pt、6 = 0.75pt、4 = 0.5pt）。
    /// </summary>
    public void Table(IReadOnlyList<int> columnWidths, uint borderSize, IEnumerable<IReadOnlyList<WordCell>> rows, int minRowHeight = 0)
    {
        ArgumentNullException.ThrowIfNull(columnWidths);
        ArgumentNullException.ThrowIfNull(rows);
        var table = new Table(new TableProperties
        {
            TableStyle = new TableStyle { Val = "TableGrid" },
            TableWidth = new TableWidth { Width = columnWidths.Sum().ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
            TableJustification = new TableJustification { Val = TableRowAlignmentValues.Left },
            TableBorders = Borders(borderSize),
            TableLayout = new TableLayout { Type = TableLayoutValues.Fixed },
            TableCellMarginDefault = new TableCellMarginDefault(
                new TopMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 100, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 100, Type = TableWidthValues.Dxa }),
        });
        table.Append(new TableGrid(columnWidths.Select(w => new GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));

        foreach (var cells in rows)
        {
            var row = new TableRow();
            if (minRowHeight > 0)
            {
                row.Append(new TableRowProperties(new TableRowHeight { Val = (uint)minRowHeight, HeightType = HeightRuleValues.AtLeast }));
            }

            var column = 0;
            foreach (var cell in cells)
            {
                var span = Math.Max(1, cell.Span);
                var width = columnWidths.Skip(column).Take(span).Sum();
                column += span;
                var properties = new TableCellProperties
                {
                    TableCellWidth = new TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                };
                if (span > 1)
                {
                    properties.GridSpan = new GridSpan { Val = span };
                }

                var paragraphProperties = new ParagraphProperties();
                if (cell.Center)
                {
                    paragraphProperties.Justification = new Justification { Val = JustificationValues.Center };
                }

                row.Append(new TableCell(properties, new Paragraph(paragraphProperties, TextRun(cell.Text, cell.Bold))));
            }

            table.Append(row);
        }

        _body.Append(table);
    }

    /// <summary>寫入最後的 section（A4、邊界、頁尾頁碼）並關閉文件。</summary>
    public void Save()
    {
        _body.Append(new SectionProperties(
            new FooterReference { Type = HeaderFooterValues.Default, Id = _footerId },
            new PageSize { Width = BlueWhaleStyles.A4Width, Height = BlueWhaleStyles.A4Height },
            new PageMargin
            {
                Top = BlueWhaleStyles.Margin,
                Right = (uint)BlueWhaleStyles.Margin,
                Bottom = BlueWhaleStyles.Margin,
                Left = (uint)BlueWhaleStyles.Margin,
                Header = BlueWhaleStyles.HeaderFooterDistance,
                Footer = BlueWhaleStyles.HeaderFooterDistance,
                Gutter = 0,
            }));
        _document.Save();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _document.Dispose();
            _disposed = true;
        }
    }

    /// <summary>文字 run；換行轉為 &lt;w:br/&gt;。</summary>
    private static Run TextRun(string text, bool bold = false, bool italic = false)
    {
        var run = new Run();
        if (bold || italic)
        {
            var properties = new RunProperties();
            if (bold)
            {
                properties.Bold = new Bold();
            }

            if (italic)
            {
                properties.Italic = new Italic();
            }

            run.Append(properties);
        }

        var lines = (text ?? "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }

        return run;
    }

    private static TableBorders Borders(uint size)
    {
        T Border<T>()
            where T : BorderType, new() => new() { Val = BorderValues.Single, Size = size, Color = "000000", Space = 0 };

        return new TableBorders(
            Border<TopBorder>(),
            Border<LeftBorder>(),
            Border<BottomBorder>(),
            Border<RightBorder>(),
            Border<InsideHorizontalBorder>(),
            Border<InsideVerticalBorder>());
    }

    /// <summary>頁尾「Page X of Y」（BlueWhale 欠缺頁碼，MyERP 補上，reference-notes.md 2.3）。</summary>
    private static Paragraph PageNumberParagraph() =>
        new(
            new ParagraphProperties { Justification = new Justification { Val = JustificationValues.Center } },
            new Run(new Text("Page ") { Space = SpaceProcessingModeValues.Preserve }),
            new SimpleField(new Run(new Text("1"))) { Instruction = " PAGE " },
            new Run(new Text(" of ") { Space = SpaceProcessingModeValues.Preserve }),
            new SimpleField(new Run(new Text("1"))) { Instruction = " NUMPAGES " });
}
