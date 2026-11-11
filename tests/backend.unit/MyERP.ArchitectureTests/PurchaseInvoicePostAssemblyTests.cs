using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoicePostAssemblyTests
{
    private static PurchaseInvoicePostSnapshot Snapshot(decimal net = 2m, string status = "Matched") => new(
        7, status, new DateOnly(2026, 11, 11), new(1, 2, 3, "TWD"),
        [new(10, new(1, 1, 2, 0, 2, 0, net, 0, net))], []);

    private static readonly PurchaseInvoiceOrderIdentity Order = new(1, 2, 3, "TWD", "Approved", false);
    private static readonly PurchaseInvoiceReceiptIdentity Receipt =
        new(10, 1, 2, 3, 3, 4, 4, "TWD", "Posted", false);
    private static readonly PurchaseInvoicePostingSource Source =
        new(10, new(3, 0, 0, 0, 1, 1), 0);

    [Fact]
    public void ValidSavedSnapshotBindsPreflightFingerprintAndFiPayload()
    {
        var result = PurchaseInvoicePostAssembly.Build(1, 7, Snapshot(), Order, [Receipt], [Source]);
        result.Posting.IsReplay.Should().BeFalse();
        result.Posting.Changes.Should().ContainSingle().Which.InvoicedChange.Should().Be(1);
        result.Payable.CompanyId.Should().Be(1);
        result.Payable.PurchaseInvoiceId.Should().Be(7);
        result.Payable.SupplierId.Should().Be(2);
        result.Payable.Currency.Should().Be("TWD");
        result.Payable.BusinessDate.Should().Be(new DateOnly(2026, 11, 11));
        result.Payable.SnapshotHash.Should().Be(PurchaseInvoiceSnapshotFingerprint.Create(
            1, 7, 2, "TWD", new DateOnly(2026, 11, 11), Snapshot().Lines, []));
        result.Payable.Lines.Should().ContainSingle().Which.InvoiceGross.Should().Be(2);
    }

    [Theory]
    [InlineData(2, 7)]
    [InlineData(1, 8)]
    public void RequestedIdentityMustEqualSavedSnapshot(long companyId, long invoiceId)
    {
        Action act = () => PurchaseInvoicePostAssembly.Build(companyId, invoiceId,
            Snapshot(), Order, [Receipt], [Source]);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");
    }

    [Fact]
    public void InvalidStatusOrCurrentSourceDoesNotReturnPayableRequest()
    {
        Action status = () => PurchaseInvoicePostAssembly.Build(1, 7,
            Snapshot(status: "Draft"), Order, [Receipt], [Source]);
        Action source = () => PurchaseInvoicePostAssembly.Build(1, 7,
            Snapshot(), Order, [Receipt with { Status = "Draft" }], [Source]);
        status.Should().Throw<BusinessRuleException>();
        source.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void StaleAcceptedDifferenceRejectsWholeAssembly()
    {
        Action act = () => PurchaseInvoicePostAssembly.Build(1, 7,
            Snapshot(net: 2.0001m), Order, [Receipt], [Source]);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_POSTING_DIFFERENCE");
    }

    [Fact]
    public void PostedReplayReturnsSameFiSourceWithNoQuantityChange()
    {
        var posted = new PurchaseInvoicePostingSource(10, new(3, 0, 0, 1, 0, 0), 1);
        var result = PurchaseInvoicePostAssembly.Build(1, 7,
            Snapshot(status: "Posted"), Order, [Receipt], [posted]);
        result.Posting.IsReplay.Should().BeTrue();
        result.Posting.Changes.Should().OnlyContain(x => x.InvoicedChange == 0 && x.ReservedChange == 0);
        result.Payable.SnapshotHash.Should().Be(PurchaseInvoiceSnapshotFingerprint.Create(
            1, 7, 2, "TWD", new DateOnly(2026, 11, 11), Snapshot().Lines, []));
    }

    [Fact]
    public void MissingOrDuplicateSavedLinesRejectWithoutPayableRequest()
    {
        Action empty = () => PurchaseInvoicePostAssembly.Build(1, 7,
            Snapshot() with { Lines = [] }, Order, [Receipt], [Source]);
        Action duplicate = () => PurchaseInvoicePostAssembly.Build(1, 7,
            Snapshot() with { Lines = [Snapshot().Lines[0], Snapshot().Lines[0]] },
            Order, [Receipt], [Source]);
        empty.Should().Throw<BusinessRuleException>();
        duplicate.Should().Throw<BusinessRuleException>();
    }
}
