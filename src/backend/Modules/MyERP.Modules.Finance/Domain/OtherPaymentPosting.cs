using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Permanent Posted source of an other (non-AP) payment (WU-FI-008a2, REQ_DS43.220, BR-FI-062/064).
/// The row snapshots the saved Draft (category, bank/reference, date, amount) and the FI mapping used at posting
/// (expense account ID and mapping version). It is never updated or deleted and keeps the withdrawal reference
/// reserved even when the Draft row is later soft deleted. It creates no AP payable, allocation or unapplied amount.
/// </summary>
public sealed class OtherPaymentSource : BusinessEntity
{
    public const string PaymentType = "OtherPayment";
    public string SourceType { get; set; } = PaymentType;
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
    public long GlJournalId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long ExpenseCategoryId { get; set; }
    public long ExpenseAccountId { get; set; }
    public int MappingVersion { get; set; }
    public long BankAccountId { get; set; }
    public string BankReference { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public decimal Amount { get; set; }
}

/// <summary>
/// FI-owned map from an MD expense category to the MD expense account debited by an other payment
/// (WU-FI-008a2, REQ_DS43.221, BR-FI-060). One row per company/category; the row version is the mapping version.
/// MD category and account are scalar IDs (no cross-module FK); the posting service validates the account
/// through the MD public contract and rejects AP control, unapplied clearing and prepaid asset accounts.
/// </summary>
public sealed class OtherPaymentAccountMapping : BusinessEntity
{
    public long ExpenseCategoryId { get; set; }
    public long ExpenseAccountId { get; set; }
}
