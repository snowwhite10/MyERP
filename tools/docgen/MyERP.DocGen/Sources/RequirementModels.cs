namespace MyERP.DocGen.Sources;

/// <summary>來源檔位置，供一致性報告指出問題所在。</summary>
public sealed record SourceLocation(string File, int Line)
{
    public override string ToString() => Line > 0 ? $"{File}:{Line}" : File;
}

/// <summary>子功能（4.1.1.{模組}.{子功能} 標題）。</summary>
public sealed record SubFunction(int Module, int Number, string Name, SourceLocation Location);

/// <summary>功能需求（SRS 4.1.1）。</summary>
public sealed record FunctionalRequirement(
    string Id,
    int Module,
    int SubFunction,
    int Sequence,
    string Version,
    string Description,
    string Author,
    string AcceptanceCriteria,
    SourceLocation Location);

/// <summary>非功能需求量化指標（SRS 4.2）。</summary>
public sealed record NonFunctionalRequirement(string Id, int Section, int Sequence, string Description, string Measurement, SourceLocation Location);

/// <summary>資料結構需求（SRS 4.3），對應一張實際資料表。</summary>
public sealed record DataStructureRequirement(
    string Id,
    int Sequence,
    string Version,
    string TableName,
    string Description,
    string Author,
    IReadOnlyList<string> RelatedFunctions,
    SourceLocation Location);

/// <summary>介面（SRS 4.4 / 4.5、SDS 4.2 共用同一份清單與次序）。</summary>
public sealed record InterfaceDefinition(int Module, int Number, string Name, IReadOnlyList<string> RelatedFunctions, SourceLocation Location)
{
    public string Key => $"{Module}.{Number}";
}

/// <summary>介面欄位 / 按鈕（SRS 4.4 每項一張表）。</summary>
public sealed record InterfaceItem(
    string Id,
    int Module,
    int Interface,
    int Sequence,
    string Version,
    string Item,
    string Description,
    string Purpose,
    string Format,
    string ValidRange,
    string RelatedIo,
    string Author,
    IReadOnlyList<string> RelatedFunctions,
    SourceLocation Location)
{
    public string InterfaceKey => $"{Module}.{Interface}";
}

/// <summary>測試案例（各模組 test-plan.md），用於追溯矩陣。</summary>
public sealed record TestCase(string Id, IReadOnlyList<string> RelatedFunctions, string Description, string Status, SourceLocation Location);

public enum ParityKind
{
    Function,
    Interface,
    Table,
}

/// <summary>BlueWhale 功能基線對照（reference-notes.md 第 5–7 節）。</summary>
public sealed record ParityEntry(ParityKind Kind, string BlueWhaleRef, string Name, string MyErpModule, string MyErpRef, string Status, SourceLocation Location);

public enum IssueSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>來源解析或一致性檢查發現的問題。</summary>
public sealed record SourceIssue(IssueSeverity Severity, string Code, string Message, SourceLocation? Location = null);

/// <summary>docgen 的全部結構化輸入（需求、資料表、介面、測試、對照表）。</summary>
public sealed class RequirementSet
{
    public List<SubFunction> SubFunctions { get; } = [];

    public List<FunctionalRequirement> Functional { get; } = [];

    public List<NonFunctionalRequirement> NonFunctional { get; } = [];

    public List<DataStructureRequirement> DataStructures { get; } = [];

    public List<InterfaceDefinition> Interfaces { get; } = [];

    public List<InterfaceItem> InterfaceItems { get; } = [];

    public List<TestCase> TestCases { get; } = [];

    public List<ParityEntry> Parity { get; } = [];

    /// <summary>解析階段發現的問題（格式錯誤的 ID、缺欄位等）。</summary>
    public List<SourceIssue> Issues { get; } = [];

    /// <summary>依 SRS 編號排序的功能需求。</summary>
    public IEnumerable<FunctionalRequirement> OrderedFunctional =>
        Functional.OrderBy(f => f.Module).ThenBy(f => f.SubFunction).ThenBy(f => f.Sequence);

    public IEnumerable<DataStructureRequirement> OrderedDataStructures => DataStructures.OrderBy(d => d.Sequence);

    /// <summary>介面次序：模組編號、介面編號。SRS 4.4、4.5 與 SDS 4.2 都用這個次序。</summary>
    public IEnumerable<InterfaceDefinition> OrderedInterfaces => Interfaces.OrderBy(i => i.Module).ThenBy(i => i.Number);

    public IEnumerable<InterfaceItem> ItemsOf(InterfaceDefinition definition) =>
        InterfaceItems.Where(i => i.Module == definition.Module && i.Interface == definition.Number).OrderBy(i => i.Sequence);

    public string SubFunctionName(int module, int number) =>
        SubFunctions.FirstOrDefault(s => s.Module == module && s.Number == number)?.Name ?? $"Function {number}";
}
