using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>
/// SD-INVOICE-U01～U06 (WU-SD-007a2, BR-SD-039): cumulative-quantity allocation of saved order-line net/tax, final
/// remainder, separate original/base rounding and rejection of inconsistent source snapshots.
/// </summary>
public sealed class SalesInvoiceAllocationTests
{
    // USD order line: 3 units, net 100.00 + tax 5.00; base TWD (0 decimals) 3,100 + 155.
    private static readonly SalesInvoiceOrderLineSnapshot Line =
        new(10, 3m, 100m, 5m, 105m, 3100m, 155m, 3255m);

    [Fact]
    public void ThreeSeparateInvoicesOfOneThirdReconcileExactlyToTheSavedLine()
    {
        // SD-INVOICE-U01 / SD-INVOICE-007: each invoice sees the previous ones as prior billing.
        var first = Single(Allocate([], new SalesInvoicePortion(1, 10, 1m)));
        first.Should().Be(new SalesInvoicePortionAmounts(1, 33.33m, 1.67m, 35.00m, 1033m, 52m, 1085m));

        var second = Single(Allocate([Prior(1m, first)], new SalesInvoicePortion(1, 10, 1m)));
        second.Should().Be(new SalesInvoicePortionAmounts(1, 33.34m, 1.66m, 35.00m, 1034m, 51m, 1085m));

        var third = Single(Allocate([Prior(2m, first, second)], new SalesInvoicePortion(1, 10, 1m)));
        third.Should().Be(new SalesInvoicePortionAmounts(1, 33.33m, 1.67m, 35.00m, 1033m, 52m, 1085m));

        (first.OriginalNet + second.OriginalNet + third.OriginalNet).Should().Be(100m);
        (first.OriginalTax + second.OriginalTax + third.OriginalTax).Should().Be(5m);
        (first.OriginalGross + second.OriginalGross + third.OriginalGross).Should().Be(105m);
        (first.BaseNet + second.BaseNet + third.BaseNet).Should().Be(3100m);
        (first.BaseTax + second.BaseTax + third.BaseTax).Should().Be(155m);
        (first.BaseGross + second.BaseGross + third.BaseGross).Should().Be(3255m);
    }

    [Fact]
    public void PortionsOfOneOrderLineInOneInvoiceAccumulateInLineOrder()
    {
        // SD-INVOICE-U02: two delivery lines of the same order line in one invoice; the second gets the remainder.
        var result = Allocate([], new SalesInvoicePortion(1, 10, 1m), new SalesInvoicePortion(2, 10, 2m));
        result.Should().Equal(
            new SalesInvoicePortionAmounts(1, 33.33m, 1.67m, 35.00m, 1033m, 52m, 1085m),
            new SalesInvoicePortionAmounts(2, 66.67m, 3.33m, 70.00m, 2067m, 103m, 2170m));
    }

    [Fact]
    public void SixDecimalPartialQuantitiesAndZeroTaxStayNonNegativeAndExact()
    {
        // SD-INVOICE-U03 / SD-INVOICE-005: micro-unit portions and a zero-tax TWD line.
        var twd = new SalesInvoiceOrderLineSnapshot(20, 1.000001m, 999m, 0m, 999m, 999m, 0m, 999m);
        var result = SalesInvoiceAllocation.Allocate("TWD", "TWD", [twd], [],
            [new SalesInvoicePortion(1, 20, 0.000001m), new SalesInvoicePortion(2, 20, 1m)]);
        result.Should().Equal(
            new SalesInvoicePortionAmounts(1, 0m, 0m, 0m, 0m, 0m, 0m),
            new SalesInvoicePortionAmounts(2, 999m, 0m, 999m, 999m, 0m, 999m));
    }

    [Fact]
    public void LargestStorableAmountsDoNotOverflowOrDrift()
    {
        // SD-INVOICE-U04: decimal(19,4) amount and decimal(19,6) quantity bounds.
        var huge = new SalesInvoiceOrderLineSnapshot(30, 9_999_999_999_999.999999m, 999_999_999_999_999.98m, 0.01m,
            999_999_999_999_999.99m, 999_999_999_999_999.98m, 0.01m, 999_999_999_999_999.99m);
        var result = SalesInvoiceAllocation.Allocate("USD", "USD", [huge], [],
            [new SalesInvoicePortion(1, 30, 0.000001m), new SalesInvoicePortion(2, 30, 9_999_999_999_999.999998m)]);
        result.Sum(x => x.OriginalNet).Should().Be(999_999_999_999_999.98m);
        result.Sum(x => x.OriginalTax).Should().Be(0.01m);
        result.Should().OnlyContain(x => x.OriginalNet >= 0 && x.OriginalTax >= 0 &&
            x.OriginalGross == x.OriginalNet + x.OriginalTax && x.BaseGross == x.BaseNet + x.BaseTax);
    }

