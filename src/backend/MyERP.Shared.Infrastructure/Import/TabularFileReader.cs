using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MyERP.Shared.Errors;

namespace MyERP.Shared.Infrastructure.Import;

/// <summary>匯入檔的檔案層錯誤代碼（422，前端 i18n key <c>errors.COMMON.IMPORT.*</c>），ADR-044。</summary>
public static class ImportErrorCodes
{
    public const string TooLarge = "COMMON.IMPORT.TOO_LARGE";
    public const string EmptyFile = "COMMON.IMPORT.EMPTY_FILE";
    public const string UnsupportedFormat = "COMMON.IMPORT.UNSUPPORTED_FORMAT";
    public const string InvalidEncoding = "COMMON.IMPORT.INVALID_ENCODING";
    public const string InvalidFile = "COMMON.IMPORT.INVALID_FILE";
    public const string TooManyRows = "COMMON.IMPORT.TOO_MANY_ROWS";
    public const string DuplicateColumn = "COMMON.IMPORT.DUPLICATE_COLUMN";
    public const string MissingColumn = "COMMON.IMPORT.MISSING_COLUMN";
}

/// <summary>一列資料；<see cref="RowNumber"/> 是檔案的實際列號（表頭為第 1 列）。</summary>
public sealed record TabularRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    public string? Get(int index) => index >= 0 && index < Cells.Count ? Cells[index] : null;
}

/// <summary>第一列為表頭的表格；整列空白的資料列已略過。</summary>
public sealed record TabularSheet(IReadOnlyList<string> Headers, IReadOnlyList<TabularRow> Rows)
{
    /// <summary>
    /// 以不分大小寫的表頭找出 <paramref name="known"/> 欄位的位置（未出現的欄位不在結果內，未知表頭忽略）；
    /// 缺少 <paramref name="required"/> 任何一欄時回 422 <see cref="ImportErrorCodes.MissingColumn"/>。
    /// </summary>
    public IReadOnlyDictionary<string, int> MapColumns(IReadOnlyList<string> known, IReadOnlyList<string> required)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(required);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in known)
        {
            var index = Headers.ToList().FindIndex(h => string.Equals(h, key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) map[key] = index;
        }

        var missing = required.Where(r => !map.ContainsKey(r)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException(ImportErrorCodes.MissingColumn, "Missing required columns: " + string.Join(", ", missing) + ".");
        return map;
    }
}

/// <summary>
/// 讀取 xlsx（第一個工作表）或 UTF-8 CSV 匯入檔（ADR-044）。依內容判斷格式，不信任副檔名；
/// 檔案上限 <see cref="MaxBytes"/>，資料列超過 maxRows 即拒絕。
/// </summary>
public static class TabularFileReader
{
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>xlsx 單一部件解壓後的字元上限，防止壓縮炸彈。</summary>
    private const long MaxCharactersInPart = 20_000_000;

    public static TabularSheet Read(byte[] content, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxBytes)
            throw new BusinessRuleException(ImportErrorCodes.TooLarge, $"The file must not exceed {MaxBytes} bytes.");
        if (content.Length == 0) throw Empty();
        IEnumerable<(int Number, List<string?> Cells)> rows = content switch
        {
            [0x50, 0x4B, 0x03, 0x04, ..] => ReadXlsx(content),
            [0xD0, 0xCF, 0x11, 0xE0, ..] => throw Unsupported(),
            _ when content.AsSpan().IndexOf((byte)0) >= 0 => throw Unsupported(),
            _ => ReadCsv(content),
        };

        List<string>? headers = null;
        var data = new List<TabularRow>();
        foreach (var (number, cells) in rows)
        {
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            if (headers is null)
            {
                headers = [.. cells.Select(c => c?.Trim() ?? "")];
                var duplicate = headers.Where(h => h.Length > 0).GroupBy(h => h, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                if (duplicate is not null)
                    throw new BusinessRuleException(ImportErrorCodes.DuplicateColumn, $"Column '{duplicate.Key}' appears more than once.");
                continue;
            }

            if (data.Count == maxRows)
                throw new BusinessRuleException(ImportErrorCodes.TooManyRows, $"The file may contain at most {maxRows} data rows.");
            data.Add(new TabularRow(number, cells));
        }

        return headers is null ? throw Empty() : new TabularSheet(headers, data);
    }

