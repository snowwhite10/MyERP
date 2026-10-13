using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using MyERP.DocGen.Consistency;
using MyERP.DocGen.Excel;
using MyERP.DocGen.Generation;
using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Tests;

public sealed class DocumentGenerationTests
{
    [Fact]
    public void ConsistencyChecker_ReportsDuplicateIdsAndDatabaseMismatch()
    {
        var location = new SourceLocation("docs/02-modules/SYS/data-structure.md", 4);
        var set = new RequirementSet();
        set.DataStructures.Add(new DataStructureRequirement("REQ_DS43.1", 1, "1.0", "sys_company", "Company", "Developer", [], location));
        set.DataStructures.Add(new DataStructureRequirement("REQ_DS43.1", 1, "1.0", "sys_branch", "Branch", "Developer", [], location));
        var schema = new DatabaseSchema(true, "test", []);
        var outlineSrs = new DocumentResult("SRS.docx", BlueWhaleOutline.Srs, []);
        var outlineSds = new DocumentResult("SDS.docx", BlueWhaleOutline.Sds, []);

        var issues = ConsistencyChecker.Check(new ConsistencyInput(set, schema, outlineSrs, outlineSds));

        issues.Should().Contain(i => i.Severity == IssueSeverity.Error && i.Code == "DUPLICATE_ID");
        issues.Count(i => i.Code == "TABLE_NOT_IN_DATABASE").Should().Be(2);
    }

    [Fact]
    public void ConsistencyChecker_RejectsInterfaceNumberReservedForDifferentBlueWhaleScreen()
    {
        var location = new SourceLocation("ui-spec.md", 1);
        var set = new RequirementSet();
        set.Interfaces.Add(new InterfaceDefinition(5, 8, "Product Initial Stock View Interface", [], location));
        set.Parity.Add(new ParityEntry(ParityKind.Interface, "4.4.1.5.8", "Sales Outbound (Approval) Interface", "INV", "4.4.1.5.8", "未開始", location));
        var srs = new DocumentResult("SRS.docx", BlueWhaleOutline.Srs, []);
        var sds = new DocumentResult("SDS.docx", BlueWhaleOutline.Sds, []);

        var issues = ConsistencyChecker.Check(new ConsistencyInput(set, DatabaseSchema.Unavailable("test"), srs, sds));

        issues.Should().Contain(i => i.Code == "PARITY_INTERFACE_CONFLICT" && i.Message.Contains("5.8", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsistencyChecker_ReservesReorderedAssemblyNumbersButAllowsCompletedAliases()
    {
        var location = new SourceLocation("ui-spec.md", 1);
        var set = new RequirementSet();
        set.Interfaces.Add(new InterfaceDefinition(5, 9, "Product Initial Stock Create Interface", [], location));
        set.Interfaces.Add(new InterfaceDefinition(5, 10, "Lot and Serial Trace Interface", [], location));
        set.Interfaces.Add(new InterfaceDefinition(8, 11, "Item View Interface", [], location));
        set.Parity.Add(new ParityEntry(ParityKind.Interface, "4.4.1.5.9", "Product Assembly Create Interface", "INV", "4.4.1.5.10", "未開始", location));
        set.Parity.Add(new ParityEntry(ParityKind.Interface, "4.4.1.5.10", "Product Assembly View Interface", "INV", "4.4.1.5.9", "未開始", location));
        set.Parity.Add(new ParityEntry(ParityKind.Interface, "4.4.1.8.11", "Product View Interface", "MD", "4.4.1.8.11", "完成（物料）", location));
        var srs = new DocumentResult("SRS.docx", BlueWhaleOutline.Srs, []);
        var sds = new DocumentResult("SDS.docx", BlueWhaleOutline.Sds, []);

        var issues = ConsistencyChecker.Check(new ConsistencyInput(set, DatabaseSchema.Unavailable("test"), srs, sds));

        issues.Where(i => i.Code == "PARITY_INTERFACE_CONFLICT").Select(i => i.Message)
            .Should().Contain(m => m.Contains("5.9", StringComparison.Ordinal))
            .And.Contain(m => m.Contains("5.10", StringComparison.Ordinal))
            .And.NotContain(m => m.Contains("8.11", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsistencyChecker_ReportsDuplicateInterfaceNumberAndDifferentDocumentOrder()
    {
        var location = new SourceLocation("ui-spec.md", 1);
        var set = new RequirementSet();
        set.Interfaces.Add(new InterfaceDefinition(5, 21, "Opening View Interface", [], location));
        set.Interfaces.Add(new InterfaceDefinition(5, 21, "Other View Interface", [], location));
        var srs = new DocumentResult("SRS.docx", [.. BlueWhaleOutline.Srs,
            new OutlineEntry(4, "4.4.1.5.21 Opening View Interface"),
            new OutlineEntry(4, "4.5.1.5.23 Trace Interface")], []);
        var sds = new DocumentResult("SDS.docx", [.. BlueWhaleOutline.Sds,
            new OutlineEntry(4, "4.2.1.5.21 Opening View Interface")], []);

        var issues = ConsistencyChecker.Check(new ConsistencyInput(set, DatabaseSchema.Unavailable("test"), srs, sds));

        issues.Should().Contain(i => i.Code == "DUPLICATE_ID" && i.Message.Contains("interface number", StringComparison.Ordinal));
        issues.Should().Contain(i => i.Code == "INTERFACE_ORDER");
    }

    [Fact]
    public void EmptySources_GenerateReadableDocumentsAndReport()
    {
        var context = new GenerationContext(
            new RepositoryLayout(Path.GetTempPath()),
            new RequirementSet(),
            DatabaseSchema.Unavailable("test"),
            new DateOnly(2026, 10, 10));

        using var srsStream = new MemoryStream();
        var srs = SrsGenerator.Generate(context, srsStream, "SRS.docx");
        using (var document = WordprocessingDocument.Open(srsStream, false))
        {
            var paragraphs = document.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>();
            paragraphs.Select(p => p.InnerText).Should().Contain("4.1.1 General Functions data");
        }

        using var sdsStream = new MemoryStream();
        var sds = SdsGenerator.Generate(context, sdsStream, "SDS.docx");
        using (var document = WordprocessingDocument.Open(sdsStream, false))
        {
            document.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .Select(p => p.InnerText).Should().Contain("4.1 Database Design");
        }

        using var workbookStream = new MemoryStream();
        RequirementsWorkbook.Generate(context, workbookStream);
        using (var workbook = SpreadsheetDocument.Open(workbookStream, false))
        {
            workbook.WorkbookPart!.Workbook!.Descendants<Sheet>()
                .Select(s => s.Name!.Value).Should().Equal(RequirementsWorkbook.SheetNames);
        }

        var issues = ConsistencyChecker.Check(new ConsistencyInput(context.Requirements, context.Schema, srs, sds));
        issues.Should().NotContain(i => i.Severity == IssueSeverity.Error);
        issues.Should().Contain(i => i.Code == "SCHEMA_NOT_CHECKED");
        var report = ConsistencyReport.Render(context.Requirements, issues, context.Date, context.Schema.Source);
        report.Should().Contain("SCHEMA_NOT_CHECKED");
    }
}
