using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>FI-owned draft for non-invoice income; posting is a separate permanent source.</summary>
public sealed class OtherCollectionDocument : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public required string PayerDescription { get; set; }
    public long IncomeCategoryId { get; set; }
    public long BankAccountId { get; set; }
    public required string BankReference { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public OtherCollectionDocumentStatus Status { get; set; } = OtherCollectionDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum OtherCollectionDocumentStatus { Draft, Pending, Rejected, Approved, Posted }
