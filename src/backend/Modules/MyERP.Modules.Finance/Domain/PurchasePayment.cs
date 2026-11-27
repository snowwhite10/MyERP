using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>Immutable identity and posted amount of a base-currency purchase payment.</summary>
public sealed class PurchasePaymentSource : BusinessEntity
{
    public const string PaymentType = "PurchasePayment";
    public long SupplierId { get; set; }
    public string SourceType { get; set; } = PaymentType;
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
    public long GlJournalId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long BankAccountId { get; set; }
    public string BankReference { get; set; } = "";
    public string BaseCurrency { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal UnappliedAmount { get; set; }
}

/// <summary>Permanent source for an approved payment write-off.</summary>
public sealed class PurchasePaymentSettlement : BusinessEntity
{
    public const string SettlementType = "PaymentWriteOff";
    public long SupplierId { get; set; }
    public string SourceType { get; set; } = SettlementType;
    public long SourceId { get; set; }
    public int SourceVersion { get; set; }
    public string Fingerprint { get; set; } = "";
    public long GlJournalId { get; set; }
    public long PaymentId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string BaseCurrency { get; set; } = "";
    public decimal TotalAmount { get; set; }
}

/// <summary>One posted settlement allocation to an FI payable.</summary>
public sealed class PurchasePaymentAllocation : BusinessEntity
{
    public long SettlementId { get; set; }
    public long PaymentId { get; set; }
    public long PayableId { get; set; }
    public decimal Amount { get; set; }
}
