using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

/// <summary>Quote query. All four values are required; the company always comes from the current-company context.</summary>
public sealed record SalesPriceQuoteQuery
{
    public long? ItemId { get; init; }
    public long? CustomerId { get; init; }

    /// <summary>Three uppercase letters, exactly as saved on the rule.</summary>
    public string? Currency { get; init; }

    /// <summary>The document business date (not today); rule dates are inclusive.</summary>
    public DateOnly? BusinessDate { get; init; }
}

/// <summary>
/// Selected rule for one item/customer/currency/date (BR-SD-027). <see cref="CustomerId"/> is null when the
/// company default rule was selected. Advisory only: document save resolves the rule again (WU-SD-005a11).
/// </summary>
public sealed record SalesPriceQuoteResult(DateOnly AsOf, long RuleId, int RuleVersion, long ItemId,
    long? CustomerId, string CurrencyCode, decimal UnitPrice, decimal DiscountCeiling,
    DateOnly ValidFrom, DateOnly ValidTo);

/// <summary>
/// WU-SD-005a10 (ADR-159): read-only dated price quote for the price-rule page and for quotation/order forms.
/// Allowed with <c>SAL.PRICING.VIEW</c> or any of <c>SAL.QUOTATION.CREATE/EDIT</c>, <c>SAL.ORDER.CREATE/EDIT</c>;
/// rules are company-wide, so the granted data scope only proves the permission. Never writes.
/// </summary>
public sealed class SalesPriceQuoteService(MyErpDbContext db, SalesCompanyAccess company,
    ICurrentAuthorization authorization, ISalesItemLookup items, ICustomerLookup customers)
{
    public static readonly string[] Permissions =
        ["SAL.PRICING.VIEW", "SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT", "SAL.ORDER.CREATE", "SAL.ORDER.EDIT"];

    /// <summary>
    /// 400 parameters (all at once) → 403 → MD item (404 hidden/unknown, 422 inactive) → MD customer (404, 422 not
    /// approved) → 422 <c>SAL.PRICING.RULE_NOT_FOUND</c> or 409 <c>SAL.PRICING.DUPLICATE_ACTIVE_RULE</c> with
    /// <c>conflictingRuleIds</c>. An unknown currency simply has no rule (422).
    /// </summary>
    public async Task<SalesPriceQuoteResult> QuoteAsync(SalesPriceQuoteQuery request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = company.RequireCompanyId();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.ItemId is not > 0) errors.TryAdd("itemId", ["Item is required and must be positive."]);
        if (request.CustomerId is not > 0) errors.TryAdd("customerId", ["Customer is required and must be positive."]);
        if (!SalesPricingRules.IsCurrencyCode(request.Currency))
            errors.TryAdd("currency", ["Currency code must be 3 uppercase letters."]);
        if (request.BusinessDate is null || request.BusinessDate == default)
            errors.TryAdd("businessDate", ["Business date is required (yyyy-MM-dd)."]);
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var current = await authorization.GetAsync(token);
        if (current is null || current.CompanyId != companyId ||
            !Permissions.Any(permission => current.Has(permission) && current.FilterFor(permission) is not null))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED",
                "Pricing view or quotation/order create or edit permission is required.");

        var itemId = request.ItemId!.Value;
        var customerId = request.CustomerId!.Value;
        var currency = request.Currency!;
        var asOf = request.BusinessDate!.Value;
        await items.RequireActiveAsync(itemId, token);
        await customers.RequireApprovedAsync(customerId, token);

        // Candidates of both tiers covering the date; the company/soft-delete filters keep other companies out.
        var candidates = await db.Set<SalesPriceRule>().AsNoTracking()
            .Where(x => x.ItemId == itemId && x.CurrencyCode == currency && x.IsActive &&
                x.ValidFrom <= asOf && asOf <= x.ValidTo &&
                (x.CustomerId == customerId || x.CustomerId == null))
            .ToListAsync(token);
        var quote = SalesPricingRules.Quote(new SalesPriceLookup(companyId, itemId, customerId, currency, asOf),
            candidates.Select(SalesPriceRuleService.Snapshot).ToList());

        switch (quote.Status)
        {
            case SalesPriceSelectionStatus.NotFound:
                throw new BusinessRuleException(SalesPricingErrors.RuleNotFound,
                    "No active price rule covers this item, customer, currency and date.");
            case SalesPriceSelectionStatus.DuplicateActiveRule:
                throw new ConflictException(SalesPricingErrors.DuplicateActiveRule,
                    "More than one active price rule applies; the price cannot be chosen.")
                {
                    Extensions = new Dictionary<string, object?> { ["conflictingRuleIds"] = quote.ConflictingRuleIds }
                };
        }

        var rule = candidates.Single(x => x.Id == quote.RuleId);
        return new SalesPriceQuoteResult(quote.AsOf, rule.Id, rule.Version, rule.ItemId, rule.CustomerId,
            rule.CurrencyCode, rule.UnitPrice, rule.DiscountCeiling, rule.ValidFrom, rule.ValidTo);
    }
}
