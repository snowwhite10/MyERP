using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Import;

namespace MyERP.Shared.Tests.Import;

/// <summary>ADR-044：xlsx／CSV 匯入讀取器與範本產生器。</summary>
public sealed class TabularFileReaderTests
{
    private static readonly string[][] Sample =
    [
        ["code", "name", "note"],
        ["C001", "台灣客戶 甲", null!],
        ["C002", "Comma, \"Quoted\"", "line1\nline2"],
    ];

    [Fact]
    public void CsvWithBomQuotesAndCrlfIsRead()
    {
        var csv = "﻿code, Name ,note\r\nC001,台灣客戶 甲,\r\n\r\nC002,\"Comma, \"\"Quoted\"\"\",\"line1\r\nline2\"\r\n , ,\r\n";
        var sheet = TabularFileReader.Read(Encoding.UTF8.GetBytes(csv), 10);

        sheet.Headers.Should().Equal("code", "Name", "note");
        sheet.Rows.Should().HaveCount(2);
        sheet.Rows[0].RowNumber.Should().Be(2);
        sheet.Rows[0].Cells.Should().Equal("C001", "台灣客戶 甲", "");
        // 空白列略過但列號仍以檔案實際列號計算；引號內可含逗號、引號與換行。
        sheet.Rows[1].RowNumber.Should().Be(4);
        sheet.Rows[1].Cells.Should().Equal("C002", "Comma, \"Quoted\"", "line1\r\nline2");
        sheet.Rows[1].Get(5).Should().BeNull();
    }

    [Fact]
    public void CsvWithoutTrailingNewlineAndLfOnlyIsRead()
    {
        var sheet = TabularFileReader.Read(Encoding.UTF8.GetBytes("a,b\nx,\"y\""), 10);
        sheet.Rows.Should().ContainSingle().Which.Cells.Should().Equal("x", "y");
    }

