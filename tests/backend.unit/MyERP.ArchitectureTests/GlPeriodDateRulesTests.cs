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
    public void CandidateOverlapIgnoresItsOwnSavedRowButFindsAnotherCompanyPeriod()
    {
        var saved = new GlPeriodWindow(7, 10, new(2026, 1, 1), new(2026, 1, 31), true);
        var other = new GlPeriodWindow(8, 10, new(2026, 2, 1), new(2026, 2, 28), false);
        var rows = new[] { saved, other, saved with { Id = 9, CompanyId = 11 } };

        Assert.False(GlPeriodDateRules.HasConflictingPeriod(saved, rows));
        Assert.True(GlPeriodDateRules.HasConflictingPeriod(saved with { EndDate = new(2026, 2, 1) }, rows));
        Assert.False(GlPeriodDateRules.HasConflictingPeriod(saved with { StartDate = new(2025, 12, 31) }, rows));
        Assert.True(GlPeriodDateRules.HasConflictingPeriod(
            new GlPeriodWindow(0, 10, new(2026, 1, 31), new(2026, 2, 1), true), rows));
        Assert.False(GlPeriodDateRules.HasConflictingPeriod(
            new GlPeriodWindow(0, 10, new(2026, 3, 1), new(2026, 3, 31), true), rows));
    }

    [Fact]
    public void CandidateOverlapIgnoresStatusAndIsolatedCompanyRows()
    {
        var candidate = new GlPeriodWindow(0, 10, new(2024, 2, 29), new(2024, 2, 29), false);
        Assert.True(GlPeriodDateRules.HasConflictingPeriod(candidate,
            [new GlPeriodWindow(1, 10, new(2024, 2, 1), new(2024, 2, 29), false)]));
        Assert.False(GlPeriodDateRules.HasConflictingPeriod(candidate,
            [new GlPeriodWindow(1, 11, new(2024, 2, 1), new(2024, 2, 29), false)]));
        Assert.False(GlPeriodDateRules.HasConflictingPeriod(candidate, []));
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

    [Fact]
    public void DateChangeMustKeepEveryPostedBusinessDate()
    {
        var saved = new GlPeriodWindow(1, 10, new(2026, 1, 1), new(2026, 1, 31), true);
        var posted = new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31) };
        Assert.Equal(GlPeriodChangeStatus.Allowed,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { StartDate = new(2025, 12, 31) }, posted));
        Assert.Equal(GlPeriodChangeStatus.ExcludesPostedDate,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { StartDate = new(2026, 1, 2) }, posted));
        Assert.Equal(GlPeriodChangeStatus.ExcludesPostedDate,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { EndDate = new(2026, 1, 30) }, posted));
        Assert.Equal(GlPeriodChangeStatus.Allowed,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { StartDate = new(2026, 1, 2) }, []));
    }

    [Fact]
    public void PeriodIdentityRangeAndDeletionHaveDistinctOutcomes()
    {
        var saved = new GlPeriodWindow(1, 10, new(2026, 1, 1), new(2026, 1, 31), false);
        Assert.Equal(GlPeriodChangeStatus.InvalidRange,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { StartDate = new(2026, 2, 1) }, []));
        Assert.Equal(GlPeriodChangeStatus.IdentityMismatch,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { CompanyId = 11 }, []));
        Assert.Equal(GlPeriodChangeStatus.IdentityMismatch,
            GlPeriodDateRules.ValidateDateChange(saved, saved with { Id = 2 }, []));
        Assert.Equal(GlPeriodChangeStatus.Allowed, GlPeriodDateRules.ValidateDeletion([]));
        Assert.Equal(GlPeriodChangeStatus.ContainsPostedJournal,
            GlPeriodDateRules.ValidateDeletion([new DateOnly(2026, 1, 15)]));
    }
}
