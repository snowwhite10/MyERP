// 刻意違反規則的範例型別，只供 ArchitectureRulesSelfTests 驗證檢查邏輯能抓到違規。
// 測試組件不在正式架構檢查範圍內（SolutionAssemblies 排除測試組件）。
namespace MyERP.ArchitectureTests.Fixtures.Domain;

internal sealed class DirectClockUser
{
    public static DateTime GetTimestamp() => DateTime.Now;
}

internal sealed class LambdaClockUser
{
    public Func<DateTimeOffset> Factory { get; } = () => DateTimeOffset.UtcNow;
}

internal sealed class TimeProviderUser
{
    public static DateTimeOffset Read() => TimeProvider.System.GetUtcNow();
}

internal sealed class HttpFieldHolder
{
#pragma warning disable CS0169, IDE0051 // 欄位只作為簽章引用的範例
    private readonly System.Net.Http.HttpClient? _client;
#pragma warning restore CS0169, IDE0051
}

internal sealed class HttpBodyUser
{
    public static int Create()
    {
        using var message = new System.Net.Http.HttpRequestMessage();
        return message.Headers.Count();
    }
}

internal sealed class CleanType
{
    public DateOnly Date { get; init; }

    public static decimal Amount(decimal qty, decimal price) => qty * price;
}
