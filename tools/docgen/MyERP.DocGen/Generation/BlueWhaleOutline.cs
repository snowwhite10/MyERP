using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MyERP.DocGen.Word;

namespace MyERP.DocGen.Generation;

/// <summary>
/// BlueWhale SRS / SDS 第 1–3 層標題（WU-P0-013 從參考 docx 抽出；參考檔不提交，所以內建於此）。
/// 已套用 MyERP 修正：「4.1.1 General Functionsdata」補空格。SRS 2.2.x 依 MyERP 模組產生，不比對。
/// </summary>
public static partial class BlueWhaleOutline
{
    public static IReadOnlyList<OutlineEntry> Srs { get; } = Parse("""
        1|Non-Disclosure Agreement
        1|1 Introduction
        2|1.1 Purpose
        2|1.2 Scope
        2|1.3 Definitions, acronyms, and abbreviations
        2|1.4 Intended Audience and Reading Suggestions
        2|1.5 References
        2|1.6 Roles and Responsibilities
        2|1.7 Overview
        1|2 Overall Description
        2|2.1 Product Perspective
        2|2.2 Product Features
        2|2.3 User Classes and Characteristics
        2|2.4 Operating Environment
        3|2.4.1 Operating System Required for Development
        3|2.4.2 Minimum Hardware Requirement for Development
        3|2.4.3 Tools Required for Development
        2|2.5 Design and Implementation Constraints
        2|2.6 Assumptions and Dependencies
        3|2.6.1 Assumptions
        3|2.6.2 Dependencies
        1|3 System Analysis
        2|3.1 Interview
        2|3.2 Feasibility Analysis
        3|3.2.1 Economic and Financial Feasibility
        3|3.2.2 Operational Feasibility
        3|3.2.3 Technical Feasibility
        1|4 Requirement Analysis
        2|4.1 Functional Requirements
        3|4.1.1 General Functions data
        2|4.2 Non-Functional Requirements
        3|4.2.1 Performance
        3|4.2.2 Security
        3|4.2.3 Quality
        3|4.2.4 Availability
        3|4.2.5 Testability
        2|4.3 Data Structure
        2|4.4 User Interface Requirements
        3|4.4.1 User Portal
        2|4.5 User Interface Prototype
        3|4.5.1 User Portal
        2|4.6 Project Planning
        3|4.6.1 Agile Scrum methodology
        3|4.6.2 Timeline
        3|4.6.3 Human Resources and Skills Needs
        3|4.6.4 Budget Allocation
        3|4.6.5 Product Design Specification
        """);

    public static IReadOnlyList<OutlineEntry> Sds { get; } = Parse("""
        1|1 Introduction
        2|1.1 Purpose
        2|1.2 Scope
        2|1.3 Definitions, acronyms, and abbreviations
        2|1.4 Intended Audience and Reading Suggestions
        2|1.5 References
        2|1.6 Overview
        1|2 Design Consideration
        2|2.1 Goals and Guidelines
        2|2.2 Development Methods & Contingencies
        2|2.3 Architecture Strategies
        1|3 System Architecture and Architecture Design
        2|3.1 System Architecture
        2|3.2 Security Architecture
        2|3.3 Software Architecture
        1|4 System Design
        2|4.1 Database Design
        2|4.2 User Interface Design
        3|4.2.1 User Portal
        """);

    /// <summary>MyERP 對 BlueWhale 標題的已知修正（reference-notes.md 第 4 節）。</summary>
    private static readonly Dictionary<string, string> KnownFixes = new(StringComparer.Ordinal)
    {
        ["4.1.1 General Functionsdata"] = "4.1.1 General Functions data",
    };

    /// <summary>只取第 1–3 層，並略過 SRS 2.2.x（MyERP 依模組產生）。</summary>
    public static IReadOnlyList<OutlineEntry> Comparable(IEnumerable<OutlineEntry> outline) =>
        outline.Where(e => e.Level <= 3 && !FeatureSubsection().IsMatch(e.Text))
            .Select(e => e with { Text = Normalize(e.Text) })
            .ToList();

    /// <summary>逐一比對標題（層級與文字）；回傳差異說明，空清單代表一致。</summary>
    public static IReadOnlyList<string> Compare(IEnumerable<OutlineEntry> expected, IEnumerable<OutlineEntry> actual)
    {
        var left = Comparable(expected);
        var right = Comparable(actual);
        var differences = new List<string>();
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var e = i < left.Count ? left[i] : null;
            var a = i < right.Count ? right[i] : null;
            if (e != a)
            {
                differences.Add($"#{i + 1}: expected {Describe(e)}, found {Describe(a)}");
            }
        }

        return differences;
    }

    /// <summary>讀取參考 docx 的第 1–3 層標題（styleId Heading1–3），用於確認內建清單沒有偏離原文。</summary>
    public static IReadOnlyList<OutlineEntry> ReadReference(string docxPath)
    {
        using var document = WordprocessingDocument.Open(docxPath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return [];
        }

        var entries = new List<OutlineEntry>();
        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var match = style is null ? Match.Empty : HeadingStyle().Match(style);
            if (!match.Success)
            {
                continue;
            }

            var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
            if (text.Length > 0)
            {
                entries.Add(new OutlineEntry(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), text));
            }
        }

        return entries;
    }

    private static string Normalize(string text)
    {
        var collapsed = Whitespace().Replace(text.Trim(), " ");
        return KnownFixes.TryGetValue(collapsed, out var fixedText) ? fixedText : collapsed;
    }

    private static string Describe(OutlineEntry? entry) => entry is null ? "(none)" : $"H{entry.Level} '{entry.Text}'";

    private static List<OutlineEntry> Parse(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', 2))
            .Select(parts => new OutlineEntry(int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), parts[1]))
            .ToList();

    [GeneratedRegex(@"^2\.2\.\d+\s")]
    private static partial Regex FeatureSubsection();

    [GeneratedRegex(@"^Heading([1-3])$")]
    private static partial Regex HeadingStyle();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
