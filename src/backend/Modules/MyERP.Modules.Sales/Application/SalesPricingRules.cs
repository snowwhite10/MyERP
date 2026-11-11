using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>Stable SD pricing error codes (BR-SD-027～030).</summary>
public static class SalesPricingErrors
{
    public const string RuleNotFound = "SAL.PRICING.RULE_NOT_FOUND";
    public const string DuplicateActiveRule = "SAL.PRICING.DUPLICATE_ACTIVE_RULE";
    public const string RuleOverlap = "SAL.PRICING.RULE_OVERLAP";
    public const string StaleQuote = "SAL.PRICING.STALE_QUOTE";
    public const string OverrideRequired = "SAL.PRICING.OVERRIDE_REQUIRED";
}

/// <summary>Saved price-rule row as read inside the caller's transaction. Null customer = default rule.</summary>
public sealed record SalesPriceRuleSnapshot(long RuleId, int Version, long CompanyId, long ItemId,
    long? CustomerId, string CurrencyCode, DateOnly ValidFrom, DateOnly ValidTo,
    decimal UnitPrice, decimal DiscountCeiling, bool IsActive, bool IsDeleted);

/// <summary>Company comes from the current-company context, never from the client.</summary>
public sealed record SalesPriceLookup(long CompanyId, long ItemId, long CustomerId,
    string CurrencyCode, DateOnly BusinessDate);

public enum SalesPriceSelectionStatus { Selected, NotFound, DuplicateActiveRule }

public sealed record SalesPriceSelection(SalesPriceSelectionStatus Status, SalesPriceRuleSnapshot? Rule,
    string? ErrorCode, IReadOnlyList<long> ConflictingRuleIds);

/// <summary>Read-only dated quote; missing/conflicting rules never expose a guessed price.</summary>
public sealed record SalesPriceQuote(DateOnly AsOf, SalesPriceSelectionStatus Status,
    long? RuleId, int? RuleVersion, decimal? UnitPrice, decimal? DiscountCeiling,
    DateOnly? ExpiresOn, string? ErrorCode, IReadOnlyList<long> ConflictingRuleIds);

public sealed record SalesPriceRuleInput(long ItemId, long? CustomerId, string? CurrencyCode,
    DateOnly ValidFrom, DateOnly ValidTo, decimal UnitPrice, decimal DiscountCeiling);

/// <summary>Client hints only; QuotedRuleId/Version must be supplied together or not at all.</summary>
public sealed record SalesPriceLineRequest(decimal UnitPrice, decimal DiscountRate,
    long? QuotedRuleId, int? QuotedRuleVersion, string? OverrideReason);

/// <summary>Pricing provenance to save on a quotation/manual-order line (BR-SD-029).</summary>
public sealed record SalesPriceDecision(long? RuleId, int? RuleVersion, decimal? RulePrice,
    decimal? RuleDiscountCeiling, decimal UnitPrice, decimal DiscountRate, bool IsOverride,
    string? OverrideReason);

/// <summary>
/// WU-SD-005a0 offline pricing rules for REQ_F4112.4.1 v1.1. The caller loads rule snapshots in its
/// transaction (WU-SD-005a); these functions never read the clock, the database or the client's identity.
/// </summary>
public static class SalesPricingRules
{
    public const int OverrideReasonMaxLength = 200;
    private const decimal DecimalNineteenSixLimit = 10_000_000_000_000m;

    /// <summary>Resolve and decide from one save-context snapshot; the caller must load rules in its transaction.</summary>
    public static SalesPriceDecision DecideForSave(SalesPriceLookup lookup,
        IReadOnlyList<SalesPriceRuleSnapshot> rules, SalesPriceLineRequest line,
        bool hasOverrideGrant, string fieldPrefix)
    {
        ValidateLine(line, fieldPrefix);
        return Decide(Select(lookup, rules), line, hasOverrideGrant, fieldPrefix);
    }

    /// <summary>Builds an advisory quote from the same selection used by transactional save.</summary>
    public static SalesPriceQuote Quote(SalesPriceLookup lookup, IReadOnlyList<SalesPriceRuleSnapshot> rules)
    {
        var selection = Select(lookup, rules);
        var rule = selection.Rule;
        return new SalesPriceQuote(lookup.BusinessDate, selection.Status,
            rule?.RuleId, rule?.Version, rule?.UnitPrice, rule?.DiscountCeiling,
            rule?.ValidTo, selection.ErrorCode, selection.ConflictingRuleIds);
    }

