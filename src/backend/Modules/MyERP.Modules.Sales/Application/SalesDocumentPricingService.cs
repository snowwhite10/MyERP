using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>One document line to price; <see cref="FieldPrefix"/> is the request path, e.g. <c>lines[0]</c>.</summary>
public sealed record SalesPricedLine(string FieldPrefix, int LineNo, long ItemId, SalesPriceLineRequest Price);

/// <summary>Saved pricing provenance of one line (BR-SD-029). Override actor/time come from the server only.</summary>
public sealed record SalesLinePricing(SalesLinePricingMode Mode, long? RuleId, int? RuleVersion,
    decimal? RuleUnitPrice, decimal? RuleDiscountCeiling, string? OverrideReason, long? OverrideBy,
    DateTime? OverrideAt)
{
    public void ApplyTo(SalesQuotationLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        line.PricingMode = Mode;
        line.PricingRuleId = RuleId;
        line.PricingRuleVersion = RuleVersion;
        line.RuleUnitPrice = RuleUnitPrice;
        line.RuleDiscountCeiling = RuleDiscountCeiling;
        line.OverrideReason = OverrideReason;
        line.OverrideBy = OverrideBy;
        line.OverrideAt = OverrideAt;
    }

    public void ApplyTo(SalesOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        line.PricingMode = Mode;
        line.PricingRuleId = RuleId;
        line.PricingRuleVersion = RuleVersion;
        line.RuleUnitPrice = RuleUnitPrice;
        line.RuleDiscountCeiling = RuleDiscountCeiling;
        line.OverrideReason = OverrideReason;
        line.OverrideBy = OverrideBy;
        line.OverrideAt = OverrideAt;
    }
}

/// <summary>
/// WU-SD-005a11a (ADR-160): re-selects price rules inside the caller's Serializable save transaction and decides
/// each line with <see cref="SalesPricingRules.Decide"/>. Override authority is <c>SAL.PRICING.OVERRIDE</c> whose SYS
/// data scope must cover the document being saved. Errors carry <c>lineNo</c>; nothing is written here.
/// </summary>
public sealed class SalesDocumentPricingService(MyErpDbContext db, ICurrentAuthorization authorization, IClock clock)
{
    public const string OverridePermission = "SAL.PRICING.OVERRIDE";

    public async Task<IReadOnlyList<SalesLinePricing>> DecideAsync(long companyId, long customerId,
        string currencyCode, DateOnly businessDate, IDataScopedEntity document,
        IReadOnlyList<SalesPricedLine> lines, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lines);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Pricing must be decided inside the caller's save transaction.");

        var current = await authorization.GetAsync(token);
        var overrideScope = current is not null && current.CompanyId == companyId
            ? current.FilterFor(OverridePermission) : null;
        var hasOverride = overrideScope is not null && overrideScope.Allows(document);

        // Both tiers of every item covering the date; Serializable range locks keep them stable until commit.
        var itemIds = lines.Select(line => line.ItemId).Distinct().ToArray();
        var candidates = await db.Set<SalesPriceRule>().AsNoTracking()
            .Where(x => itemIds.Contains(x.ItemId) && x.CurrencyCode == currencyCode && x.IsActive &&
                x.ValidFrom <= businessDate && businessDate <= x.ValidTo &&
                (x.CustomerId == customerId || x.CustomerId == null))
            .ToListAsync(token);
        var snapshots = candidates.Select(SalesPriceRuleService.Snapshot).ToList();

