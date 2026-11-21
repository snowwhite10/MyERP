using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// WU-SD-005b1a (ADR-161): adds current MD item/customer code and name to price-rule responses through MD contracts,
/// so the price-rule page needs no MD permission and SD never reads MD tables. Labels are current values, not
/// snapshots; inactive records keep their label, a soft-deleted item has none (the page falls back to its ID).
/// One batch read per kind for a whole page.
/// </summary>
public sealed class SalesPriceRuleLabels(IInventorySelectionOptions references, ICustomerLabels customers)
{
    public async Task<IReadOnlyList<SalesPriceRuleDetail>> DescribeAsync(IReadOnlyList<SalesPriceRuleDetail> rules,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0) return rules;
        var saved = await references.SavedAsync([.. rules.Select(x => x.ItemId).Distinct()], [], [], token);
        var items = saved.Items.ToDictionary(x => x.Id);
        var customerIds = rules.Where(x => x.CustomerId is not null).Select(x => x.CustomerId!.Value).Distinct().ToArray();
        var labels = customerIds.Length == 0
            ? new Dictionary<long, CustomerLabel>()
            : await customers.GetLabelsAsync(customerIds, token);
        return [.. rules.Select(rule =>
        {
            var item = items.GetValueOrDefault(rule.ItemId);
            var customer = rule.CustomerId is { } id ? labels.GetValueOrDefault(id) : null;
            return rule with
            {
                ItemCode = item?.Code, ItemName = item?.Name,
                CustomerCode = customer?.Code, CustomerName = customer?.Name
            };
        })];
    }

    public async Task<SalesPriceRuleDetail> DescribeAsync(SalesPriceRuleDetail rule, CancellationToken token = default) =>
        (await DescribeAsync([rule], token))[0];

    public Task<SalesPriceRuleDetail> DescribeAsync(SalesPriceRule rule, CancellationToken token = default) =>
        DescribeAsync(SalesPriceRuleService.Map(rule), token);
}
