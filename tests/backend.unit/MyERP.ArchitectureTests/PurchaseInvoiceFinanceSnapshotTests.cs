using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-008b1 (ADR-205): saved PUR invoice → FI purchase invoice snapshot mapping.</summary>
public sealed class PurchaseInvoiceFinanceSnapshotTests
{
    private const string Fingerprint = "PUR-INVOICE-POST-v1:abc";
    private static readonly DateOnly Date = new(2026, 11, 27);

    private static PurchaseInvoiceFinanceSavedLine L(int no, decimal net, decimal tax) => new(no, net, tax, net + tax);

    private static PurchaseInvoiceFinanceSaved Saved(string currency = "USD",
        PurchaseInvoiceStatus status = PurchaseInvoiceStatus.Matched, bool deleted = false, int version = 3,
        IReadOnlyList<PurchaseInvoiceFinanceSavedLine>? lines = null)
    {
        lines ??= [L(2, 20.10m, 1.01m), L(1, 10.55m, 0.53m)];
        return new(1, 7, version, 3, status, deleted, Date, currency,
            lines.Sum(x => x.Net), lines.Sum(x => x.Tax), lines.Sum(x => x.Gross), lines);
    }

    private static FinancePurchaseInvoiceSnapshot Build(PurchaseInvoiceFinanceSaved saved,
        string baseCurrency = "TWD", decimal rate = 32.123456m, string fingerprint = Fingerprint) =>
        PurchaseInvoiceFinanceSnapshot.Build(saved, fingerprint,
            new PurchaseInvoiceExchangeSnapshot(baseCurrency, rate));

    [Fact]
    public void PUR_FISNAP_001_ForeignCurrencyLinesConvertPerLineAndHeaderBaseIsLineSum()
    {
        var s = Build(Saved());

        s.CompanyId.Should().Be(1);
        s.InvoiceId.Should().Be(7);
        s.SourceVersion.Should().Be(3);
        s.SupplierId.Should().Be(3);
        s.Fingerprint.Should().Be(Fingerprint);
        s.State.Should().Be(FinancePurchaseSourceState.Matched);
        s.BusinessDate.Should().Be(Date);
        s.Currency.Should().Be("USD");
        s.BaseCurrency.Should().Be("TWD");
        s.ExchangeRate.Should().Be(32.123456m);
        s.Lines.Select(x => x.LineNo).Should().Equal(1, 2);
        // 10.55 × 32.123456 = 338.902 → 339; 0.53 × rate = 17.025 → 17 (TWD 0 decimals, away from zero)
        s.Lines[0].Should().Be(new FinancePurchaseInvoiceLine(1, 10.55m, 0.53m, 11.08m, 339m, 17m, 356m));
        // 20.10 × rate = 645.681 → 646; 1.01 × rate = 32.445 → 32
        s.Lines[1].Should().Be(new FinancePurchaseInvoiceLine(2, 20.10m, 1.01m, 21.11m, 646m, 32m, 678m));
        (s.NetAmount, s.TaxAmount, s.GrossAmount).Should().Be((30.65m, 1.54m, 32.19m));
        (s.BaseNetAmount, s.BaseTaxAmount, s.BaseGrossAmount).Should().Be((985m, 49m, 1034m));
    }

    [Fact]
    public void PUR_FISNAP_002_MappedSnapshotPassesFinanceReconciliationForMatchingCommand()
    {
        var s = Build(Saved());
        var command = new PurchaseInvoicePostingCommand(1, 7, 3, 3, Fingerprint);

        PurchaseInvoicePostingRules.CheckSnapshot(command, s).Should().BeNull();
        PurchaseInvoicePostingRules.CheckSnapshot(command,
            Build(Saved("TWD"), rate: 1m)).Should().BeNull();
    }

    [Fact]
    public void PUR_FISNAP_003_BaseCurrencyInvoiceKeepsOriginalAmountsAndRequiresRateOne()
    {
        var saved = Saved("TWD", lines: [L(1, 100.5m, 5.0250m)]);
        var s = Build(saved, rate: 1m);

        s.Lines.Single().Should().Be(new FinancePurchaseInvoiceLine(1, 100.5m, 5.025m, 105.525m, 100.5m, 5.025m, 105.525m));
        (s.BaseNetAmount, s.BaseTaxAmount, s.BaseGrossAmount).Should().Be((100.5m, 5.025m, 105.525m));
        ((Action)(() => Build(saved, rate: 1.1m))).Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");
    }

