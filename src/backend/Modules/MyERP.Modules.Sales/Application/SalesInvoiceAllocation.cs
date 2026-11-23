using MyERP.Shared.Errors;
using MyERP.Shared.Monetary;

namespace MyERP.Modules.Sales.Application;

/// <summary>Saved order-line quantity and commercial amounts carried to the invoice (BR-SD-039).</summary>
public sealed record SalesInvoiceOrderLineSnapshot(long OrderLineId, decimal OrderedQuantity,
    decimal OriginalNet, decimal OriginalTax, decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>Quantity and net/tax components already billed on committed invoices for one order line.</summary>
public sealed record SalesInvoicePriorBilling(long OrderLineId, decimal Quantity,
    decimal OriginalNet, decimal OriginalTax, decimal BaseNet, decimal BaseTax);

/// <summary>One requested invoice line, in line-number order.</summary>
public sealed record SalesInvoicePortion(int LineNo, long OrderLineId, decimal Quantity);

public sealed record SalesInvoicePortionAmounts(int LineNo, decimal OriginalNet, decimal OriginalTax,
    decimal OriginalGross, decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>
/// WU-SD-007a2 (BR-SD-039): cumulative allocation of saved order-line amounts. For each order line the new cumulative
/// component is <c>Money.Round(saved × cumulativeQuantity ÷ ordered)</c> (base components in the base currency) and the
/// portion is that minus everything billed before, so the portion completing the ordered quantity receives the exact
/// saved remainder and repeated partial invoices never drift. Gross = net + tax per portion. Inconsistent snapshots
/// (gross ≠ net + tax, negative amounts, over-billing, negative portion, overflow) are rejected, never patched.
/// </summary>
public static class SalesInvoiceAllocation
{
    public const string SourceInvalid = "SAL.INVOICE.SOURCE_INVALID";

    public static IReadOnlyList<SalesInvoicePortionAmounts> Allocate(string currency, string baseCurrency,
        IReadOnlyList<SalesInvoiceOrderLineSnapshot> sources, IReadOnlyList<SalesInvoicePriorBilling> prior,
        IReadOnlyList<SalesInvoicePortion> portions)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(portions);
        var byId = sources.ToDictionary(x => x.OrderLineId);
        var billed = prior.ToDictionary(x => x.OrderLineId);
        var running = new Dictionary<long, SalesInvoicePriorBilling>();
        var result = new List<SalesInvoicePortionAmounts>(portions.Count);
        foreach (var portion in portions)
        {
            if (!byId.TryGetValue(portion.OrderLineId, out var source) || !Consistent(source))
                throw Invalid(portion.LineNo);
            var before = running.GetValueOrDefault(portion.OrderLineId)
                ?? billed.GetValueOrDefault(portion.OrderLineId)
                ?? new SalesInvoicePriorBilling(portion.OrderLineId, 0m, 0m, 0m, 0m, 0m);
            try
            {
                var cumulative = checked(before.Quantity + portion.Quantity);
                if (portion.Quantity <= 0m || before.Quantity < 0m || cumulative > source.OrderedQuantity)
                    throw Invalid(portion.LineNo);
                var after = new SalesInvoicePriorBilling(portion.OrderLineId, cumulative,
                    Share(source.OriginalNet, cumulative, source.OrderedQuantity, currency),
                    Share(source.OriginalTax, cumulative, source.OrderedQuantity, currency),
                    Share(source.BaseNet, cumulative, source.OrderedQuantity, baseCurrency),
                    Share(source.BaseTax, cumulative, source.OrderedQuantity, baseCurrency));
                var amounts = new SalesInvoicePortionAmounts(portion.LineNo,
                    after.OriginalNet - before.OriginalNet, after.OriginalTax - before.OriginalTax, 0m,
                    after.BaseNet - before.BaseNet, after.BaseTax - before.BaseTax, 0m);
                if (amounts.OriginalNet < 0m || amounts.OriginalTax < 0m || amounts.BaseNet < 0m || amounts.BaseTax < 0m)
                    throw Invalid(portion.LineNo);
                result.Add(amounts with
                {
                    OriginalGross = amounts.OriginalNet + amounts.OriginalTax,
                    BaseGross = amounts.BaseNet + amounts.BaseTax
                });
                running[portion.OrderLineId] = after;
            }
            catch (OverflowException)
            {
                throw Invalid(portion.LineNo);
            }
        }
        return result;
    }

    /// <summary>The completing portion takes the saved component itself, independent of arithmetic precision.</summary>
    private static decimal Share(decimal saved, decimal cumulative, decimal ordered, string currency) =>
        cumulative == ordered ? saved : Money.Round(saved * cumulative / ordered, currency);

    private static bool Consistent(SalesInvoiceOrderLineSnapshot x) =>
        x.OrderedQuantity > 0m && x.OriginalNet >= 0m && x.OriginalTax >= 0m && x.BaseNet >= 0m && x.BaseTax >= 0m &&
        x.OriginalGross == x.OriginalNet + x.OriginalTax && x.BaseGross == x.BaseNet + x.BaseTax;

    private static BusinessRuleException Invalid(int lineNo) =>
        new(SourceInvalid, "The saved order-line amounts cannot be allocated to this invoice line.")
        {
            Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo }
        };
}
