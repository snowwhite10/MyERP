using System.Text.Json.Serialization;
using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Permanent FI lock target for one company/customer pair (ar_credit_key). Never deleted, so every credit
/// decision and every FI limit/AR write for the pair serializes on the same row.
/// </summary>
public sealed class CreditKey : BusinessEntity
{
    public long CustomerId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<CreditLimitStatus>))]
public enum CreditLimitStatus
{
    Active,
    Deactivated
}

/// <summary>Dated company-base-currency credit limit (ar_credit_limit). History is retained after deactivation.</summary>
public sealed class CreditLimit : BusinessEntity
{
    public long CustomerId { get; set; }
    public string BaseCurrency { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public decimal Amount { get; set; }
    public CreditLimitStatus Status { get; set; } = CreditLimitStatus.Active;
}

/// <summary>
/// Posted receivable exposure source (ar_posted_receivable). Only FI posting writes rows (WU-FI-002a);
/// source identity is immutable and unique per company including history.
/// </summary>
public sealed class PostedReceivable : BusinessEntity
{
    public long CustomerId { get; set; }
    public string SourceType { get; set; } = "";
    public long SourceId { get; set; }
    public string BaseCurrency { get; set; } = "";
    public decimal OriginalBaseAmount { get; set; }
    public decimal OpenBaseAmount { get; set; }
    public DateOnly PostingDate { get; set; }
}
