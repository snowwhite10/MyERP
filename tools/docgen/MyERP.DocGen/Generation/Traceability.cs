using MyERP.DocGen.Sources;

namespace MyERP.DocGen.Generation;

/// <summary>一個功能需求的追溯關係（REQ_F ↔ REQ_DS ↔ 介面 / REQ_IO ↔ 測試）。</summary>
public sealed record TraceRow(
    FunctionalRequirement Requirement,
    IReadOnlyList<string> DataStructures,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> InterfaceItems,
    IReadOnlyList<string> Tests)
{
    public bool HasData => DataStructures.Count > 0;

    public bool HasInterface => Interfaces.Count > 0 || InterfaceItems.Count > 0;

    public bool HasTests => Tests.Count > 0;

    public string Coverage => (HasData, HasInterface, HasTests) switch
    {
        (true, true, true) => "Covered",
        (false, false, false) => "Not covered",
        _ => "Partial",
    };
}

public static class Traceability
{
    public static IReadOnlyList<TraceRow> Build(RequirementSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.OrderedFunctional.Select(f => new TraceRow(
                f,
                set.OrderedDataStructures.Where(d => d.RelatedFunctions.Contains(f.Id)).Select(d => d.Id).ToList(),
                set.OrderedInterfaces.Where(i => i.RelatedFunctions.Contains(f.Id)).Select(i => i.Key).ToList(),
                set.InterfaceItems.Where(i => i.RelatedFunctions.Contains(f.Id)).Select(i => i.Id).ToList(),
                set.TestCases.Where(t => t.RelatedFunctions.Contains(f.Id)).Select(t => t.Id).ToList()))
            .ToList();
    }
}
