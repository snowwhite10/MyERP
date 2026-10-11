using MyERP.Shared.Domain;
using MyERP.Shared.Security;

namespace MyERP.Modules.System.Domain;

/// <summary>
/// IP 白名單（sys_ip_whitelist，REQ_DS43.102）：全系統設定，不分公司。層級為角色時只有 <see cref="RoleId"/>、為用戶時只有
/// <see cref="UserId"/>（CHECK 保護）。<see cref="IpRange"/> 存正規化文字（見 <see cref="MyERP.Shared.Security.IpRange"/>）。
/// 有效期限為含頭尾的日期（<c>IClock</c> 時區），空白表示不限。不刪除，以停用處理。
/// </summary>
public sealed class IpWhitelistEntry : Entity, IAuditable, IVersioned
{
    public const int MaxDescriptionLength = 200;

    public IpAccessScope Scope { get; set; }
    public long? RoleId { get; set; }
    public long? UserId { get; set; }
    public required string IpRange { get; set; }
    public string? Description { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public int Version { get; set; }

    public long? TargetId => Scope switch
    {
        IpAccessScope.Role => RoleId,
        IpAccessScope.User => UserId,
        _ => null,
    };
}