    public static TheoryData<string, SalesInvoiceOrderLineSnapshot, SalesInvoicePriorBilling[], decimal> Invalid() => new()
    {
        { "gross", Line with { OriginalGross = 105.01m }, [], 1m },
        { "base gross", Line with { BaseGross = 3254m }, [], 1m },
        { "negative tax", Line with { OriginalTax = -5m, OriginalGross = 95m }, [], 1m },
        { "zero ordered", Line with { OrderedQuantity = 0m }, [], 1m },
        { "above ordered", Line, [], 3.000001m },
        { "above ordered after prior", Line, [new(10, 2.5m, 83.33m, 4.17m, 2583m, 129m)], 0.500001m },
        { "prior above saved", Line, [new(10, 1m, 100.01m, 0m, 0m, 0m)], 1m },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InconsistentSourceSnapshotsNameTheInvoiceLine(string _, SalesInvoiceOrderLineSnapshot source,
        SalesInvoicePriorBilling[] prior, decimal quantity)
    {
        // SD-INVOICE-U05: BR-SD-039 rejects inconsistent snapshots instead of producing negative or drifting portions.
        Action act = () => SalesInvoiceAllocation.Allocate("USD", "TWD", [source], prior,
            [new SalesInvoicePortion(7, 10, quantity)]);
        var error = act.Should().Throw<BusinessRuleException>().Which;
        error.ErrorCode.Should().Be("SAL.INVOICE.SOURCE_INVALID");
        error.Extensions!["lineNo"].Should().Be(7);
    }

    [Fact]
    public void PortionWithoutLoadedSourceIsRejected()
    {
        // SD-INVOICE-U06: the caller must load every referenced order line.
        Action act = () => SalesInvoiceAllocation.Allocate("USD", "TWD", [Line], [],
            [new SalesInvoicePortion(4, 99, 1m)]);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.INVOICE.SOURCE_INVALID");
    }

    private static IReadOnlyList<SalesInvoicePortionAmounts> Allocate(SalesInvoicePriorBilling[] prior,
        params SalesInvoicePortion[] portions) =>
        SalesInvoiceAllocation.Allocate("USD", "TWD", [Line], prior, portions);

    private static SalesInvoicePortionAmounts Single(IReadOnlyList<SalesInvoicePortionAmounts> result) =>
        result.Should().ContainSingle().Subject;

    private static SalesInvoicePriorBilling Prior(decimal quantity, params SalesInvoicePortionAmounts[] billed) =>
        new(10, quantity, billed.Sum(x => x.OriginalNet), billed.Sum(x => x.OriginalTax),
            billed.Sum(x => x.BaseNet), billed.Sum(x => x.BaseTax));
}

/// <summary>SD-INVOICE-U07～U08 (WU-SD-007a2, SD-INVOICE-004/008): ISSUE input shape and canonical fingerprint.</summary>
public sealed class SalesInvoiceIssueInputTests
{
    private static readonly DateOnly Day = new(2028, 2, 29);

    public static TheoryData<string, SalesInvoiceIssueInput, string> Invalid()
    {
        var ok = new SalesInvoiceLineInput(1, 5, 1m);
        return new()
        {
            { "null key", new(null, Day, [ok]), "requestKey" },
            { "blank key", new("  ", Day, [ok]), "requestKey" },
            { "oversize key", new(new string('k', 65), Day, [ok]), "requestKey" },
            { "bad char key", new("key with space", Day, [ok]), "requestKey" },
            { "no date", new("K1", default, [ok]), "businessDate" },
            { "null lines", new("K1", Day, null), "lines" },
            { "empty lines", new("K1", Day, []), "lines" },
            { "too many lines", new("K1", Day, [.. Enumerable.Range(1, 1001).Select(i => new SalesInvoiceLineInput(i, i, 1m))]), "lines" },
            { "null line", new("K1", Day, [null!]), "lines[0]" },
            { "zero line no", new("K1", Day, [ok with { LineNo = 0 }]), "lines[0].lineNo" },
            { "zero source", new("K1", Day, [ok with { DeliveryLineId = 0 }]), "lines[0].deliveryLineId" },
            { "zero quantity", new("K1", Day, [ok with { Quantity = 0m }]), "lines[0].quantity" },
            { "negative quantity", new("K1", Day, [ok with { Quantity = -1m }]), "lines[0].quantity" },
            { "seven decimals", new("K1", Day, [ok with { Quantity = 0.0000001m }]), "lines[0].quantity" },
            { "quantity too large", new("K1", Day, [ok with { Quantity = 10_000_000_000_000m }]), "lines[0].quantity" },
            { "duplicate line no", new("K1", Day, [ok, ok with { DeliveryLineId = 6 }]), "lines[1].lineNo" },
            { "duplicate source", new("K1", Day, [ok, ok with { LineNo = 2 }]), "lines[1].deliveryLineId" },
        };
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void InvalidShapesFailByField(string _, SalesInvoiceIssueInput input, string field)
    {
        // SD-INVOICE-U07: 400 by field before any SQL work.
        Action act = input.Validate;
        act.Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void FingerprintIsCanonicalForScaleAndOrderButSensitiveToContent()
    {
        // SD-INVOICE-U08: 1 and 1.000000 are the same request; lines are canonicalised by line number.
        var a = new SalesInvoiceIssueInput("K1", Day, [new(2, 6, 1.5m), new(1, 5, 1m)]);
        var b = new SalesInvoiceIssueInput("K1", Day, [new(1, 5, 1.000000m), new(2, 6, 1.50m)]);
        a.Validate();
        a.Fingerprint().Should().Be(b.Fingerprint()).And.HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        a.Fingerprint().Should().NotBe((b with { BusinessDate = Day.AddDays(1) }).Fingerprint());
        a.Fingerprint().Should().NotBe(new SalesInvoiceIssueInput("K1", Day, [new(1, 5, 1m), new(2, 6, 1.500001m)]).Fingerprint());
        a.Fingerprint().Should().NotBe(new SalesInvoiceIssueInput("K1", Day, [new(1, 6, 1.5m), new(2, 5, 1m)]).Fingerprint());
        // The key itself is the lookup, not part of the payload.
        a.Fingerprint().Should().Be((a with { RequestKey = "K2" }).Fingerprint());
    }
}