        var result = new List<SalesLinePricing>(lines.Count);
        foreach (var line in lines)
        {
            var lookup = new SalesPriceLookup(companyId, line.ItemId, customerId, currencyCode, businessDate);
            var selection = SalesPricingRules.Select(lookup, snapshots.Where(x => x.ItemId == line.ItemId).ToList());
            SalesPriceDecision decision;
            try
            {
                if (selection.Status == SalesPriceSelectionStatus.DuplicateActiveRule)
                    throw new ConflictException(SalesPricingErrors.DuplicateActiveRule,
                        "More than one active price rule applies; the price cannot be chosen.")
                    {
                        Extensions = new Dictionary<string, object?>
                        {
                            ["conflictingRuleIds"] = selection.ConflictingRuleIds
                        }
                    };
                decision = SalesPricingRules.Decide(selection, line.Price, hasOverride, line.FieldPrefix);
            }
            catch (ErpException ex) when (ex is ConflictException or ForbiddenException)
            {
                throw WithLine(ex, line.LineNo);
            }

            result.Add(decision.IsOverride
                ? new SalesLinePricing(SalesLinePricingMode.Override, decision.RuleId, decision.RuleVersion,
                    decision.RulePrice, decision.RuleDiscountCeiling, decision.OverrideReason, current!.UserId,
                    clock.LocalNow)
                : new SalesLinePricing(SalesLinePricingMode.Rule, decision.RuleId, decision.RuleVersion,
                    decision.RulePrice, decision.RuleDiscountCeiling, null, null, null));
        }
        return result;
    }

    private static ErpException WithLine(ErpException ex, int lineNo)
    {
        var extensions = new Dictionary<string, object?>(ex.Extensions ?? new Dictionary<string, object?>())
        {
            ["lineNo"] = lineNo
        };
        return ex switch
        {
            ConflictException => new ConflictException(ex.ErrorCode, ex.Message) { Extensions = extensions },
            _ => new ForbiddenException(ex.ErrorCode, ex.Message) { Extensions = extensions },
        };
    }
}

/// <summary>Applies the transactional pricing decision to every line of a built quotation snapshot.</summary>
internal static class SalesQuotationPricing
{
    public static async Task ApplyAsync(SalesDocumentPricingService pricing, SalesQuotation quotation,
        SalesQuotationInput input, IDataScopedEntity document, CancellationToken token)
    {
        var requests = input.Lines!.Select((line, index) => new SalesPricedLine($"lines[{index}]", line.LineNo,
            line.ItemId, line.ToPriceRequest())).ToList();
        var decisions = await pricing.DecideAsync(quotation.CompanyId, quotation.CustomerId,
            quotation.CurrencyCode, quotation.BusinessDate, document, requests, token);
        var lines = quotation.Lines.ToDictionary(line => line.LineNo);
        for (var index = 0; index < requests.Count; index++)
            decisions[index].ApplyTo(lines[requests[index].LineNo]);
    }
}

/// <summary>Applies the transactional pricing decision to every line of a built manual order (WU-SD-005a11b).</summary>
internal static class SalesOrderPricing
{
    public static async Task ApplyAsync(SalesDocumentPricingService pricing, SalesOrder order,
        SalesOrderDraftInput input, IDataScopedEntity document, CancellationToken token)
    {
        var requests = input.Lines!.Select((line, index) => new SalesPricedLine($"lines[{index}]", line.LineNo,
            line.ItemId, line.ToPriceRequest())).ToList();
        var decisions = await pricing.DecideAsync(order.CompanyId, order.CustomerId, order.CurrencyCode,
            order.BusinessDate, document, requests, token);
        var lines = order.Lines.ToDictionary(line => line.LineNo);
        for (var index = 0; index < requests.Count; index++)
            decisions[index].ApplyTo(lines[requests[index].LineNo]);
    }
}

/// <summary>Shape checks for the optional quote hint pair and override reason on a document line (ADR-160).</summary>
internal static class SalesPricingHints
{
    public static void Validate(string field, long? ruleId, int? ruleVersion, string? overrideReason,
        Action<string, string> fail)
    {
        if (ruleId is not null && ruleVersion is null)
            fail($"{field}.pricingRuleVersion", "Quoted rule version is required with the rule ID.");
        if (ruleId is null && ruleVersion is not null)
            fail($"{field}.pricingRuleId", "Quoted rule ID is required with the rule version.");
        if (ruleId is <= 0) fail($"{field}.pricingRuleId", "Quoted rule ID must be positive.");
        if (ruleVersion is <= 0) fail($"{field}.pricingRuleVersion", "Quoted rule version must be positive.");
        if (overrideReason?.Trim().Length > SalesPricingRules.OverrideReasonMaxLength)
            fail($"{field}.overrideReason",
                $"Override reason must not exceed {SalesPricingRules.OverrideReasonMaxLength} characters.");
    }
}