    public static SalesPriceSelection Select(SalesPriceLookup lookup, IReadOnlyList<SalesPriceRuleSnapshot> rules)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(rules);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (lookup.CompanyId <= 0) errors.TryAdd("companyId", ["Current company is required."]);
        if (lookup.ItemId <= 0) errors.TryAdd("itemId", ["Item is required."]);
        if (lookup.CustomerId <= 0) errors.TryAdd("customerId", ["Customer is required."]);
        if (!ValidCurrency(lookup.CurrencyCode)) errors.TryAdd("currencyCode", ["Currency code is invalid."]);
        if (lookup.BusinessDate == default) errors.TryAdd("businessDate", ["Business date is required."]);
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var ids = new HashSet<long>();
        foreach (var rule in rules)
        {
            if (rule is null || rule.RuleId <= 0 || !ids.Add(rule.RuleId))
                throw new RequestValidationException("rules", "Price-rule snapshot is invalid.");
        }

        var applicable = rules.Where(rule => rule.IsActive && !rule.IsDeleted &&
            rule.CompanyId == lookup.CompanyId && rule.ItemId == lookup.ItemId &&
            string.Equals(rule.CurrencyCode, lookup.CurrencyCode, StringComparison.Ordinal) &&
            rule.ValidFrom <= lookup.BusinessDate && lookup.BusinessDate <= rule.ValidTo).ToList();

        // The customer tier decides whenever it has any candidate; the default tier is consulted only
        // when the customer tier is empty. Several candidates on the deciding tier are never tie-broken.
        var tier = applicable.Where(rule => rule.CustomerId == lookup.CustomerId).ToList();
        if (tier.Count == 0) tier = applicable.Where(rule => rule.CustomerId is null).ToList();

