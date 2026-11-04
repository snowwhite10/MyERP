using System.Data;
using Microsoft.Data.SqlClient;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// Real SQL proof of the FI credit lock protocol and snapshot (FI-CREDIT-010/014/016/018/021, BR-FI-003/006).
/// Each test uses its own company id so rows never collide.
/// </summary>
public sealed class CustomerCreditServiceTests(TestDatabase database)
{
    private static readonly DateOnly Today = new(2026, 11, 4);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CustomerCreditService Service(int lockTimeoutMilliseconds = 15_000) =>
        new(new FixedClock(new DateTime(2026, 11, 4, 9, 0, 0)), new FixedUser(7)) { LockTimeoutMilliseconds = lockTimeoutMilliseconds };

    private async Task<(SqlConnection Connection, SqlTransaction Transaction)> BeginAsync()
    {
        var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        return (connection, connection.BeginTransaction(IsolationLevel.ReadCommitted));
    }

    private async Task<int> CountKeysAsync(long companyId, long customerId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.ar_credit_key WHERE company_id = @c AND customer_id = @u", connection);
        command.Parameters.AddWithValue("@c", companyId);
        command.Parameters.AddWithValue("@u", customerId);
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task ExecuteAsync(string sql, long companyId, long customerId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@c", companyId);
        command.Parameters.AddWithValue("@u", customerId);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private Task SeedKeyAsync(long companyId, long customerId) => ExecuteAsync(
        "INSERT dbo.ar_credit_key (company_id, customer_id, created_at, created_by, version) VALUES (@c, @u, '2026-11-01', 1, 1)",
        companyId, customerId);

    private Task SeedLimitAsync(long companyId, long customerId, string from, string to, decimal amount,
        string status = "Active", string currency = "TWD") => ExecuteAsync(
        $"INSERT dbo.ar_credit_limit (company_id, customer_id, base_currency, effective_from, effective_to, amount, status, created_at, created_by, version) " +
        $"VALUES (@c, @u, '{currency}', '{from}', '{to}', {amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{status}', '2026-11-01', 1, 3)",
        companyId, customerId);

    private Task SeedReceivableAsync(long companyId, long customerId, long sourceId, decimal original, decimal open,
        string currency = "TWD") => ExecuteAsync(
        $"INSERT dbo.ar_posted_receivable (company_id, customer_id, source_type, source_id, base_currency, original_base_amount, open_base_amount, posting_date, created_at, created_by, version) " +
        $"VALUES (@c, @u, 'SD_INVOICE', {sourceId}, '{currency}', {original.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {open.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '2026-10-31', '2026-11-01', 1, 1)",
        companyId, customerId);

    [Fact]
    public async Task FirstUseRaceWaitsOnPairLockAndRollbackLetsWaiterCreateTheKey()
    {
        // FI-CREDIT-021 and FI-CREDIT-018 (rollback leaves no key).
        var companyId = TestCompanies.Next();
        var (firstConnection, first) = await BeginAsync();
        await using var _1 = firstConnection;
        await Service().AcquireCreditLockAsync(companyId, 501, first, Ct);

        var (secondConnection, second) = await BeginAsync();
        await using var _2 = secondConnection;
        var waiting = Service().AcquireCreditLockAsync(companyId, 501, second, Ct);
        await Task.Delay(1000, Ct);
        Assert.False(waiting.IsCompleted, "The second caller must wait on the pair lock before inserting a key.");

        // A different customer of the same company is independent while the first pair is held.
        var (otherConnection, other) = await BeginAsync();
        await using var _3 = otherConnection;
        await Service(2000).AcquireCreditLockAsync(companyId, 502, other, Ct);
        await other.CommitAsync(Ct);

        await first.RollbackAsync(Ct);
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await second.CommitAsync(Ct);

        Assert.Equal(1, await CountKeysAsync(companyId, 501));
        Assert.Equal(1, await CountKeysAsync(companyId, 502));
    }

    [Fact]
    public async Task FirstUseRaceAfterCommitReusesTheSingleKey()
    {
        // FI-CREDIT-021: exactly one committed key survives; no duplicate-key error reaches the waiter.
        var companyId = TestCompanies.Next();
        var (firstConnection, first) = await BeginAsync();
        await using var _1 = firstConnection;
        await Service().AcquireCreditLockAsync(companyId, 601, first, Ct);
        var (secondConnection, second) = await BeginAsync();
        await using var _2 = secondConnection;
        var waiting = Service().AcquireCreditLockAsync(companyId, 601, second, Ct);
        await Task.Delay(500, Ct);
        Assert.False(waiting.IsCompleted);

        await first.CommitAsync(Ct);
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await second.CommitAsync(Ct);

        Assert.Equal(1, await CountKeysAsync(companyId, 601));
    }

    [Fact]
    public async Task LockTimeoutIsNamedRetryableConflictAndCreatesNothing()
    {
        // FI-CREDIT-010: the loser never gets a snapshot; a negative applock result is a named 409.
        var companyId = TestCompanies.Next();
        var (firstConnection, first) = await BeginAsync();
        await using var _1 = firstConnection;
        await Service().AcquireCreditLockAsync(companyId, 701, first, Ct);

        var (secondConnection, second) = await BeginAsync();
        await using var _2 = secondConnection;
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Service(300).AcquireCreditLockAsync(companyId, 701, second, Ct));
        Assert.Equal(FinanceModuleContracts.CreditLockTimeout, error.ErrorCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().GetSnapshotAsync(companyId, 701, Today, second, Ct));
        await second.RollbackAsync(Ct);

        await first.RollbackAsync(Ct);
        Assert.Equal(0, await CountKeysAsync(companyId, 701));
    }

    [Fact]
    public async Task SnapshotRequiresLockHeldInSameTransaction()
    {
        // FI-CREDIT-016: reading credit without the pair lock fails closed.
        var companyId = TestCompanies.Next();
        await SeedKeyAsync(companyId, 801);
        await SeedLimitAsync(companyId, 801, "2026-01-01", "2026-12-31", 1000m);
        var (connection, transaction) = await BeginAsync();
        await using var _ = connection;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().GetSnapshotAsync(companyId, 801, Today, transaction, Ct));
        await transaction.RollbackAsync(Ct);
    }

