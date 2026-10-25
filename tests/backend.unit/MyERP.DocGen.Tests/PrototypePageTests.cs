using MyERP.DocGen.Generation;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Tests;

public sealed class PrototypePageTests
{
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
            File.ReadAllText(Path.Combine(directory, pages[1].FileName)).Should().Contain("No interface items are specified");

            set.Interfaces.RemoveAt(2);
            PrototypePages.Generate(set, directory);
            File.Exists(Path.Combine(directory, "4.5.1.1.1.html")).Should().BeFalse();
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
