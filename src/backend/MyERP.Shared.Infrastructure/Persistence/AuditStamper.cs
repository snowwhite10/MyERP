using MyERP.Shared.Domain;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 審計 / 公司 / 版本欄位的蓋章規則（純邏輯，不依賴 EF，可直接單元測試）；
/// 由 <see cref="AuditSaveChangesInterceptor"/> 在 SaveChanges 前對新增與修改的實體呼叫。
/// </summary>
public sealed class AuditStamper(IClock clock, ICurrentUser currentUser, ICurrentCompany currentCompany)
{
    private long UserId => currentUser.UserId ?? SystemUser.Id;

    public void StampAdded(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is IAuditable auditable)
        {
            auditable.CreatedAt = clock.LocalNow;
            auditable.CreatedBy = UserId;
            auditable.UpdatedAt = null;
            auditable.UpdatedBy = null;
        }

        if (entity is IVersioned versioned)
        {
            versioned.Version = 1;
        }

        // 已明確指定公司（例如種子程序為其他公司建資料）則保留；否則必須有公司上下文
        if (entity is ICompanyOwned { CompanyId: 0 } owned)
        {
            owned.CompanyId = currentCompany.CompanyId
                ?? throw new InvalidOperationException(
                    $"Cannot add {entity.GetType().Name}: no current company (X-Company-Id) and CompanyId was not set explicitly.");
        }
    }

    /// <param name="entity">被修改的實體。</param>
    /// <param name="originalVersion">載入時（或用戶端送來）的版本，即樂觀鎖比對值；新版本 = 此值 + 1。</param>
    public void StampModified(object entity, int originalVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is IAuditable auditable)
        {
            auditable.UpdatedAt = clock.LocalNow;
            auditable.UpdatedBy = UserId;
        }

        if (entity is IVersioned versioned)
        {
            versioned.Version = checked(originalVersion + 1);
        }
    }
}
