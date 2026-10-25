using MyERP.DocGen.Generation;
using MyERP.DocGen.Sources;
using MyERP.DocGen.Schema;
using DocumentFormat.OpenXml.Packaging;

namespace MyERP.DocGen.Tests;

public sealed class PrototypePageTests
{
    [Fact]
    public void Srs_EmbedsOrderedPrototypeScreenshotsWithCaptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "myerp-srs-prototype-" + Guid.NewGuid().ToString("N"));
        try
        {
            var set = new RequirementSet();
            var source = new SourceLocation("ui-spec.md", 1);
            set.Interfaces.Add(new InterfaceDefinition(2, 3, "Third Screen", [], source));
            set.Interfaces.Add(new InterfaceDefinition(1, 1, "First Screen", [], source));
            var layout = new RepositoryLayout(root);
            var directory = Path.Combine(layout.OutputDirectory, "prototypes");
            Directory.CreateDirectory(directory);
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lXcAAAAASUVORK5CYII=");
            File.WriteAllBytes(Path.Combine(directory, "4.5.1.1.1.png"), png);
            File.WriteAllBytes(Path.Combine(directory, "4.5.1.2.3.png"), png);
            var context = new GenerationContext(layout, set, DatabaseSchema.Unavailable("test"), new DateOnly(2026, 10, 25));

            using var stream = new MemoryStream();
            SrsGenerator.Generate(context, stream, "SRS.docx");
            using var document = WordprocessingDocument.Open(stream, false);
            document.MainDocumentPart!.ImageParts.Should().HaveCount(2);
            var body = document.MainDocumentPart.Document!.Body!.InnerText;
            body.IndexOf("Figure 4.5.1.1.1 First Screen", StringComparison.Ordinal).Should().BeLessThan(
                body.IndexOf("Figure 4.5.1.2.3 Third Screen", StringComparison.Ordinal));
            body.Should().NotContain("[Pending] Prototype screenshot");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Srs_RejectsMissingPrototypeScreenshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "myerp-srs-prototype-" + Guid.NewGuid().ToString("N"));
        try
        {
            var set = new RequirementSet();
            set.Interfaces.Add(new InterfaceDefinition(1, 1, "First Screen", [], new SourceLocation("ui-spec.md", 1)));
            var context = new GenerationContext(new RepositoryLayout(root), set, DatabaseSchema.Unavailable("test"), new DateOnly(2026, 10, 25));
            using var stream = new MemoryStream();
            Action action = () => SrsGenerator.Generate(context, stream, "SRS.docx");
            action.Should().Throw<FileNotFoundException>().WithMessage("*4.5.1.1.1.png*");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Generate_UsesSrsInterfaceOrderAndEscapesSourceText()
    {
        var set = new RequirementSet();
        var source = new SourceLocation("ui-spec.md", 1);
        set.Interfaces.Add(new InterfaceDefinition(2, 3, "Third <Screen>", [], source));
        set.Interfaces.Add(new InterfaceDefinition(1, 2, "Second Screen", [], source));
        set.Interfaces.Add(new InterfaceDefinition(1, 1, "First Screen", [], source));
        set.InterfaceItems.Add(Item("REQ_IO_A1.1.2", 1, 1, 2, "Save (Button)"));
        set.InterfaceItems.Add(Item("REQ_IO_A1.1.1", 1, 1, 1, "Name <script> (Input)"));
        set.InterfaceItems.Add(new InterfaceItem("REQ_IO_A1.1.3", 1, 1, 3, "1.0", "Help (Label)", "Description", "顯示說明", "Text", "Any", "None", "Developer", [], source));
        var directory = Path.Combine(Path.GetTempPath(), "myerp-prototype-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pages = PrototypePages.Generate(set, directory);

            pages.Select(p => p.InterfaceKey).Should().Equal("1.1", "1.2", "2.3");
            pages.Select(p => p.FileName).Should().Equal("4.5.1.1.1.html", "4.5.1.1.2.html", "4.5.1.2.3.html");
            var index = File.ReadAllText(Path.Combine(directory, "index.html"));
            index.IndexOf("First Screen", StringComparison.Ordinal).Should().BeLessThan(index.IndexOf("Second Screen", StringComparison.Ordinal));
            index.IndexOf("Second Screen", StringComparison.Ordinal).Should().BeLessThan(index.IndexOf("Third &lt;Screen&gt;", StringComparison.Ordinal));
            var first = File.ReadAllText(Path.Combine(directory, pages[0].FileName));
            first.Should().Contain("REQ_IO_A1.1.1").And.Contain("REQ_IO_A1.1.2");
            first.IndexOf("REQ_IO_A1.1.1", StringComparison.Ordinal).Should().BeLessThan(first.IndexOf("REQ_IO_A1.1.2", StringComparison.Ordinal));
            first.Should().Contain("Name &lt;script&gt;").And.NotContain("<script>");
            first.Should().Contain("English purpose is pending for REQ_IO_A1.1.3.").And.NotContain("顯示說明");
            File.ReadAllText(Path.Combine(directory, pages[1].FileName)).Should().Contain("No interface items are specified");

            File.WriteAllBytes(Path.Combine(directory, "4.5.1.1.1.png"), [1]);
            set.Interfaces.RemoveAt(2);
            PrototypePages.Generate(set, directory);
            File.Exists(Path.Combine(directory, "4.5.1.1.1.html")).Should().BeFalse();
            File.Exists(Path.Combine(directory, "4.5.1.1.1.png")).Should().BeFalse();
            File.ReadAllText(Path.Combine(directory, "index.html")).Should().NotContain("First Screen");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static InterfaceItem Item(string id, int module, int screen, int sequence, string name) =>
        new(id, module, screen, sequence, "1.0", name, "Description", "Purpose", "Text", "Any", "None", "Developer", [], new SourceLocation("ui-spec.md", sequence));
}
