using MyERP.Shared.Domain;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Domain;

public enum RfqStatus { Draft, Sent, Closed }

public sealed class Rfq : BusinessEntity, ISoftDeletable, IDataScopedEntity
{
    public required string DocumentNo { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateOnly ReplyDeadline { get; set; }
    public long OwnerId { get; set; }
    public long? BranchId { get; set; }
    public long? DepartmentId { get; set; }
    public long OwnerUserId => OwnerId;
    public RfqStatus Status { get; set; } = RfqStatus.Draft;
    public string? Remark { get; set; }
    public string? CustomFields { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public List<RfqLine> Lines { get; } = [];
    public List<RfqInvitation> Invitations { get; } = [];
}

public sealed class RfqLine : BusinessEntity
{
    public long RfqId { get; set; }
    public int LineNo { get; set; }
    public long ItemId { get; set; }
    public long? RequisitionLineId { get; set; }
    public DateOnly RequiredDate { get; set; }
    public decimal Quantity { get; set; }
    public string? Remark { get; set; }
}

public sealed class RfqInvitation : BusinessEntity
{
    public long RfqId { get; set; }
    public long SupplierId { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierNameSnapshot { get; set; }
    public DateTime? SentAt { get; set; }
}
