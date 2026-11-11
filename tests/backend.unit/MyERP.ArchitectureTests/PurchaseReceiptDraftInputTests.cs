using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseReceiptDraftInputTests
{
    private static readonly DateOnly LeapDay = new(2028, 2, 29);

    [Fact]
    public void ValidDraftReturnsIndependentOrderedSourceSnapshot()
    {
        var lines = new List<PurchaseReceiptDraftLine>
        {
            new(2, 12, 0.000001m), new(1, 11, 2m)
        };
        var result = PurchaseReceiptDraftInput.Validate(new(7, LeapDay, lines));
        lines[0] = new(2, 99, 9m);
        result.Lines.Select(x => x.PurchaseOrderLineId).Should().Equal(11, 12);
        var mutable = result.Lines.Should().BeAssignableTo<IList<PurchaseReceiptDraftLine>>().Subject;
        Action change = () => mutable[0] = new(1, 99, 9m);
        change.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void DuplicateSourceLinesRemainDistinctForLaterAggregation()
    {
        var result = PurchaseReceiptDraftInput.Validate(new(7, LeapDay,
            [new(1, 11, 1m), new(2, 11, 2m)]));
        result.Lines.Should().HaveCount(2);
    }

    [Fact]
    public void InvalidHeaderOrLinesRejectWholeDraft()
    {
        AssertCode(null, "PUR.RECEIPT.INVALID_HEADER");
        AssertCode(new(0, LeapDay, [new(1, 11, 1m)]), "PUR.RECEIPT.INVALID_HEADER");
        AssertCode(new(7, default, [new(1, 11, 1m)]), "PUR.RECEIPT.INVALID_HEADER");
        AssertCode(new(7, LeapDay, []), "PUR.RECEIPT.INVALID_LINES");
        AssertCode(new(7, LeapDay, [new(1, 11, 1m), new(1, 12, 1m)]), "PUR.RECEIPT.INVALID_LINES");
        AssertCode(new(7, LeapDay, [new(1, 11, 1m), new(2, 0, 1m)]), "PUR.RECEIPT.INVALID_LINES");
        AssertCode(new(7, LeapDay, [new(1, 11, 1m), new(2, 12, 0m)]), "PUR.RECEIPT.INVALID_LINES");
    }

    [Fact]
    public void EnforcesQuantityPrecisionAndSqlLimit()
    {
        PurchaseReceiptDraftInput.Validate(new(7, DateOnly.MaxValue,
            [new(1, 11, 0.000001m)])).Lines.Should().ContainSingle();
        AssertCode(new(7, LeapDay, [new(1, 11, 0.0000001m)]), "PUR.RECEIPT.INVALID_LINES");
        AssertCode(new(7, LeapDay, [new(1, 11, 10_000_000_000_000m)]), "PUR.RECEIPT.INVALID_LINES");
    }

    [Fact]
    public void AllowsThousandLinesButRejectsThousandAndOne()
    {
        var lines = Enumerable.Range(1, 1000)
            .Select(number => new PurchaseReceiptDraftLine(number, number, 1m)).ToArray();
        PurchaseReceiptDraftInput.Validate(new(7, LeapDay, lines)).Lines.Should().HaveCount(1000);
        AssertCode(new(7, LeapDay, [.. lines, new(1001, 1001, 1m)]),
            "PUR.RECEIPT.INVALID_LINES");
    }

    [Theory]
    [InlineData("Draft", true)]
    [InlineData("Rejected", true)]
    [InlineData("PendingApproval", false)]
    [InlineData("Posted", false)]
    [InlineData("Cancelled", false)]
    public void OnlyDraftOrRejectedCanBeRevised(string status, bool allowed)
    {
        Action action = () => PurchaseReceiptDraftInput.EnsureEditable(status);
        if (allowed) action.Should().NotThrow();
        else action.Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.RECEIPT.STATUS_NOT_EDITABLE");
    }

    private static void AssertCode(PurchaseReceiptDraftInput? input, string code)
    {
        Action action = () => PurchaseReceiptDraftInput.Validate(input);
        action.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be(code);
    }
}
