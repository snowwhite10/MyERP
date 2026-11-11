using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftLinesTests
{
    [Fact]
    public void AcceptsOneAndOneThousandLinesAndPreservesRepeatedReceiptSources()
    {
        PurchaseInvoiceDraftLines.Validate([Line(1, 7, 0.000001m)]);
        var lines = Enumerable.Range(1, 1000)
            .Select(number => Line(number, 7, 1m)).ToArray();
        PurchaseInvoiceDraftLines.Validate(lines);
    }

    [Fact]
    public void RejectsEmptyAndExcessiveLineSets()
    {
        AssertInvalid([]);
        AssertInvalid(Enumerable.Range(1, 1001)
            .Select(number => Line(number, 7, 1m)).ToArray());
    }

    [Theory]
    [InlineData(0, 7, 1)]
    [InlineData(-1, 7, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, -7, 1)]
    [InlineData(1, 7, 0)]
    [InlineData(1, 7, -1)]
    [InlineData(1, 7, 0.0000001)]
    [InlineData(1, 7, 10000000000000)]
    public void RejectsInvalidLineNumbersSourcesAndQuantities(int number, long source, decimal quantity)
        => AssertInvalid([Line(number, source, quantity)]);

    [Fact]
    public void RejectsDuplicateLineNumberWithoutRejectingRepeatedReceiptSource()
        => AssertInvalid([Line(1, 7, 1m), Line(1, 7, 2m)]);

    [Fact]
    public void RejectsNullLineAndNullCollection()
    {
        AssertInvalid([null!]);
        AssertInvalid(null!);
    }

    [Fact]
    public void AcceptsLargestSqlQuantityAndLineNumber()
        => PurchaseInvoiceDraftLines.Validate([Line(int.MaxValue, long.MaxValue,
            9_999_999_999_999.999999m)]);

    [Fact]
    public void RejectsLateInvalidLineAsWholeDocument()
        => AssertInvalid([Line(1, 7, 1m), Line(2, 8, 0.0000001m)]);

    [Theory]
    [InlineData(-1, 10, 0, 0, 0)]
    [InlineData(0.0000001, 10, 0, 0, 0)]
    [InlineData(1, 100.0001, 0, 0, 0)]
    [InlineData(1, 10, -1, 0, 0)]
    [InlineData(1, 10, 0, 0.00001, 0)]
    [InlineData(1, 10, 0, 0, 1000000000000000)]
    public void RejectsInvalidMoneyFields(decimal price, decimal rate, decimal net,
        decimal tax, decimal gross)
        => AssertInvalid([new(1, 7, 1m, price, rate, net, tax, gross)]);

    [Fact]
    public void RejectsDocumentTotalsBeyondSqlAmountLimit()
        => AssertInvalid([
            new(1, 7, 1m, 1m, 0m, 999_999_999_999_999.9999m, 0m, 999_999_999_999_999.9999m),
            new(2, 8, 1m, 1m, 0m, 1m, 0m, 1m)]);

    private static PurchaseInvoiceDraftLine Line(int number, long receipt, decimal quantity) =>
        new(number, receipt, quantity, 1m, 10m, 1m, 0.1m, 1.1m);

    private static void AssertInvalid(IReadOnlyList<PurchaseInvoiceDraftLine> lines)
    {
        Action action = () => PurchaseInvoiceDraftLines.Validate(lines);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_LINES");
    }
}
