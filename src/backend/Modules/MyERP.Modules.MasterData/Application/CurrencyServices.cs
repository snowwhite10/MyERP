using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

/// <summary>
/// 幣別 CRUD（ADR-037）。代碼為 ISO 4217 三碼；每間公司最多一個本位幣，設定新本位幣時自動取消舊的，
/// 公司已有匯率後不可更換。幣別與匯率的維護共用公司級 applock <c>md_currency</c>。
/// </summary>
public sealed class CurrencyService(MyErpDbContext db)
    : MasterCategoryService<Currency>(db, "MD.CURRENCY", "Currency")
{
    internal const string LockName = "md_currency";

    public async Task<Currency> CreateAsync(string? code, string? name, string? symbol, int decimalPlaces, bool isBase,
        CancellationToken token = default)
    {
        var isoCode = IsoCode(code);
        MasterDataInput.Name(name);
        var normalizedSymbol = ValidateDetails(symbol, decimalPlaces);
        var companyId = RequireCompany();
        return await CompanyLock.RunAsync(Db, LockName, companyId, async () =>
        {
            // 先檢查重複，避免取消舊本位幣後才失敗。
            if (await Db.Set<Currency>().IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Code == isoCode, token))
                throw new ConflictException(ErrorPrefix + ".DUPLICATE_CODE", "Currency code already exists in this company.");
            if (isBase) await ReleaseBaseAsync(token);
            return await CreateCoreAsync(isoCode, name, x => Apply(x, normalizedSymbol, decimalPlaces, isBase), token);
        }, token);
    }

    /// <summary>按版本修改名稱、符號、小數位、本位幣與啟用狀態；代碼不可修改。</summary>
    public async Task<Currency> UpdateAsync(long id, string? name, string? symbol, int decimalPlaces, bool isBase, bool isActive,
        int version, CancellationToken token = default)
    {
        MasterDataInput.Name(name);
        var normalizedSymbol = ValidateDetails(symbol, decimalPlaces);
        return await CompanyLock.RunAsync(Db, LockName, RequireCompany(), async () =>
        {
            var entity = await FindAsync(id, token);
            if (version < 1 || entity.Version != version) throw new ConflictException();
            if (entity.IsBase && !isBase)
                throw new BusinessRuleException(ErrorPrefix + ".BASE_REQUIRED", "Set another currency as the base currency instead.");
            if (isBase && !isActive)
                throw new BusinessRuleException(ErrorPrefix + ".BASE_INACTIVE", "The base currency must be active.");
            if (isBase && !entity.IsBase) await ReleaseBaseAsync(token);
            return await UpdateCoreAsync(id, name, isActive, version, x => Apply(x, normalizedSymbol, decimalPlaces, isBase), token);
        }, token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockName, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    protected override Currency New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<Currency>().AnyAsync(x => x.Id == id && x.IsBase, token))
            throw new ConflictException(ErrorPrefix + ".IS_BASE", "The base currency cannot be deleted.");
        if (await Db.Set<ExchangeRate>().AnyAsync(x => x.CurrencyId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Currency has exchange rates.");
        if (Db.Model.FindEntityType(typeof(FundAccount)) is not null &&
            await Db.Set<FundAccount>().AnyAsync(x => x.CurrencyId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Currency is referenced by fund accounts.");
    }

    /// <summary>ISO 4217：三個英文字母，轉大寫。</summary>
    internal static string IsoCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        if (code is not { Length: Currency.CodeLength } || !code.All(char.IsAsciiLetterUpper))
            throw new RequestValidationException("code", "Use a three-letter ISO 4217 currency code.");
        return code;
    }

    /// <summary>取消目前本位幣；公司已有匯率時不可更換（匯率都是對舊本位幣的報價）。</summary>
    private async Task ReleaseBaseAsync(CancellationToken token)
    {
        var current = await Db.Set<Currency>().SingleOrDefaultAsync(x => x.IsBase, token);
        if (current is null) return;
        if (await Db.Set<ExchangeRate>().AnyAsync(token))
            throw new ConflictException(ErrorPrefix + ".BASE_LOCKED", "The base currency cannot be changed after exchange rates exist.");
        current.IsBase = false;
        await Db.SaveChangesAsync(token);
    }

    private static string? ValidateDetails(string? symbol, int decimalPlaces)
    {
        if (decimalPlaces is < 0 or > Currency.MaxDecimalPlaces)
            throw new RequestValidationException("decimalPlaces", $"Use 0–{Currency.MaxDecimalPlaces} decimal places.");
        return MasterDataInput.OptionalText(symbol, "symbol", Currency.SymbolMaxLength);
    }

    private static void Apply(Currency entity, string? symbol, int decimalPlaces, bool isBase) =>
        (entity.Symbol, entity.DecimalPlaces, entity.IsBase) = (symbol, decimalPlaces, isBase);
}

/// <summary>外幣匯率（ADR-037）：1 外幣 = rate 本位幣；同幣別期間不可重疊，在幣別 applock 內檢查。</summary>
public sealed class ExchangeRateService(MyErpDbContext db)
{
    private const string ErrorPrefix = "MD.EXCHANGE_RATE";
    private const decimal MaxRate = 99_999_999_999.99999999m;

    public Task<PagedResult<ExchangeRate>> ListAsync(long currencyId, PagedRequest request, CancellationToken token) =>
        ListAsync(currencyId, request, null, token);

    /// <summary>某幣別的匯率，預設按生效日新到舊；<paramref name="on"/> 只列出該日適用的匯率。</summary>
    public async Task<PagedResult<ExchangeRate>> ListAsync(long currencyId, PagedRequest request, DateOnly? on,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["effectiveFrom"], "-effectiveFrom");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        await FindCurrencyAsync(currencyId, token);
        var query = db.Set<ExchangeRate>().AsNoTracking().Where(x => x.CurrencyId == currencyId);
        if (on is { } date) query = query.Where(x => x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date));
        query = sorts[0].Descending
            ? query.OrderByDescending(x => x.EffectiveFrom).ThenBy(x => x.Id)
            : query.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id);
        var count = await query.LongCountAsync(token);
        return PagedResult.Create(await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(token), request, count);
    }

    public async Task<ExchangeRate> CreateAsync(long currencyId, DateOnly effectiveFrom, DateOnly? effectiveTo, decimal rate,
        CancellationToken token = default)
    {
        var companyId = RequireCompany();
        Validate(effectiveFrom, effectiveTo, rate);
        return await CompanyLock.RunAsync(db, CurrencyService.LockName, companyId, async () =>
        {
            var currency = await FindCurrencyAsync(currencyId, token);
            if (currency.IsBase)
                throw new BusinessRuleException(ErrorPrefix + ".BASE_CURRENCY", "The base currency has no exchange rate.");
            if (!await db.Set<Currency>().AnyAsync(x => x.IsBase, token))
                throw new BusinessRuleException(ErrorPrefix + ".NO_BASE_CURRENCY", "Set the base currency before adding exchange rates.");
            if (!currency.IsActive)
                throw new BusinessRuleException(ErrorPrefix + ".CURRENCY_INACTIVE", "Exchange rates can only be added to an active currency.");
            await EnsureNoOverlapAsync(currencyId, null, effectiveFrom, effectiveTo, token);
            var entity = new ExchangeRate
            {
                CompanyId = companyId, CurrencyId = currencyId, EffectiveFrom = effectiveFrom, EffectiveTo = effectiveTo, Rate = rate,
            };
            db.Add(entity);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (MasterDataQueries.IsUniqueViolation(ex)) { throw Overlap(); }
            return entity;
        }, token);
    }

    /// <summary>按版本修改期間與匯率；幣別不可修改。</summary>
    public async Task<ExchangeRate> UpdateAsync(long currencyId, long id, DateOnly effectiveFrom, DateOnly? effectiveTo, decimal rate,
        int version, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        Validate(effectiveFrom, effectiveTo, rate);
        return await CompanyLock.RunAsync(db, CurrencyService.LockName, companyId, async () =>
        {
            var entity = await FindAsync(currencyId, id, version, token);
            await EnsureNoOverlapAsync(currencyId, id, effectiveFrom, effectiveTo, token);
            (entity.EffectiveFrom, entity.EffectiveTo, entity.Rate) = (effectiveFrom, effectiveTo, rate);
            await SaveUpdateAsync(entity, version, token);
            return entity;
        }, token);
    }

    public async Task DeleteAsync(long currencyId, long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(currencyId, id, version, token);
        entity.IsDeleted = true;
        await SaveUpdateAsync(entity, version, token);
    }

    private static void Validate(DateOnly effectiveFrom, DateOnly? effectiveTo, decimal rate)
    {
        if (effectiveTo < effectiveFrom)
            throw new RequestValidationException("effectiveTo", "The end date cannot be earlier than the start date.");
        if (rate <= 0 || rate > MaxRate || rate != Math.Round(rate, ExchangeRate.RateScale))
            throw new RequestValidationException("rate", "Use a positive rate with up to 11 integer digits and 8 decimals.");
    }

    private async Task EnsureNoOverlapAsync(long currencyId, long? excludeId, DateOnly from, DateOnly? to, CancellationToken token)
    {
        var query = db.Set<ExchangeRate>().Where(x => x.CurrencyId == currencyId && x.Id != excludeId &&
            (x.EffectiveTo == null || x.EffectiveTo >= from));
        if (to is { } end) query = query.Where(x => x.EffectiveFrom <= end);
        if (await query.AnyAsync(token)) throw Overlap();
    }

    private async Task<Currency> FindCurrencyAsync(long currencyId, CancellationToken token) =>
        await db.Set<Currency>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == currencyId, token)
        ?? throw new NotFoundException("MD.CURRENCY.NOT_FOUND", "Currency", currencyId);

    private async Task<ExchangeRate> FindAsync(long currencyId, long id, int version, CancellationToken token)
    {
        var entity = await db.Set<ExchangeRate>().SingleOrDefaultAsync(x => x.Id == id && x.CurrencyId == currencyId, token)
            ?? throw new NotFoundException(ErrorPrefix + ".NOT_FOUND", "Exchange rate", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        return entity;
    }

    private async Task SaveUpdateAsync(ExchangeRate entity, int version, CancellationToken token)
    {
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        catch (DbUpdateException ex) when (MasterDataQueries.IsUniqueViolation(ex)) { throw Overlap(); }
    }

    private long RequireCompany() => MasterDataQueries.RequireCompany(db);

    private static ConflictException Overlap() =>
        new(ErrorPrefix + ".OVERLAP", "The period overlaps another exchange rate of this currency.");
}

