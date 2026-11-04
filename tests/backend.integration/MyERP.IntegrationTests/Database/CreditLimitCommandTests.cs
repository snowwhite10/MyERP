using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>
/// Real SQL proof of the FI credit-limit write path (WU-FI-001b1): lock first, then inclusive-overlap, version,
/// history, MD customer and company base-currency checks (FI-CREDIT-004/005/007/008/009/011/015, BR-FI-004).
/// </summary>
public sealed class CreditLimitCommandTests(TestDatabase database)
{
    private const long InactiveCustomer = 99;
    private static readonly DateTime Now = new(2026, 11, 4, 9, 0, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreditLimitInput Input(long customer, string from, string to, decimal amount) =>
        new(customer, DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture), amount);

    [Fact]
    public async Task CreateUsesServerBaseCurrencyAndAdjacentPeriodsCoexist()
    {
        // FI-CREDIT-001/005/007: base currency comes from MD, never the client; adjacent inclusive periods work.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var service = Service(db, company);

        var first = await service.CreateAsync(Input(10, "2026-01-01", "2026-11-04", 50_000m), Ct);
        var second = await service.CreateAsync(Input(10, "2026-11-05", "2026-12-31", 0m), Ct);

        Assert.Equal("TWD", first.BaseCurrency);
        Assert.Equal(CreditLimitStatus.Active, first.Status);
        Assert.Equal(1, first.Version);
        Assert.Equal(0m, second.Amount);
        Assert.Equal(2, await CountAsync(company, "ar_credit_limit"));
        Assert.Equal(1, await CountAsync(company, "ar_credit_key"));
    }

    [Theory]
    [InlineData("2026-11-04", "2026-11-30")] // touches the end of the existing period
    [InlineData("2026-01-01", "2026-01-01")] // touches its start
    [InlineData("2025-12-01", "2027-01-31")] // encloses it
    [InlineData("2026-03-01", "2026-03-31")] // inside it
    public async Task OverlappingActivePeriodIsConflictAndChangesNothing(string from, string to)
    {
        // FI-CREDIT-007/011: inclusive endpoints overlap; the rejected write leaves only the original row.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var service = Service(db, company);
        await service.CreateAsync(Input(10, "2026-01-01", "2026-11-04", 1000m), Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(Input(10, from, to, 5m), Ct));

        Assert.Equal(CreditLimitErrors.Overlap, error.ErrorCode);
        Assert.Equal(1, await CountAsync(company, "ar_credit_limit"));
    }

    [Fact]
    public async Task OverlapIsPerCustomerAndPerCompany()
    {
        var company = TestCompanies.Next();
        var otherCompany = TestCompanies.Next();
        await using (var db = Context(company))
        {
            var service = Service(db, company);
            await service.CreateAsync(Input(10, "2026-01-01", "2026-12-31", 1m), Ct);
            await service.CreateAsync(Input(11, "2026-01-01", "2026-12-31", 2m), Ct);
        }
        await using (var db = Context(otherCompany))
            await Service(db, otherCompany).CreateAsync(Input(10, "2026-01-01", "2026-12-31", 3m), Ct);

        Assert.Equal(2, await CountAsync(company, "ar_credit_limit"));
        Assert.Equal(1, await CountAsync(otherCompany, "ar_credit_limit"));
    }

