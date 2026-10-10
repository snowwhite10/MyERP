using System.Text.RegularExpressions;
using MyERP.DocGen.Generation;
using MyERP.DocGen.Schema;
using MyERP.DocGen.Sources;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Consistency;

/// <summary>一致性檢查的輸入：來源、資料庫結構，以及剛產生的兩份文件大綱。</summary>
public sealed record ConsistencyInput(
    RequirementSet Requirements,
    DatabaseSchema Schema,
    DocumentResult Srs,
    DocumentResult Sds,
    IReadOnlyList<OutlineEntry>? ReferenceSrs = null,
    IReadOnlyList<OutlineEntry>? ReferenceSds = null);

/// <summary>
/// MASTER_PROMPT 15.8 的一致性檢查：ID 唯一、追溯完整、三處介面清單一致、表清單與資料庫一致、
/// BlueWhale 基線對照、章節結構與 BlueWhale 一致。Error 令 docgen 以非 0 結束。
/// </summary>
public static partial class ConsistencyChecker
{
    private static readonly string[] DoneStatuses = ["完成", "WONT_DO"];

    public static IReadOnlyList<SourceIssue> Check(ConsistencyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var set = input.Requirements;
        var issues = new List<SourceIssue>(set.Issues);

        CheckDuplicates(issues, set);
        CheckModules(issues, set);
        CheckReferences(issues, set);
        CheckCoverage(issues, set);
        CheckInterfaceOrder(issues, input.Srs.Outline, input.Sds.Outline);
        CheckOutline(issues, "OUTLINE_SRS", BlueWhaleOutline.Srs, input.Srs.Outline, IssueSeverity.Error);
        CheckOutline(issues, "OUTLINE_SDS", BlueWhaleOutline.Sds, input.Sds.Outline, IssueSeverity.Error);
        if (input.ReferenceSrs is not null)
        {
            CheckOutline(issues, "REFERENCE_DRIFT_SRS", input.ReferenceSrs, BlueWhaleOutline.Srs, IssueSeverity.Warning);
        }

        if (input.ReferenceSds is not null)
        {
            CheckOutline(issues, "REFERENCE_DRIFT_SDS", input.ReferenceSds, BlueWhaleOutline.Sds, IssueSeverity.Warning);
        }

        CheckSchema(issues, set, input.Schema);
        CheckParity(issues, set);
        CheckMissingSources(issues, input.Srs);
        CheckMissingSources(issues, input.Sds);
        return issues;
    }

    private static void CheckDuplicates(List<SourceIssue> issues, RequirementSet set)
    {
        Duplicates(issues, "functional requirement ID", set.Functional.Select(f => (f.Id, f.Location)));
        Duplicates(issues, "non-functional requirement ID", set.NonFunctional.Select(n => (n.Id, n.Location)));
        Duplicates(issues, "data structure requirement ID", set.DataStructures.Select(d => (d.Id, d.Location)));
        Duplicates(issues, "data structure table name", set.DataStructures.Select(d => (d.TableName.ToUpperInvariant(), d.Location)));
        Duplicates(issues, "interface number", set.Interfaces.Select(i => (i.Key, i.Location)));
        Duplicates(issues, "interface item ID", set.InterfaceItems.Select(i => (i.Id, i.Location)));
        Duplicates(issues, "sub-function number", set.SubFunctions.Select(s => ($"{s.Module}.{s.Number}", s.Location)));
        Duplicates(issues, "test case ID", set.TestCases.Select(t => (t.Id, t.Location)));
    }

