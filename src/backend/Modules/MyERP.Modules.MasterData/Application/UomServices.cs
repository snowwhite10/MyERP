using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.MasterData.Application;

/// <summary>計量單位 CRUD；沿用分類規則，另驗證小數位數；已被換算引用的單位只可停用。</summary>
public sealed class UomService(MyErpDbContext db) : MasterCategoryService<UnitOfMeasure>(db, "MD.UOM", "Unit of measure")
{
    public Task<UnitOfMeasure> CreateAsync(string? code, string? name, int decimalPlaces, CancellationToken token = default)
    {
        ValidateDecimalPlaces(decimalPlaces);
        return CreateCoreAsync(code, name, x => x.DecimalPlaces = decimalPlaces, token);
    }

    public Task<UnitOfMeasure> UpdateAsync(
        long id, string? name, bool isActive, int decimalPlaces, int version, CancellationToken token = default)
    {
        ValidateDecimalPlaces(decimalPlaces);
        return UpdateCoreAsync(id, name, isActive, version, x => x.DecimalPlaces = decimalPlaces, token);
    }

    protected override UnitOfMeasure New(long companyId, string code, string name) =>
        new() { CompanyId = companyId, Code = code, Name = name };

    protected override async Task EnsureNotInUseAsync(long id, CancellationToken token)
    {
        if (await Db.Set<UomConversion>().AnyAsync(x => x.FromUomId == id || x.ToUomId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Unit is referenced by conversions.");
        if (await Db.Set<ItemUomConversion>().AnyAsync(x => x.UomId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Unit is referenced by item conversions.");
        if (await Db.Set<Item>().AnyAsync(x => x.BaseUomId == id, token))
            throw new ConflictException(ErrorPrefix + ".IN_USE", "Unit is referenced by items.");
    }

    private static void ValidateDecimalPlaces(int value)
    {
        if (value is < 0 or > UnitOfMeasure.MaxDecimalPlaces)
            throw new RequestValidationException("decimalPlaces", "Use 0–6 decimal places.");
    }
}

/// <summary>換算列表項目，附兩端單位代碼。</summary>
public sealed record UomConversionView(
    long Id, long FromUomId, string FromUomCode, long ToUomId, string ToUomCode, decimal Factor, int Version);

/// <summary>
/// 單位換算維護與數量換算。兩單位之間只允許一條換算路徑（不可自換、反向重複或經其他單位形成循環），
/// 新增時以公司級 applock 序列化，避免並行建立反向或循環換算。
/// </summary>
public sealed class UomConversionService(MyErpDbContext db) : IUomConverter
{
    private const string ErrorPrefix = "MD.UOM_CONVERSION";

    public async Task<IReadOnlyList<UomConversionView>> ListAsync(CancellationToken token = default)
    {
        RequireCompany();
        return await db.Set<UomConversion>().AsNoTracking()
            .OrderBy(x => x.FromUom!.Code).ThenBy(x => x.ToUom!.Code)
            .Select(x => new UomConversionView(x.Id, x.FromUomId, x.FromUom!.Code, x.ToUomId, x.ToUom!.Code, x.Factor, x.Version))
            .ToListAsync(token);
    }

    public async Task<UomConversionView> CreateAsync(long fromUomId, long toUomId, decimal factor, CancellationToken token = default)
    {
        var companyId = RequireCompany();
        ValidateFactor(factor);
        if (fromUomId == toUomId) throw new RequestValidationException("toUomId", "Choose a different unit.");
        var from = await ActiveUomAsync(fromUomId, "fromUomId", token);
        var to = await ActiveUomAsync(toUomId, "toUomId", token);
        var entity = await CompanyLock.RunAsync(db, "md_uom_conversion", companyId, async () =>
        {
            if (UomConversionGraph.FindRatio(await EdgesAsync(token), fromUomId, toUomId) is not null)
                throw new ConflictException(ErrorPrefix + ".PATH_EXISTS", "These units are already convertible.");
            var created = new UomConversion { CompanyId = companyId, FromUomId = fromUomId, ToUomId = toUomId, Factor = factor };
            db.Add(created);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                throw new ConflictException(ErrorPrefix + ".PATH_EXISTS", "These units are already convertible.");
            }
            return created;
        }, token);
        return new UomConversionView(entity.Id, from.Id, from.Code, to.Id, to.Code, entity.Factor, entity.Version);
    }

    public async Task<UomConversionView> UpdateAsync(long id, decimal factor, int version, CancellationToken token = default)
    {
        RequireCompany();
        ValidateFactor(factor);
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        entity.Factor = factor;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(token);
        return new UomConversionView(entity.Id, entity.FromUomId, entity.FromUom!.Code, entity.ToUomId, entity.ToUom!.Code,
            entity.Factor, entity.Version);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireCompany();
        var entity = await FindAsync(id, token);
        CheckVersion(entity, version);
        entity.IsDeleted = true;
        db.Entry(entity).Property(x => x.Version).OriginalValue = version;
        await SaveUpdateAsync(token);
    }

    /// <inheritdoc />
    public async Task<decimal> ConvertAsync(long fromUomId, long toUomId, decimal quantity, CancellationToken token = default)
    {
        RequireCompany();
        var target = await TargetUomAsync(fromUomId, toUomId, token);
        var edges = await EdgesAsync(token);
        return Apply(quantity, () => UomConversionGraph.FindRatio(edges, fromUomId, toUomId), target);
    }

    /// <inheritdoc />
    public async Task<decimal> ConvertForItemAsync(
        long itemId, long fromUomId, long toUomId, decimal quantity, CancellationToken token = default)
    {
        RequireCompany();
        var baseUomId = await db.Set<Item>().AsNoTracking().Where(x => x.Id == itemId).Select(x => (long?)x.BaseUomId)
            .SingleOrDefaultAsync(token) ?? throw new NotFoundException("MD.ITEM.NOT_FOUND", "Item", itemId);
        var target = await TargetUomAsync(fromUomId, toUomId, token);
        var itemFactors = await db.Set<ItemUomConversion>().AsNoTracking().Where(x => x.ItemId == itemId)
            .ToDictionaryAsync(x => x.UomId, x => x.Factor, token);
        var edges = await EdgesAsync(token);
        // ADR-035：兩端先換到基本單位（物料專屬係數優先，否則走公司級路徑）；任一端換不到時退回公司級直接路徑。
        return Apply(quantity, () => ToBase(fromUomId) is { } from && ToBase(toUomId) is { } to
            ? (from.Numerator * to.Denominator, from.Denominator * to.Numerator)
            : UomConversionGraph.FindRatio(edges, fromUomId, toUomId), target);

        (decimal Numerator, decimal Denominator)? ToBase(long uomId) =>
            uomId == baseUomId ? (1m, 1m)
            : itemFactors.TryGetValue(uomId, out var factor) ? (factor, 1m)
            : UomConversionGraph.FindRatio(edges, uomId, baseUomId);
    }

    private async Task<UnitOfMeasure> TargetUomAsync(long fromUomId, long toUomId, CancellationToken token)
    {
        var target = await db.Set<UnitOfMeasure>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == toUomId, token)
            ?? throw new NotFoundException("MD.UOM.NOT_FOUND", "Unit of measure", toUomId);
        if (!await db.Set<UnitOfMeasure>().AnyAsync(x => x.Id == fromUomId, token))
            throw new NotFoundException("MD.UOM.NOT_FOUND", "Unit of measure", fromUomId);
        return target;
    }

    /// <summary>比率在 try 內計算：分子分母累乘也可能溢位。</summary>
    private static decimal Apply(decimal quantity, Func<(decimal Numerator, decimal Denominator)?> ratio, UnitOfMeasure target)
    {
        try
        {
            var (numerator, denominator) = ratio()
                ?? throw new BusinessRuleException(ErrorPrefix + ".NO_PATH", "No conversion path between these units.");
            return Math.Round(quantity * numerator / denominator, target.DecimalPlaces, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            throw new RequestValidationException("quantity", "The converted quantity is out of range.");
        }
    }

    private async Task<List<(long From, long To, decimal Factor)>> EdgesAsync(CancellationToken token) =>
        [.. (await db.Set<UomConversion>().AsNoTracking().Select(x => new { x.FromUomId, x.ToUomId, x.Factor }).ToListAsync(token))
            .Select(x => (x.FromUomId, x.ToUomId, x.Factor))];

    private async Task<UnitOfMeasure> ActiveUomAsync(long id, string field, CancellationToken token) =>
        await db.Set<UnitOfMeasure>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive, token)
        ?? throw new RequestValidationException(field, "Choose an active unit of the current company.");

    private async Task<UomConversion> FindAsync(long id, CancellationToken token) =>
        await db.Set<UomConversion>().Include(x => x.FromUom).Include(x => x.ToUom).SingleOrDefaultAsync(x => x.Id == id, token)
        ?? throw new NotFoundException(ErrorPrefix + ".NOT_FOUND", "Unit conversion", id);

    private long RequireCompany() => db.CurrentCompanyId is > 0 ? db.CurrentCompanyId.Value
        : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    internal static void ValidateFactor(decimal factor)
    {
        if (factor <= 0 || factor > UomConversion.MaxFactor || decimal.Round(factor, UomConversion.FactorScale) != factor)
            throw new RequestValidationException("factor", "Use a positive factor with at most 9 integer and 6 decimal digits.");
    }

    private static void CheckVersion(UomConversion entity, int version)
    {
        if (version < 1 || entity.Version != version) throw new ConflictException();
    }

    private async Task SaveUpdateAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
    }
}