    [Fact]
    public async Task UpdateByVersionReplacesIntervalAndStaleVersionPreservesSavedRow()
    {
        // FI-CREDIT-009: the stale write is 409 and the saved limit is unchanged.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var service = Service(db, company);
        var created = await service.CreateAsync(Input(10, "2026-01-01", "2026-06-30", 100m), Ct);

        var updated = await service.UpdateAsync(created.Id, created.Version, Input(10, "2026-01-01", "2026-12-31", 250.5m), Ct);
        Assert.Equal(2, updated.Version);
        Assert.Equal(new DateOnly(2026, 12, 31), updated.EffectiveTo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(created.Id, created.Version, Input(10, "2026-01-01", "2026-12-31", 999m), Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.DeactivateAsync(created.Id, created.Version, Ct));

        var saved = await ReadAsync(company, created.Id);
        Assert.Equal(250.5m, saved.Amount);
        Assert.Equal(CreditLimitStatus.Active, saved.Status);
        Assert.Equal(2, saved.Version);
    }

    [Fact]
    public async Task UpdateCannotMoveIntoAnotherActivePeriodButMayKeepItsOwn()
    {
        // FI-CREDIT-007: self is excluded from the overlap check; another active row is not.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var service = Service(db, company);
        var first = await service.CreateAsync(Input(10, "2026-01-01", "2026-06-30", 100m), Ct);
        await service.CreateAsync(Input(10, "2026-07-01", "2026-12-31", 100m), Ct);

        var same = await service.UpdateAsync(first.Id, 1, Input(10, "2026-01-01", "2026-06-30", 120m), Ct);
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateAsync(first.Id, same.Version, Input(10, "2026-01-01", "2026-07-01", 120m), Ct));

        Assert.Equal(CreditLimitErrors.Overlap, error.ErrorCode);
        Assert.Equal(new DateOnly(2026, 6, 30), (await ReadAsync(company, first.Id)).EffectiveTo);
    }

    [Fact]
    public async Task UpdateCannotChangeCustomer()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var service = Service(db, company);
        var created = await service.CreateAsync(Input(10, "2026-01-01", "2026-06-30", 100m), Ct);

