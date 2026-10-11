using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

/// <summary>幣別與匯率（ADR-037）：ISO 代碼、本位幣唯一與鎖定、匯率期間不重疊、查詢契約。</summary>
public sealed class CurrencyTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CurrencyDbModel()], company);
    }

    private static long NewCompany(long from) => Random.Shared.NextInt64(from, from + 1000000);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public async Task CurrencyUsesIsoCodesAndIsCompanyScoped()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany(26000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var service = new CurrencyService(db);
        var other = new CurrencyService(otherDb);

        var usd = await service.CreateAsync(" usd ", " US Dollar ", " $ ", 2, false, token);
        Assert.Equal(("USD", "US Dollar", "$", 2, false, true, 7L),
            (usd.Code, usd.Name, usd.Symbol, usd.DecimalPlaces, usd.IsBase, usd.IsActive, usd.CreatedBy));
        var jpy = await service.CreateAsync("JPY", "Yen", "", 0, false, token);
        Assert.Null(jpy.Symbol);

        foreach (var bad in new[] { "US", "USDX", "U5D", "US-", "" })
            Assert.Equal("code", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => service.CreateAsync(bad, "Bad", null, 2, false, token))).Errors).Key);
        Assert.Equal("decimalPlaces", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("EUR", "Euro", null, 5, false, token))).Errors).Key);
        Assert.Equal("decimalPlaces", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("EUR", "Euro", null, -1, false, token))).Errors).Key);
        Assert.Equal("symbol", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("EUR", "Euro", new string('€', 11), 2, false, token))).Errors).Key);
        Assert.Equal("name", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => service.CreateAsync("EUR", " ", null, 2, false, token))).Errors).Key);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync("usd", "Again", null, 2, false, token));
        Assert.Equal("MD.CURRENCY.DUPLICATE_CODE", duplicate.ErrorCode);
        Assert.Equal(0, (await other.ListAsync(new PagedRequest { Keyword = "USD" }, token)).TotalCount);
        Assert.NotEqual(usd.Id, (await other.CreateAsync("USD", "US Dollar", "US$", 2, false, token)).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => other.DeleteAsync(usd.Id, usd.Version, token));

        var page = await service.ListAsync(new PagedRequest { Sort = "-code" }, token);
        Assert.Equal(["USD", "JPY"], page.Items.Select(x => x.Code));

        var stale = usd.Version;
        var updated = await service.UpdateAsync(usd.Id, "Dollar", "US$", 3, false, false, usd.Version, token);
        Assert.Equal(("Dollar", "US$", 3, false), (updated.Name, updated.Symbol, updated.DecimalPlaces, updated.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(usd.Id, "Stale", null, 2, false, true, stale, token));

        await service.DeleteAsync(jpy.Id, jpy.Version, token);
        var reused = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync("JPY", "Yen", null, 0, false, token));
        Assert.Equal("MD.CURRENCY.DUPLICATE_CODE", reused.ErrorCode);

        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new CurrencyService(noCompany).ListAsync(new PagedRequest(), token));
        await Assert.ThrowsAsync<ForbiddenException>(() => new ExchangeRateService(noCompany).ListAsync(1, new PagedRequest(), token));
    }

    [Fact]
    public async Task BaseCurrencyIsUniqueAndLockedOnceRatesExist()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany(27000000));
        var currencies = new CurrencyService(db);
        var rates = new ExchangeRateService(db);

        var usd = await currencies.CreateAsync("USD", "US Dollar", "$", 2, false, token);
        var noBase = await Assert.ThrowsAsync<BusinessRuleException>(() => rates.CreateAsync(usd.Id, D(1, 1), null, 32m, token));
        Assert.Equal("MD.EXCHANGE_RATE.NO_BASE_CURRENCY", noBase.ErrorCode);

        var twd = await currencies.CreateAsync("TWD", "New Taiwan Dollar", "NT$", 0, true, token);
        var hkd = await currencies.CreateAsync("HKD", "Hong Kong Dollar", "HK$", 2, true, token);
        // 新本位幣取代舊本位幣。
        var all = await currencies.ListAsync(new PagedRequest(), token);
        Assert.Equal(["HKD"], all.Items.Where(x => x.IsBase).Select(x => x.Code));
        twd = all.Items.Single(x => x.Code == "TWD");
        twd = await currencies.UpdateAsync(twd.Id, twd.Name, twd.Symbol, 0, true, true, twd.Version, token);
        hkd = (await currencies.ListAsync(new PagedRequest { Keyword = "HKD" }, token)).Items.Single();
        Assert.Equal((true, false), (twd.IsBase, hkd.IsBase));

        // 不可直接取消、停用或刪除本位幣。
        var required = await Assert.ThrowsAsync<BusinessRuleException>(
            () => currencies.UpdateAsync(twd.Id, twd.Name, twd.Symbol, 0, false, true, twd.Version, token));
        Assert.Equal("MD.CURRENCY.BASE_REQUIRED", required.ErrorCode);
        var inactive = await Assert.ThrowsAsync<BusinessRuleException>(
            () => currencies.UpdateAsync(twd.Id, twd.Name, twd.Symbol, 0, true, false, twd.Version, token));
        Assert.Equal("MD.CURRENCY.BASE_INACTIVE", inactive.ErrorCode);
        var isBase = await Assert.ThrowsAsync<ConflictException>(() => currencies.DeleteAsync(twd.Id, twd.Version, token));
        Assert.Equal("MD.CURRENCY.IS_BASE", isBase.ErrorCode);
        // 停用的幣別不可設為本位幣。
        var inactiveCandidate = await Assert.ThrowsAsync<BusinessRuleException>(
            () => currencies.UpdateAsync(hkd.Id, hkd.Name, hkd.Symbol, 2, true, false, hkd.Version, token));
        Assert.Equal("MD.CURRENCY.BASE_INACTIVE", inactiveCandidate.ErrorCode);

        // 本位幣不可設匯率；有匯率後不可更換本位幣，有匯率的幣別不可刪除。
        var baseRate = await Assert.ThrowsAsync<BusinessRuleException>(() => rates.CreateAsync(twd.Id, D(1, 1), null, 1m, token));
        Assert.Equal("MD.EXCHANGE_RATE.BASE_CURRENCY", baseRate.ErrorCode);
        await rates.CreateAsync(usd.Id, D(1, 1), null, 32.15m, token);
        var locked = await Assert.ThrowsAsync<ConflictException>(
            () => currencies.UpdateAsync(hkd.Id, hkd.Name, hkd.Symbol, 2, true, true, hkd.Version, token));
        Assert.Equal("MD.CURRENCY.BASE_LOCKED", locked.ErrorCode);
        var lockedCreate = await Assert.ThrowsAsync<ConflictException>(() => currencies.CreateAsync("CNY", "Yuan", "¥", 2, true, token));
        Assert.Equal("MD.CURRENCY.BASE_LOCKED", lockedCreate.ErrorCode);
        usd = (await currencies.ListAsync(new PagedRequest { Keyword = "USD" }, token)).Items.Single();
        var inUse = await Assert.ThrowsAsync<ConflictException>(() => currencies.DeleteAsync(usd.Id, usd.Version, token));
        Assert.Equal("MD.CURRENCY.IN_USE", inUse.ErrorCode);

        // 停用的外幣不可新增匯率。
        usd = await currencies.UpdateAsync(usd.Id, usd.Name, usd.Symbol, 2, false, false, usd.Version, token);
        var currencyInactive = await Assert.ThrowsAsync<BusinessRuleException>(() => rates.CreateAsync(usd.Id, D(6, 1), null, 31m, token));
        Assert.Equal("MD.EXCHANGE_RATE.CURRENCY_INACTIVE", currencyInactive.ErrorCode);
    }

    [Fact]
    public async Task ExchangeRatePeriodsCannotOverlapAndLookupUsesBusinessDate()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany(28000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var currencies = new CurrencyService(db);
        var rates = new ExchangeRateService(db);
        var lookup = new CurrencyLookup(db);
        Assert.Null(await lookup.GetBaseCurrencyAsync(token));

        await currencies.CreateAsync("TWD", "New Taiwan Dollar", "NT$", 0, true, token);
        var usd = await currencies.CreateAsync("USD", "US Dollar", "$", 2, false, token);
        var jpy = await currencies.CreateAsync("JPY", "Yen", "¥", 0, false, token);

        var q1 = await rates.CreateAsync(usd.Id, D(1, 1), D(3, 31), 31.5m, token);
        Assert.Equal((usd.Id, D(1, 1), D(3, 31), 31.5m, 7L), (q1.CurrencyId, q1.EffectiveFrom, q1.EffectiveTo, q1.Rate, q1.CreatedBy));
        var open = await rates.CreateAsync(usd.Id, D(4, 1), null, 32.12345678m, token);
        Assert.Equal(32.12345678m, open.Rate);
        // 其他幣別的期間互不影響。
        await rates.CreateAsync(jpy.Id, D(1, 1), null, 0.2134m, token);

        foreach (var (from, to) in new (DateOnly, DateOnly?)[] { (D(3, 31), D(3, 31)), (D(2, 1), null), (D(12, 1), D(12, 31)), (new DateOnly(2025, 12, 1), D(1, 1)) })
        {
            var overlap = await Assert.ThrowsAsync<ConflictException>(() => rates.CreateAsync(usd.Id, from, to, 30m, token));
            Assert.Equal("MD.EXCHANGE_RATE.OVERLAP", overlap.ErrorCode);
        }
        Assert.Equal("effectiveTo", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => rates.CreateAsync(usd.Id, new DateOnly(2025, 6, 2), new DateOnly(2025, 6, 1), 30m, token))).Errors).Key);
        foreach (var bad in new[] { 0m, -1m, 0.000000001m, 100000000000m })
            Assert.Equal("rate", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => rates.CreateAsync(usd.Id, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31), bad, token))).Errors).Key);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => rates.CreateAsync(999999999, D(1, 1), null, 1m, token));
        Assert.Equal("MD.CURRENCY.NOT_FOUND", missing.ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => new ExchangeRateService(otherDb).ListAsync(usd.Id, new PagedRequest(), token));

        // 列表預設按生效日新到舊；可篩選某日適用的匯率。
        var list = await rates.ListAsync(usd.Id, new PagedRequest(), token);
        Assert.Equal([D(4, 1), D(1, 1)], list.Items.Select(x => x.EffectiveFrom));
        var asc = await rates.ListAsync(usd.Id, new PagedRequest { Sort = "effectiveFrom" }, token);
        Assert.Equal([D(1, 1), D(4, 1)], asc.Items.Select(x => x.EffectiveFrom));
        var onDate = await rates.ListAsync(usd.Id, new PagedRequest(), D(2, 15), token);
        Assert.Equal(q1.Id, Assert.Single(onDate.Items).Id);

        // 查詢契約：按業務日期取匯率，本位幣回 1，代碼不分大小寫。
        Assert.Equal(31.5m, await lookup.GetRateAsync("usd", D(3, 31), token));
        Assert.Equal(32.12345678m, await lookup.GetRateAsync("USD", D(4, 1), token));
        Assert.Equal(1m, await lookup.GetRateAsync("TWD", D(4, 1), token));
        var noRate = await Assert.ThrowsAsync<BusinessRuleException>(() => lookup.GetRateAsync("USD", new DateOnly(2025, 12, 31), token));
        Assert.Equal("MD.EXCHANGE_RATE.NOT_FOUND", noRate.ErrorCode);
        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => lookup.GetRateAsync("EUR", D(4, 1), token));
        Assert.Equal("MD.CURRENCY.NOT_FOUND", unknown.ErrorCode);
        Assert.Equal(("TWD", 0, true), (await lookup.GetBaseCurrencyAsync(token)) is { } b ? (b.Code, b.DecimalPlaces, b.IsBase) : default);
        Assert.Equal("JPY", (await lookup.FindCurrencyAsync("jpy", token))?.Code);
        Assert.Null(await lookup.FindCurrencyAsync("EUR", token));
        await Assert.ThrowsAsync<NotFoundException>(() => new CurrencyLookup(otherDb).GetRateAsync("USD", D(4, 1), token));

        // 修改：期間與匯率，版本須相符，不可改成重疊；匯率不屬該幣別 404。
        var stale = q1.Version;
        var edited = await rates.UpdateAsync(usd.Id, q1.Id, D(1, 1), D(2, 28), 31.25m, q1.Version, token);
        Assert.Equal((D(2, 28), 31.25m), (edited.EffectiveTo, edited.Rate));
        await Assert.ThrowsAsync<ConflictException>(() => rates.UpdateAsync(usd.Id, q1.Id, D(1, 1), D(2, 28), 31m, stale, token));
        var overlapEdit = await Assert.ThrowsAsync<ConflictException>(
            () => rates.UpdateAsync(usd.Id, q1.Id, D(1, 1), D(4, 1), 31m, edited.Version, token));
        Assert.Equal("MD.EXCHANGE_RATE.OVERLAP", overlapEdit.ErrorCode);
        var wrongCurrency = await Assert.ThrowsAsync<NotFoundException>(
            () => rates.UpdateAsync(jpy.Id, q1.Id, D(1, 1), D(2, 28), 31m, edited.Version, token));
        Assert.Equal("MD.EXCHANGE_RATE.NOT_FOUND", wrongCurrency.ErrorCode);
        var gap = await Assert.ThrowsAsync<BusinessRuleException>(() => lookup.GetRateAsync("USD", D(3, 15), token));
        Assert.Equal("MD.EXCHANGE_RATE.NOT_FOUND", gap.ErrorCode);

        // 刪除後期間可重新使用；匯率可在停用幣別上查詢（歷史單據）。
        await rates.DeleteAsync(usd.Id, q1.Id, edited.Version, token);
        Assert.Single((await rates.ListAsync(usd.Id, new PagedRequest(), token)).Items);
        var refilled = await rates.CreateAsync(usd.Id, D(1, 1), D(3, 31), 31.75m, token);
        Assert.Equal(31.75m, await lookup.GetRateAsync("USD", D(3, 15), token));
        await Assert.ThrowsAsync<NotFoundException>(() => rates.DeleteAsync(usd.Id, q1.Id, edited.Version, token));
        usd = (await currencies.ListAsync(new PagedRequest { Keyword = "USD" }, token)).Items.Single();
        await currencies.UpdateAsync(usd.Id, usd.Name, usd.Symbol, 2, false, false, usd.Version, token);
        Assert.Equal(31.75m, await lookup.GetRateAsync("USD", D(3, 15), token));
        Assert.False((await lookup.FindCurrencyAsync("USD", token))!.IsActive);
        Assert.NotEqual(0, refilled.Id);
    }

    [Fact]
    public async Task PrecisionFollowsCurrencyMasterDecimals()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany(29000000));
        var currencies = new CurrencyService(db);
        await currencies.CreateAsync("TWD", "New Taiwan Dollar", null, 2, true, token);
        await currencies.CreateAsync("KWD", "Kuwaiti Dinar", null, 3, false, token);
        var precision = await new CurrencyLookup(db).GetPrecisionAsync(token);
        Assert.Equal((2, 3, 0, 2), (precision.DecimalsFor("TWD"), precision.DecimalsFor("KWD"), precision.DecimalsFor("JPY"), precision.DecimalsFor("EUR")));
        Assert.Equal(1.235m, Money.Round(1.2345m, "KWD", precision));
        Assert.Equal(10.13m, Money.Round(10.125m, "TWD", precision));
    }

    [Fact]
    public async Task CurrencyTablesUseCompanyScopedKeysAndChecks()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var foreignKey = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_exchange_rate')").SingleAsync(token);
        Assert.Equal("fk_md_exchange_rate_md_currency_company_id_currency_id", foreignKey);
        var unique = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id IN (OBJECT_ID('md_currency'), OBJECT_ID('md_exchange_rate')) AND is_unique = 1 AND is_primary_key = 0")
            .ToListAsync(token);
        Assert.Contains("ux_md_currency_company_id_base", unique);
        Assert.Contains("ux_md_exchange_rate_company_id_currency_id_effective_from", unique);
        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('md_currency'), OBJECT_ID('md_exchange_rate'))")
            .ToListAsync(token);
        Assert.Contains("ck_md_exchange_rate_rate", checks);
        Assert.Contains("ck_md_exchange_rate_period", checks);
        Assert.Contains("ck_md_currency_decimal_places", checks);
        var codeType = await db.Database.SqlQueryRaw<string>(
            "SELECT DATA_TYPE + '(' + CAST(CHARACTER_MAXIMUM_LENGTH AS varchar(5)) + ')' AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'md_currency' AND COLUMN_NAME = 'code'")
            .SingleAsync(token);
        Assert.Equal("char(3)", codeType);
    }
}
