using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-008b3 (ADR-207): accepted-difference evidence rebuild and the not-postable FI snapshot.</summary>
public sealed class PurchaseInvoiceFinanceEvidenceTests
{
    // Order basis: 5 × 2.000000 at 20 % → 10 / 2 / 12.
    private static PurchaseInvoiceAmountLine Line(int no, decimal invoicePrice = 2m) =>
        new(no, 5m, 2m, 20m, invoicePrice, 20m, 5m * invoicePrice, 5m * invoicePrice * 0.2m, 5m * invoicePrice * 1.2m);

    private static PurchaseInvoiceSavedResolution Row(int version, int line, string action = "Accept",
        decimal net = 0.5m, decimal tax = 0.1m, decimal gross = 0.6m) =>
        new(version, line, action, "供應商調價", net, tax, gross);

    [Fact]
    public void PUR_FISRC_001_NoCurrentDifferencesMeansNoEvidenceEvenWithOlderRounds()
    {
        var amount = PurchaseInvoiceAmountMatch.Check([Line(1), Line(2)]);

        PurchaseInvoiceFinanceEvidence.Accepted(amount, []).Should().BeEmpty();
        PurchaseInvoiceFinanceEvidence.Accepted(amount, [Row(2, 1, "Reject")]).Should().BeEmpty();
    }

    [Fact]
    public void PUR_FISRC_002_LatestRoundRowsCarryTheCurrentPerKindDifferencesAndPassTheFingerprint()
    {
        var lines = new[] { Line(1), Line(2, 2.1m) };
        var amount = PurchaseInvoiceAmountMatch.Check(lines);
        // Round at version 2 rejected line 2; after edit and MATCH, round at version 5 accepted it.
        var evidence = PurchaseInvoiceFinanceEvidence.Accepted(amount,
            [Row(2, 2, "Reject"), Row(5, 2)]);

        var accepted = evidence.Should().ContainSingle().Subject;
        (accepted.LineNo, accepted.Action, accepted.Reason).Should().Be((2, "Accept", "供應商調價"));
        accepted.Differences.Select(x => x.Kind).Should().Equal("UNIT_PRICE", "NET", "TAX", "GROSS");
        accepted.Differences.Should().Equal(amount.Lines[1].Differences);

        var match = lines.Select(x => new PurchaseInvoiceMatchLine(100 + x.LineNo, x)).ToArray();
        PurchaseInvoiceSnapshotFingerprint.Create(1, 7, 3, "USD", new DateOnly(2026, 11, 27), match, evidence)
            .Should().StartWith("PUR-INVOICE-POST-v1:");
    }

    [Fact]
    public void PUR_FISRC_003_StoredSumsThatDifferFromCurrentAmountsAreRejected()
    {
        var amount = PurchaseInvoiceAmountMatch.Check([Line(1, 2.1m)]);

        foreach (var row in new[]
                 {
                     Row(3, 1, net: 0.4m), Row(3, 1, tax: 0m), Row(3, 1, gross: 0.7m),
                     Row(3, 9) // a line without a current difference
                 })
            Assert.Throws<BusinessRuleException>(() => PurchaseInvoiceFinanceEvidence.Accepted(amount, [row]))
                .ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_DIFFERENCE");
        // Current differences but no RESOLVE rows at all.
        Assert.Throws<BusinessRuleException>(() => PurchaseInvoiceFinanceEvidence.Accepted(amount, []))
            .ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_DIFFERENCE");
    }

    [Fact]
    public void PUR_FISRC_004_LatestRoundWithRejectOrMissingLineFailsTheFingerprintPreflight()
    {
        var lines = new[] { Line(1, 2.1m), Line(2, 2.1m) };
        var amount = PurchaseInvoiceAmountMatch.Check(lines);
        var match = lines.Select(x => new PurchaseInvoiceMatchLine(100 + x.LineNo, x)).ToArray();

        // Latest round only covers line 1 (older complete round is ignored); latest round contains a Reject.
        foreach (var rows in new[]
                 {
                     new[] { Row(2, 1), Row(2, 2), Row(4, 1) },
                     new[] { Row(4, 1), Row(4, 2, "Reject") }
                 })
        {
            var evidence = PurchaseInvoiceFinanceEvidence.Accepted(amount, rows);
            Assert.Throws<BusinessRuleException>(() => PurchaseInvoiceSnapshotFingerprint.Create(1, 7, 3, "USD",
                    new DateOnly(2026, 11, 27), match, evidence))
                .ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_DIFFERENCE");
        }
    }

    [Theory]
    [InlineData(PurchaseInvoiceStatus.Draft, false, FinancePurchaseSourceState.Draft)]
    [InlineData(PurchaseInvoiceStatus.Disputed, false, FinancePurchaseSourceState.Disputed)]
    [InlineData(PurchaseInvoiceStatus.Rejected, false, FinancePurchaseSourceState.Rejected)]
    [InlineData(PurchaseInvoiceStatus.Posted, false, FinancePurchaseSourceState.Posted)]
    [InlineData(PurchaseInvoiceStatus.Matched, true, FinancePurchaseSourceState.Deleted)]
    public void PUR_FISRC_005_NotPostableSnapshotCarriesOnlyIdentityAndStateAndFinanceAnswers422(
        PurchaseInvoiceStatus status, bool deleted, FinancePurchaseSourceState expected)
    {
        var saved = new PurchaseInvoiceFinanceSaved(1, 7, 4, 3, status, deleted, new DateOnly(2026, 11, 27), "USD",
            10m, 1m, 11m, [new(1, 10m, 1m, 11m)]);

        var s = PurchaseInvoiceFinanceSnapshot.NotPostable(saved);

        (s.CompanyId, s.InvoiceId, s.SourceVersion, s.SupplierId, s.State).Should().Be((1L, 7L, 4, 3L, expected));
        (s.Fingerprint, s.BaseCurrency, s.ExchangeRate).Should().Be((string.Empty, string.Empty, 0m));
        s.Lines.Should().BeEmpty();
        PurchaseInvoicePostingRules.CheckSnapshot(new PurchaseInvoicePostingCommand(1, 7, 3, 4, "fp"), s)
            .Should().Be((422, PurchasePostingErrors.SourceNotPostable, (int?)null));
    }

    [Fact]
    public void PUR_FISRC_006_NotPostableRefusesMatchedAndInvalidIdentity()
    {
        var matched = new PurchaseInvoiceFinanceSaved(1, 7, 4, 3, PurchaseInvoiceStatus.Matched, false,
            new DateOnly(2026, 11, 27), "USD", 10m, 1m, 11m, []);
        foreach (var saved in new[] { matched, matched with { Status = PurchaseInvoiceStatus.Draft, Version = 0 },
                     matched with { Status = PurchaseInvoiceStatus.Draft, Currency = "usd" } })
            Assert.Throws<BusinessRuleException>(() => PurchaseInvoiceFinanceSnapshot.NotPostable(saved))
                .ErrorCode.Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");
        Assert.Throws<BusinessRuleException>(() => PurchaseInvoiceFinanceSnapshot.NotPostable(null!));
    }
}
