using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlPeriodWriteRulesTests
{
    private static readonly GlPeriodWindow January = new(7, 10, new(2026, 1, 1), new(2026, 1, 31), true);
    private static readonly GlPeriodWindow February = new(8, 10, new(2026, 2, 1), new(2026, 2, 28), false);

    [Fact]
    public void CreateChecksRangeAndAllSavedPeriodsIncludingClosed()
    {
        Assert.Equal(GlPeriodWriteStatus.Allowed, GlPeriodWriteRules.Create(
            new(0, 10, new(2026, 3, 1), new(2026, 3, 31), true), [January, February]));
        Assert.Equal(GlPeriodWriteStatus.InvalidRange, GlPeriodWriteRules.Create(
            new(0, 10, new(2026, 3, 2), new(2026, 3, 1), true), [January]));
        Assert.Equal(GlPeriodWriteStatus.Overlap, GlPeriodWriteRules.Create(
            new(0, 10, new(2026, 2, 28), new(2026, 3, 1), true), [January, February]));
        Assert.Equal(GlPeriodWriteStatus.Allowed, GlPeriodWriteRules.Create(
            new(0, 11, new(2026, 2, 28), new(2026, 3, 1), true), [January, February]));
    }

    [Fact]
    public void CreateCannotSupplyPersistedIdentityToBypassOverlap()
    {
        Assert.Equal(GlPeriodWriteStatus.IdentityMismatch, GlPeriodWriteRules.Create(
            January with { StartDate = new(2026, 1, 2) }, [January]));
        Assert.Equal(GlPeriodWriteStatus.IdentityMismatch, GlPeriodWriteRules.Create(
            new(0, 0, new(2026, 3, 1), new(2026, 3, 31), true), []));
    }

    [Fact]
    public void UpdateRejectsStaleVersionBeforeOtherDecisions()
    {
        var candidate = January with { CompanyId = 11, StartDate = new(2026, 2, 2) };
        Assert.Equal(GlPeriodWriteStatus.StaleVersion, GlPeriodWriteRules.Update(
            January, candidate, 2, 3, [January, February], [new DateOnly(2026, 1, 1)]));
        Assert.Equal(GlPeriodWriteStatus.IdentityMismatch, GlPeriodWriteRules.Update(
            January, candidate, 3, 3, [January, February], []));
    }

    [Fact]
    public void UpdateChecksPostedDatesAndOverlapUnderSameDecision()
    {
        var posted = new[] { new DateOnly(2026, 1, 1) };
        Assert.Equal(GlPeriodWriteStatus.ExcludesPostedDate, GlPeriodWriteRules.Update(
            January, January with { StartDate = new(2026, 1, 2) }, 3, 3, [January, February], posted));
        Assert.Equal(GlPeriodWriteStatus.Overlap, GlPeriodWriteRules.Update(
            January, January with { EndDate = new(2026, 2, 1) }, 3, 3, [January, February], posted));
        Assert.Equal(GlPeriodWriteStatus.Allowed, GlPeriodWriteRules.Update(
            January, January with { StartDate = new(2025, 12, 31), IsOpen = false }, 3, 3, [January, February], posted));
    }

    [Fact]
    public void DeleteRejectsStaleVersionAndPostedReferences()
    {
        Assert.Equal(GlPeriodWriteStatus.StaleVersion, GlPeriodWriteRules.Delete(1, 2, []));
        Assert.Equal(GlPeriodWriteStatus.ContainsPostedJournal, GlPeriodWriteRules.Delete(
            2, 2, [new DateOnly(2026, 1, 31)]));
        Assert.Equal(GlPeriodWriteStatus.Allowed, GlPeriodWriteRules.Delete(2, 2, []));
    }
}
