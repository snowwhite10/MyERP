using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

public sealed class TaxCode : MasterCategory
{
    public string TaxType { get; set; } = "Both";
}

public sealed class TaxRate : BusinessEntity, ISoftDeletable
{
    public long TaxCodeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public decimal RatePercent { get; set; }
    public bool IsDeleted { get; set; }
    public TaxCode? TaxCode { get; set; }
}
