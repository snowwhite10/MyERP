using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>Company accounting period. The application transaction enforces nonoverlap.</summary>
public sealed class GlPeriod : BusinessEntity
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public GlPeriodStatus Status { get; set; } = GlPeriodStatus.Open;
}

public enum GlPeriodStatus { Open, Closed }
