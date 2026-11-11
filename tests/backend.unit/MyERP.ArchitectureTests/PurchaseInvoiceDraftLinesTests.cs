using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceDraftLinesTests
{
    [Fact]
    public void AcceptsOneAndOneThousandLinesAndPreservesRepeatedReceiptSources()
    {
        PurchaseInvoiceDraftLines.Validate([new(1, 7, 0.000001m)]);
        var lines = Enumerable.Range(1, 1000)
            .Select(number => new PurchaseInvoiceDraftLine(number, 7, 1m)).ToArray();
        PurchaseInvoiceDraftLines.Validate(lines);
    }

    [Fact]
    public void RejectsEmptyAndExcessiveLineSets()
    {
        AssertInvalid([]);
        AssertInvalid(Enumerable.Range(1, 1001)
            .Select(number => new PurchaseInvoiceDraftLine(number, 7, 1m)).ToArray());
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
        => AssertInvalid([new(number, source, quantity)]);

    [Fact]
    public void RejectsDuplicateLineNumberWithoutRejectingRepeatedReceiptSource()
        => AssertInvalid([new(1, 7, 1m), new(1, 7, 2m)]);

    [Fact]
    public void RejectsNullLineAndNullCollection()
    {
        AssertInvalid([null!]);
        AssertInvalid(null!);
    }

    [Fact]
    public void AcceptsLargestSqlQuantityAndLineNumber()
        => PurchaseInvoiceDraftLines.Validate([new(int.MaxValue, long.MaxValue,
            9_999_999_999_999.999999m)]);

    [Fact]
    public void RejectsLateInvalidLineAsWholeDocument()
        => AssertInvalid([new(1, 7, 1m), new(2, 8, 0.0000001m)]);

    private static void AssertInvalid(IReadOnlyList<PurchaseInvoiceDraftLine> lines)
    {
        Action action = () => PurchaseInvoiceDraftLines.Validate(lines);
        action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_LINES");
    }
}
