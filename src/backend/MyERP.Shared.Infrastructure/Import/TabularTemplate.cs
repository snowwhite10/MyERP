using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace MyERP.Shared.Infrastructure.Import;

/// <summary>產生匯入範本（ADR-044）：xlsx 以內嵌字串寫入單一工作表；CSV 為 UTF-8 加 BOM、RFC 4180 引號規則。</summary>
public static class TabularTemplate
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string CsvContentType = "text/csv";

    public static byte[] CreateXlsx(string sheetName, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var worksheet = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            for (var r = 0; r < rows.Count; r++)
            {
                var row = new Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < rows[r].Count; c++)
                {
                    if (rows[r][c] is not { } value) continue;
                    row.Append(new Cell
                    {
                        CellReference = ColumnName(c) + (r + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }),
                    });
                }

                data.Append(row);
            }

            worksheet.Worksheet = new Worksheet(data);
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = sheetName }));
        }

        return stream.ToArray();
    }

    public static byte[] CreateCsv(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendJoin(',', row.Select(Quote)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];

        static string Quote(string? value) =>
            value is null ? "" : value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26)
        {
            name = (char)('A' + (index - 1) % 26) + name;
        }

        return name;
    }
}