    private static void Duplicates(List<SourceIssue> issues, string kind, IEnumerable<(string Key, SourceLocation Location)> items)
    {
        foreach (var group in items.GroupBy(i => i.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var locations = group.Select(g => g.Location).ToList();
            issues.Add(new SourceIssue(
                IssueSeverity.Error,
                "DUPLICATE_ID",
                $"Duplicate {kind} '{group.Key}' ({locations.Count} occurrences: {string.Join("; ", locations.Skip(1))}).",
                locations[0]));
        }
    }

    private static void CheckModules(List<SourceIssue> issues, RequirementSet set)
    {
        var used = set.Functional.Select(f => (f.Module, f.Id, f.Location))
            .Concat(set.Interfaces.Select(i => (i.Module, Id: i.Key, i.Location)))
            .Concat(set.InterfaceItems.Select(i => (i.Module, i.Id, i.Location)));
        foreach (var (module, id, location) in used.Where(u => ModuleCatalog.Find(u.Module) is null))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "UNKNOWN_MODULE", $"'{id}' uses SRS module {module}, which is not in the module catalog (MASTER_PROMPT 15.1).", location));
        }

        foreach (var f in set.Functional.Where(f => !set.SubFunctions.Any(s => s.Module == f.Module && s.Number == f.SubFunction)))
        {
            issues.Add(new SourceIssue(IssueSeverity.Warning, "MISSING_SUBFUNCTION", $"'{f.Id}' has no sub-function name for {f.Module}.{f.SubFunction} (add a 'Sub-function No. | Sub-function' table).", f.Location));
        }
    }

    private static void CheckReferences(List<SourceIssue> issues, RequirementSet set)
    {
        var functional = set.Functional.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var references = set.DataStructures.SelectMany(d => d.RelatedFunctions.Select(r => (Owner: d.Id, Ref: r, d.Location)))
            .Concat(set.Interfaces.SelectMany(i => i.RelatedFunctions.Select(r => (Owner: "interface " + i.Key, Ref: r, i.Location))))
            .Concat(set.InterfaceItems.SelectMany(i => i.RelatedFunctions.Select(r => (Owner: i.Id, Ref: r, i.Location))))
            .Concat(set.TestCases.SelectMany(t => t.RelatedFunctions.Select(r => (Owner: t.Id, Ref: r, t.Location))));
        foreach (var (owner, reference, location) in references.Where(r => !functional.Contains(r.Ref)))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "UNKNOWN_REQ_F", $"{owner} references undefined functional requirement '{reference}'.", location));
        }

        var interfaces = set.Interfaces.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in set.InterfaceItems.Where(i => !interfaces.Contains(i.InterfaceKey)))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "ITEM_WITHOUT_INTERFACE", $"'{item.Id}' belongs to interface {item.InterfaceKey}, which is not defined.", item.Location));
        }

        foreach (var definition in set.Interfaces.Where(d => !set.InterfaceItems.Any(i => i.InterfaceKey == d.Key)))
        {
            issues.Add(new SourceIssue(IssueSeverity.Warning, "INTERFACE_WITHOUT_ITEMS", $"Interface {definition.Key} '{definition.Name}' has no REQ_IO items.", definition.Location));
        }
    }

    private static void CheckCoverage(List<SourceIssue> issues, RequirementSet set)
    {
        foreach (var row in Traceability.Build(set).Where(r => r.Coverage != "Covered"))
        {
            var missing = new List<string>(3);
            if (!row.HasData)
            {
                missing.Add("data structure");
            }

            if (!row.HasInterface)
            {
                missing.Add("interface");
            }

            if (!row.HasTests)
            {
                missing.Add("test case");
            }

            issues.Add(new SourceIssue(IssueSeverity.Warning, "TRACE_GAP", $"'{row.Requirement.Id}' has no {string.Join(", ", missing)}.", row.Requirement.Location));
        }
    }

    /// <summary>SRS 4.4、4.5 與 SDS 4.2 的介面標題（去掉章節前綴後）必須完全相同且同次序。</summary>
    private static void CheckInterfaceOrder(List<SourceIssue> issues, IReadOnlyList<OutlineEntry> srs, IReadOnlyList<OutlineEntry> sds)
    {
        var requirements = InterfaceHeadings(srs, "4.4.1");
        var prototype = InterfaceHeadings(srs, "4.5.1");
        var design = InterfaceHeadings(sds, "4.2.1");
        if (!requirements.SequenceEqual(prototype, StringComparer.Ordinal))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "INTERFACE_ORDER", $"SRS 4.4 and SRS 4.5 interface lists differ ({requirements.Count} vs {prototype.Count} entries, first difference: {FirstDifference(requirements, prototype)})."));
        }

        if (!requirements.SequenceEqual(design, StringComparer.Ordinal))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "INTERFACE_ORDER", $"SRS 4.4 and SDS 4.2 interface lists differ ({requirements.Count} vs {design.Count} entries, first difference: {FirstDifference(requirements, design)})."));
        }
    }

    internal static List<string> InterfaceHeadings(IEnumerable<OutlineEntry> outline, string prefix) =>
        outline.Where(e => e.Level is 4 or 5 && e.Text.StartsWith(prefix + ".", StringComparison.Ordinal))
            .Select(e => $"H{e.Level} {e.Text[(prefix.Length + 1)..]}")
            .ToList();

    private static string FirstDifference(List<string> left, List<string> right)
    {
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var l = i < left.Count ? left[i] : "(none)";
            var r = i < right.Count ? right[i] : "(none)";
            if (l != r)
            {
                return $"#{i + 1} '{l}' vs '{r}'";
            }
        }

        return "none";
    }

    private static void CheckOutline(List<SourceIssue> issues, string code, IReadOnlyList<OutlineEntry> expected, IReadOnlyList<OutlineEntry> actual, IssueSeverity severity)
    {
        foreach (var difference in BlueWhaleOutline.Compare(expected, actual))
        {
            issues.Add(new SourceIssue(severity, code, "Heading structure differs from BlueWhale: " + difference));
        }
    }

    /// <summary>SRS 4.3 的表清單（REQ_DS43）必須與實際資料庫一致；SDS 4.1 直接由資料庫產生。</summary>
    private static void CheckSchema(List<SourceIssue> issues, RequirementSet set, DatabaseSchema schema)
    {
        if (!schema.IsAvailable)
        {
            issues.Add(new SourceIssue(IssueSeverity.Warning, "SCHEMA_NOT_CHECKED", $"Database schema not read ({schema.Source}); SRS 4.3 vs SDS 4.1 vs database was not checked."));
            return;
        }

        var database = schema.Tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var documented = set.DataStructures.Select(d => d.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in set.DataStructures.Where(d => !database.Contains(d.TableName)))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "TABLE_NOT_IN_DATABASE", $"'{d.Id}' table '{d.TableName}' does not exist in the database.", d.Location));
        }

        foreach (var table in schema.Tables.Where(t => !documented.Contains(t.Name)).Select(t => t.Name).Order(StringComparer.Ordinal))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "TABLE_NOT_DOCUMENTED", $"Database table '{table}' has no REQ_DS43 entry in any data-structure.md."));
        }
    }

    private static void CheckParity(List<SourceIssue> issues, RequirementSet set)
    {
        foreach (var kind in set.Parity.GroupBy(p => p.Kind))
        {
            var open = kind.Count(p => !DoneStatuses.Contains(p.Status));
            if (open > 0)
            {
                issues.Add(new SourceIssue(IssueSeverity.Warning, "PARITY_OPEN", $"BlueWhale parity ({kind.Key}): {open} of {kind.Count()} entries are not done yet."));
            }
        }

        var functional = set.Functional.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in set.Parity.Where(p => p.Kind == ParityKind.Function && p.Status == "完成" && FunctionalId().IsMatch(p.MyErpRef) && !functional.Contains(p.MyErpRef)))
        {
            issues.Add(new SourceIssue(IssueSeverity.Error, "PARITY_MISSING_REQ", $"Parity entry {entry.BlueWhaleRef} is marked done but '{entry.MyErpRef}' is not defined in any requirements.md.", entry.Location));
        }
    }

    private static void CheckMissingSources(List<SourceIssue> issues, DocumentResult document)
    {
        if (document.MissingSources.Count > 0)
        {
            issues.Add(new SourceIssue(
                IssueSeverity.Warning,
                "MISSING_SOURCE",
                $"{Path.GetFileName(document.Path)}: {document.MissingSources.Count} narrative source file(s) not written yet: {string.Join(", ", document.MissingSources)}."));
        }
    }

    [GeneratedRegex(@"^REQ_F411\d+\.\d+\.\d+$")]
    private static partial Regex FunctionalId();
}
