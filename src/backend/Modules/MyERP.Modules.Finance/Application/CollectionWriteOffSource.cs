using System.Data.Common;
using System.Text.Json.Serialization;

namespace MyERP.Modules.Finance.Application;

/// <summary>Saved FI collection write-off document state (BR-FI-032). Only <see cref="Approved"/> may be posted.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceCollectionWriteOffState>))]
public enum FinanceCollectionWriteOffState
{
    Draft,
    Pending,
    Rejected,
    Deleted,

    /// <summary>Final SYS approval saved in the posting transaction; FI may post it.</summary>
    Approved,

    /// <summary>Already Posted. Accepted only as a replay of FI's own committed settlement row (checked before the read).</summary>
    Posted
}

/// <summary>One saved settlement line: the posted FI receivable (<c>ar_posted_receivable.id</c>) and its base amount.</summary>
public sealed record FinanceCollectionWriteOffLine(long ReceivableId, decimal Amount);

/// <summary>
/// The saved FI write-off document read inside the caller's transaction (BR-FI-035). <see cref="ReceiptId"/> is the FI
/// receipt document ID (the receipt's GL/FI source ID). Amounts are in the company base currency only (BR-FI-031).
/// </summary>
public sealed record FinanceCollectionWriteOffSnapshot(
    long CompanyId, long SettlementId, int SourceVersion, string Fingerprint, long CustomerId,
    FinanceCollectionWriteOffState State, DateOnly BusinessDate, string Currency, string BaseCurrency, long ReceiptId,
    decimal TotalAmount, IReadOnlyList<FinanceCollectionWriteOffLine> Lines);

/// <summary>
/// FI write-off document read port (ADR-122). The settlement document and its workflow are built in WU-FI-005d, which
/// implements this port over its own FI table; until then only tests register it. It must read through the supplied
/// transaction and return null for a missing, foreign-company or out-of-scope settlement.
/// </summary>
public interface IFinanceCollectionWriteOffSource
{
    Task<FinanceCollectionWriteOffSnapshot?> ReadWriteOffAsync(long companyId, long settlementId,
        DbTransaction transaction, CancellationToken cancellationToken = default);
}
