namespace MyERP.Modules.System.Contracts;

/// <summary>各模組在目前公司向一名用戶發布站內通知。SourceKey 由來源單據與事件組成，重試時保持不變。</summary>
public sealed record NotificationMessage(long RecipientUserId, string Category, string Title, string Body, string? TargetPath, string SourceKey);

/// <summary>通知發布契約；呼叫端只引用 SYS Contracts，不讀寫 SYS 資料表。</summary>
public interface INotificationPublisher
{
    Task<long> PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
