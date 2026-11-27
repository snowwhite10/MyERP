using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>FI purchase payment draft. Posting creates a separate permanent source keyed by this row's ID.</summary>
public sealed class PurchasePaymentDocument : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public long SupplierId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public long BankAccountId { get; set; }
    public required string BankReference { get; set; }
    public string? Remark { get; set; }
    public PurchasePaymentDocumentStatus Status { get; set; } = PurchasePaymentDocumentStatus.Draft;
    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum PurchasePaymentDocumentStatus { Draft, Pending, Rejected, Approved, Posted }
