using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>FI-SALES-INV-U01～U04（WU-FI-002a2b）：命令與 SD 發票快照的純規則（BR-FI-007、009、014）。</summary>
public sealed class SalesInvoicePostingRulesTests
{
    private static readonly SalesInvoicePostingCommand Command = new(9, 100, 7, 2, "sha256:x");

    private static FinanceSalesInvoiceSnapshot Snapshot(FinanceSalesSourceState state = FinanceSalesSourceState.Issuing) =>
        new(9, 100, 2, "sha256:x", 7, state, new DateOnly(2028, 2, 29), "USD", "TWD", 31.5m,
            100m, 5m, 105m, 3150m, 157.5m, 3307.5m);

    [Theory]
    [InlineData(0, 100, 7, 2, "sha256:x")]
    [InlineData(9, 0, 7, 2, "sha256:x")]
    [InlineData(9, 100, 0, 2, "sha256:x")]
    [InlineData(9, 100, 7, 0, "sha256:x")]
    [InlineData(9, 100, 7, 2, "")]
    [InlineData(9, 100, 7, 2, " sha256:x")]
    [InlineData(9, 100, 7, 2, "sha256 x")]
    [InlineData(9, 100, 7, 2, "sha256:é")]
    public void MalformedCommandsAreNotWellFormed(long company, long invoice, long customer, int version, string fingerprint)
    {
        // FI-SALES-INV-U01
        Assert.False(SalesInvoicePostingRules.IsWellFormed(new(company, invoice, customer, version, fingerprint)));
    }

    [Fact]
    public void FingerprintLengthAndNullCommandBoundaries()
    {
        Assert.False(SalesInvoicePostingRules.IsWellFormed(null));
        Assert.True(SalesInvoicePostingRules.IsWellFormed(Command));
        Assert.True(SalesInvoicePostingRules.IsWellFormed(Command with { Fingerprint = new string('a', 128) }));
        Assert.False(SalesInvoicePostingRules.IsWellFormed(Command with { Fingerprint = new string('a', 129) }));
    }

    [Fact]
    public void SnapshotChecksRunIdentityStateClaimThenAmounts()
    {
        // FI-SALES-INV-U02：閏日營業日期的正常快照通過；各類失敗依序命名。
        Assert.Null(SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot()));
        Assert.Null(SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot(FinanceSalesSourceState.Posted)));
        Assert.Equal((404, SalesPostingErrors.SourceNotFound), SalesInvoicePostingRules.CheckSnapshot(Command, null));
        Assert.Equal((404, SalesPostingErrors.SourceNotFound),
            SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { CompanyId = 8 }));
        Assert.Equal((404, SalesPostingErrors.SourceNotFound),
            SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { InvoiceId = 101 }));
        foreach (var state in new[] { FinanceSalesSourceState.Draft, FinanceSalesSourceState.Pending,
                     FinanceSalesSourceState.Rejected, FinanceSalesSourceState.Deleted })
            Assert.Equal((422, SalesPostingErrors.SourceNotPostable),
                SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot(state) with { GrossAmount = 0m }));
        Assert.Equal((409, SalesPostingErrors.SourceMismatch),
            SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { CustomerId = 8 }));
        Assert.Equal((409, SalesPostingErrors.SourceMismatch),
            SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { SourceVersion = 3 }));
        Assert.Equal((409, SalesPostingErrors.SourceMismatch),
            SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { Fingerprint = "SHA256:x" }));
        Assert.Equal((422, SalesPostingErrors.InvalidAmounts),
            SalesInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { BaseTaxAmount = 157.4m }));
    }

    public static TheoryData<string, FinanceSalesInvoiceSnapshot> InvalidAmounts()
    {
        var s = Snapshot();
        return new()
        {
            { "net+tax≠gross", s with { GrossAmount = 105.0001m } },
            { "base net+tax≠gross", s with { BaseGrossAmount = 3307.4m } },
            { "zero gross", s with { NetAmount = 0m, TaxAmount = 0m, GrossAmount = 0m } },
            { "negative tax", s with { TaxAmount = -5m, GrossAmount = 95m } },
            { "5 decimals", s with { NetAmount = 99.99999m, GrossAmount = 104.99999m } },
            { "over decimal(19,4)", s with { NetAmount = 1_000_000_000_000_000m, GrossAmount = 1_000_000_000_000_005m } },
            { "lowercase currency", s with { Currency = "usd" } },
            { "2-letter base", s with { BaseCurrency = "TW" } },
            { "zero rate", s with { ExchangeRate = 0m } },
            { "9-decimal rate", s with { ExchangeRate = 31.123456789m } },
            { "default date", s with { BusinessDate = default } },
            { "base currency rate≠1", s with { Currency = "TWD", NetAmount = 3150m, TaxAmount = 157.5m, GrossAmount = 3307.5m } },
            { "base currency amounts differ", s with { Currency = "TWD", ExchangeRate = 1m } },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void UnreconciledAmountsCurrencyOrRateFail(string reason, FinanceSalesInvoiceSnapshot snapshot)
    {
        // FI-SALES-INV-U03（FI-SALES-006 金額部分）
        Assert.False(SalesInvoicePostingRules.AmountsReconcile(snapshot), reason);
    }

    [Fact]
    public void ExactMaximumZeroTaxAndBaseCurrencyInvoicesReconcile()
    {
        // FI-SALES-INV-U04
        const decimal max = SalesInvoicePostingRules.MaxAmount;
        Assert.True(SalesInvoicePostingRules.AmountsReconcile(Snapshot() with
        {
            NetAmount = max, TaxAmount = 0m, GrossAmount = max, BaseNetAmount = max, BaseTaxAmount = 0m, BaseGrossAmount = max
        }));
        Assert.True(SalesInvoicePostingRules.AmountsReconcile(Snapshot() with
        {
            Currency = "TWD", ExchangeRate = 1m, NetAmount = 3150m, TaxAmount = 157.5m, GrossAmount = 3307.5m
        }));
        Assert.True(SalesInvoicePostingRules.AmountsReconcile(Snapshot() with { ExchangeRate = 0.00000001m }));
    }
}
