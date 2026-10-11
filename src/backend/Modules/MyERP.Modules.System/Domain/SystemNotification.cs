using MyERP.Shared.Domain;

namespace MyERP.Modules.System.Domain;

/// <summary>公司內單一收件人的站內通知。已讀時間為 null 代表未讀，保留 90 天。</summary>
public sealed class SystemNotification : BusinessEntity
{
    public long RecipientUserId { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public string? TargetPath { get; set; }
    public required string SourceKey { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
