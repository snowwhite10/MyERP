using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Generation;

/// <summary>
/// 產生 MyERP_SRS.docx，章節結構沿用 BlueWhale SRS（MASTER_PROMPT 15.5；標題清單見 <see cref="BlueWhaleOutline.Srs"/>）。
/// 敘述章節來自 docs/05-doc-sources/srs/，4.1 / 4.3 / 4.4 來自 docs/02-modules/。
/// </summary>
public static class SrsGenerator
{
    public const string FileName = "MyERP_SRS.docx";
    public const string Title = "Software Requirements Specification (SRS) Document";

    public static DocumentResult Generate(GenerationContext context, Stream output, string path)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var writer = new WordDocumentWriter(output, "MyERP " + Title);
        var narrative = new NarrativeRenderer(context.Layout);
        var requirements = context.Requirements;

        void Section(int level, string heading, string source)
        {
            writer.Heading(level, heading);
            narrative.Render(writer, "srs/" + source);
        }

        void Optional(string source)
        {
            if (File.Exists(Path.Combine(context.Layout.DocSourcesDirectory, "srs", source)))
            {
                narrative.Render(writer, "srs/" + source);
            }
        }

        Section(1, "Non-Disclosure Agreement", "nda.md");
        writer.PageBreak();
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
        Section(2, "1.6 Roles and Responsibilities", "ch1-6-roles.md");
        Section(2, "1.7 Overview", "ch1-7-overview.md");

        writer.Heading(1, "2 Overall Description");
        Section(2, "2.1 Product Perspective", "ch2-1-product-perspective.md");
        writer.Heading(2, "2.2 Product Features");
        Optional("ch2-2-product-features.md");
        var feature = 0;
        foreach (var module in ModuleCatalog.All.Where(m => m.FeatureTitle is not null))
        {
            feature++;
            Section(3, $"2.2.{feature} {module.FeatureTitle}", $"ch2-2-{module.Number:00}-{Slug(module.Name)}.md");
        }

        Section(2, "2.3 User Classes and Characteristics", "ch2-3-user-classes.md");
        writer.Heading(2, "2.4 Operating Environment");
        Section(3, "2.4.1 Operating System Required for Development", "ch2-4-1-operating-system.md");
        Section(3, "2.4.2 Minimum Hardware Requirement for Development", "ch2-4-2-hardware.md");
        Section(3, "2.4.3 Tools Required for Development", "ch2-4-3-tools.md");
        Section(2, "2.5 Design and Implementation Constraints", "ch2-5-constraints.md");
        writer.Heading(2, "2.6 Assumptions and Dependencies");
        Section(3, "2.6.1 Assumptions", "ch2-6-1-assumptions.md");
        Section(3, "2.6.2 Dependencies", "ch2-6-2-dependencies.md");

        writer.Heading(1, "3 System Analysis");
        Section(2, "3.1 Interview", "ch3-1-interview.md");
        writer.Heading(2, "3.2 Feasibility Analysis");
        Section(3, "3.2.1 Economic and Financial Feasibility", "ch3-2-1-economic.md");
        Section(3, "3.2.2 Operational Feasibility", "ch3-2-2-operational.md");
        Section(3, "3.2.3 Technical Feasibility", "ch3-2-3-technical.md");

        writer.Heading(1, "4 Requirement Analysis");
        Section(2, "4.1 Functional Requirements", "ch4-1-intro.md");
        writer.Heading(3, "4.1.1 General Functions data");
        WriteFunctional(writer, requirements);

        writer.Heading(2, "4.2 Non-Functional Requirements");
        Section(3, "4.2.1 Performance", "ch4-2-1-performance.md");
        Section(3, "4.2.2 Security", "ch4-2-2-security.md");
        Section(3, "4.2.3 Quality", "ch4-2-3-quality.md");
        Section(3, "4.2.4 Availability", "ch4-2-4-availability.md");
        Section(3, "4.2.5 Testability", "ch4-2-5-testability.md");

        Section(2, "4.3 Data Structure", "ch4-3-intro.md");
        WriteDataStructures(writer, requirements);

        Section(2, "4.4 User Interface Requirements", "ch4-4-intro.md");
        writer.Heading(3, "4.4.1 User Portal");
        InterfaceSections.Write(writer, requirements, "4.4.1", definition =>
        {
            foreach (var item in requirements.ItemsOf(definition))
            {
                RequirementTables.InterfaceItem(writer, item);
                writer.Blank();
            }
        });

        writer.Heading(2, "4.5 User Interface Prototype");
        writer.Heading(3, "4.5.1 User Portal");
        InterfaceSections.Write(writer, requirements, "4.5.1", definition =>
        {
            var file = Path.Combine(context.Layout.OutputDirectory, "prototypes", $"4.5.1.{definition.Key}.png");
            if (!File.Exists(file)) throw new FileNotFoundException($"Prototype screenshot is missing: {file}", file);
            writer.Image(File.ReadAllBytes(file));
            writer.Caption($"Figure 4.5.1.{definition.Key} {definition.Name}");
        });

        writer.Heading(2, "4.6 Project Planning");
        Section(3, "4.6.1 Agile Scrum methodology", "ch4-6-1-scrum.md");
        Section(3, "4.6.2 Timeline", "ch4-6-2-timeline.md");
        Section(3, "4.6.3 Human Resources and Skills Needs", "ch4-6-3-human-resources.md");
        Section(3, "4.6.4 Budget Allocation", "ch4-6-4-budget.md");
        Section(3, "4.6.5 Product Design Specification", "ch4-6-5-product-design.md");

        writer.Save();
        return new DocumentResult(path, writer.Outline.ToList(), narrative.MissingSources.ToList());
    }

    private static void WriteFunctional(WordDocumentWriter writer, RequirementSet requirements)
    {
        var functional = requirements.OrderedFunctional.ToList();
        if (functional.Count == 0)
        {
            writer.Paragraph("[Pending] No functional requirements have been defined yet.", italic: true);
            return;
        }

        foreach (var module in functional.GroupBy(f => f.Module))
        {
            writer.Heading(4, $"4.1.1.{module.Key} {ModuleCatalog.NameOf(module.Key)}");
            foreach (var sub in module.GroupBy(f => f.SubFunction))
            {
                writer.Heading(5, $"4.1.1.{module.Key}.{sub.Key} {requirements.SubFunctionName(module.Key, sub.Key)}");
                foreach (var requirement in sub)
                {
                    RequirementTables.Functional(writer, requirement);
                    writer.Blank();
                }
            }
        }
    }

    private static void WriteDataStructures(WordDocumentWriter writer, RequirementSet requirements)
    {
        var tables = requirements.OrderedDataStructures.ToList();
        if (tables.Count == 0)
        {
            writer.Paragraph("[Pending] No data structures have been defined yet.", italic: true);
            return;
        }

        foreach (var table in tables)
        {
            RequirementTables.DataStructure(writer, table);
            writer.Blank();
        }
    }

    internal static string Slug(string name) => name.ToLowerInvariant().Replace(' ', '-');
}
