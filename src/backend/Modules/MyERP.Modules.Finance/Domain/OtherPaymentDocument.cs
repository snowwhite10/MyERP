using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>FI-owned draft for a non-AP expense payment; posting is a separate permanent source.</summary>
public sealed class OtherPaymentDocument : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public required string PayeeDescription { get; set; }
    public long ExpenseCategoryId { get; set; }
    public long BankAccountId { get; set; }
    public required string BankReference { get; set; }
    public DateOnly BusinessDate { get; set; }
    public required string Currency { get; set; }
    public decimal Amount { get; set; }
    public OtherPaymentDocumentStatus Status { get; set; } = OtherPaymentDocumentStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>Saved other payment state; serialized by name in API responses and query strings.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OtherPaymentDocumentStatus>))]
public enum OtherPaymentDocumentStatus { Draft, Pending, Rejected, Approved, Posted }
