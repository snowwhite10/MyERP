using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Application;

public sealed class TaxCodeService(MyErpDbContext db)
    : MasterCategoryService<TaxCode>(db, "MD.TAX_CODE", "Tax code")
{
    internal const string LockName = "md_tax_code";

    /// <summary>一次取出目前頁面在指定日期的稅率，避免逐筆查詢。</summary>
    public async Task<IReadOnlyDictionary<long, decimal>> RatesOnAsync(IEnumerable<long> taxCodeIds, DateOnly on,
        CancellationToken token = default)
    {
        RequireCompany();
        var ids = taxCodeIds.ToArray();
        return await Db.Set<TaxRate>().AsNoTracking()
            .Where(x => ids.Contains(x.TaxCodeId) && x.EffectiveFrom <= on &&
                (x.EffectiveTo == null || x.EffectiveTo >= on))
            .ToDictionaryAsync(x => x.TaxCodeId, x => x.RatePercent, token);
    }

    public async Task<TaxCode> CreateAsync(string? code, string? name, string? taxType, CancellationToken token = default)
    {
        var kind = ValidateTaxType(taxType);
        return await CompanyLock.RunAsync(Db, LockName, RequireCompany(),
            () => CreateCoreAsync(code, name, x => x.TaxType = kind, token), token);
    }

    public Task<TaxCode> UpdateAsync(long id, string? name, string? taxType, bool isActive, int version,
        CancellationToken token = default)
    {
        var kind = ValidateTaxType(taxType);
        return CompanyLock.RunAsync(Db, LockName, RequireCompany(),
            () => UpdateCoreAsync(id, name, isActive, version, x => x.TaxType = kind, token), token);
    }

    public override Task DeleteAsync(long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(Db, LockName, RequireCompany(), async () =>
        {
            await base.DeleteAsync(id, version, token);
            return true;
        }, token);

    protected override TaxCode New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<TaxRate>().AnyAsync(x => x.TaxCodeId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Tax code has tax rates.");
    }

    private static string ValidateTaxType(string? value) => value switch
    {
        "Output" or "Input" or "Both" => value,
        _ => throw new RequestValidationException("taxType", "Use Output, Input or Both."),
    };
}

public sealed class TaxRateService(MyErpDbContext db)
{
    private const string ErrorPrefix = "MD.TAX_RATE";

    public async Task<PagedResult<TaxRate>> ListAsync(long taxCodeId, PagedRequest request, DateOnly? on,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        RequireCompany();
        var sorts = SortSpec.Parse(request.Sort, ["effectiveFrom"], "-effectiveFrom");
        if (sorts.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        await FindCodeAsync(taxCodeId, token);
        var query = db.Set<TaxRate>().AsNoTracking().Where(x => x.TaxCodeId == taxCodeId);
        if (on is { } date) query = query.Where(x => x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date));
        query = sorts[0].Descending ? query.OrderByDescending(x => x.EffectiveFrom).ThenBy(x => x.Id)
            : query.OrderBy(x => x.EffectiveFrom).ThenBy(x => x.Id);
        var count = await query.LongCountAsync(token);
        return PagedResult.Create(await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(token), request, count);
    }

    public Task<TaxRate> CreateAsync(long taxCodeId, DateOnly from, DateOnly? to, decimal ratePercent,
        CancellationToken token = default)
    {
        Validate(from, to, ratePercent);
        var companyId = RequireCompany();
        return CompanyLock.RunAsync(db, TaxCodeService.LockName, companyId, async () =>
        {
            var code = await FindCodeAsync(taxCodeId, token);
            if (!code.IsActive) throw new BusinessRuleException(ErrorPrefix + ".TAX_CODE_INACTIVE", "Tax code is inactive.");
            await EnsureNoOverlapAsync(taxCodeId, null, from, to, token);
            var entity = new TaxRate { CompanyId = companyId, TaxCodeId = taxCodeId,
                EffectiveFrom = from, EffectiveTo = to, RatePercent = ratePercent };
            db.Add(entity);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Overlap(); }
            return entity;
        }, token);
    }

    public Task<TaxRate> UpdateAsync(long taxCodeId, long id, DateOnly from, DateOnly? to, decimal ratePercent,
        int version, CancellationToken token = default)
    {
        Validate(from, to, ratePercent);
        return CompanyLock.RunAsync(db, TaxCodeService.LockName, RequireCompany(), async () =>
        {
            var entity = await FindAsync(taxCodeId, id, version, token);
            await EnsureNoOverlapAsync(taxCodeId, id, from, to, token);
            (entity.EffectiveFrom, entity.EffectiveTo, entity.RatePercent) = (from, to, ratePercent);
            await SaveUpdateAsync(entity, version, token);
            return entity;
        }, token);
    }

    public Task DeleteAsync(long taxCodeId, long id, int version, CancellationToken token = default) =>
        CompanyLock.RunAsync(db, TaxCodeService.LockName, RequireCompany(), async () =>
        {
            var entity = await FindAsync(taxCodeId, id, version, token);
            entity.IsDeleted = true;
            await SaveUpdateAsync(entity, version, token);
            return true;
        }, token);

    private static void Validate(DateOnly from, DateOnly? to, decimal rate)
    {
        if (to < from) throw new RequestValidationException("effectiveTo", "The end date cannot precede the start date.");
        if (rate is < 0 or > 100 || rate != Math.Round(rate, 4))
            throw new RequestValidationException("ratePercent", "Use 0–100 with up to four decimals.");
    }

    private async Task EnsureNoOverlapAsync(long codeId, long? excludeId, DateOnly from, DateOnly? to, CancellationToken token)
    {
        var query = db.Set<TaxRate>().Where(x => x.TaxCodeId == codeId && x.Id != excludeId &&
            (x.EffectiveTo == null || x.EffectiveTo >= from));
        if (to is { } end) query = query.Where(x => x.EffectiveFrom <= end);
        if (await query.AnyAsync(token)) throw Overlap();
    }

    private async Task<TaxCode> FindCodeAsync(long id, CancellationToken token) =>
        await db.Set<TaxCode>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
        ?? throw new NotFoundException("MD.TAX_CODE.NOT_FOUND", "Tax code", id);

    private async Task<TaxRate> FindAsync(long codeId, long id, int version, CancellationToken token)
    {
        var entity = await db.Set<TaxRate>().SingleOrDefaultAsync(x => x.Id == id && x.TaxCodeId == codeId, token)
            ?? throw new NotFoundException(ErrorPrefix + ".NOT_FOUND", "Tax rate", id);
        if (version < 1 || entity.Version != version) throw new ConflictException();
        return entity;
    }

    private async Task SaveUpdateAsync(TaxRate entity, int version, CancellationToken token)
    {
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { throw Overlap(); }
    }

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    private static ConflictException Overlap() => new(ErrorPrefix + ".OVERLAP", "Tax rate period overlaps another period.");
}

