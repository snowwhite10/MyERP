using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.System.Application.Notifications;

/// <summary>通知寫入端（REQ_F4118.32.1）。目前公司隔離，收件人必須在公司中啟用；來源鍵防止重試重複發布。</summary>
public sealed class NotificationPublisher(MyErpDbContext db, IClock clock) : INotificationPublisher
{
    public async Task<long> PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var companyId = db.CurrentCompanyId ?? throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "A company context is required.");
        Validate(message);
        if (!await db.Set<SystemCompany>().AsNoTracking().AnyAsync(x => x.Id == companyId && x.IsActive, cancellationToken))
            throw new ForbiddenException("SYS.COMPANY.UNAVAILABLE", "An active company context is required.");

        var recipientExists = await db.Set<SystemUserCompany>().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.UserId == message.RecipientUserId && x.IsActive, cancellationToken)
            && await db.Set<SystemUser>().AsNoTracking()
                .AnyAsync(x => x.Id == message.RecipientUserId && x.IsActive, cancellationToken);
        if (!recipientExists)
            throw new ForbiddenException("SYS.NOTIFICATION.RECIPIENT_UNAVAILABLE", "The recipient is not active in this company.");

        var existing = await db.Set<SystemNotification>().AsNoTracking()
            .Where(x => x.RecipientUserId == message.RecipientUserId && x.SourceKey == message.SourceKey.Trim())
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing is { } id) return id;

        var item = new SystemNotification
        {
            CompanyId = companyId,
            RecipientUserId = message.RecipientUserId,
            Category = message.Category.Trim(),
            Title = message.Title.Trim(),
            Body = message.Body.Trim(),
            TargetPath = message.TargetPath,
            SourceKey = message.SourceKey.Trim(),
            ExpiresAt = clock.LocalNow.AddDays(90),
        };
        db.Set<SystemNotification>().Add(item);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("ux_sys_notification_company_recipient_source", StringComparison.Ordinal))
        {
            // 並發重試同時找不到舊列時，唯一索引決定勝者；卸載失敗實體後讀回勝者。
            db.Entry(item).State = EntityState.Detached;
            return await db.Set<SystemNotification>().AsNoTracking()
                .Where(x => x.RecipientUserId == message.RecipientUserId && x.SourceKey == message.SourceKey.Trim())
                .Select(x => x.Id).SingleAsync(cancellationToken);
        }
        return item.Id;
    }

    private static void Validate(NotificationMessage message)
    {
        if (message.RecipientUserId <= 0 || string.IsNullOrWhiteSpace(message.Category) || message.Category.Length > 40
            || string.IsNullOrWhiteSpace(message.Title) || message.Title.Length > 160
            || string.IsNullOrWhiteSpace(message.Body) || message.Body.Length > 2000
            || string.IsNullOrWhiteSpace(message.SourceKey) || message.SourceKey.Length > 160
            || message.TargetPath is { Length: > 500 }
            || message.TargetPath is { } path && (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\')))
            throw new RequestValidationException("notification", "Invalid notification recipient or content.");
    }
}
