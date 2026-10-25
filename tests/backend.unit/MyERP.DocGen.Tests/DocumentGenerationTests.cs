using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using MyERP.DocGen.Consistency;
using MyERP.DocGen.Excel;
using MyERP.DocGen.Generation;
using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;
using MyERP.DocGen.Markdown;

namespace MyERP.DocGen.Tests;

public sealed class DocumentGenerationTests
{
    [Fact]
    public void NarrativeMermaid_EmbedsImageAndRetainsFollowingCaption()
    {
        using var stream = new MemoryStream();
        using (var writer = new WordDocumentWriter(stream, "Diagram test"))
        {
            NarrativeRenderer.RenderBlocks(writer, [
                new MarkdownCode(1, "mermaid", "flowchart LR\nA --> B"),
                new MarkdownParagraph(4, "Figure 3.1 Architecture")],
                new StubMermaidRenderer());
            writer.Save();
        }

        using var document = WordprocessingDocument.Open(stream, false);
        var main = document.MainDocumentPart!;
        main.ImageParts.Should().ContainSingle();
        var body = main.Document!.Body!;
        body.InnerText.Should().Contain("Figure 3.1 Architecture");
        body.InnerText.Should().NotContain("[Pending] Diagram");
        body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>()
            .Should().ContainSingle();
    }

    private sealed class StubMermaidRenderer : IMermaidRenderer
    {
        public byte[] RenderPng(string source) => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lXcAAAAASUVORK5CYII=");
    }

    [Fact]
    public void SdsDatabaseDesign_EmbedsErDiagramsInModuleAndSourceOrderWithoutSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "myerp-er-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var module in new[] { "SYS", "SD" })
            {
                Directory.CreateDirectory(Path.Combine(root, "docs", "02-modules", module));
            }
            File.WriteAllText(Path.Combine(root, "docs", "02-modules", "SYS", "data-structure.md"),
                "```mermaid\nerDiagram\n A ||--o{ B : first\n```\n```mermaid\nerDiagram\n B ||--o{ C : second\n```\n");
            File.WriteAllText(Path.Combine(root, "docs", "02-modules", "SD", "data-structure.md"),
                "```mermaid\nerDiagram\n X ||--o{ Y : third\n```\n");
            var renderer = new RecordingMermaidRenderer();
            var context = new GenerationContext(new RepositoryLayout(root), new RequirementSet(),
                DatabaseSchema.Unavailable("test"), new DateOnly(2026, 10, 25));

            using var stream = new MemoryStream();
            SdsGenerator.Generate(context, stream, "SDS.docx", renderer);

            renderer.Sources.Should().Equal(
                "erDiagram\n A ||--o{ B : first", "erDiagram\n B ||--o{ C : second",
                "erDiagram\n X ||--o{ Y : third");
            using var document = WordprocessingDocument.Open(stream, false);
            document.MainDocumentPart!.ImageParts.Should().HaveCount(3);
            var body = document.MainDocumentPart.Document!.Body!;
            body.InnerText.Should().Contain("Figure 4.1.1 Entity Relationship Diagram (Part 1: User / SYS)");
            body.InnerText.Should().Contain("Figure 4.1.2 Entity Relationship Diagram (Part 2: User / SYS)");
            body.InnerText.Should().Contain("Figure 4.1.3 Entity Relationship Diagram (Part 3: Sales / SD)");
            body.InnerText.Should().NotContain("[Pending] Entity relationship diagram");
            body.InnerText.Should().Contain("Database schema was not read");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingMermaidRenderer : IMermaidRenderer
    {
        public List<string> Sources { get; } = [];
        public byte[] RenderPng(string source)
        {
            Sources.Add(source);
            return new StubMermaidRenderer().RenderPng(source);
        }
    }

