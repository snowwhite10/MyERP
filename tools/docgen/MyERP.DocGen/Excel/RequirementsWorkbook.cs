using System.Globalization;
using MyERP.DocGen.Generation;
using MyERP.DocGen.Markdown;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Excel;

/// <summary>MyERP_Requirements.xlsx 的分頁（MASTER_PROMPT 15.7）。</summary>
public static class RequirementsWorkbook
{
    public const string FileName = "MyERP_Requirements.xlsx";

    public static readonly string[] SheetNames =
    [
        "Functional", "Non-Functional", "Data Structure", "Interface", "Traceability Matrix",
        "BlueWhale Parity", "Timeline", "Human Resources", "Budget",
    ];

    public static void Generate(GenerationContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);
        XlsxWriter.Write(output, Build(context));
    }

    public static IReadOnlyList<WorksheetContent> Build(GenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var set = context.Requirements;
        return
        [
            new("Functional",
                ["Requirement ID", "Module", "Sub-function", "Version", "Description", "Author", "Acceptance Criteria", "Source"],
                set.OrderedFunctional.Select(f => Row(
                    f.Id, ModuleCatalog.NameOf(f.Module), set.SubFunctionName(f.Module, f.SubFunction), f.Version,
                    f.Description, f.Author, f.AcceptanceCriteria, f.Location.ToString())).ToList()),
            new("Non-Functional",
                ["Requirement ID", "Section", "Description", "Measurement", "Source"],
                set.NonFunctional.OrderBy(n => n.Section).ThenBy(n => n.Sequence).Select(n => Row(
                    n.Id, "4.2." + n.Section.ToString(CultureInfo.InvariantCulture), n.Description, n.Measurement, n.Location.ToString())).ToList()),
            new("Data Structure",
                ["Requirement ID", "Version", "Table Name", "Description", "Author", "Related REQ_F"],
                set.OrderedDataStructures.Select(d => Row(
                    d.Id, d.Version, d.TableName, d.Description, d.Author, string.Join(", ", d.RelatedFunctions))).ToList()),
            new("Interface",
                ["Requirement ID", "Interface No.", "Interface", "Item", "Description", "Purpose", "Format", "Valid Range", "Related I/O", "Version", "Author"],
                set.OrderedInterfaces.SelectMany(i => set.ItemsOf(i).Select(item => Row(
                    item.Id, "4.4.1." + i.Key, i.Name, item.Item, item.Description, item.Purpose, item.Format,
                    item.ValidRange, item.RelatedIo, item.Version, item.Author))).ToList()),
            new("Traceability Matrix",
                ["REQ_F", "Description", "REQ_DS", "Interfaces", "REQ_IO", "Test Cases", "Coverage"],
                Traceability.Build(set).Select(t => Row(
                    t.Requirement.Id, t.Requirement.Description, string.Join(", ", t.DataStructures),
                    string.Join(", ", t.Interfaces.Select(k => "4.4.1." + k)), string.Join(", ", t.InterfaceItems),
                    string.Join(", ", t.Tests), t.Coverage)).ToList()),
            new("BlueWhale Parity",
                ["Kind", "BlueWhale Ref", "Name", "MyERP Module", "MyERP Ref", "Status"],
                set.Parity.Select(p => Row(p.Kind.ToString(), p.BlueWhaleRef, p.Name, p.MyErpModule, p.MyErpRef, p.Status)).ToList()),
            FromSource(context, "Timeline", "ch4-6-2-timeline.md", ["Phase", "Milestone", "Start", "End"]),
            FromSource(context, "Human Resources", "ch4-6-3-human-resources.md", ["No.", "Position", "Skill Requirement"]),
            FromSource(context, "Budget", "ch4-6-4-budget.md", ["Item", "Allocation (%)", "Payment Stage"]),
        ];
    }

    /// <summary>4.6 規劃分頁取來源檔的第一張表格；來源未寫時只輸出表頭。</summary>
    private static WorksheetContent FromSource(GenerationContext context, string name, string source, IReadOnlyList<string> defaultHeader)
    {
        var path = Path.Combine(context.Layout.DocSourcesDirectory, "srs", source);
        var table = File.Exists(path) ? MarkdownDocument.Load(path).Tables.FirstOrDefault() : null;
        if (table is null)
        {
            return new WorksheetContent(name, defaultHeader, []);
        }

        return new WorksheetContent(
            name,
            table.Header.Select(MarkdownInline.ToPlainText).ToList(),
            table.Rows.Select(r => (IReadOnlyList<string>)r.Select(MarkdownInline.ToPlainText).ToList()).ToList());
    }

    private static string[] Row(params string[] values) => values;
}
