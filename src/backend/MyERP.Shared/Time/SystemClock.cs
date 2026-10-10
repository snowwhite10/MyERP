using System.Globalization;
using System.Text.RegularExpressions;

namespace MyERP.Shared.Time;

/// <summary>
/// <see cref="IClock"/> 的正式實作，也是唯一允許讀取系統時間的型別
/// （ArchitectureTests 的 ClockSourceAllowlist 只放行本型別）。
/// </summary>
public sealed partial class SystemClock : IClock
{
    private static readonly TimeSpan MaxOffset = TimeSpan.FromHours(14);

    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider timeProvider, TimeSpan offset)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (offset.Duration() > MaxOffset || offset.Ticks % TimeSpan.TicksPerMinute != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be whole minutes between -14:00 and +14:00.");
        }

        _timeProvider = timeProvider;
        Offset = offset;
    }

    public TimeSpan Offset { get; }

    public DateTimeOffset Now => _timeProvider.GetUtcNow().ToOffset(Offset);

    public DateTime LocalNow => Now.DateTime;

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    /// <summary>解析 <c>+08:00</c> / <c>-05:30</c> / <c>08:00</c> 格式的時區偏移。</summary>
    /// <exception cref="FormatException">格式不正確或超出 ±14:00。</exception>
    public static TimeSpan ParseOffset(string? text)
    {
        var match = OffsetPattern().Match(text?.Trim() ?? "");
        if (!match.Success)
        {
            throw new FormatException($"Invalid time zone offset '{text}'. Expected format: +08:00.");
        }

        var hours = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var offset = new TimeSpan(hours, minutes, 0);
        if (offset > MaxOffset)
        {
            throw new FormatException($"Invalid time zone offset '{text}'. Must be between -14:00 and +14:00.");
        }

        return match.Groups["sign"].Value == "-" ? offset.Negate() : offset;
    }

    [GeneratedRegex(@"^(?<sign>[+-]?)(?<h>\d{2}):(?<m>[0-5]\d)$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();
}