    [Fact]
    public void SdsDatabaseDesign_IgnoresNonErDiagramsAndExplainsMissingErSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "myerp-no-er-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = Path.Combine(root, "docs", "02-modules", "BI");
            Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module, "data-structure.md"),
                "```mermaid\nflowchart LR\n A --> B\n```\n");
            var renderer = new RecordingMermaidRenderer();
            var context = new GenerationContext(new RepositoryLayout(root), new RequirementSet(),
                DatabaseSchema.Unavailable("test"), new DateOnly(2026, 10, 25));

            using var stream = new MemoryStream();
            SdsGenerator.Generate(context, stream, "SDS.docx", renderer);

            renderer.Sources.Should().BeEmpty();
            using var document = WordprocessingDocument.Open(stream, false);
            document.MainDocumentPart!.ImageParts.Should().BeEmpty();
            document.MainDocumentPart.Document!.Body!.InnerText.Should().Contain("No module ER diagram source was found");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NarrativeMermaid_RejectsEmptySource()
    {
        using var stream = new MemoryStream();
        using var writer = new WordDocumentWriter(stream, "Empty diagram");
        var action = () => NarrativeRenderer.RenderBlocks(writer,
            [new MarkdownCode(7, "mermaid", "  ")], new StubMermaidRenderer());
        action.Should().Throw<InvalidDataException>().WithMessage("*line 7*");
    }

    [Fact]
    public void RepositoryLayout_UsesSeparateOutputWithoutMovingSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "myerp source with spaces");
        var output = Path.Combine(Path.GetTempPath(), "myerp isolated output");

        var layout = new RepositoryLayout(root, output);

        layout.OutputDirectory.Should().Be(Path.GetFullPath(output));
        layout.ModulesDirectory.Should().Be(Path.GetFullPath(Path.Combine(root, "docs/02-modules")));
        new RepositoryLayout(root).OutputDirectory.Should().Be(Path.GetFullPath(Path.Combine(root, "docs/99-output")));
        new RepositoryLayout(root, ".tools/output with spaces").OutputDirectory.Should()
            .Be(Path.GetFullPath(Path.Combine(root, ".tools/output with spaces")));
    }

    [Fact]
    public void SafetyStockDesign_HasTraceableRequirementsTableAndInterface()
    {
        var set = new RequirementSourceLoader(RepositoryLayout.Discover(Directory.GetCurrentDirectory())).Load();

        set.Functional.Select(r => r.Id).Should().Contain([
            "REQ_F4115.26.1", "REQ_F4115.26.2", "REQ_F4115.26.3"]);
        set.DataStructures.Should().ContainSingle(r => r.Id == "REQ_DS43.157" &&
            r.TableName == "inv_safety_stock_threshold");
        set.Interfaces.Should().ContainSingle(i => i.Module == 5 && i.Number == 26 &&
            i.Name == "Safety Stock View Interface");
        set.InterfaceItems.Where(i => i.Module == 5 && i.Interface == 26)
            .Select(i => i.Id).Should().Contain([
                "REQ_IO_A5.26.1", "REQ_IO_A5.26.2", "REQ_IO_A5.26.8"]);
    }

    [Fact]
    public void AssemblyDesign_ReservesThreeInterfacesAndTracesItsRequirements()
    {
        var set = new RequirementSourceLoader(RepositoryLayout.Discover(Directory.GetCurrentDirectory())).Load();

        set.Functional.Select(r => r.Id).Should().Contain([
            "REQ_F4115.4.1", "REQ_F4115.4.2", "REQ_F4115.4.3",
            "REQ_F4115.4.4", "REQ_F4115.4.5", "REQ_F4115.4.6"]);
        set.Interfaces.Where(i => i.Module == 5 && i.Number is >= 9 and <= 11).OrderBy(i => i.Number)
            .Select(i => i.Name).Should().Equal(
                "Product Assembly View Interface",
                "Product Assembly Create Interface",
                "Product Assembly Edit Interface");
        set.InterfaceItems.Where(i => i.Module == 5 && i.Interface is >= 9 and <= 11)
            .Select(i => i.Id).Should().Contain(["REQ_IO_A5.9.1", "REQ_IO_A5.10.1", "REQ_IO_A5.11.1"]);
    }

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
