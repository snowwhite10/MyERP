using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlPeriodDateRulesTests
{
    private static DateOnly Date(string value) => DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-01-01", "2026-01-01", true)]
    [InlineData("2026-02-28", "2026-03-01", true)]
    [InlineData("2026-03-01", "2026-02-28", false)]
    public void IntervalMustBeInclusiveAndOrdered(string from, string to, bool valid)
    {
        Assert.Equal(valid, GlPeriodDateRules.IsValid(Date(from), Date(to)));
    }

    [Theory]
    [InlineData("2026-01-31", "2026-02-28", true)]
    [InlineData("2025-12-01", "2027-01-01", true)]
    [InlineData("2026-01-15", "2026-01-20", true)]
    [InlineData("2026-02-01", "2026-03-01", false)]
    [InlineData("2025-12-01", "2025-12-31", false)]
    public void SharedEndpointOverlapsButAdjacentDayDoesNot(string from, string to, bool overlaps)
    {
        var existing = new GlPeriodWindow(1, 10, new(2026, 1, 1), new(2026, 1, 31), true);
        var candidate = new GlPeriodWindow(2, 10, Date(from), Date(to), false);
        Assert.Equal(overlaps, GlPeriodDateRules.Overlaps(existing, candidate));
        Assert.False(GlPeriodDateRules.Overlaps(existing, candidate with { CompanyId = 11 }));
    }

    [Fact]
    public void SavedBusinessDateUsesExactlyOneOpenCompanyPeriod()
    {
        var periods = new[]
        {
            new GlPeriodWindow(1, 10, new(2024, 2, 1), new(2024, 2, 29), true),
            new GlPeriodWindow(2, 11, new(2024, 2, 1), new(2024, 2, 29), true)
        };
        Assert.Equal(new GlPeriodResolution(GlPeriodResolutionStatus.Found, 1),
            GlPeriodDateRules.Resolve(10, new(2024, 2, 29), periods));
        Assert.Equal(new GlPeriodResolution(GlPeriodResolutionStatus.Missing, null),
            GlPeriodDateRules.Resolve(10, new(2024, 3, 1), periods));
        Assert.Equal(new GlPeriodResolution(GlPeriodResolutionStatus.Closed, null),
            GlPeriodDateRules.Resolve(10, new(2024, 2, 29), [periods[0] with { IsOpen = false }]));
        Assert.Equal(new GlPeriodResolution(GlPeriodResolutionStatus.Ambiguous, null),
            GlPeriodDateRules.Resolve(10, new(2024, 2, 29), [periods[0], periods[0] with { Id = 3 }]));
    }
}
