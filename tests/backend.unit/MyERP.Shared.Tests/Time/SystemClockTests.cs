using MyERP.Shared.Time;

namespace MyERP.Shared.Tests.Time;

public sealed class SystemClockTests
{
    private static readonly TimeSpan Utc8 = TimeSpan.FromHours(8);

    [Fact]
    public void Now_ConvertsUtcToConfiguredOffset()
    {
        var clock = new SystemClock(new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 3, 27, 0, TimeSpan.Zero)), Utc8);

        clock.Now.Should().Be(new DateTimeOffset(2026, 10, 10, 11, 27, 0, Utc8));
        clock.Now.Offset.Should().Be(Utc8);
    }

    [Fact]
    public void Today_UsesLocalDateNotUtcDate()
    {
        // UTC 10-09 16:30 = UTC+8 10-10 00:30：業務日期必須是 10-10，不能是 UTC 的 10-09
        var clock = new SystemClock(new FixedTimeProvider(new DateTimeOffset(2026, 10, 9, 16, 30, 0, TimeSpan.Zero)), Utc8);

        clock.Today.Should().Be(new DateOnly(2026, 10, 10));
    }

    [Fact]
    public void LocalNow_IsWallClockDateTimeWithoutOffset()
    {
        var clock = new SystemClock(new FixedTimeProvider(new DateTimeOffset(2026, 10, 10, 3, 27, 0, TimeSpan.Zero)), Utc8);

        clock.LocalNow.Should().Be(new DateTime(2026, 10, 10, 11, 27, 0));
        clock.LocalNow.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Fact]
    public void Offset_IsExposed()
    {
        new SystemClock(TimeProvider.System, Utc8).Offset.Should().Be(Utc8);
    }

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        var act = () => new SystemClock(null!, Utc8);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(15)]
    [InlineData(-15)]
    public void Constructor_OffsetOutOfRange_Throws(int hours)
    {
        var act = () => new SystemClock(TimeProvider.System, TimeSpan.FromHours(hours));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_OffsetNotWholeMinutes_Throws()
    {
        var act = () => new SystemClock(TimeProvider.System, TimeSpan.FromSeconds(30));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("+08:00", 8, 0)]
    [InlineData("08:00", 8, 0)]
    [InlineData("-05:30", -5, -30)]
    [InlineData("+00:00", 0, 0)]
    [InlineData(" +09:00 ", 9, 0)]
    public void ParseOffset_AcceptsSignedHoursMinutes(string text, int hours, int minutes)
    {
        SystemClock.ParseOffset(text).Should().Be(new TimeSpan(hours, minutes, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UTC+8")]
    [InlineData("8")]
    [InlineData("+25:00")]
    [InlineData("+08:00:30")]
    public void ParseOffset_RejectsInvalidText(string? text)
    {
        var act = () => SystemClock.ParseOffset(text);

        act.Should().Throw<FormatException>();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
