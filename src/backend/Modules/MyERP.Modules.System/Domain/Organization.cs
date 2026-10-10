using MyERP.Shared.Domain;

namespace MyERP.Modules.System.Domain;

/// <summary>公司為隔離根，不能套用目前公司篩選；只有具備 SYS 權限的服務可查詢或修改。</summary>
public sealed class SystemCompany : Entity, IAuditable, IVersioned, ISoftDeletable
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? CustomFields { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public int Version { get; set; }
}

public sealed class SystemBranch : MasterEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? CustomFields { get; set; }
}

public sealed class SystemDepartment : MasterEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public long? BranchId { get; set; }
    public long? ParentDepartmentId { get; set; }
    public string? CustomFields { get; set; }
}
