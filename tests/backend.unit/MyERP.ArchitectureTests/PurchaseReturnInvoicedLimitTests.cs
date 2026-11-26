using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>PUR-RETLIM-001～008（WU-PUR-008b5，BR-PUR-044）：退貨不得動用已過帳開票量或 Matched／Disputed 發票預留。</summary>
public sealed class PurchaseReturnInvoicedLimitTests
{
    private static PurchaseReturnSourceQuantities Q(decimal received, decimal returned = 0, decimal pending = 0,
        decimal invoiced = 0, decimal matched = 0, decimal current = 0) =>
        new(received, returned, pending, invoiced, matched, current);

    private static PurchaseReturnReceiptRequest R(long id, decimal quantity) => new(id, quantity);
    private static PurchaseReturnReceiptSource S(long id, PurchaseReturnSourceQuantities q) => new(id, q);

    [Theory]
    [InlineData(10, 0, 0, 0, 0, 0, 10)]
    [InlineData(10, 2, 1, 3, 1, 0, 3)]   // 10-2-1-3-1
    [InlineData(10, 2, 3, 3, 1, 2, 3)]   // 本單已預留 2 不重複扣：10-2-(3-2)-3-1
    [InlineData(5, 0, 0, 5, 0, 0, 0)]    // 全數已過帳開票 → 0
    [InlineData(5, 0, 0, 0, 5, 0, 0)]    // 全數被 Matched 預留 → 0
    public void PUR_RETLIM_001_CalculateSubtractsInvoicedMatchedAndOtherReturns(double received, double returned,
        double pending, double invoiced, double matched, double current, double expected) =>
        PurchaseReturnInvoicedLimit.Calculate(Q((decimal)received, (decimal)returned, (decimal)pending,
            (decimal)invoiced, (decimal)matched, (decimal)current)).Should().Be((decimal)expected);

    [Fact]
    public void PUR_RETLIM_002_RequestWithinUninvoicedQuantityPasses()
    {
        var decision = PurchaseReturnInvoicedLimit.Check([R(10, 1.5m), R(20, 2)],
            [S(20, Q(8, invoiced: 5, matched: 1)), S(10, Q(3, invoiced: 1.5m))]);
        decision.CanReturn.Should().BeTrue();
        decision.Shortages.Should().BeEmpty();
    }

    [Fact]
    public void PUR_RETLIM_003_DuplicateLinesAggregateAndEveryShortLineIsReported()
    {
        var decision = PurchaseReturnInvoicedLimit.Check([R(20, 2), R(10, 1), R(20, 1.000001m), R(10, 1)],
            [S(10, Q(5, invoiced: 3)), S(20, Q(8, invoiced: 4, matched: 1))]);
        decision.CanReturn.Should().BeFalse();
        decision.Shortages.Should().Equal(new PurchaseReturnShortage(20, 3.000001m, 3m, 5m));

        var both = PurchaseReturnInvoicedLimit.Check([R(10, 3), R(20, 4)],
            [S(10, Q(5, invoiced: 3)), S(20, Q(8, invoiced: 4, matched: 1))]);
        both.Shortages.Select(x => x.ReceiptLineId).Should().Equal(10, 20);
    }

    [Fact]
    public void PUR_RETLIM_004_EnsureWithinLimitThrows422NamingLineAvailableAndInvoiced()
    {
        var error = Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.EnsureWithinLimit(
            [R(7, 1)], [S(7, Q(5, invoiced: 5))]));
        error.ErrorCode.Should().Be(PurchaseReturnInvoicedLimit.ExceedsUninvoiced);
        error.Message.Should().Contain("receipt line 7").And.Contain("returnable 0").And.Contain("credit note");

        PurchaseReturnInvoicedLimit.EnsureWithinLimit([R(7, 1)], [S(7, Q(6, invoiced: 5))]);
    }

    [Fact]
    public void PUR_RETLIM_005_OwnPendingReservationIsNotCountedTwiceOnFinalApproval()
    {
        // SUBMIT 已預留 2；最終核准重驗時本單 2 不算他單佔量。
        PurchaseReturnInvoicedLimit.EnsureWithinLimit([R(7, 2)], [S(7, Q(5, pending: 2, invoiced: 3, current: 2))]);
        // 之後他單開票把餘量吃掉（不應發生，CHECK 會擋）時快照不一致 → 拒絕而非負餘量。
        Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.Calculate(
            Q(5, pending: 2, invoiced: 4, current: 2))).ErrorCode
            .Should().Be(PurchaseReturnInvoicedLimit.InvalidSourceCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    public void PUR_RETLIM_006_InvalidRequestQuantityIsRejected(double quantity) =>
        Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.Check([R(1, (decimal)quantity)],
            [S(1, Q(5))])).ErrorCode.Should().Be(PurchaseReturnInvoicedLimit.InvalidRequestCode);

    [Fact]
    public void PUR_RETLIM_007_EmptyNullOrBadIdRequestIsRejected()
    {
        Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.Check([], [S(1, Q(5))]));
        Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.Check([R(0, 1)], [S(1, Q(5))]));
        Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.Check([null!], [S(1, Q(5))]));
        Assert.Throws<ArgumentNullException>(() => PurchaseReturnInvoicedLimit.Check(null!, [S(1, Q(5))]));
    }

    [Fact]
    public void PUR_RETLIM_008_MissingExtraDuplicateOrInconsistentSourcesAreRejected()
    {
        void Invalid(IReadOnlyList<PurchaseReturnReceiptSource> sources) =>
            Assert.Throws<BusinessRuleException>(() => PurchaseReturnInvoicedLimit.Check([R(1, 1)], sources))
                .ErrorCode.Should().Be(PurchaseReturnInvoicedLimit.InvalidSourceCode);

        Invalid([]);
        Invalid([S(2, Q(5))]);
        Invalid([S(1, Q(5)), S(2, Q(5))]);
        Invalid([S(1, Q(5)), S(1, Q(5))]);
        Invalid([S(1, null!)]);
        Invalid([S(1, Q(0))]);
        Invalid([S(1, Q(5, returned: -1))]);
        Invalid([S(1, Q(5, pending: 1, current: 2))]);
        Invalid([S(1, Q(5, invoiced: 4, matched: 2))]);
        Invalid([S(1, Q(5.0000001m))]);
    }
}