        var error = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UpdateAsync(created.Id, 1, Input(11, "2026-01-01", "2026-06-30", 100m), Ct));

        Assert.Contains("customerId", error.Errors.Keys);
        Assert.Equal(10, (await ReadAsync(company, created.Id)).CustomerId);
    }

    [Fact]
    public async Task DeactivatedHistoryIsKeptNotEditableAndDoesNotBlockANewIdentity()
    {
        // FI-CREDIT-008: deactivation keeps the row; it cannot be edited or re-deactivated, and a new interval
        // over the same dates is a new identity rather than a resurrection.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var service = Service(db, company);
        var created = await service.CreateAsync(Input(10, "2026-01-01", "2026-12-31", 100m), Ct);

        var deactivated = await service.DeactivateAsync(created.Id, created.Version, Ct);
        Assert.Equal(CreditLimitStatus.Deactivated, deactivated.Status);
        Assert.Equal(2, deactivated.Version);

        var edit = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.UpdateAsync(created.Id, 2, Input(10, "2026-01-01", "2026-12-31", 1m), Ct));
        Assert.Equal(CreditLimitErrors.NotActive, edit.ErrorCode);
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeactivateAsync(created.Id, 2, Ct));
        Assert.Equal(CreditLimitErrors.NotActive, again.ErrorCode);

        var replacement = await service.CreateAsync(Input(10, "2026-06-01", "2026-12-31", 80m), Ct);
        Assert.NotEqual(created.Id, replacement.Id);
        var history = await ReadAsync(company, created.Id);
        Assert.Equal(CreditLimitStatus.Deactivated, history.Status);
        Assert.Equal(100m, history.Amount);
    }

    [Fact]
    public async Task InactiveCustomerOrMissingBaseCurrencyBlocksWriteWithoutCreatingKey()
    {
        // FI-CREDIT-015/011: MD and SYS references fail closed with no key or limit left behind.
        var company = TestCompanies.Next();
        await using (var db = Context(company))
        {
            var inactive = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, company).CreateAsync(Input(InactiveCustomer, "2026-01-01", "2026-12-31", 1m), Ct));
            Assert.Equal("MD.CUSTOMER.NOT_APPROVED", inactive.ErrorCode);
            var noBase = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                Service(db, company, baseCurrency: null).CreateAsync(Input(10, "2026-01-01", "2026-12-31", 1m), Ct));
            Assert.Equal(CreditLimitErrors.BaseCurrencyMissing, noBase.ErrorCode);
        }
        Assert.Equal(0, await CountAsync(company, "ar_credit_key"));
        Assert.Equal(0, await CountAsync(company, "ar_credit_limit"));
    }

    [Fact]
    public async Task ChangedCompanyBaseCurrencyBlocksEditButAllowsDeactivation()
    {
        // FI-CREDIT-015: a TWD limit is never relabelled as HKD; history can still be retired.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var created = await Service(db, company).CreateAsync(Input(10, "2026-01-01", "2026-12-31", 100m), Ct);
        var hkd = Service(db, company, baseCurrency: "HKD");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            hkd.UpdateAsync(created.Id, 1, Input(10, "2026-01-01", "2026-12-31", 200m), Ct));
        Assert.Equal(CreditLimitErrors.CurrencyMismatch, error.ErrorCode);
        var deactivated = await hkd.DeactivateAsync(created.Id, 1, Ct);

        Assert.Equal("TWD", deactivated.BaseCurrency);
        Assert.Equal(100m, deactivated.Amount);
    }

    [Theory]
    [InlineData(0L, "2026-01-01", "2026-12-31", "1", "customerId")]
    [InlineData(10L, "0001-01-01", "2026-12-31", "1", "effectiveFrom")]
    [InlineData(10L, "2026-01-01", "0001-01-01", "1", "effectiveTo")]
    [InlineData(10L, "2026-12-31", "2026-01-01", "1", "effectiveTo")]
    [InlineData(10L, "2026-01-01", "2026-12-31", "-0.0001", "amount")]
    [InlineData(10L, "2026-01-01", "2026-12-31", "0.00001", "amount")]
    [InlineData(10L, "2026-01-01", "2026-12-31", "1000000000000000", "amount")]
    public async Task InvalidFieldsAreRejectedBeforeAnySqlWrite(long customer, string from, string to, string amount, string field)
    {
        // FI-CREDIT-004/005/011: field-specific 400, zero rows written.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var error = await Assert.ThrowsAsync<RequestValidationException>(() => Service(db, company).CreateAsync(
            new(customer, DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), Ct));

        Assert.Contains(field, error.Errors.Keys);
        Assert.Equal(0, await CountAsync(company, "ar_credit_key"));
    }

    [Fact]
    public async Task MaximumAmountAndLeapDaySingleDayPeriodAreValid()
    {
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var saved = await Service(db, company).CreateAsync(Input(10, "2028-02-29", "2028-02-29", 999_999_999_999_999.9999m), Ct);
        Assert.Equal(999_999_999_999_999.9999m, (await ReadAsync(company, saved.Id)).Amount);
    }

    [Fact]
    public async Task EachActionNeedsItsOwnPermissionAndCompanyMatch()
    {
        // FI-CREDIT-012/013 at service level: denied actions do not mutate; other-company IDs are 404.
        var company = TestCompanies.Next();
        await using var db = Context(company);
        var created = await Service(db, company).CreateAsync(Input(10, "2026-01-01", "2026-12-31", 100m), Ct);

        var viewOnly = Service(db, company, permissions: ["AR.CREDIT.VIEW"]);
        await Assert.ThrowsAsync<ForbiddenException>(() => viewOnly.CreateAsync(Input(10, "2027-01-01", "2027-12-31", 1m), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            viewOnly.UpdateAsync(created.Id, 1, Input(10, "2026-01-01", "2026-12-31", 1m), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => viewOnly.DeactivateAsync(created.Id, 1, Ct));
        var wrongCompany = Service(db, company, authorizedCompany: company + 1);
        await Assert.ThrowsAsync<ForbiddenException>(() => wrongCompany.CreateAsync(Input(10, "2027-01-01", "2027-12-31", 1m), Ct));

        var otherCompany = TestCompanies.Next();
        await using var otherDb = Context(otherCompany);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(otherDb, otherCompany).DeactivateAsync(created.Id, 1, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service(otherDb, otherCompany).UpdateAsync(created.Id, 1, Input(10, "2026-01-01", "2026-12-31", 1m), Ct));

        var saved = await ReadAsync(company, created.Id);
        Assert.Equal(100m, saved.Amount);
        Assert.Equal(CreditLimitStatus.Active, saved.Status);
    }

    [Fact]
    public async Task WriteWaitsForTheCreditKeyLockAndThenSeesTheCommittedOverlap()
    {
        // FI-CREDIT-007/010: the write serializes on the pair lock, so a concurrent overlapping insert committed
        // by the lock holder is visible and rejected instead of producing two active periods.
        var company = TestCompanies.Next();
        await using var holder = new SqlConnection(database.ConnectionString);
        await holder.OpenAsync(Ct);
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(IsolationLevel.ReadCommitted, Ct);
        await new CustomerCreditService(new FixedClock(Now), new FixedUser(1)).AcquireCreditLockAsync(company, 10, transaction, Ct);

        await using var db = Context(company);
        var pending = Service(db, company).CreateAsync(Input(10, "2026-06-01", "2026-06-30", 1m), Ct);
        await Task.Delay(800, Ct);
        Assert.False(pending.IsCompleted, "The write must wait for the credit key lock.");

        await using (var insert = new SqlCommand(
            "INSERT dbo.ar_credit_limit (company_id, customer_id, base_currency, effective_from, effective_to, amount, status, created_at, created_by, version) " +
            "VALUES (@c, 10, 'TWD', '2026-01-01', '2026-12-31', 5, 'Active', '2026-11-04', 1, 1)", holder, transaction))
        {
            insert.Parameters.AddWithValue("@c", company);
            await insert.ExecuteNonQueryAsync(Ct);
        }
        await transaction.CommitAsync(Ct);

        var error = await Assert.ThrowsAsync<ConflictException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15), Ct));
        Assert.Equal(CreditLimitErrors.Overlap, error.ErrorCode);
        Assert.Equal(1, await CountAsync(company, "ar_credit_limit"));
    }

    private MyErpDbContext Context(long company)
    {
        var current = new FixedCompany(company);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), current);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CustomerCreditDbModel()], current);
    }

    private static CreditLimitCommandService Service(MyErpDbContext db, long company, string? baseCurrency = "TWD",
        string[]? permissions = null, long? authorizedCompany = null)
    {
        permissions ??= ["AR.CREDIT.VIEW", "AR.CREDIT.CREATE", "AR.CREDIT.EDIT", "AR.CREDIT.DEACTIVATE"];
        var authorization = new UserCompanyAuthorization(7, authorizedCompany ?? company, null, null, false,
            permissions.ToDictionary(x => x, _ => DataScope.Company));
        return new(db, new FixedCompany(company), new Authorization(authorization),
            new CustomerCreditService(new FixedClock(Now), new FixedUser(7)) { LockTimeoutMilliseconds = 10_000 },
            new Customers(), new Currencies(baseCurrency));
    }

    private async Task<int> CountAsync(long company, string table)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM dbo.{table} WHERE company_id = @c", connection);
        command.Parameters.AddWithValue("@c", company);
        return (int)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<CreditLimit> ReadAsync(long company, long id)
    {
        await using var db = Context(company);
        return await db.Set<CreditLimit>().AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }

    private sealed class Authorization(UserCompanyAuthorization value) : ICurrentAuthorization
    {
        public Task<UserCompanyAuthorization?> GetAsync(CancellationToken token) => Task.FromResult<UserCompanyAuthorization?>(value);
    }

    private sealed class Customers : ICustomerLookup
    {
        public Task<CustomerIdentity> RequireApprovedAsync(long id, CancellationToken token = default) =>
            id == InactiveCustomer
                ? throw new BusinessRuleException("MD.CUSTOMER.NOT_APPROVED", "Customer is not approved and active.")
                : Task.FromResult(new CustomerIdentity(id, "C" + id, "Customer " + id));
    }

    private sealed class Currencies(string? baseCurrency) : ICurrencyLookup
    {
        public Task<CurrencyInfo?> GetBaseCurrencyAsync(CancellationToken token = default) =>
            Task.FromResult(baseCurrency is null ? null : new CurrencyInfo(baseCurrency, baseCurrency, null, 0, true, true));
        public Task<CurrencyInfo?> FindCurrencyAsync(string currencyCode, CancellationToken token = default) => throw new NotSupportedException();
        public Task<decimal> GetRateAsync(string currencyCode, DateOnly date, CancellationToken token = default) => throw new NotSupportedException();
        public Task<ExchangeRateSnapshot> GetRateSnapshotAsync(string currencyCode, DateOnly date, CancellationToken token = default) => throw new NotSupportedException();
        public Task<CurrencyPrecision> GetPrecisionAsync(CancellationToken token = default) => throw new NotSupportedException();
    }
}
