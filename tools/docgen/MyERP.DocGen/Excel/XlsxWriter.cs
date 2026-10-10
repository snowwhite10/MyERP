using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace MyERP.DocGen.Excel;

/// <summary>簡單的 xlsx 工作表：首列為粗體表頭並凍結，其餘為文字列。</summary>
public sealed record WorksheetContent(string Name, IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>以 OpenXML SDK 寫 xlsx（只用 inline string，不需要 shared string table）。</summary>
public static class XlsxWriter
{
    private const uint BoldStyle = 1;
    private const uint WrapStyle = 2;

    public static void Write(Stream output, IEnumerable<WorksheetContent> sheets)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(sheets);
        using var document = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook);
        document.PackageProperties.Creator = "Developer";
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = CreateStylesheet();

        var sheetList = workbookPart.Workbook.AppendChild(new Sheets());
        uint sheetId = 0;
        foreach (var sheet in sheets)
        {
            sheetId++;
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = CreateWorksheet(sheet);
            sheetList.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = sheetId, Name = sheet.Name });
        }

        workbookPart.Workbook.Save();
    }

    /// <summary>欄名 A、B…Z、AA…。</summary>
    public static string ColumnName(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var name = "";
        var n = index + 1;
        while (n > 0)
        {
            var remainder = (n - 1) % 26;
            name = (char)('A' + remainder) + name;
            n = (n - 1) / 26;
        }

        return name;
    }

    private static Worksheet CreateWorksheet(WorksheetContent sheet)
    {
        var columnCount = sheet.Header.Count;
        var columns = new Columns();
        for (var c = 0; c < columnCount; c++)
        {
            var longest = sheet.Rows.Select(r => c < r.Count ? r[c].Length : 0).Append(sheet.Header[c].Length).Max();
            columns.Append(new Column { Min = (uint)(c + 1), Max = (uint)(c + 1), Width = Math.Clamp(longest + 2, 10, 60), CustomWidth = true });
        }

        var data = new SheetData();
        data.Append(CreateRow(1, sheet.Header, BoldStyle));
        uint rowIndex = 1;
        foreach (var row in sheet.Rows)
        {
            rowIndex++;
            data.Append(CreateRow(rowIndex, row, WrapStyle));
        }

        var view = new SheetViews(new SheetView(
            new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
        {
            WorkbookViewId = 0,
        });
        return new Worksheet(view, columns, data);
    }

    private static Row CreateRow(uint index, IReadOnlyList<string> values, uint style)
    {
        var row = new Row { RowIndex = index };
        for (var c = 0; c < values.Count; c++)
        {
            row.Append(new Cell(new InlineString(new Text(values[c] ?? "") { Space = SpaceProcessingModeValues.Preserve }))
            {
                CellReference = ColumnName(c) + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DataType = CellValues.InlineString,
                StyleIndex = style,
            });
        }

        return row;
    }

    private static Stylesheet CreateStylesheet() =>
        new(
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Arial" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Arial" }))
            { Count = 2 },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
            { Count = 2 },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
            new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
            new CellFormats(
                new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
                new CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
                new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top })
                {
                    NumberFormatId = 0,
                    FontId = 0,
                    FillId = 0,
                    BorderId = 0,
                    FormatId = 0,
                    ApplyAlignment = true,
                })
            { Count = 3 });
}