    [Fact]
    public async Task SnapshotReturnsActiveLimitAndOnlyOpenPostedExposure()
    {
        // FI-CREDIT-001/002/014: active dated limit, committed open AR only, version and observation time.
        var companyId = TestCompanies.Next();
        await SeedKeyAsync(companyId, 901);
        await SeedLimitAsync(companyId, 901, "2026-01-01", "2026-11-04", 50_000m);
        await SeedLimitAsync(companyId, 901, "2026-11-05", "2026-12-31", 1m);
        await SeedLimitAsync(companyId, 901, "2026-11-01", "2026-11-30", 9m, "Deactivated");
        await SeedReceivableAsync(companyId, 901, 1, 1200.5m, 700.25m);
        await SeedReceivableAsync(companyId, 901, 2, 300m, 0m);
        await SeedReceivableAsync(companyId, 901, 3, 0.0001m, 0.0001m);

        var (connection, transaction) = await BeginAsync();
        await using var _ = connection;
        var service = Service();
        await service.AcquireCreditLockAsync(companyId, 901, transaction, Ct);
        var snapshot = await service.GetSnapshotAsync(companyId, 901, Today, transaction, Ct);
        await transaction.CommitAsync(Ct);

        Assert.True(snapshot.IsAvailable);
        Assert.Equal("TWD", snapshot.BaseCurrency);
        Assert.Equal(50_000m, snapshot.Limit);
        Assert.Equal(700.2501m, snapshot.PostedOpenAr);
        Assert.Equal(3, snapshot.LimitVersion);
        Assert.Equal(Today, snapshot.EvaluationDate);
        Assert.Equal(new DateTime(2026, 11, 4, 9, 0, 0), snapshot.ObservedAt);
        Assert.Equal(1, await CountKeysAsync(companyId, 901));
    }

