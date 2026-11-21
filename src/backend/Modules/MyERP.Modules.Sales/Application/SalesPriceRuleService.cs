using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.Sales.Application;

/// <summary>Saved price rule as returned by the API (REQ_F4112.4.1). A null customer is the company default.</summary>
public sealed record SalesPriceRuleDetail(long Id, long ItemId, long? CustomerId, string CurrencyCode,
    DateOnly ValidFrom, DateOnly ValidTo, decimal UnitPrice, decimal DiscountCeiling, bool IsActive,
    int Version, DateTime CreatedAt, DateTime? UpdatedAt)
{
    /// <summary>Current MD item code (ADR-161); null when the item is soft-deleted or not yet described.</summary>
    public string? ItemCode { get; init; }

    public string? ItemName { get; init; }

    /// <summary>Current MD customer code; null for a company default rule.</summary>
    public string? CustomerCode { get; init; }

    public string? CustomerName { get; init; }
}

/// <summary>Price-rule list filters. <see cref="DefaultOnly"/> lists only rules without a customer.</summary>
public sealed record SalesPriceRuleQuery : PagedRequest
{
    public long? ItemId { get; init; }
    public long? CustomerId { get; init; }
    public bool? DefaultOnly { get; init; }
    public string? CurrencyCode { get; init; }

    /// <summary>Only rules whose inclusive valid dates cover this day.</summary>
    public DateOnly? AsOf { get; init; }

    public bool? Active { get; init; }
}

/// <summary>Edit body: the full rule plus the version the client last read (ADR-158).</summary>
public sealed record SalesPriceRuleUpdateRequest(long ItemId, long? CustomerId, string? CurrencyCode,
    DateOnly ValidFrom, DateOnly ValidTo, decimal UnitPrice, decimal DiscountCeiling, int Version)
{
    public SalesPriceRuleInput ToInput() =>
        new(ItemId, CustomerId, CurrencyCode, ValidFrom, ValidTo, UnitPrice, DiscountCeiling);
}

/// <summary>Body carrying only the version the client last read.</summary>
public sealed record SalesPriceRuleVersionRequest(int Version);

