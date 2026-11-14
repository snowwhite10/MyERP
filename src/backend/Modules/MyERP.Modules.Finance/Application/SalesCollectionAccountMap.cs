namespace MyERP.Modules.Finance.Application;

/// <summary>
/// FI account mapping for sales collections and settlements (BR-FI-033, BR-FI-037; ADR-120). The version must equal the one
/// the GL posting path reads for <c>FI/SalesCollection</c> and <c>FI/CollectionWriteOff</c> through
/// <see cref="IGlPostingMappingVersionSource"/>. The bank debit account is not here: it is the MD fund account's ledger account.
/// </summary>
public sealed record SalesCollectionAccountMap(int MappingVersion, string UnappliedCashAccountCode, string ReceivableAccountCode);

/// <summary>Reads the company's current collection account map; null means no usable mapping (422).</summary>
public interface ISalesCollectionAccountMapSource
{
    Task<SalesCollectionAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken);
}
