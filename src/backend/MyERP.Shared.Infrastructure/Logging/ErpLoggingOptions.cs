using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace MyERP.Shared.Infrastructure.Logging;

/// <summary>
/// 應用程式日誌設定（logging.md 第 3 節）。由設定讀取並在啟動時驗證，格式錯誤時啟動即失敗。
/// 最低等級與命名空間覆寫由 <c>Serilog:MinimumLevel</c> 區段設定（Serilog.Settings.Configuration）。
/// </summary>
public sealed record ErpLoggingOptions
{
    public const string SlowRequestMillisecondsKey = "App:SlowRequestMilliseconds";
    public const string FileEnabledKey = "App:Logging:FileEnabled";
    public const string FilePathKey = "App:Logging:FilePath";
    public const string RetainedDaysKey = "App:Logging:RetainedDays";
    public const string ConsoleEnabledKey = "App:Logging:ConsoleEnabled";
    public const string ApplicationInsightsConnectionStringKey = "ApplicationInsights:ConnectionString";

    /// <summary>Application Insights 標準環境變數（經環境變數設定提供者讀入）。</summary>
    public const string ApplicationInsightsEnvironmentKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public const int DefaultSlowRequestMilliseconds = 1000;
    public const int DefaultRetainedDays = 30;
    public const string DefaultFilePath = "logs/app-.log";

    /// <summary>API 回應時間超過（不含等於）此毫秒數時寫 Warning；0 表示每個請求都寫（只供測試）。</summary>
    public int SlowRequestMilliseconds { get; init; } = DefaultSlowRequestMilliseconds;

    public bool FileEnabled { get; init; } = true;

    /// <summary>滾動檔案路徑（Serilog 會在副檔名前加上日期，例如 <c>app-20261010.log</c>）；相對路徑以內容根目錄為準。</summary>
    public string FilePath { get; init; } = DefaultFilePath;

    /// <summary>日誌檔保留天數（MASTER_PROMPT 8.1：30 天）。</summary>
    public int RetainedDays { get; init; } = DefaultRetainedDays;

    /// <summary>是否同時寫入主控台；未設定時只在 Development 環境啟用。</summary>
    public bool? ConsoleEnabled { get; init; }

    /// <summary>Application Insights 連線字串；空白時不啟用該 sink。不得寫入版控的設定檔（用環境變數或 user-secrets）。</summary>
    public string? ApplicationInsightsConnectionString { get; init; }

    public static ErpLoggingOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var slow = ReadInt(configuration, SlowRequestMillisecondsKey, DefaultSlowRequestMilliseconds);
        if (slow < 0)
        {
            throw new FormatException($"Configuration '{SlowRequestMillisecondsKey}' must be zero or greater, but was {slow}.");
        }

        var days = ReadInt(configuration, RetainedDaysKey, DefaultRetainedDays);
        if (days < 1)
        {
            throw new FormatException($"Configuration '{RetainedDaysKey}' must be at least 1, but was {days}.");
        }

        var path = configuration[FilePathKey];
        var connectionString = configuration[ApplicationInsightsConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration[ApplicationInsightsEnvironmentKey];
        }

        return new ErpLoggingOptions
        {
            SlowRequestMilliseconds = slow,
            FileEnabled = ReadBool(configuration, FileEnabledKey) ?? true,
            FilePath = string.IsNullOrWhiteSpace(path) ? DefaultFilePath : path,
            RetainedDays = days,
            ConsoleEnabled = ReadBool(configuration, ConsoleEnabledKey),
            ApplicationInsightsConnectionString = string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim(),
        };
    }

    private static int ReadInt(IConfiguration configuration, string key, int defaultValue)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"Configuration '{key}' must be an integer, but was '{text}'.");
    }

    private static bool? ReadBool(IConfiguration configuration, string key)
    {
        var text = configuration[key];
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return bool.TryParse(text, out var value)
            ? value
            : throw new FormatException($"Configuration '{key}' must be true or false, but was '{text}'.");
    }
}
