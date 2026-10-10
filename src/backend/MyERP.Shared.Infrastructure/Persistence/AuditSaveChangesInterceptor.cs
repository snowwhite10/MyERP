using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyERP.Shared.Domain;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// SaveChanges 前填入審計欄位、公司、遞增 version，並禁止修改 created_* 與 company_id。
/// 樂觀鎖：Application 層把用戶端送來的 version 設為 <c>Property(e =&gt; e.Version).OriginalValue</c>，
/// 本 interceptor 以 OriginalValue + 1 作為新版本，EF 以 OriginalValue 產生 <c>WHERE version = @original</c>。
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly string[] ImmutableAfterInsert =
    [
        nameof(IAuditable.CreatedAt),
        nameof(IAuditable.CreatedBy),
        nameof(ICompanyOwned.CompanyId),
    ];

    private readonly AuditStamper _stamper;

    public AuditSaveChangesInterceptor(AuditStamper stamper)
    {
        ArgumentNullException.ThrowIfNull(stamper);
        _stamper = stamper;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData?.Context);
        return base.SavingChanges(eventData!, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData?.Context);
        return base.SavingChangesAsync(eventData!, result, cancellationToken);
    }

    /// <summary>對 ChangeTracker 中新增 / 修改的實體蓋章（SaveChanges 前呼叫；公開以便不連線測試）。</summary>
    public void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    _stamper.StampAdded(entry.Entity);
                    break;

                case EntityState.Modified:
                    foreach (var name in ImmutableAfterInsert)
                    {
                        if (entry.Metadata.FindProperty(name) is not null)
                        {
                            var property = entry.Property(name);
                            property.CurrentValue = property.OriginalValue;
                            property.IsModified = false;
                        }
                    }

                    var originalVersion = entry.Entity is IVersioned
                        ? (int)entry.Property(nameof(IVersioned.Version)).OriginalValue!
                        : 0;
                    _stamper.StampModified(entry.Entity, originalVersion);
                    break;

                default:
                    break;
            }
        }
    }
}
