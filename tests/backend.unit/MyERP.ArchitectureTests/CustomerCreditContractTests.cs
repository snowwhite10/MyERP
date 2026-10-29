using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

public sealed class CustomerCreditContractTests
{
    [Fact]
    public void AvailableSnapshotRejectsInvalidFinancialData()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditSnapshot.Available(1, 1, "TWD", -1m, 0m, new DateOnly(2026, 10, 29), 1, DateTime.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditSnapshot.Available(1, 1, "TWD", 0m, -1m, new DateOnly(2026, 10, 29), 1, DateTime.UnixEpoch));
        Assert.Throws<ArgumentException>(() => CreditSnapshot.Available(1, 1, "US", 0m, 0m, new DateOnly(2026, 10, 29), 1, DateTime.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditSnapshot.Available(1, 0, "TWD", 0m, 0m, new DateOnly(2026, 10, 29), 1, DateTime.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditSnapshot.Available(1, 1, "TWD", 0.00001m, 0m, new DateOnly(2026, 10, 29), 1, DateTime.UnixEpoch));
    }

    [Fact]
    public void ZeroLimitIsAvailableButMissingLimitIsUnavailable()
    {
        var today = new DateOnly(2026, 10, 29);
        var zero = CreditSnapshot.Available(1, 2, "TWD", 0m, 0m, today, 1, DateTime.UnixEpoch);
        var missing = CreditSnapshot.Unavailable(1, 2, today, CreditUnavailableReason.MissingLimit);

        Assert.True(zero.IsAvailable);
        Assert.Equal(0m, zero.Limit);
        Assert.False(missing.IsAvailable);
        Assert.Null(missing.Limit);
        Assert.Equal(CreditUnavailableReason.MissingLimit, missing.UnavailableReason);
    }
}
