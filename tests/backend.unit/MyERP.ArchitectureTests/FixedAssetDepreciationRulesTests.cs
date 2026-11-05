using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>FI-FA-003／006／007／005（純規則部分）：直線法月折舊排程，不涉及 SQL、來源鍵或過帳。</summary>
public sealed class FixedAssetDepreciationRulesTests
{
    private static FixedAssetDepreciationInput Card(
        decimal cost, decimal residual, int life, int year = 2026, int month = 1, int day = 15, int decimals = 2) =>
        new(cost, residual, life, new DateOnly(year, month, day), decimals);

    private static List<FixedAssetDepreciationResult> Schedule(FixedAssetDepreciationInput card)
    {
        var results = new List<FixedAssetDepreciationResult>();
        var year = card.InServiceDate.Year;
        var month = card.InServiceDate.Month;
        for (var i = 0; i < card.UsefulLifeMonths; i++)
        {
            results.Add(FixedAssetDepreciationRules.CalculateMonth(card, year, month));
            if (++month > 12) { month = 1; year++; }
        }
        return results;
    }

    [Fact]
    public void EvenStraightLineCoversExactlyTheUsefulLifeMonths()
    {
        var card = Card(1200m, 0m, 12, 2026, 1, 31);
        var first = FixedAssetDepreciationRules.CalculateMonth(card, 2026, 1);
        Assert.Equal(FixedAssetDepreciationStatus.Allowed, first.Status);
        Assert.Equal(1, first.PeriodNumber);
        Assert.Equal(100m, first.Amount);
        Assert.Equal(100m, first.AccumulatedAfter);
        Assert.Equal(1100m, first.RemainingAfter);

        var last = FixedAssetDepreciationRules.CalculateMonth(card, 2026, 12);
        Assert.Equal(FixedAssetDepreciationStatus.Allowed, last.Status);
        Assert.Equal(12, last.PeriodNumber);
        Assert.Equal(100m, last.Amount);
        Assert.Equal(1200m, last.AccumulatedAfter);
        Assert.Equal(0m, last.RemainingAfter);

        var before = FixedAssetDepreciationRules.CalculateMonth(card, 2025, 12);
        Assert.Equal(FixedAssetDepreciationStatus.BeforeInService, before.Status);
        Assert.Equal(0m, before.Amount);
        Assert.Null(before.PeriodNumber);
        var after = FixedAssetDepreciationRules.CalculateMonth(card, 2027, 1);
        Assert.Equal(FixedAssetDepreciationStatus.LifeExhausted, after.Status);
        Assert.Equal(0m, after.Amount);
    }

    [Fact]
    public void LastPeriodAbsorbsRoundingRemainderAndStopsAtResidual()
    {
        var card = Card(1000m, 100m, 7);
        var schedule = Schedule(card);
        Assert.All(schedule.Take(6), x => Assert.Equal(128.57m, x.Amount));
        Assert.Equal(128.58m, schedule[6].Amount);
        Assert.Equal(900m, schedule.Sum(x => x.Amount));
        Assert.Equal(900m, schedule[6].AccumulatedAfter);
        Assert.Equal(0m, schedule[6].RemainingAfter);
        Assert.All(schedule, x => Assert.Equal(FixedAssetDepreciationStatus.Allowed, x.Status));
    }

    [Fact]
    public void MidpointRoundsAwayFromZeroAndLastPeriodTakesTheDifference()
    {
        var schedule = Schedule(Card(5m, 0m, 2, decimals: 0));
        Assert.Equal([3m, 2m], schedule.Select(x => x.Amount));
    }

    [Fact]
    public void RoundedUpBaseIsCappedSoCumulativeNeverExceedsDepreciableAmount()
    {
        var schedule = Schedule(Card(5m, 0m, 8, decimals: 0));
        Assert.Equal([1m, 1m, 1m, 1m, 1m, 0m, 0m, 0m], schedule.Select(x => x.Amount));
        Assert.All(schedule.Take(5), x => Assert.Equal(FixedAssetDepreciationStatus.Allowed, x.Status));
        Assert.All(schedule.Skip(5), x =>
        {
            Assert.Equal(FixedAssetDepreciationStatus.FullyDepreciated, x.Status);
            Assert.Equal(5m, x.AccumulatedAfter);
            Assert.Equal(0m, x.RemainingAfter);
        });
        Assert.All(schedule, x => Assert.True(x.Amount >= 0 && x.AccumulatedAfter <= 5m));
    }

    [Fact]
    public void BaseBelowSmallestUnitDefersEverythingToTheLastPeriod()
    {
        var schedule = Schedule(Card(1m, 0m, 3, decimals: 0));
        Assert.Equal([0m, 0m, 1m], schedule.Select(x => x.Amount));
        Assert.All(schedule.Take(2), x =>
        {
            Assert.Equal(FixedAssetDepreciationStatus.ZeroAmount, x.Status);
            Assert.Equal(1m, x.RemainingAfter);
        });
        Assert.Equal(FixedAssetDepreciationStatus.Allowed, schedule[2].Status);
        Assert.Equal(0m, schedule[2].RemainingAfter);
    }

