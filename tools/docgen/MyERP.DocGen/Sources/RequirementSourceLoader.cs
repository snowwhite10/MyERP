using MyERP.DocGen.Markdown;

namespace MyERP.DocGen.Sources;

/// <summary>
/// 從 Markdown 來源讀取結構化需求（單一事實來源，MASTER_PROMPT 第 3 節）。
/// 表格依表頭欄位辨識種類，格式見 tools/docgen/README.md。
/// </summary>
public sealed class RequirementSourceLoader(RepositoryLayout layout)
{
    private const string DefaultVersion = "1.0";
    private const string DefaultAuthor = "Developer";

    public RequirementSet Load()
    {
        var set = new RequirementSet();
        if (Directory.Exists(layout.ModulesDirectory))
        {
            foreach (var file in Directory.GetFiles(layout.ModulesDirectory, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                LoadModuleFile(set, MarkdownDocument.Parse(File.ReadAllText(file), layout.Relative(file)));
            }
        }

        foreach (var file in NonFunctionalSources())
        {
            LoadNonFunctional(set, MarkdownDocument.Parse(File.ReadAllText(file), layout.Relative(file)));
        }

        if (File.Exists(layout.ReferenceNotes))
        {
            LoadParity(set, MarkdownDocument.Parse(File.ReadAllText(layout.ReferenceNotes), layout.Relative(layout.ReferenceNotes)));
        }

        return set;
    }

    private IEnumerable<string> NonFunctionalSources()
    {
        if (File.Exists(layout.Architecture))
        {
            yield return layout.Architecture;
        }

        var srs = Path.Combine(layout.DocSourcesDirectory, "srs");
        if (Directory.Exists(srs))
        {
            foreach (var file in Directory.GetFiles(srs, "ch4-2-*.md").Order(StringComparer.Ordinal))
            {
                yield return file;
            }
        }
    }

    /// <summary>解析單一模組來源檔（requirements / data-structure / ui-spec / test-plan 皆可，依表頭判斷）。</summary>
    public static void LoadModuleFile(RequirementSet set, MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(document);
        foreach (var table in document.Tables)
        {
            if (table.HasColumns("Sub-function No.", "Sub-function"))
            {
                ReadSubFunctions(set, document.Path, table);
            }
            else if (table.HasColumns("Interface No.", "Interface"))
            {
                ReadInterfaces(set, document.Path, table);
            }
            else if (table.HasColumns("Test Case ID", "Related REQ_F"))
            {
                ReadTestCases(set, document.Path, table);
            }
            else if (table.HasColumns("Requirement ID", "Table Name"))
            {
                ReadDataStructures(set, document.Path, table);
            }
            else if (table.HasColumns("Requirement ID", "Item"))
            {
                ReadInterfaceItems(set, document.Path, table);
            }
            else if (table.HasColumns("Requirement ID", "Description"))
            {
                ReadFunctional(set, document.Path, table);
            }
        }
    }

    public static void LoadNonFunctional(RequirementSet set, MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(document);
        foreach (var table in document.Tables)
        {
            var idColumn = table.ColumnIndex("Requirement ID") >= 0 ? table.ColumnIndex("Requirement ID") : table.ColumnIndex("ID");
            if (idColumn < 0 || table.Header.Count < 2)
            {
                continue;
            }

            var descriptionColumn = idColumn == 0 ? 1 : 0;
            var measurementColumn = table.Header.Count > 2 ? table.Header.Count - 1 : -1;
            foreach (var (row, index) in table.Rows.Select((r, i) => (r, i)))
            {
                var id = MarkdownInline.ToPlainText(row[idColumn]);
                if (!id.StartsWith("REQ_NF", StringComparison.Ordinal))
                {
                    continue;
                }

                var location = new SourceLocation(document.Path, table.Line + 2 + index);
                if (!RequirementIds.TryParseNonFunctional(id, out var section, out var sequence))
                {
                    set.Issues.Add(new SourceIssue(IssueSeverity.Error, "ID_FORMAT", $"Invalid non-functional requirement ID '{id}'.", location));
                    continue;
                }

                set.NonFunctional.Add(new NonFunctionalRequirement(
                    id,
                    section,
                    sequence,
                    MarkdownInline.ToPlainText(row[descriptionColumn]),
                    measurementColumn > descriptionColumn ? MarkdownInline.ToPlainText(row[measurementColumn]) : "",
                    location));
            }
        }
    }

    public static void LoadParity(RequirementSet set, MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(document);
        foreach (var table in document.Tables)
        {
            var status = table.ColumnIndex("狀態");
            if (status < 0)
            {
                continue;
            }

            (ParityKind Kind, int Ref, int Name, int Module, int MyErp)? shape =
                table.HasColumns("BlueWhale ID", "功能描述", "MyERP 需求 ID")
                    ? (ParityKind.Function, table.ColumnIndex("BlueWhale ID"), table.ColumnIndex("功能描述"), table.ColumnIndex("MyERP 模組"), table.ColumnIndex("MyERP 需求 ID"))
                    : table.HasColumns("BlueWhale 編號", "介面名稱", "MyERP 介面編號")
                        ? (ParityKind.Interface, table.ColumnIndex("BlueWhale 編號"), table.ColumnIndex("介面名稱"), table.ColumnIndex("MyERP 模組"), table.ColumnIndex("MyERP 介面編號"))
                        : table.HasColumns("BlueWhale ID", "表名", "MyERP 表名")
                            ? (ParityKind.Table, table.ColumnIndex("BlueWhale ID"), table.ColumnIndex("表名"), -1, table.ColumnIndex("MyERP 表名"))
                            : null;
            if (shape is not { } s)
            {
                continue;
            }

            foreach (var (row, index) in table.Rows.Select((r, i) => (r, i)))
            {
                set.Parity.Add(new ParityEntry(
                    s.Kind,
                    MarkdownInline.ToPlainText(row[s.Ref]),
                    MarkdownInline.ToPlainText(row[s.Name]),
                    s.Module >= 0 ? MarkdownInline.ToPlainText(row[s.Module]) : "",
                    MarkdownInline.ToPlainText(row[s.MyErp]),
                    MarkdownInline.ToPlainText(row[status]),
                    new SourceLocation(document.Path, table.Line + 2 + index)));
            }
        }
    }

    private static void ReadSubFunctions(RequirementSet set, string file, MarkdownTable table)
    {
        var no = table.ColumnIndex("Sub-function No.");
        var name = table.ColumnIndex("Sub-function");
        foreach (var (row, location) in Rows(file, table))
        {
            var text = MarkdownInline.ToPlainText(row[no]);
            if (!RequirementIds.TryParseInterfaceNo(text, out var module, out var number))
            {
                set.Issues.Add(new SourceIssue(IssueSeverity.Error, "ID_FORMAT", $"Invalid sub-function number '{text}' (expected '{{module}}.{{sub-function}}').", location));
                continue;
            }

            set.SubFunctions.Add(new SubFunction(module, number, MarkdownInline.ToPlainText(row[name]), location));
        }
    }

    private static void ReadFunctional(RequirementSet set, string file, MarkdownTable table)
    {
        var id = table.ColumnIndex("Requirement ID");
        foreach (var (row, location) in Rows(file, table))
        {
            var text = MarkdownInline.ToPlainText(row[id]);
            if (!RequirementIds.TryParseFunctional(text, out var module, out var sub, out var sequence))
            {
                set.Issues.Add(new SourceIssue(IssueSeverity.Error, "ID_FORMAT", $"Invalid functional requirement ID '{text}'.", location));
                continue;
            }

            set.Functional.Add(new FunctionalRequirement(
                text,
                module,
                sub,
                sequence,
                Cell(table, row, "Version", DefaultVersion),
                Cell(table, row, "Description", ""),
                Cell(table, row, "Author", DefaultAuthor),
                Cell(table, row, "Acceptance Criteria", ""),
                location));
        }
    }

    private static void ReadDataStructures(RequirementSet set, string file, MarkdownTable table)
    {
        var id = table.ColumnIndex("Requirement ID");
        foreach (var (row, location) in Rows(file, table))
        {
            var text = MarkdownInline.ToPlainText(row[id]);
            if (!RequirementIds.TryParseDataStructure(text, out var sequence))
            {
                set.Issues.Add(new SourceIssue(IssueSeverity.Error, "ID_FORMAT", $"Invalid data structure requirement ID '{text}'.", location));
                continue;
            }

            set.DataStructures.Add(new DataStructureRequirement(
                text,
                sequence,
                Cell(table, row, "Version", DefaultVersion),
                Cell(table, row, "Table Name", ""),
                Cell(table, row, "Description", ""),
                Cell(table, row, "Author", DefaultAuthor),
                Ids(table, row, "Related REQ_F"),
                location));
        }
    }

    private static void ReadInterfaces(RequirementSet set, string file, MarkdownTable table)
    {
        var no = table.ColumnIndex("Interface No.");
        foreach (var (row, location) in Rows(file, table))
        {
            var text = MarkdownInline.ToPlainText(row[no]);
            if (!RequirementIds.TryParseInterfaceNo(text, out var module, out var number))
            {
                set.Issues.Add(new SourceIssue(IssueSeverity.Error, "ID_FORMAT", $"Invalid interface number '{text}' (expected '{{module}}.{{interface}}').", location));
                continue;
            }

            set.Interfaces.Add(new InterfaceDefinition(module, number, Cell(table, row, "Interface", ""), Ids(table, row, "Related REQ_F"), location));
        }
    }

    private static void ReadInterfaceItems(RequirementSet set, string file, MarkdownTable table)
    {
        var id = table.ColumnIndex("Requirement ID");
        foreach (var (row, location) in Rows(file, table))
        {
            var text = MarkdownInline.ToPlainText(row[id]);
            if (!RequirementIds.TryParseInterfaceItem(text, out var module, out var number, out var sequence))
            {
                set.Issues.Add(new SourceIssue(IssueSeverity.Error, "ID_FORMAT", $"Invalid interface item ID '{text}'.", location));
                continue;
            }

            set.InterfaceItems.Add(new InterfaceItem(
                text,
                module,
                number,
                sequence,
                Cell(table, row, "Version", DefaultVersion),
                Cell(table, row, "Item", ""),
                Cell(table, row, "Description", ""),
                Cell(table, row, "Purpose", ""),
                Cell(table, row, "Format", ""),
                Cell(table, row, "Valid Range", ""),
                Cell(table, row, "Related I/O", "None"),
                Cell(table, row, "Author", DefaultAuthor),
                Ids(table, row, "Related REQ_F"),
                location));
        }
    }

    private static void ReadTestCases(RequirementSet set, string file, MarkdownTable table)
    {
        var id = table.ColumnIndex("Test Case ID");
        foreach (var (row, location) in Rows(file, table))
        {
            var text = MarkdownInline.ToPlainText(row[id]);
            if (text.Length == 0)
            {
                continue;
            }

            set.TestCases.Add(new TestCase(text, Ids(table, row, "Related REQ_F"), Cell(table, row, "Description", ""), Cell(table, row, "Status", ""), location));
        }
    }

    private static IEnumerable<(IReadOnlyList<string> Row, SourceLocation Location)> Rows(string file, MarkdownTable table) =>
        table.Rows.Select((row, index) => (row, new SourceLocation(file, table.Line + 2 + index)));

    private static string Cell(MarkdownTable table, IReadOnlyList<string> row, string column, string fallback)
    {
        var index = table.ColumnIndex(column);
        if (index < 0)
        {
            return fallback;
        }

        var value = MarkdownInline.ToPlainText(row[index]);
        return value.Length == 0 ? fallback : value;
    }

    private static IReadOnlyList<string> Ids(MarkdownTable table, IReadOnlyList<string> row, string column)
    {
        var index = table.ColumnIndex(column);
        return index < 0 ? [] : MarkdownInline.SplitIds(row[index]);
    }
}