/// <summary><see cref="ICurrencyLookup"/> 的實作；只讀目前公司的幣別與匯率（ADR-037）。</summary>
public sealed class CurrencyLookup(MyErpDbContext db) : ICurrencyLookup
{
    public async Task<CurrencyInfo?> GetBaseCurrencyAsync(CancellationToken token = default)
    {
        RequireCompany();
        var entity = await db.Set<Currency>().AsNoTracking().SingleOrDefaultAsync(x => x.IsBase, token);
        return entity is null ? null : Map(entity);
    }

    public async Task<CurrencyInfo?> FindCurrencyAsync(string currencyCode, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(currencyCode, token);
        return entity is null ? null : Map(entity);
    }

    public async Task<decimal> GetRateAsync(string currencyCode, DateOnly date, CancellationToken token = default)
    {
        RequireCompany();
        var currency = await FindAsync(currencyCode, token)
            ?? throw new NotFoundException("MD.CURRENCY.NOT_FOUND", "Currency", currencyCode);
        if (currency.IsBase) return 1m;
        if (!await db.Set<Currency>().AnyAsync(x => x.IsBase, token))
            throw new BusinessRuleException("MD.EXCHANGE_RATE.NO_BASE_CURRENCY", "The company has no base currency.");
        var rates = await db.Set<ExchangeRate>().AsNoTracking()
            .Where(x => x.CurrencyId == currency.Id && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date))
            .Select(x => x.Rate).Take(1).ToListAsync(token);
        return rates.Count == 1 ? rates[0]
            : throw new BusinessRuleException("MD.EXCHANGE_RATE.NOT_FOUND", $"No {currency.Code} exchange rate on {date:yyyy-MM-dd}.");
    }

    public async Task<CurrencyPrecision> GetPrecisionAsync(CancellationToken token = default)
    {
        RequireCompany();
        var currencies = await db.Set<Currency>().AsNoTracking().Select(x => new { x.Code, x.DecimalPlaces }).ToListAsync(token);
        return currencies.Aggregate(CurrencyPrecision.Default, (precision, x) => precision.With(x.Code, x.DecimalPlaces));
    }

    private Task<Currency?> FindAsync(string currencyCode, CancellationToken token)
    {
        var code = currencyCode?.Trim().ToUpperInvariant();
        return db.Set<Currency>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == code, token);
    }

    private void RequireCompany()
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
    }

    private static CurrencyInfo Map(Currency x) => new(x.Code, x.Name, x.Symbol, x.DecimalPlaces, x.IsBase, x.IsActive);
}
