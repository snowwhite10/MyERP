using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyERP.Modules.System.Application.Notifications;

/// <summary>每日清理 90 天到期的通知；失敗保留資料並於下次重試。</summary>
public sealed class ExpiredNotificationCleanup(IServiceScopeFactory scopes, ILogger<ExpiredNotificationCleanup> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, Exception?> Deleted =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "ExpiredNotificationsDeleted"), "Deleted {Count} expired notifications");
    private static readonly Action<ILogger, Exception?> Failed =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, "ExpiredNotificationCleanupFailed"), "Expired notification cleanup failed; retrying tomorrow");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var count = await scope.ServiceProvider.GetRequiredService<NotificationInboxService>().DeleteExpiredAsync(stoppingToken);
                Deleted(logger, count, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed(logger, ex);
            }
        }
    }
}
