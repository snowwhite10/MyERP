using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-008b4 (ADR-208): receipt comparison, FI result mapping and the shared receipt price basis.</summary>
public sealed class PurchaseInvoicePostServiceRulesTests
{
    private static readonly PurchaseInvoicePostingCommand Command = new(3, 40, 701, 2, "PUR-INVOICE-POST-v1:abc");

    private static readonly FinancePurchaseInvoiceSnapshot Expected = new(3, 40, 2, Command.Fingerprint, 701,
        FinancePurchaseSourceState.Matched, new DateOnly(2026, 11, 6), "USD", "TWD", 31.5m,
        10.5m, 2.1m, 12.6m, 330.75m, 66.15m, 396.90m, []);

    private static PurchaseInvoicePostingReceipt Receipt() =>
        new(3, 40, 701, 2, Command.Fingerprint, "USD", 12.6m, "TWD", 396.90m, 11, 12, 13);

    [Fact]
    public void PUR_POST_101_ReceiptMustProveIdentityVersionFingerprintAndBothGrossAmounts()
    {
        PurchaseInvoicePostService.Matches(Receipt(), Command, Expected).Should().BeTrue();

        PurchaseInvoicePostingReceipt[] wrong =
        [
            Receipt() with { CompanyId = 4 },
            Receipt() with { InvoiceId = 41 },
            Receipt() with { SupplierId = 702 },
            Receipt() with { SourceVersion = 3 },
            Receipt() with { Fingerprint = Command.Fingerprint.ToUpperInvariant() },
            Receipt() with { Currency = "usd" },
            Receipt() with { GrossAmount = 12.61m },
            Receipt() with { BaseCurrency = "JPY" },
            Receipt() with { BaseGrossAmount = 396.91m },
            Receipt() with { PurchaseSourceId = 0 },
            Receipt() with { PayableId = -1 },
            Receipt() with { JournalId = 0 },
        ];
        wrong.Should().OnlyContain(x => !PurchaseInvoicePostService.Matches(x, Command, Expected));
    }

    [Fact]
    public void PUR_POST_102_FiRejectionsKeepFiCodeAndStatusOthersBecomePostingFailures()
    {
        PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Rejected, false, 404,
                PurchasePostingErrors.SourceNotFound))
            .Should().BeOfType<NotFoundException>().Which.ErrorCode.Should().Be(PurchasePostingErrors.SourceNotFound);
        PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Rejected, false, 409,
                PurchasePostingErrors.LockTimeout))
            .Should().BeOfType<ConflictException>().Which.ErrorCode.Should().Be(PurchasePostingErrors.LockTimeout);

        var account = PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Rejected, false, 422,
            PurchasePostingErrors.AccountInvalid, InvalidLine: 2)).Should().BeOfType<BusinessRuleException>().Which;
        account.ErrorCode.Should().Be(PurchasePostingErrors.AccountInvalid);
        account.Extensions.Should().ContainKey("journalLineNo").WhoseValue.Should().Be(2);
        var amounts = (BusinessRuleException)PurchaseInvoicePostService.Failure(new(
            PurchasePostingOutcomeKind.Rejected, false, 422, PurchasePostingErrors.InvalidAmounts,
            InvalidSourceLine: 3));
        amounts.Extensions.Should().ContainKey("lineNo").WhoseValue.Should().Be(3);
        ((BusinessRuleException)PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Rejected, false,
            422, PurchasePostingErrors.MappingMissing))).Extensions.Should().BeNull();

        // Fault, Unchanged for a Matched invoice, a Rejected 400/500 or a missing code: PUR built a bad command or FI is
        // inconsistent → 500 with FI's code when it has one.
        PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Fault, false, 500,
                PurchasePostingErrors.WriteFailed))
            .Should().BeOfType<PurchaseInvoicePostingException>().Which.ErrorCode
            .Should().Be(PurchasePostingErrors.WriteFailed);
        PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Unchanged, false, 200))
            .Should().BeOfType<PurchaseInvoicePostingException>().Which.ErrorCode
            .Should().Be(PurchaseInvoicePostService.PostingFailed);
        PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Rejected, false, 400,
                PurchasePostingErrors.InvalidInput))
            .Should().BeOfType<PurchaseInvoicePostingException>();
        PurchaseInvoicePostService.Failure(new(PurchasePostingOutcomeKind.Rejected, false, 422))
            .Should().BeOfType<PurchaseInvoicePostingException>();
    }

    private static PurchaseInvoiceOrderLinePrice Price(long receiptLineId, decimal unitPrice = 2m) =>
        new(receiptLineId, 3, 9, 90 + receiptLineId, 51, unitPrice, 20m);

    [Fact]
    public void PUR_POST_103_PriceBasisNeedsExactlyOnePricePerRequestedReceiptLine()
    {
        var prices = PurchaseInvoicePriceBasis.Require([1, 2], [Price(2), Price(1, 3m)]);
        prices.Keys.Should().BeEquivalentTo([1L, 2L]);
        prices[1].UnitPrice.Should().Be(3m);

        foreach (var (ids, rows) in new (long[], PurchaseInvoiceOrderLinePrice[])[]
                 {
                     ([1, 2], [Price(1)]),
                     ([1], [Price(1), Price(1)]),
                     ([1, 2], [Price(1), Price(1)]),
                     ([1], [Price(3)]),
                     ([1], [Price(0)]),
                 })
            FluentActions.Invoking(() => PurchaseInvoicePriceBasis.Require(ids, rows))
                .Should().Throw<BusinessRuleException>().Which.ErrorCode
                .Should().Be("PUR.INVOICE.INVALID_SOURCE_IDENTITY");
        FluentActions.Invoking(() => PurchaseInvoicePriceBasis.Require([1], null!))
            .Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void PUR_POST_104_MatchLinesPairSavedLinesWithTheirOrderPrice()
    {
        var line = new PurchaseInvoiceLine
        {
            LineNo = 4, ReceiptLineId = 1, Quantity = 5m, UnitPrice = 2.1m, TaxRatePercent = 20m,
            Net = 10.5m, Tax = 2.1m, Gross = 12.6m
        };
        var matched = Assert.Single(PurchaseInvoicePriceBasis.MatchLines([line],
            PurchaseInvoicePriceBasis.Require([1], [Price(1)])));
        matched.Should().Be(new PurchaseInvoiceMatchLine(1,
            new PurchaseInvoiceAmountLine(4, 5m, 2m, 20m, 2.1m, 20m, 10.5m, 2.1m, 12.6m)));

        FluentActions.Invoking(() => PurchaseInvoicePriceBasis.MatchLines([line],
                new Dictionary<long, PurchaseInvoiceOrderLinePrice>()))
            .Should().Throw<BusinessRuleException>();
    }
}
