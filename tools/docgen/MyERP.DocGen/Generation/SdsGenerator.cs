using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Generation;

/// <summary>
/// 產生 MyERP_SDS.docx，章節結構沿用 BlueWhale SDS（MASTER_PROMPT 15.6；標題清單見 <see cref="BlueWhaleOutline.Sds"/>）。
/// 4.1 欄位表一律由實際資料庫結構產生（MASTER_PROMPT 15.3），不手寫。
/// </summary>
public static class SdsGenerator
{
    public const string FileName = "MyERP_SDS.docx";
    public const string Title = "Software Design Specification (SDS) Document";

    public static DocumentResult Generate(GenerationContext context, Stream output, string path)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var writer = new WordDocumentWriter(output, "MyERP " + Title);
        var narrative = new NarrativeRenderer(context.Layout);

        void Section(int level, string heading, string source)
        {
            writer.Heading(level, heading);
            narrative.Render(writer, "sds/" + source);
        }

        CoverPage.Write(writer, Title, context.Date);
        writer.PageBreak();
        writer.TableOfContents();
        writer.PageBreak();

        writer.Heading(1, "1 Introduction");
        Section(2, "1.1 Purpose", "ch1-1-purpose.md");
        Section(2, "1.2 Scope", "ch1-2-scope.md");
        Section(2, "1.3 Definitions, acronyms, and abbreviations", "ch1-3-definitions.md");
        Section(2, "1.4 Intended Audience and Reading Suggestions", "ch1-4-intended-audience.md");
        Section(2, "1.5 References", "ch1-5-references.md");
        Section(2, "1.6 Overview", "ch1-6-overview.md");

        writer.Heading(1, "2 Design Consideration");
        Section(2, "2.1 Goals and Guidelines", "ch2-1-goals.md");
        Section(2, "2.2 Development Methods & Contingencies", "ch2-2-development-methods.md");
        Section(2, "2.3 Architecture Strategies", "ch2-3-architecture-strategies.md");

        writer.Heading(1, "3 System Architecture and Architecture Design");
        Section(2, "3.1 System Architecture", "ch3-1-system-architecture.md");
        Section(2, "3.2 Security Architecture", "ch3-2-security-architecture.md");
        Section(2, "3.3 Software Architecture", "ch3-3-software-architecture.md");

        writer.Heading(1, "4 System Design");
        writer.Heading(2, "4.1 Database Design");
        WriteDatabase(writer, context);

        writer.Heading(2, "4.2 User Interface Design");
        writer.Heading(3, "4.2.1 User Portal");
        InterfaceSections.Write(writer, context.Requirements, "4.2.1", definition => InterfaceSections.ScreenshotPlaceholder(writer, definition, "System"));

        writer.Save();
        return new DocumentResult(path, writer.Outline.ToList(), narrative.MissingSources.ToList());
    }

    private static void WriteDatabase(WordDocumentWriter writer, GenerationContext context)
    {
        if (!context.Schema.IsAvailable)
        {
            writer.Paragraph($"[Pending] Database schema was not read ({context.Schema.Source}). Run docgen with --connection to generate this section.", italic: true);
            return;
        }

        if (context.Schema.Tables.Count == 0)
        {
            writer.Paragraph("[Pending] The database has no business tables yet.", italic: true);
            return;
        }

        // ER 圖（按模組分拆為多張）由之後的工作單元以 Mermaid 產生，此處先放圖說佔位。
        writer.Paragraph("[Pending] Entity relationship diagram.", italic: true, center: true);
        writer.Caption("Figure 4.1.1 Entity Relationship Diagram (Part 1)");

        var descriptions = context.Requirements.DataStructures
            .GroupBy(d => d.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Description, StringComparer.OrdinalIgnoreCase);
        foreach (var table in context.Schema.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            writer.Paragraph("Table Name: " + table.Name);
            var key = string.Join(", ", table.PrimaryKey);
            writer.Paragraph("Primary Key: " + (key.Length == 0 ? "None" : key));
            writer.Paragraph(descriptions.TryGetValue(table.Name, out var description) ? description : "[Pending] Table description (REQ_DS43 not defined).");
            RequirementTables.Columns(writer, table);
            writer.Blank();
        }
    }
}
