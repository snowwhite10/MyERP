using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseReceiptApprovalAssemblyTests
{
    [Fact]
    public void BuildsFromSavedRowsAndOwnEvidenceInSourceOrder()
    {
        var saved = Saved();
        var evidence = Evidence();
        var result = PurchaseReceiptApprovalAssembly.Build(7, 41, saved,
            new(9, 7, "Approved", false), evidence);

        result.Select(x => (x.PurchaseOrderLineId, x.PendingChange, x.PostedChange))
            .Should().Equal((11L, -3m, 3m), (12L, -1m, 1m));
        saved.Lines[0].Quantity.Should().Be(1m);
        evidence[0].Line.PendingQuantity.Should().Be(2m);
    }

    [Fact]
    public void RejectsMismatchedRequestedOrSavedIdentity()
    {
        var saved = Saved();
        foreach (var (company, receipt, snapshot) in new[]
        {
            (8L, 41L, saved), (7L, 42L, saved), (0L, 41L, saved),
            (7L, 41L, saved with { CompanyId = 8 }),
            (7L, 41L, saved with { ReceiptId = 42 }),
            (7L, 41L, saved with { PurchaseOrderId = 10 })
        })
        {
            Action act = () => PurchaseReceiptApprovalAssembly.Build(company, receipt, snapshot,
                new(9, 7, "Approved", false), Evidence());
            act.Should().Throw<BusinessRuleException>();
        }
    }

    [Fact]
    public void InvalidSavedRowsOrEvidenceRejectsWholePlan()
    {
        var saved = Saved();
        var invalid = new[]
        {
            saved with { Lines = [] },
            saved with { Lines = [saved.Lines[0], saved.Lines[0]] },
            saved with { Lines = [saved.Lines[0] with { Quantity = 0m }] }
        };
        foreach (var snapshot in invalid)
        {
            Action act = () => PurchaseReceiptApprovalAssembly.Build(7, 41, snapshot,
                new(9, 7, "Approved", false), Evidence());
            act.Should().Throw<BusinessRuleException>();
        }
        Action stale = () => PurchaseReceiptApprovalAssembly.Build(7, 41, saved,
            new(9, 7, "Approved", false),
            [Evidence()[0], Evidence()[1] with { CurrentReceiptReserved = 0m }]);
        stale.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void PostedReplayNeedsSavedRowsAndMatchingPostedEvidence()
    {
        var saved = Saved() with { Status = "Posted" };
        var evidence = new[]
        {
            Source(12, 1m, 1m, 0m, 0m, 1m),
            Source(11, 3m, 3m, 0m, 0m, 3m)
        };
        var changes = PurchaseReceiptApprovalAssembly.Build(7, 41, saved,
            new(9, 7, "Approved", false), evidence);
        changes.Should().OnlyContain(x => x.PendingChange == 0m && x.PostedChange == 0m);
        Action stale = () => PurchaseReceiptApprovalAssembly.Build(7, 41, saved,
            new(9, 7, "Approved", false),
            [evidence[0], evidence[1] with { CurrentReceiptPosted = 2m }]);
        stale.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void SavedQuantityAndEvidenceMustAgreeAndResultCannotBeEdited()
    {
        var saved = Saved() with
        {
            Lines = [new(1, 12, 1m), new(2, 11, 2m), new(3, 11, 2m)]
        };
        Action mismatch = () => PurchaseReceiptApprovalAssembly.Build(7, 41, saved,
            new(9, 7, "Approved", false), Evidence());
        mismatch.Should().Throw<BusinessRuleException>();

        var changes = PurchaseReceiptApprovalAssembly.Build(7, 41, Saved(),
            new(9, 7, "Approved", false), Evidence());
        Action edit = () => ((IList<PurchaseReceiptApprovalChange>)changes)[0] =
            new(11, 0m, 0m, 0m, 0m);
        edit.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Rejected")]
    [InlineData("Unknown")]
    public void NonFinalStateCannotBuildApproval(string status)
    {
        Action act = () => PurchaseReceiptApprovalAssembly.Build(7, 41,
            Saved() with { Status = status }, new(9, 7, "Approved", false), Evidence());
        act.Should().Throw<BusinessRuleException>();
    }

    private static PurchaseReceiptApprovalSnapshot Saved() => new(41, 7, 9,
        "PendingApproval", new DateOnly(2028, 2, 29),
        [new(1, 12, 1m), new(2, 11, 2m), new(3, 11, 1m)]);

    private static PurchaseReceiptApprovalSource[] Evidence() =>
        [Source(12, 1m, 0m, 2m, 1m, 0m), Source(11, 3m, 0m, 5m, 3m, 0m)];

    private static PurchaseReceiptApprovalSource Source(long id, decimal approved,
        decimal posted, decimal pending, decimal reserved, decimal ownPosted) =>
        new(new(id, 9, 7, false, approved + posted + pending, posted, pending), reserved, ownPosted);
}