    private static List<(int, List<string?>)> ReadXlsx(byte[] content)
    {
        try
        {
            using var document = SpreadsheetDocument.Open(new MemoryStream(content, false), false,
                new OpenSettings { MaxCharactersInPart = MaxCharactersInPart });
            var workbook = document.WorkbookPart ?? throw Invalid();
            var first = workbook.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw Invalid();
            if (first.Id?.Value is not { } partId || workbook.GetPartById(partId) is not WorksheetPart worksheet) throw Invalid();
            var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
                .Select(item => string.Concat(item.Descendants<Text>().Where(t => t.Parent is not PhoneticRun).Select(t => t.Text)))
                .ToArray() ?? [];

            var result = new List<(int, List<string?>)>();
            var rowNumber = 0;
            foreach (var row in worksheet.Worksheet?.Descendants<Row>() ?? [])
            {
                rowNumber = row.RowIndex?.Value is { } index ? checked((int)index) : rowNumber + 1;
                var cells = new List<string?>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : cells.Count;
                    while (cells.Count < column) cells.Add(null);
                    var value = CellText(cell, shared);
                    if (column < cells.Count) cells[column] = value;
                    else cells.Add(value);
                }

                result.Add((rowNumber, cells));
            }

            return result;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or XmlException or FileFormatException
            or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException or FormatException)
        {
            throw Invalid();
        }
    }

    private static string? CellText(Cell cell, string[] shared)
    {
        var raw = cell.CellValue?.Text;
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString)
            return raw is null ? null : shared[int.Parse(raw, NumberStyles.None, CultureInfo.InvariantCulture)];
        if (type == CellValues.InlineString) return cell.InlineString?.InnerText;
        if (type == CellValues.Boolean) return raw is null ? null : raw == "1" ? "TRUE" : "FALSE";
        return raw;
    }

    /// <summary>儲存格參照（例如 <c>AB12</c>）的欄位索引，從 0 起算。</summary>
    private static int ColumnIndex(string reference)
    {
        var index = 0;
        var letters = 0;
        foreach (var c in reference)
        {
            if (!char.IsAsciiLetter(c)) break;
            index = checked(index * 26 + char.ToUpperInvariant(c) - 'A' + 1);
            letters++;
        }

        if (letters is 0 or > 3) throw Invalid();
        return index - 1;
    }

    private static List<(int, List<string?>)> ReadCsv(byte[] content)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            throw new BusinessRuleException(ImportErrorCodes.InvalidEncoding, "Save the CSV file as UTF-8.");
        }

        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var rows = new List<(int, List<string?>)>();
        var cells = new List<string?>();
        var field = new StringBuilder();
        var line = 1;
        var rowStart = 1;
        var quoted = false;
        var quoteClosed = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') { quoted = false; quoteClosed = true; }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0 && !quoteClosed) quoted = true;
            else if (c == ',') { cells.Add(field.ToString()); field.Clear(); quoteClosed = false; }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cells.Add(field.ToString());
                field.Clear();
                quoteClosed = false;
                rows.Add((rowStart, cells));
                cells = [];
                rowStart = ++line;
            }
            else if (c == '"' || quoteClosed) throw Invalid();
            else field.Append(c);
        }

        if (quoted) throw Invalid();
        if (field.Length > 0 || cells.Count > 0)
        {
            cells.Add(field.ToString());
            rows.Add((rowStart, cells));
        }

        return rows;
    }

    private static BusinessRuleException Empty() => new(ImportErrorCodes.EmptyFile, "The file has no header row.");

    private static BusinessRuleException Unsupported() =>
        new(ImportErrorCodes.UnsupportedFormat, "Upload an .xlsx workbook or a UTF-8 .csv file.");

    private static BusinessRuleException Invalid() => new(ImportErrorCodes.InvalidFile, "The file is damaged or not a valid workbook or CSV file.");
}