    [Theory]
    [InlineData(2024, 2, 29, 2024, 2, 1)]
    [InlineData(2024, 2, 29, 2024, 3, 2)]
    [InlineData(2026, 12, 31, 2026, 12, 1)]
    [InlineData(2026, 12, 31, 2027, 1, 2)]
    [InlineData(2026, 1, 1, 2026, 1, 1)]
    public void FullMonthPolicyStartsInTheInServiceCalendarMonthRegardlessOfDay(
        int year, int month, int day, int periodYear, int periodMonth, int expectedPeriod)
    {
        var card = Card(120m, 0m, 12, year, month, day);
        var result = FixedAssetDepreciationRules.CalculateMonth(card, periodYear, periodMonth);
        Assert.Equal(FixedAssetDepreciationStatus.Allowed, result.Status);
        Assert.Equal(expectedPeriod, result.PeriodNumber);
        Assert.Equal(10m, result.Amount);
    }

    [Fact]
    public void MaximumLifeAndSqlMaximumCostDepreciateExactlyWithoutOverflow()
    {
        var card = Card(999999999999999.9999m, 0.0001m, 600, decimals: 4);
        var schedule = Schedule(card);
        Assert.All(schedule, x => Assert.Equal(FixedAssetDepreciationStatus.Allowed, x.Status));
        Assert.Equal(999999999999999.9998m, schedule.Sum(x => x.Amount));
        Assert.Equal(0m, schedule[^1].RemainingAfter);
    }

    [Theory]
    [InlineData("0", "0", 12, 2, FixedAssetDepreciationStatus.InvalidCost)]
    [InlineData("-1", "0", 12, 2, FixedAssetDepreciationStatus.InvalidCost)]
    [InlineData("1.001", "0", 12, 2, FixedAssetDepreciationStatus.InvalidCost)]
    [InlineData("1000000000000000", "0", 12, 4, FixedAssetDepreciationStatus.InvalidCost)]
    [InlineData("100", "-1", 12, 2, FixedAssetDepreciationStatus.InvalidResidual)]
    [InlineData("100", "100", 12, 2, FixedAssetDepreciationStatus.InvalidResidual)]
    [InlineData("100", "101", 12, 2, FixedAssetDepreciationStatus.InvalidResidual)]
    [InlineData("100", "0.5", 12, 0, FixedAssetDepreciationStatus.InvalidResidual)]
    [InlineData("100", "0", 0, 2, FixedAssetDepreciationStatus.InvalidUsefulLife)]
    [InlineData("100", "0", 601, 2, FixedAssetDepreciationStatus.InvalidUsefulLife)]
    [InlineData("100", "0", 12, -1, FixedAssetDepreciationStatus.InvalidCurrencyDecimals)]
    [InlineData("100", "0", 12, 5, FixedAssetDepreciationStatus.InvalidCurrencyDecimals)]
    public void InvalidCardIsRejectedByFieldWithoutAmount(
        string costText, string residualText, int life, int decimals, FixedAssetDepreciationStatus status)
    {
        var cost = decimal.Parse(costText, System.Globalization.CultureInfo.InvariantCulture);
        var residual = decimal.Parse(residualText, System.Globalization.CultureInfo.InvariantCulture);
        var result = FixedAssetDepreciationRules.CalculateMonth(Card(cost, residual, life, decimals: decimals), 2026, 1);
        Assert.Equal(status, result.Status);
        Assert.Null(result.PeriodNumber);
        Assert.Equal(0m, result.Amount);
        Assert.Equal(0m, result.AccumulatedAfter);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(0, 1)]
    [InlineData(10000, 1)]
    public void InvalidPeriodIsRejected(int year, int month)
    {
        var result = FixedAssetDepreciationRules.CalculateMonth(Card(100m, 0m, 12), year, month);
        Assert.Equal(FixedAssetDepreciationStatus.InvalidPeriod, result.Status);
        Assert.Equal(0m, result.Amount);
    }

    [Fact]
    public void SingleMonthLifeTakesTheWholeDepreciableAmountThenExhausts()
    {
        var card = Card(100.01m, 0.02m, 1, 2026, 3, 31);
        var only = FixedAssetDepreciationRules.CalculateMonth(card, 2026, 3);
        Assert.Equal(FixedAssetDepreciationStatus.Allowed, only.Status);
        Assert.Equal(1, only.PeriodNumber);
        Assert.Equal(99.99m, only.Amount);
        Assert.Equal(0m, only.RemainingAfter);

        var next = FixedAssetDepreciationRules.CalculateMonth(card, 2026, 4);
        Assert.Equal(FixedAssetDepreciationStatus.LifeExhausted, next.Status);
        Assert.Null(next.PeriodNumber);
        Assert.Equal(0m, next.Amount);
        Assert.Equal(99.99m, next.AccumulatedAfter);
        Assert.Equal(0m, next.RemainingAfter);
    }

    [Fact]
    public void MissingCardIsRejected()
    {
        var result = FixedAssetDepreciationRules.CalculateMonth(null, 2026, 1);
        Assert.Equal(FixedAssetDepreciationStatus.InvalidInput, result.Status);
        Assert.Equal(0m, result.Amount);
    }
}
