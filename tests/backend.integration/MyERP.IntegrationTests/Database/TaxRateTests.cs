using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class TaxRateTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new TaxDbModel(), new CurrencyDbModel()], company);
    }

    [Fact]
    public async Task TaxCodesAreScopedAndRatesHaveNonoverlappingPeriods()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(31000000, 32000000);
        await using var db = Context(companyId);
        await using var other = Context(companyId + 1);
        var codes = new TaxCodeService(db);
        var rates = new TaxRateService(db);
        var code = await codes.CreateAsync(" vat ", "VAT", "Both", token);
        Assert.Equal(("VAT", "Both"), (code.Code, code.TaxType));
        Assert.Equal(0, (await new TaxCodeService(other).ListAsync(new PagedRequest(), token)).TotalCount);
        Assert.Equal("MD.TAX_CODE.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => codes.CreateAsync("VAT", "Again", "Output", token))).ErrorCode);
        Assert.Equal("taxType", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => codes.CreateAsync("BAD", "Bad", "Unknown", token))).Errors).Key);
        var first = await rates.CreateAsync(code.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), 5m, token);
        await rates.CreateAsync(code.Id, new DateOnly(2026, 7, 1), null, 6.5m, token);
        var todayRates = await codes.RatesOnAsync([code.Id], new DateOnly(2026, 7, 1), token);
        Assert.Equal(6.5m, todayRates[code.Id]);
        Assert.Empty(await codes.RatesOnAsync([code.Id], new DateOnly(2025, 1, 1), token));
        Assert.Equal("MD.TAX_RATE.OVERLAP", (await Assert.ThrowsAsync<ConflictException>(() =>
            rates.CreateAsync(code.Id, new DateOnly(2026, 6, 30), null, 5m, token))).ErrorCode);
        Assert.Equal("ratePercent", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(() =>
            rates.CreateAsync(code.Id, new DateOnly(2025, 1, 1), null, 5.12345m, token))).Errors).Key);
        Assert.Equal("MD.TAX_CODE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            codes.DeleteAsync(code.Id, code.Version, token))).ErrorCode);
        Assert.Equal(1, (await rates.ListAsync(code.Id, new PagedRequest(), new DateOnly(2026, 1, 1), token)).TotalCount);
        Assert.Equal("MD.TAX_CODE.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            new TaxRateService(other).ListAsync(code.Id, new PagedRequest(), null, token))).ErrorCode);
        await rates.DeleteAsync(code.Id, first.Id, first.Version, token);
        await rates.CreateAsync(code.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), 0m, token);
    }

    [Fact]
    public async Task LookupChecksDirectionAndCalculatesUsingCurrencyPrecision()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(32000000, 33000000));
        var code = await new TaxCodeService(db).CreateAsync("SALE", "Sales", "Output", token);
        await new TaxRateService(db).CreateAsync(code.Id, new DateOnly(2026, 1, 1), null, 5m, token);
        await new CurrencyService(db).CreateAsync("TWD", "Taiwan Dollar", null, 0, true, token);
        var lookup = new TaxRateLookup(db, new CurrencyLookup(db));
        Assert.Equal(5m, await lookup.GetRatePercentAsync("sale", TaxDirection.Output, new DateOnly(2026, 2, 1), token));
        Assert.Equal("MD.TAX_RATE.DIRECTION_MISMATCH", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            lookup.GetRatePercentAsync("SALE", TaxDirection.Input, new DateOnly(2026, 2, 1), token))).ErrorCode);
        Assert.Equal("MD.TAX_RATE.NOT_FOUND", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            lookup.GetRatePercentAsync("SALE", TaxDirection.Output, new DateOnly(2025, 1, 1), token))).ErrorCode);
        var amount = await lookup.CalculateAsync("SALE", TaxDirection.Output, new DateOnly(2026, 2, 1), 105m, true, "TWD", token);
        Assert.Equal((5m, 100m, 5m, 105m), (amount.RatePercent, amount.Net, amount.Tax, amount.Gross));
    }

    [Fact]
    public async Task RatesCanBeEditedButInactiveCodesCannotReceiveNewRates()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(33000000, 34000000));
        var codes = new TaxCodeService(db);
        var rates = new TaxRateService(db);
        var code = await codes.CreateAsync("INPUT", "Input tax", "Input", token);
        var first = await rates.CreateAsync(code.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), 5m, token);
        await rates.CreateAsync(code.Id, new DateOnly(2026, 4, 1), null, 6m, token);
        var overlap = await Assert.ThrowsAsync<ConflictException>(() => rates.UpdateAsync(code.Id, first.Id,
            first.EffectiveFrom, new DateOnly(2026, 4, 1), 5m, first.Version, token));
        Assert.Equal("MD.TAX_RATE.OVERLAP", overlap.ErrorCode);
        var staleVersion = first.Version;
        var updated = await rates.UpdateAsync(code.Id, first.Id, first.EffectiveFrom,
            new DateOnly(2026, 2, 28), 0m, first.Version, token);
        await Assert.ThrowsAsync<ConflictException>(() => rates.UpdateAsync(code.Id, first.Id,
            first.EffectiveFrom, first.EffectiveTo, 5m, staleVersion, token));
        code = await codes.UpdateAsync(code.Id, code.Name, "Both", false, code.Version, token);
        Assert.Equal("MD.TAX_RATE.TAX_CODE_INACTIVE", (await Assert.ThrowsAsync<BusinessRuleException>(() =>
            rates.CreateAsync(code.Id, new DateOnly(2025, 1, 1), null, 5m, token))).ErrorCode);
        Assert.Equal(0m, await new TaxRateLookup(db, new CurrencyLookup(db))
            .GetRatePercentAsync(code.Code, TaxDirection.Output, new DateOnly(2026, 1, 15), token));
        Assert.Equal(0m, updated.RatePercent);
    }

    [Fact]
    public async Task TaxTablesHaveCompanyForeignKeyAndDatabaseChecks()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var fk = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_tax_rate')").SingleAsync(token);
        Assert.Equal("fk_md_tax_rate_md_tax_code_company_id_tax_code_id", fk);
        var index = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('md_tax_rate') AND is_unique = 1 AND is_primary_key = 0")
            .SingleAsync(token);
        Assert.Equal("ux_md_tax_rate_company_id_tax_code_id_effective_from", index);
        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('md_tax_code'), OBJECT_ID('md_tax_rate'))")
            .ToListAsync(token);
        Assert.Contains("ck_md_tax_code_tax_type", checks);
        Assert.Contains("ck_md_tax_rate_rate_percent", checks);
        Assert.Contains("ck_md_tax_rate_period", checks);
    }
}