    [Fact]
    public async Task ZeroLimitIsAvailableButMissingOrDeactivatedLimitIsNot()
    {
        // FI-CREDIT-004/014/016: zero is a valid limit; absence/deactivation is never represented as zero.
        var companyId = TestCompanies.Next();
        await SeedKeyAsync(companyId, 1001);
        await SeedLimitAsync(companyId, 1001, "2026-11-04", "2026-11-04", 0m);
        await SeedKeyAsync(companyId, 1002);
        await SeedLimitAsync(companyId, 1002, "2026-01-01", "2026-12-31", 500m, "Deactivated");

        var (connection, transaction) = await BeginAsync();
        await using var _ = connection;
        var service = Service();
        await service.AcquireCreditLockAsync(companyId, 1001, transaction, Ct);
        await service.AcquireCreditLockAsync(companyId, 1002, transaction, Ct);
        await service.AcquireCreditLockAsync(companyId, 1003, transaction, Ct);
        var zero = await service.GetSnapshotAsync(companyId, 1001, Today, transaction, Ct);
        var deactivated = await service.GetSnapshotAsync(companyId, 1002, Today, transaction, Ct);
        var unseen = await service.GetSnapshotAsync(companyId, 1003, Today, transaction, Ct);
        var expired = await service.GetSnapshotAsync(companyId, 1001, Today.AddDays(1), transaction, Ct);
        await transaction.RollbackAsync(Ct);

        Assert.True(zero.IsAvailable);
        Assert.Equal(0m, zero.Limit);
        Assert.Equal(CreditUnavailableReason.MissingLimit, deactivated.UnavailableReason);
        Assert.Equal(CreditUnavailableReason.MissingLimit, unseen.UnavailableReason);
        Assert.Equal(CreditUnavailableReason.MissingLimit, expired.UnavailableReason);
        Assert.Null(deactivated.Limit);
        Assert.Equal(0, await CountKeysAsync(companyId, 1003));
    }

    [Fact]
    public async Task OverlappingActiveLimitsAndForeignCurrencyExposureFailClosed()
    {
        // FI-CREDIT-016: ambiguous limit and currency-mismatched exposure have distinct reasons.
        var companyId = TestCompanies.Next();
        await SeedKeyAsync(companyId, 1101);
        await SeedLimitAsync(companyId, 1101, "2026-01-01", "2026-11-04", 100m);
        await SeedLimitAsync(companyId, 1101, "2026-11-04", "2026-12-31", 200m);
        await SeedKeyAsync(companyId, 1102);
        await SeedLimitAsync(companyId, 1102, "2026-01-01", "2026-12-31", 100m);
        await SeedReceivableAsync(companyId, 1102, 11, 10m, 10m, "HKD");

        var (connection, transaction) = await BeginAsync();
        await using var _ = connection;
        var service = Service();
        await service.AcquireCreditLockAsync(companyId, 1101, transaction, Ct);
        await service.AcquireCreditLockAsync(companyId, 1102, transaction, Ct);
        var ambiguous = await service.GetSnapshotAsync(companyId, 1101, Today, transaction, Ct);
        var foreign = await service.GetSnapshotAsync(companyId, 1102, Today, transaction, Ct);
        await transaction.RollbackAsync(Ct);

        Assert.Equal(CreditUnavailableReason.AmbiguousLimit, ambiguous.UnavailableReason);
        Assert.Equal(CreditUnavailableReason.ExposureUnavailable, foreign.UnavailableReason);
    }

    [Fact]
    public async Task SqlConstraintsRejectInvalidRowsAndOrphans()
    {
        // FI-CREDIT-004/005: reversed interval, negative amount, open over original and missing key are rejected by SQL.
        var companyId = TestCompanies.Next();
        await SeedKeyAsync(companyId, 1201);
        await Assert.ThrowsAsync<SqlException>(() => SeedLimitAsync(companyId, 1201, "2026-12-31", "2026-01-01", 1m));
        await Assert.ThrowsAsync<SqlException>(() => SeedLimitAsync(companyId, 1201, "2026-01-01", "2026-12-31", -0.0001m));
        await Assert.ThrowsAsync<SqlException>(() => SeedReceivableAsync(companyId, 1201, 21, 10m, 10.0001m));
        await Assert.ThrowsAsync<SqlException>(() => SeedLimitAsync(companyId, 1299, "2026-01-01", "2026-12-31", 1m));
        await SeedReceivableAsync(companyId, 1201, 22, 10m, 10m);
        await Assert.ThrowsAsync<SqlException>(() => SeedReceivableAsync(companyId, 1201, 22, 5m, 5m));
        await Assert.ThrowsAsync<SqlException>(() => SeedKeyAsync(companyId, 1201));
    }

    [Fact]
    public async Task InvalidArgumentsAreRejectedBeforeSql()
    {
        var (connection, transaction) = await BeginAsync();
        await using var _ = connection;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().AcquireCreditLockAsync(0, 1, transaction, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().AcquireCreditLockAsync(1, -1, transaction, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().AcquireCreditLockAsync(1, 1, null!, Ct));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().GetSnapshotAsync(1, 1, default, transaction, Ct));
        await transaction.RollbackAsync(Ct);
    }
}