        return tier.Count switch
        {
            0 => new(SalesPriceSelectionStatus.NotFound, null, SalesPricingErrors.RuleNotFound, []),
            1 => new(SalesPriceSelectionStatus.Selected, tier[0], null, []),
            _ => new(SalesPriceSelectionStatus.DuplicateActiveRule, null, SalesPricingErrors.DuplicateActiveRule,
                tier.Select(rule => rule.RuleId).Order().ToArray()),
        };
    }

    /// <summary>Active, undeleted rules of the same company/item/currency/customer scope whose inclusive
    /// intervals intersect the proposed rule, by ascending ID. An inactive proposal overlaps nothing.</summary>
    public static IReadOnlyList<long> FindOverlaps(SalesPriceRuleSnapshot proposed,
        IReadOnlyList<SalesPriceRuleSnapshot> existing)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(existing);
        if (!proposed.IsActive || proposed.IsDeleted) return [];
        return existing.Where(rule => rule is not null && rule.RuleId != proposed.RuleId &&
                rule.IsActive && !rule.IsDeleted && rule.CompanyId == proposed.CompanyId &&
                rule.ItemId == proposed.ItemId && rule.CustomerId == proposed.CustomerId &&
                string.Equals(rule.CurrencyCode, proposed.CurrencyCode, StringComparison.Ordinal) &&
                rule.ValidFrom <= proposed.ValidTo && proposed.ValidFrom <= rule.ValidTo)
            .Select(rule => rule.RuleId).Distinct().Order().ToArray();
    }

    /// <summary>Field errors in form order (SD-PRICE-006); MD identity checks belong to the transaction.</summary>
    public static void ValidateRule(SalesPriceRuleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.ItemId <= 0) errors.TryAdd("itemId", ["Item is required."]);
        if (input.CustomerId is <= 0) errors.TryAdd("customerId", ["Customer must be empty or a saved customer."]);
        if (!ValidCurrency(input.CurrencyCode)) errors.TryAdd("currencyCode", ["Currency code must be 3 uppercase letters."]);
        if (input.ValidFrom == default) errors.TryAdd("validFrom", ["Valid-from date is required."]);
        if (input.ValidTo == default || (input.ValidFrom != default && input.ValidTo < input.ValidFrom))
            errors.TryAdd("validTo", ["Valid-to date must not precede valid-from."]);
        if (!ValidPrice(input.UnitPrice))
            errors.TryAdd("unitPrice", ["Unit price must be nonnegative, below 10^13, with at most 6 decimals."]);
        if (!ValidPercent(input.DiscountCeiling))
            errors.TryAdd("discountCeiling", ["Discount ceiling must be 0 to 100 with at most 4 decimals."]);
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    /// <summary>
    /// Server-side pricing decision for one line (BR-SD-028). Order: 400 field/hint shape → 409 duplicate
    /// rule → 409 stale quote → 403 override authority → 400 override reason. Out-of-range values and
    /// duplicate rules are never overridable.
    /// </summary>
    public static SalesPriceDecision Decide(SalesPriceSelection selection, SalesPriceLineRequest line,
        bool hasOverrideGrant, string fieldPrefix)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ValidateLine(line, fieldPrefix);

        if (selection.Status == SalesPriceSelectionStatus.DuplicateActiveRule)
            throw new ConflictException(SalesPricingErrors.DuplicateActiveRule,
                "More than one active price rule applies; the price cannot be chosen.");

        var rule = selection.Status == SalesPriceSelectionStatus.Selected ? selection.Rule : null;
        if (selection.Status == SalesPriceSelectionStatus.Selected && rule is null)
            throw new ArgumentException("A selected price result needs its rule.", nameof(selection));
        if (line.QuotedRuleId is not null &&
            (rule is null || rule.RuleId != line.QuotedRuleId || rule.Version != line.QuotedRuleVersion))
            throw new ConflictException(SalesPricingErrors.StaleQuote,
                "The displayed price quote is no longer current; request a new quote.");

        var needsOverride = rule is null || line.UnitPrice != rule.UnitPrice ||
            line.DiscountRate > rule.DiscountCeiling;
        string? reason = null;
        if (needsOverride)
        {
            if (!hasOverrideGrant)
                throw new ForbiddenException(SalesPricingErrors.OverrideRequired,
                    "A manual price, missing rule or discount above the ceiling needs pricing override permission.");
            reason = line.OverrideReason?.Trim();
            if (string.IsNullOrEmpty(reason) || reason.Length > OverrideReasonMaxLength)
                throw new RequestValidationException($"{fieldPrefix}.overrideReason",
                    $"An override reason of 1 to {OverrideReasonMaxLength} characters is required.");
        }

        return new SalesPriceDecision(rule?.RuleId, rule?.Version, rule?.UnitPrice, rule?.DiscountCeiling,
            line.UnitPrice, line.DiscountRate, needsOverride, reason);
    }

    private static void ValidateLine(SalesPriceLineRequest line, string fieldPrefix)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPrefix);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!ValidPrice(line.UnitPrice))
            errors.TryAdd($"{fieldPrefix}.unitPrice", ["Unit price must be nonnegative, below 10^13, with at most 6 decimals."]);
        if (!ValidPercent(line.DiscountRate))
            errors.TryAdd($"{fieldPrefix}.discountRate", ["Discount must be 0 to 100 with at most 4 decimals."]);
        if (line.QuotedRuleId is not null && line.QuotedRuleVersion is null)
            errors.TryAdd($"{fieldPrefix}.pricingRuleVersion", ["Quoted rule version is required with the rule ID."]);
        if (line.QuotedRuleId is null && line.QuotedRuleVersion is not null)
            errors.TryAdd($"{fieldPrefix}.pricingRuleId", ["Quoted rule ID is required with the rule version."]);
        if (line.QuotedRuleId is <= 0)
            errors.TryAdd($"{fieldPrefix}.pricingRuleId", ["Quoted rule ID must be positive."]);
        if (line.QuotedRuleVersion is <= 0)
            errors.TryAdd($"{fieldPrefix}.pricingRuleVersion", ["Quoted rule version must be positive."]);
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private static bool ValidCurrency(string? code) =>
        code is { Length: 3 } && code.All(ch => ch is >= 'A' and <= 'Z');

    private static bool ValidPrice(decimal value) =>
        value >= 0 && value < DecimalNineteenSixLimit && decimal.Round(value, 6) == value;

    private static bool ValidPercent(decimal value) =>
        value is >= 0 and <= 100 && decimal.Round(value, 4) == value;
}