    [Theory]
    [InlineData(PurchaseInvoiceStatus.Draft, false, FinancePurchaseSourceState.Draft)]
    [InlineData(PurchaseInvoiceStatus.Matched, false, FinancePurchaseSourceState.Matched)]
    [InlineData(PurchaseInvoiceStatus.Disputed, false, FinancePurchaseSourceState.Disputed)]
    [InlineData(PurchaseInvoiceStatus.Rejected, false, FinancePurchaseSourceState.Rejected)]
    [InlineData(PurchaseInvoiceStatus.Posted, false, FinancePurchaseSourceState.Posted)]
    [InlineData(PurchaseInvoiceStatus.Matched, true, FinancePurchaseSourceState.Deleted)]
    [InlineData(PurchaseInvoiceStatus.Posted, true, FinancePurchaseSourceState.Deleted)]
    public void PUR_FISNAP_004_StateMapsOneToOneAndSoftDeleteWins(PurchaseInvoiceStatus status, bool deleted,
        FinancePurchaseSourceState expected) =>
        Build(Saved(status: status, deleted: deleted)).State.Should().Be(expected);

    [Fact]
    public void PUR_FISNAP_005_NonMatchedStatesAreMappedButFinanceRejectsThem()
    {
        var command = new PurchaseInvoicePostingCommand(1, 7, 3, 3, Fingerprint);
        PurchaseInvoicePostingRules.CheckSnapshot(command, Build(Saved(status: PurchaseInvoiceStatus.Disputed)))
            .Should().Be((422, PurchasePostingErrors.SourceNotPostable, (int?)null));
    }

    [Fact]
    public void PUR_FISNAP_006_SavedHeaderIsPassedThroughNotRecalculated()
    {
        // A drifted header (not equal to line sum) is reported as saved; FI detects it (422), PUR never repairs it.
        var lines = new[] { L(1, 10m, 1m) };
        var drifted = new PurchaseInvoiceFinanceSaved(1, 7, 3, 3, PurchaseInvoiceStatus.Matched, false, Date, "TWD",
            11m, 1m, 12m, lines);
        var s = Build(drifted, rate: 1m);

        (s.NetAmount, s.TaxAmount, s.GrossAmount).Should().Be((11m, 1m, 12m));
        PurchaseInvoicePostingRules.CheckSnapshot(new(1, 7, 3, 3, Fingerprint), s)
            .Should().Be((422, PurchasePostingErrors.InvalidAmounts, (int?)null));
    }

    [Fact]
    public void PUR_FISNAP_007_EightDecimalRateIsAcceptedAndZeroTaxLineIsKept()
    {
        var s = Build(Saved("JPY", lines: [L(1, 1000m, 0m)]), "USD", 0.00671234m);

        s.Lines.Single().Should().Be(new FinancePurchaseInvoiceLine(1, 1000m, 0m, 1000m, 6.71m, 0m, 6.71m));
        s.ExchangeRate.Should().Be(0.00671234m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.123456789)]
    public void PUR_FISNAP_008_InvalidRateIsRejected(decimal rate) =>
        ((Action)(() => Build(Saved(), rate: rate))).Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");

    [Fact]
    public void PUR_FISNAP_009_InvalidIdentityCurrencyFingerprintAndLinesAreRejected()
    {
        var bad = new List<Action>
        {
            () => Build(Saved(), "twd"),
            () => Build(Saved(), "TW"),
            () => Build(Saved(), fingerprint: " "),
            () => Build(Saved(version: 0)),
            () => Build(Saved() with { CompanyId = 0 }),
            () => Build(Saved() with { InvoiceId = 0 }),
            () => Build(Saved() with { SupplierId = 0 }),
            () => Build(Saved() with { Currency = "usd" }),
            () => Build(Saved() with { Lines = [] }),
            () => Build(Saved() with { Lines = [L(1, 1m, 0m), L(1, 2m, 0m)] }),
            () => Build(Saved() with { Lines = [null!] }),
            () => Build(Saved() with { Lines = [new PurchaseInvoiceFinanceSavedLine(0, 1m, 0m, 1m)] }),
            () => PurchaseInvoiceFinanceSnapshot.Build(null!, Fingerprint, new("TWD", 1m)),
            () => PurchaseInvoiceFinanceSnapshot.Build(Saved(), Fingerprint, null!),
        };

        foreach (var action in bad)
            action.Should().Throw<BusinessRuleException>()
                .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");
    }

    [Fact]
    public void PUR_FISNAP_010_OverflowingConversionIsRejectedNotPartial()
    {
        var huge = Saved("USD", lines: [L(1, 900_000_000_000_000m, 0m)]);

        ((Action)(() => Build(huge, rate: 99_999_999_999m))).Should().Throw<BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");
    }

    [Fact]
    public void PUR_FISNAP_011_CallerLineCollectionIsCopied()
    {
        var lines = new List<PurchaseInvoiceFinanceSavedLine> { L(1, 10m, 1m) };
        var s = Build(Saved("TWD", lines: lines), rate: 1m);
        lines.Add(L(2, 5m, 0m));

        s.Lines.Should().HaveCount(1);
    }
}
