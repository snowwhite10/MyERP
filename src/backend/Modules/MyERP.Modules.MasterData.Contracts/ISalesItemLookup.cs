namespace MyERP.Modules.MasterData.Contracts;

/// <summary>Current-company active item identity for a sales document snapshot.</summary>
public sealed record SalesItemIdentity(long Id, string Code, string Name);

/// <summary>Sales must resolve item identities through MD rather than reading MD tables.</summary>
public interface ISalesItemLookup
{
    Task<SalesItemIdentity> RequireActiveAsync(long id, CancellationToken token = default);
}
