using MyERP.Shared.Domain;

namespace MyERP.Modules.Purchasing.Domain;

public enum RequisitionStatus { Draft, PendingApproval, Rejected, Approved, Closed }

public sealed class Requisition : BusinessEntity, ISoftDeletable
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public long RequesterId { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<RequisitionLine> Lines { get; } = [];
}

public sealed class RequisitionLine : BusinessEntity
{
    public long RequisitionId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public DateOnly RequiredDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal OrderedQuantity { get; set; }
    public string? Remark { get; set; }
}
