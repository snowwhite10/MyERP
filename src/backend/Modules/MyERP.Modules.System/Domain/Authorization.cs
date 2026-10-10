using MyERP.Shared.Domain;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Domain;

/// <summary>全系統角色定義；在哪間公司生效由 <see cref="SystemUserRole"/> 決定。資料範圍套用於本角色授予的全部權限。</summary>
public sealed class SystemRole : Entity, IAuditable, IVersioned, ISoftDeletable
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DataScope DataScope { get; set; } = DataScope.Self;

    /// <summary>全部權限（例如 admin）：不需逐一列出權限，且為全公司資料範圍。</summary>
    public bool IsFullAccess { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public int Version { get; set; }
}

/// <summary>角色授予的權限代碼（代碼由各模組宣告於權限目錄，不另建權限表，ADR-014）。</summary>
public sealed class SystemRoleGrant: Entity, IAuditable
{
    public long RoleId { get; set; }
    public required string PermissionCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>用戶可存取的公司，以及在該公司的分店／部門歸屬（資料範圍判斷用）。</summary>
public sealed class SystemUserCompany : Entity, IAuditable, IVersioned
{
    public long UserId { get; set; }
    public long CompanyId { get; set; }
    public long? BranchId { get; set; }
    public long? DepartmentId { get; set; }

    /// <summary>登入後預設的操作公司；每位用戶最多一間。</summary>
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public int Version { get; set; }
}

/// <summary>用戶在某公司擁有的角色；必須先有對應的 <see cref="SystemUserCompany"/>（複合外鍵保護）。</summary>
public sealed class SystemUserRole : Entity, IAuditable
{
    public long UserId { get; set; }
    public long CompanyId { get; set; }
    public long RoleId { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}