    [Fact]
    public void TemplatesRoundTripThroughTheReader()
    {
        foreach (var bytes in new[] { TabularTemplate.CreateXlsx("Customers", Sample), TabularTemplate.CreateCsv(Sample) })
        {
            var sheet = TabularFileReader.Read(bytes, 10);
            sheet.Headers.Should().Equal("code", "name", "note");
            sheet.Rows.Select(r => r.RowNumber).Should().Equal(2, 3);
            sheet.Rows[0].Get(0).Should().Be("C001");
            sheet.Rows[0].Get(2).Should().BeNullOrEmpty();
            sheet.Rows[1].Get(1).Should().Be("Comma, \"Quoted\"");
            sheet.Rows[1].Get(2).Should().Be("line1\nline2");
        }

        TabularTemplate.CreateCsv(Sample).Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public void XlsxSharedStringsNumbersBooleansAndSparseCellsAreRead()
    {
        var bytes = BuildXlsx(sheetData =>
        {
            sheetData.Append(new Row(Cell("A1", 0), Cell("C1", 1)) { RowIndex = 1 });
            sheetData.Append(new Row(
                new Cell { CellReference = "A3", CellValue = new CellValue("12.5") },
                new Cell { CellReference = "B3", DataType = CellValues.Boolean, CellValue = new CellValue("1") },
                new Cell { CellReference = "C3", DataType = CellValues.InlineString, InlineString = new InlineString(new Text("inline")) })
            { RowIndex = 3 });
            sheetData.Append(new Row(new Cell { CellReference = "B5", DataType = CellValues.String, CellValue = new CellValue("formula text") }) { RowIndex = 5 });
        }, "code", "名稱");

        var sheet = TabularFileReader.Read(bytes, 10);

        sheet.Headers.Should().Equal("code", "", "名稱");
        sheet.Rows.Select(r => r.RowNumber).Should().Equal(3, 5);
        sheet.Rows[0].Cells.Should().Equal("12.5", "TRUE", "inline");
        sheet.Rows[1].Get(0).Should().BeNull();
        sheet.Rows[1].Get(1).Should().Be("formula text");
    }

    [Fact]
    public void RowLimitCountsOnlyNonBlankDataRows()
    {
        var csv = "code\n" + string.Join("\n", Enumerable.Range(1, 3).Select(i => "C" + i)) + "\n,\n";
        TabularFileReader.Read(Encoding.UTF8.GetBytes(csv), 3).Rows.Should().HaveCount(3);
        Code(() => TabularFileReader.Read(Encoding.UTF8.GetBytes(csv), 2)).Should().Be(ImportErrorCodes.TooManyRows);
    }

    [Fact]
    public void UnsupportedOrBrokenFilesAreRejected()
    {
        Code(() => TabularFileReader.Read([], 10)).Should().Be(ImportErrorCodes.EmptyFile);
        Code(() => TabularFileReader.Read("﻿ \r\n,\r\n"u8.ToArray(), 10)).Should().Be(ImportErrorCodes.EmptyFile);
        Code(() => TabularFileReader.Read([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0], 10)).Should().Be(ImportErrorCodes.UnsupportedFormat);
        Code(() => TabularFileReader.Read([0x41, 0x00, 0x42], 10)).Should().Be(ImportErrorCodes.UnsupportedFormat);
        // Big5「客戶」不是合法 UTF-8。
        Code(() => TabularFileReader.Read([0xAB, 0xC8, 0xA4, 0xE1, 0x2C, 0x41], 10)).Should().Be(ImportErrorCodes.InvalidEncoding);
        Code(() => TabularFileReader.Read("PK\u0003\u0004 not really a zip"u8.ToArray(), 10)).Should().Be(ImportErrorCodes.InvalidFile);
        Code(() => TabularFileReader.Read(ZipWithoutWorkbook(), 10)).Should().Be(ImportErrorCodes.InvalidFile);
        Code(() => TabularFileReader.Read("a,\"b\nc,d"u8.ToArray(), 10)).Should().Be(ImportErrorCodes.InvalidFile);
        Code(() => TabularFileReader.Read("a,b\nx,\"y\"z"u8.ToArray(), 10)).Should().Be(ImportErrorCodes.InvalidFile);
        Code(() => TabularFileReader.Read("a,b\nx,y\"z"u8.ToArray(), 10)).Should().Be(ImportErrorCodes.InvalidFile);
        Code(() => TabularFileReader.Read("code,Code\nA,B"u8.ToArray(), 10)).Should().Be(ImportErrorCodes.DuplicateColumn);
        Code(() => TabularFileReader.Read(new byte[TabularFileReader.MaxBytes + 1], 10)).Should().Be(ImportErrorCodes.TooLarge);
    }

    [Fact]
    public void ColumnLookupIsCaseInsensitiveAndReportsMissingRequiredColumns()
    {
        var sheet = TabularFileReader.Read("Code,NAME,extra\nA,B,C"u8.ToArray(), 10);
        var columns = sheet.MapColumns(["code", "name", "categoryCode"], ["code", "name"]);
        columns["code"].Should().Be(0);
        columns["name"].Should().Be(1);
        columns.ContainsKey("categoryCode").Should().BeFalse();

        var error = Assert.Throws<BusinessRuleException>(() => sheet.MapColumns(["code", "name", "categoryCode"], ["code", "categoryCode"]));
        error.ErrorCode.Should().Be(ImportErrorCodes.MissingColumn);
        error.Message.Should().Contain("categoryCode");
    }

    private static string Code(Action act) => Assert.Throws<BusinessRuleException>(act).ErrorCode;

    private static Cell Cell(string reference, int sharedIndex) =>
        new() { CellReference = reference, DataType = CellValues.SharedString, CellValue = new CellValue(sharedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)) };

    private static byte[] BuildXlsx(Action<SheetData> fill, params string[] sharedStrings)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var strings = workbook.AddNewPart<SharedStringTablePart>();
            strings.SharedStringTable = new SharedStringTable(sharedStrings.Select(s => new SharedStringItem(new Text(s))));
            var worksheet = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            fill(data);
            worksheet.Worksheet = new Worksheet(data);
            workbook.Workbook.AppendChild(new Sheets(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1, Name = "S" }));
        }

        return stream.ToArray();
    }

    private static byte[] ZipWithoutWorkbook()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using var entry = new StreamWriter(zip.CreateEntry("hello.txt").Open());
            entry.Write("hello");
        }

        return stream.ToArray();
    }
}
