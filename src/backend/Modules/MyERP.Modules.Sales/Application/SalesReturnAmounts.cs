using MyERP.Shared.Errors;
using MyERP.Shared.Monetary;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// Quantity and net/tax components of one source (an invoice line for billed portions, an order line for unbilled
/// portions) that no committed document has consumed yet.
/// </summary>
public sealed record SalesReturnRemaining(decimal Quantity, decimal OriginalNet, decimal OriginalTax,
    decimal BaseNet, decimal BaseTax);

public sealed record SalesReturnPortionAmounts(decimal OriginalNet, decimal OriginalTax, decimal OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross)
{
    public static readonly SalesReturnPortionAmounts Zero = new(0m, 0m, 0m, 0m, 0m, 0m);

    public SalesReturnPortionAmounts Add(SalesReturnPortionAmounts other) => new(
        OriginalNet + other.OriginalNet, OriginalTax + other.OriginalTax, OriginalGross + other.OriginalGross,
        BaseNet + other.BaseNet, BaseTax + other.BaseTax, BaseGross + other.BaseGross);
}

/// <summary>
/// WU-SD-008a2a (ADR-185, BR-SD-046/047): remaining-proportional allocation of saved amounts to a returned portion.
/// Each component is <c>Money.Round(remaining × quantity ÷ remainingQuantity)</c> (base components in the base
/// currency); a portion that consumes the whole remaining quantity takes the exact remaining components, so successive
/// returns never drift and never exceed the saved amounts. Gross = net + tax per portion. Several portions against the
/// same source in one request are taken in sequence from the returned remainder. Inconsistent remainders (negative
/// amounts, non-positive quantity) or over-consumption are rejected with <see cref="SourceInvalid"/>, never patched.
/// </summary>
public static class SalesReturnAmounts
{
    public const string SourceInvalid = "SAL.RETURN.SOURCE_INVALID";

    public static (SalesReturnPortionAmounts Amounts, SalesReturnRemaining After) Take(SalesReturnRemaining remaining,
        decimal quantity, string currency, string baseCurrency, int lineNo)
    {
        ArgumentNullException.ThrowIfNull(remaining);
        if (quantity <= 0m || remaining.Quantity <= 0m || quantity > remaining.Quantity ||
            remaining.OriginalNet < 0m || remaining.OriginalTax < 0m || remaining.BaseNet < 0m || remaining.BaseTax < 0m)
            throw Invalid(lineNo);
        SalesReturnPortionAmounts amounts;
        if (quantity == remaining.Quantity)
        {
            amounts = Build(remaining.OriginalNet, remaining.OriginalTax, remaining.BaseNet, remaining.BaseTax);
        }
        else
        {
            try
            {
                amounts = Build(Share(remaining.OriginalNet), Share(remaining.OriginalTax),
                    ShareBase(remaining.BaseNet), ShareBase(remaining.BaseTax));
            }
            catch (OverflowException)
            {
                throw Invalid(lineNo);
            }
        }
        var after = new SalesReturnRemaining(remaining.Quantity - quantity,
            remaining.OriginalNet - amounts.OriginalNet, remaining.OriginalTax - amounts.OriginalTax,
            remaining.BaseNet - amounts.BaseNet, remaining.BaseTax - amounts.BaseTax);
        return (amounts, after);

        decimal Share(decimal component) => Money.Round(component * quantity / remaining.Quantity, currency);
        decimal ShareBase(decimal component) => Money.Round(component * quantity / remaining.Quantity, baseCurrency);
    }

    private static SalesReturnPortionAmounts Build(decimal net, decimal tax, decimal baseNet, decimal baseTax) =>
        new(net, tax, net + tax, baseNet, baseTax, baseNet + baseTax);

    public static BusinessRuleException Invalid(int lineNo) =>
        new(SourceInvalid, "The saved source amounts cannot be allocated to this return line.")
        {
            Extensions = new Dictionary<string, object?> { ["lineNo"] = lineNo }
        };
}
