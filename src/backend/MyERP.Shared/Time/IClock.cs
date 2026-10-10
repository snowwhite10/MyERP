namespace MyERP.Shared.Time;

/// <summary>
/// 全系統唯一的時間來源（MASTER_PROMPT 6.3）。禁止直接使用 DateTime.Now / UtcNow、DateTimeOffset.Now / UtcNow 或 TimeProvider，
/// 一律注入本介面，測試才能固定時間。
/// </summary>
public interface IClock
{
    /// <summary>目前時間，偏移為設定的時區（appsettings <c>App:TimeZoneOffset</c>，預設 +08:00）。</summary>
    DateTimeOffset Now { get; }

    /// <summary>目前牆上時間（不含偏移），用於 DATETIME2 時間戳欄位（created_at 等，存 UTC+8）。</summary>
    DateTime LocalNow { get; }

    /// <summary>今天的業務日期（依設定時區，不是 UTC 日期）。</summary>
    DateOnly Today { get; }

    /// <summary>設定的時區偏移。</summary>
    TimeSpan Offset { get; }
}