/// <summary>
/// WU-SD-005a8/a9 (ADR-157/158): company-wide price rules. Reads need <c>SAL.PRICING.VIEW</c>, creation
/// <c>SAL.PRICING.CREATE</c>, edit <c>SAL.PRICING.EDIT</c>, retirement <c>SAL.PRICING.DEACTIVATE</c>; rules have no owner, branch or department, so the granted data scope only proves the
/// permission. Same-key writers serialise on a SQL application lock and re-check overlaps inside the transaction.
/// </summary>
public sealed class SalesPriceRuleService(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company,
    ISalesItemLookup items, ICustomerLookup customers, ICurrencyLookup currencies)
{
    public const string NotFoundCode = "SAL.PRICE_RULE.NOT_FOUND";

    /// <summary>Raised by the key lock T-SQL when <c>sp_getapplock</c> is not granted (timeout, deadlock, cancel).</summary>
    private const int LockNotGranted = 51020;

    /// <summary>Maximum wait for another writer of the same rule key.</summary>
    internal int LockTimeoutMilliseconds { get; init; } = 15_000;

    public async Task<PagedResult<SalesPriceRuleDetail>> ListAsync(SalesPriceRuleQuery request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        company.RequireCompanyId();
        request.EnsureValid();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ItemId is <= 0) errors.TryAdd("itemId", ["Item must be positive."]);
        if (request.CustomerId is <= 0) errors.TryAdd("customerId", ["Customer must be positive."]);
        if (request.CustomerId is not null && request.DefaultOnly == true)
            errors.TryAdd("defaultOnly", ["Choose a customer or default rules, not both."]);
        if (request.CurrencyCode is not null && !SalesPricingRules.IsCurrencyCode(request.CurrencyCode))
            errors.TryAdd("currencyCode", ["Currency code must be 3 uppercase letters."]);
        if (errors.Count > 0) throw new RequestValidationException(errors);
        var sorts = SortSpec.Parse(request.Sort, ["validFrom", "id"], "-validFrom");
        if (sorts.Count != 1)
            throw new RequestValidationException("sort", "Choose one sort field.");
        await access.RequireScopeAsync("SAL.PRICING.VIEW", token);

        var query = db.Set<SalesPriceRule>().AsNoTracking();
        if (request.ItemId is { } itemId) query = query.Where(x => x.ItemId == itemId);
        if (request.CustomerId is { } customerId) query = query.Where(x => x.CustomerId == customerId);
        if (request.DefaultOnly == true) query = query.Where(x => x.CustomerId == null);
        if (request.CurrencyCode is { } currency) query = query.Where(x => x.CurrencyCode == currency);
        if (request.AsOf is { } asOf) query = query.Where(x => x.ValidFrom <= asOf && asOf <= x.ValidTo);
        if (request.Active is { } active) query = query.Where(x => x.IsActive == active);
        query = (sorts[0].Field, sorts[0].Descending) switch
        {
            ("id", false) => query.OrderBy(x => x.Id),
            ("id", true) => query.OrderByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.ValidFrom).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var rows = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(token);
        return PagedResult.Create(rows.Select(Map).ToList(), request, count);
    }

    public async Task<SalesPriceRuleDetail> GetAsync(long id, CancellationToken token = default)
    {
        company.RequireCompanyId();
        await access.RequireScopeAsync("SAL.PRICING.VIEW", token);
        var rule = await db.Set<SalesPriceRule>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFoundCode, "Price rule", id);
        return Map(rule);
    }

    /// <summary>
    /// 400 fields → 403 permission → MD item/customer/currency (404 missing, 422 inactive/unapproved) →
    /// 409 <c>SAL.PRICING.RULE_OVERLAP</c> with <c>conflictingRuleIds</c>. The rule is active immediately.
    /// </summary>
    public async Task<SalesPriceRule> CreateAsync(SalesPriceRuleInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        SalesPricingRules.ValidateRule(input);
        await access.RequireScopeAsync("SAL.PRICING.CREATE", token);
        var companyId = company.RequireCompanyId();
        await RequireMasterDataAsync(input, token);

        var rule = new SalesPriceRule
        {
            CompanyId = companyId, ItemId = input.ItemId, CustomerId = input.CustomerId,
            CurrencyCode = input.CurrencyCode!, ValidFrom = input.ValidFrom, ValidTo = input.ValidTo,
            UnitPrice = input.UnitPrice, DiscountCeiling = input.DiscountCeiling, IsActive = true
        };
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await LockKeysAsync([Key(rule)], token);
            await EnsureNoOverlapAsync(rule, token);
            db.Add(rule);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return rule;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    internal static string LockResource(long companyId, long itemId, string currencyCode, long? customerId)
    {
        var customer = customerId is { } id ? id.ToString(CultureInfo.InvariantCulture) : "default";
        return string.Create(CultureInfo.InvariantCulture,
            $"sal_price_rule:{companyId}:{itemId}:{currencyCode}:{customer}");
    }

    /// <summary>
    /// Edit an active rule (ADR-158). 400 fields → 403 <c>SAL.PRICING.EDIT</c> → 404 → 409 stale version → 422
    /// inactive rule → MD (404/422) → lock old and new keys in ordinal order → re-check → 409 overlap (self
    /// excluded). The row changes in place; the audit trail keeps old/new values and the version rises by one.
    /// </summary>
    public async Task<SalesPriceRule> UpdateAsync(long id, SalesPriceRuleUpdateRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var input = request.ToInput();
        SalesPricingRules.ValidateRule(input);
        await access.RequireScopeAsync("SAL.PRICING.EDIT", token);
        var companyId = company.RequireCompanyId();
        var current = await LoadEditableAsync(id, request.Version, tracked: false, token);
        await RequireMasterDataAsync(input, token);

        string[] keys =
        [
            LockResource(companyId, current.ItemId, current.CurrencyCode, current.CustomerId),
            LockResource(companyId, input.ItemId, input.CurrencyCode!, input.CustomerId)
        ];
        return await WriteAsync(id, request.Version, keys, async rule =>
        {
            rule.ItemId = input.ItemId;
            rule.CustomerId = input.CustomerId;
            rule.CurrencyCode = input.CurrencyCode!;
            rule.ValidFrom = input.ValidFrom;
            rule.ValidTo = input.ValidTo;
            rule.UnitPrice = input.UnitPrice;
            rule.DiscountCeiling = input.DiscountCeiling;
            await EnsureNoOverlapAsync(rule, token);
        }, token);
    }

    /// <summary>
    /// Retire an active rule (ADR-158): 403 <c>SAL.PRICING.DEACTIVATE</c> → 404 → 409 stale version → 422 already
    /// inactive. The rule stays stored and readable; its dates become free. Reactivation is a new rule.
    /// </summary>
    public async Task<SalesPriceRule> DeactivateAsync(long id, int version, CancellationToken token = default)
    {
        await access.RequireScopeAsync("SAL.PRICING.DEACTIVATE", token);
        company.RequireCompanyId();
        var current = await LoadEditableAsync(id, version, tracked: false, token);
        return await WriteAsync(id, version, [Key(current)], rule =>
        {
            rule.IsActive = false;
            return Task.CompletedTask;
        }, token);
    }

    /// <summary>
    /// One write transaction: lock the keys, reload the rule, re-check version and status, apply, save. Every writer
    /// of a rule holds at least its current key lock, so the reload sees the last committed state; if another
    /// writer moved the rule to a different key first, its version changed and this write is a 409.
    /// </summary>
    private async Task<SalesPriceRule> WriteAsync(long id, int version, IReadOnlyList<string> keys,
        Func<SalesPriceRule, Task> apply, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await LockKeysAsync(keys, token);
            var rule = await LoadEditableAsync(id, version, tracked: true, token);
            db.Entry(rule).Property(x => x.Version).OriginalValue = version;
            await apply(rule);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return rule;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }

    private async Task<SalesPriceRule> LoadEditableAsync(long id, int version, bool tracked, CancellationToken token)
    {
        var query = tracked ? db.Set<SalesPriceRule>() : db.Set<SalesPriceRule>().AsNoTracking();
        var rule = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFoundCode, "Price rule", id);
        if (version < 1 || rule.Version != version) throw new ConflictException();
        if (!rule.IsActive)
            throw new BusinessRuleException(SalesPricingErrors.RuleInactive,
                "The price rule is inactive and cannot be changed; create a new rule instead.");
        return rule;
    }

    /// <summary>MD item active (404/422), customer approved (404/422), currency exists with the exact code and is active.</summary>
    private async Task RequireMasterDataAsync(SalesPriceRuleInput input, CancellationToken token)
    {
        await items.RequireActiveAsync(input.ItemId, token);
        if (input.CustomerId is { } customerId) await customers.RequireApprovedAsync(customerId, token);
        var currency = await currencies.FindCurrencyAsync(input.CurrencyCode!, token);
        if (currency is null || !string.Equals(currency.Code, input.CurrencyCode, StringComparison.Ordinal))
            throw new NotFoundException("MD.CURRENCY.NOT_FOUND", "Currency", input.CurrencyCode!);
        if (!currency.IsActive)
            throw new BusinessRuleException("MD.CURRENCY.INACTIVE", "Currency is inactive.");
    }

    private static string Key(SalesPriceRule rule) =>
        LockResource(rule.CompanyId, rule.ItemId, rule.CurrencyCode, rule.CustomerId);

    /// <summary>
    /// Exclusive transaction-owned locks on the distinct keys in ordinal order (a null customer is its own default
    /// key), so two writers that need the same pair always ask in the same order and cannot deadlock. A lock not
    /// granted in time is 409 <c>SAL.PRICING.RULE_LOCK_TIMEOUT</c>, not a server error.
    /// </summary>
    private async Task LockKeysAsync(IReadOnlyList<string> keys, CancellationToken token)
    {
        foreach (var resource in keys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DECLARE @result int;
                    EXEC @result = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1};
                    IF @result < 0 THROW 51020, 'SD price-rule lock was not granted.', 1;
                    """, [resource, LockTimeoutMilliseconds], token);
            }
            catch (SqlException ex) when (ex.Number == LockNotGranted)
            {
                throw new ConflictException(SalesPricingErrors.RuleLockTimeout,
                    "Another user is changing price rules for the same item, currency and customer. Please retry.");
            }
        }
    }

    private async Task EnsureNoOverlapAsync(SalesPriceRule proposed, CancellationToken token)
    {
        var query = db.Set<SalesPriceRule>().AsNoTracking()
            .Where(x => x.ItemId == proposed.ItemId && x.CurrencyCode == proposed.CurrencyCode && x.IsActive &&
                x.ValidFrom <= proposed.ValidTo && proposed.ValidFrom <= x.ValidTo);
        query = proposed.CustomerId is { } customerId
            ? query.Where(x => x.CustomerId == customerId)
            : query.Where(x => x.CustomerId == null);
        if (proposed.Id > 0) query = query.Where(x => x.Id != proposed.Id);
        var candidates = await query.ToListAsync(token);
        var overlaps = SalesPricingRules.FindOverlaps(Snapshot(proposed), candidates.Select(Snapshot).ToList());
        if (overlaps.Count > 0)
            throw new ConflictException(SalesPricingErrors.RuleOverlap,
                "An active price rule for the same item, currency and customer already covers these dates.")
            {
                Extensions = new Dictionary<string, object?> { ["conflictingRuleIds"] = overlaps }
            };
    }

    internal static SalesPriceRuleSnapshot Snapshot(SalesPriceRule rule) => new(rule.Id,
        rule.Version <= 0 ? 1 : rule.Version, rule.CompanyId, rule.ItemId, rule.CustomerId, rule.CurrencyCode,
        rule.ValidFrom, rule.ValidTo, rule.UnitPrice, rule.DiscountCeiling, rule.IsActive, rule.IsDeleted);

    public static SalesPriceRuleDetail Map(SalesPriceRule rule) => new(rule.Id, rule.ItemId, rule.CustomerId,
        rule.CurrencyCode, rule.ValidFrom, rule.ValidTo, rule.UnitPrice, rule.DiscountCeiling, rule.IsActive,
        rule.Version, rule.CreatedAt, rule.UpdatedAt);
}
