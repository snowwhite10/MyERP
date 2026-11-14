using System.Data.Common;
using System.Text.Json.Serialization;

namespace MyERP.Modules.Finance.Application;

/// <summary>Saved FI receipt document state (BR-FI-032). Only <see cref="Approved"/> may be posted.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceCollectionReceiptState>))]
public enum FinanceCollectionReceiptState
{
    Draft,
    Pending,
    Rejected,
    Deleted,

    /// <summary>Final SYS approval saved in the posting transaction; FI may post it.</summary>
    Approved,

    /// <summary>Already Posted. Accepted only as a replay of FI's own committed receipt row (checked before the read).</summary>
    Posted
}

/// <summary>
/// The saved FI receipt document read inside the caller's transaction (BR-FI-033). The amount is in the company base
/// currency only (BR-FI-031); <see cref="BankAccountId"/> is an MD fund account ID; <see cref="BankReference"/> is the
/// saved, already trimmed bank reference.
/// </summary>
public sealed record FinanceCollectionReceiptSnapshot(
    long CompanyId, long ReceiptId, int SourceVersion, string Fingerprint, long CustomerId,
    FinanceCollectionReceiptState State, DateOnly BusinessDate, string Currency, string BaseCurrency, decimal Amount,
    long BankAccountId, string BankReference);

/// <summary>
/// FI receipt document read port (ADR-121). The receipt document and its workflow are built in WU-FI-005b, which
/// implements this port over its own FI table; until then only tests register it. It must read through the supplied
/// transaction and return null for a missing, foreign-company or out-of-scope receipt.
/// </summary>
public interface IFinanceCollectionReceiptSource
{
    Task<FinanceCollectionReceiptSnapshot?> ReadReceiptAsync(long companyId, long receiptId, DbTransaction transaction,
        CancellationToken cancellationToken = default);
}
