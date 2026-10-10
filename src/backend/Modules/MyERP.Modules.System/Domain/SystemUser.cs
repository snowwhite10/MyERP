using MyERP.Shared.Auditing;
using MyERP.Shared.Domain;

namespace MyERP.Modules.System.Domain;

/// <summary>全系統登入身分；公司可見範圍由後續授權關聯表決定。</summary>
public sealed class SystemUser : Entity, IAuditable, IVersioned, ISoftDeletable
{
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; }
    [SensitiveData]
    public required string PasswordHash { get; set; }
    public DateTime PasswordChangedAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public int Version { get; set; }
}
