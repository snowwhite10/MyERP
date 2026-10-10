using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using MyERP.DocGen.Consistency;
using MyERP.DocGen.Excel;
using MyERP.DocGen.Generation;
using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;

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
