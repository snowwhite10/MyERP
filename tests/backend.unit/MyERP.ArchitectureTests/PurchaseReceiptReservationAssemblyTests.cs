using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseReceiptReservationAssemblyTests
{
    [Fact]
    public void SubmitUsesSavedRowsAndReturnsSortedReadonlyPlan()
    {
        var saved = Saved("Draft");
        var sources = Sources(0m, 0m);
        var result = PurchaseReceiptReservationAssembly.ForSubmit(5, 41, saved,
            new(7, 5, "Approved", false), sources);
        result.Select(x => (x.PurchaseOrderLineId, x.Change))
            .Should().Equal((11L, 3m), (12L, 1m));
        saved.Lines[0].Quantity.Should().Be(1m);
        sources[0].Line.PendingQuantity.Should().Be(0m);
        Action edit = () => ((IList<PurchaseReceiptReservationChange>)result)[0] = new(11, 0m, 0m);
        edit.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void PendingReplayAndRejectUseOnlyOwnSavedReservations()
    {
        var saved = Saved("PendingApproval");
        var sources = Sources(3m, 1m);
        PurchaseReceiptReservationAssembly.ForSubmit(5, 41, saved,
            new(7, 5, "Approved", false), sources).Should().OnlyContain(x => x.Change == 0m);
        PurchaseReceiptReservationAssembly.ForReject(5, 41, saved,
            new(7, 5, "Approved", false), sources)
            .Select(x => x.Change).Should().Equal(-3m, -1m);
    }

    [Fact]
    public void MismatchedIdentityRowsAndEvidenceRejectWholePlan()
    {
        var saved = Saved("PendingApproval");
        var order = new PurchaseReceiptOrderSnapshot(7, 5, "Approved", false);
        foreach (var snapshot in new[]
        {
            saved with { CompanyId = 6 }, saved with { ReceiptId = 42 },
            saved with { PurchaseOrderId = 8 }, saved with { Lines = [] },
            saved with { Lines = [saved.Lines[0], saved.Lines[0]] }
        })
        {
            Action act = () => PurchaseReceiptReservationAssembly.ForReject(5, 41, snapshot, order, Sources(3m, 1m));
            act.Should().Throw<BusinessRuleException>();
        }
        Action stale = () => PurchaseReceiptReservationAssembly.ForReject(5, 41, saved, order, Sources(2m, 1m));
        stale.Should().Throw<BusinessRuleException>();
        Action missing = () => PurchaseReceiptReservationAssembly.ForReject(5, 41, saved, order, [Sources(3m, 1m)[0]]);
        missing.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void InvalidStatesAndRequestIdentityReject()
    {
        var order = new PurchaseReceiptOrderSnapshot(7, 5, "Approved", false);
        foreach (var status in new[] { "Draft", "Rejected", "Posted", "Unknown" })
        {
            Action reject = () => PurchaseReceiptReservationAssembly.ForReject(5, 41,
                Saved(status), order, Sources(3m, 1m));
            reject.Should().Throw<BusinessRuleException>();
        }
        Action wrongCompany = () => PurchaseReceiptReservationAssembly.ForSubmit(6, 41,
            Saved("Draft"), order, Sources(0m, 0m));
        wrongCompany.Should().Throw<BusinessRuleException>();
        Action wrongReceipt = () => PurchaseReceiptReservationAssembly.ForSubmit(5, 42,
            Saved("Draft"), order, Sources(0m, 0m));
        wrongReceipt.Should().Throw<BusinessRuleException>();
        Action posted = () => PurchaseReceiptReservationAssembly.ForSubmit(5, 41,
            Saved("Posted"), order, Sources(0m, 0m));
        posted.Should().Throw<BusinessRuleException>();
    }

    private static PurchaseReceiptApprovalSnapshot Saved(string status) => new(41, 5, 7,
        status, new DateOnly(2028, 2, 29),
        [new(3, 12, 1m), new(1, 11, 2m), new(2, 11, 1m)]);

    private static PurchaseReceiptReservationSource[] Sources(decimal own11, decimal own12) =>
        [new(new(12, 7, 5, false, 4m, 1m, own12), own12),
         new(new(11, 7, 5, false, 10m, 2m, own11), own11)];
}
