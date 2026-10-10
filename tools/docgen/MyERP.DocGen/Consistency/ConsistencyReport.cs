using System.Globalization;
using System.Text;
using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Consistency;

/// <summary>把一致性檢查結果寫成 docs/99-output/consistency-report.md。</summary>
public static class ConsistencyReport
{
    public const string FileName = "consistency-report.md";

    public static string Render(RequirementSet set, IReadOnlyList<SourceIssue> issues, DateOnly date, string schemaSource)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(issues);
        var text = new StringBuilder();
        text.AppendLine("# 一致性檢查報告");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"> 由 `tools/docgen` 自動產生（MASTER_PROMPT 15.8），不得手動修改。產生日期：{date:yyyy-MM-dd}；資料庫：{Escape(schemaSource)}。");
        text.AppendLine();
        text.AppendLine("## 摘要");
        text.AppendLine();
        text.AppendLine("| 項目 | 數量 |");
        text.AppendLine("|---|---|");
        Count(text, "功能需求 REQ_F", set.Functional.Count);
        Count(text, "非功能需求 REQ_NF", set.NonFunctional.Count);
        Count(text, "資料結構 REQ_DS43", set.DataStructures.Count);
        Count(text, "介面", set.Interfaces.Count);
        Count(text, "介面欄位 REQ_IO_A", set.InterfaceItems.Count);
        Count(text, "測試案例", set.TestCases.Count);
        Count(text, "BlueWhale 對照項目", set.Parity.Count);
        Count(text, "Error", issues.Count(i => i.Severity == IssueSeverity.Error));
        Count(text, "Warning", issues.Count(i => i.Severity == IssueSeverity.Warning));
        Count(text, "Info", issues.Count(i => i.Severity == IssueSeverity.Info));
        text.AppendLine();
        text.AppendLine(issues.Any(i => i.Severity == IssueSeverity.Error) ? "**結果：未通過（有 Error）。**" : "**結果：通過（沒有 Error）。**");

        foreach (var (severity, title) in new[] { (IssueSeverity.Error, "Error"), (IssueSeverity.Warning, "Warning"), (IssueSeverity.Info, "Info") })
        {
            var list = issues.Where(i => i.Severity == severity).ToList();
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"## {title}（{list.Count}）");
            text.AppendLine();
            if (list.Count == 0)
            {
                text.AppendLine("無。");
                continue;
            }

            text.AppendLine("| 代碼 | 說明 | 位置 |");
            text.AppendLine("|---|---|---|");
            foreach (var issue in list.OrderBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.Location?.ToString(), StringComparer.Ordinal))
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {issue.Code} | {Escape(issue.Message)} | {Escape(issue.Location?.ToString() ?? "-")} |");
            }
        }

        return text.ToString();
    }

    private static void Count(StringBuilder text, string label, int count) =>
        text.AppendLine(CultureInfo.InvariantCulture, $"| {label} | {count} |");

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