public sealed class TaxRateLookup(MyErpDbContext db, ICurrencyLookup currencies) : ITaxRateLookup
{
    public async Task<TaxCodeInfo?> FindTaxCodeAsync(string code, CancellationToken token = default)
    {
        RequireCompany();
        var normalized = code?.Trim().ToUpperInvariant();
        var entity = await db.Set<TaxCode>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, token);
        return entity is null ? null : new TaxCodeInfo(entity.Code, entity.Name, entity.IsActive,
            entity.TaxType is "Output" or "Both", entity.TaxType is "Input" or "Both");
    }

    public async Task<decimal> GetRatePercentAsync(string code, TaxDirection direction, DateOnly date, CancellationToken token = default)
    {
        RequireCompany();
        var normalized = code?.Trim().ToUpperInvariant();
        var entity = await db.Set<TaxCode>().AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, token)
            ?? throw new NotFoundException("MD.TAX_CODE.NOT_FOUND", "Tax code", code ?? string.Empty);
        if (!Enum.IsDefined(direction) || (direction == TaxDirection.Output && entity.TaxType == "Input") ||
            (direction == TaxDirection.Input && entity.TaxType == "Output"))
            throw new BusinessRuleException("MD.TAX_RATE.DIRECTION_MISMATCH", "Tax code does not apply to this direction.");
        var rates = await db.Set<TaxRate>().AsNoTracking().Where(x => x.TaxCodeId == entity.Id && x.EffectiveFrom <= date &&
            (x.EffectiveTo == null || x.EffectiveTo >= date)).Select(x => x.RatePercent).Take(1).ToListAsync(token);
        return rates.Count == 1 ? rates[0] : throw new BusinessRuleException("MD.TAX_RATE.NOT_FOUND", "No tax rate on this date.");
    }

    public async Task<TaxCalculation> CalculateAsync(string code, TaxDirection direction, DateOnly date, decimal amount,
        bool priceIncludesTax, string currencyCode, CancellationToken token = default)
    {
        if (currencyCode is null || currencyCode.Trim().Length != 3 || !currencyCode.Trim().All(char.IsAsciiLetter))
            throw new RequestValidationException("currencyCode", "Use a three-letter currency code.");
        var rate = await GetRatePercentAsync(code, direction, date, token);
        var precision = await currencies.GetPrecisionAsync(token);
        var result = Tax.Calculate(amount, rate, priceIncludesTax, currencyCode.Trim().ToUpperInvariant(), precision);
        return new TaxCalculation(rate, result.Net, result.Tax, result.Gross);
    }

    private void RequireCompany()
    {
        if (db.CurrentCompanyId is not > 0)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
    }
}
