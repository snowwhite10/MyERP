using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-SALES-CR-U01～U09（WU-FI-002a3a，ADR-113）：銷退信用命令、SD 銷退快照、逐發票行累計上限、原發票總額、
/// 原應收餘額與信用傳票分錄的純規則（BR-FI-011、012、014）。
/// </summary>
public sealed class SalesCreditPostingRulesTests
{
    private static readonly SalesCreditPostingCommand Command = new(9, 500, 7, 3, "sha256:r");

    private static readonly FinanceSalesInvoiceLineCap Cap = new(10m, 1000m, 50m, 31500m, 1575m);

    // USD → TWD 31.5；第 1、2 行屬發票 100，第 3 行屬發票 101。
    private static FinanceSalesReturnLine Line(long invoice, long line, decimal qty, decimal net, decimal tax) =>
        new(invoice, line, qty, net, tax, net + tax, net * 31.5m, tax * 31.5m, (net + tax) * 31.5m, Cap);

    private static FinanceSalesReturnSnapshot Snapshot(params FinanceSalesReturnLine[] lines)
    {
        if (lines.Length == 0)
            lines = [Line(100, 1001, 2m, 200m, 10m), Line(100, 1002, 1m, 100m, 5m), Line(101, 2001, 1.5m, 150m, 7.5m)];
        return new(9, 500, 3, "sha256:r", 7, FinanceSalesSourceState.Issuing, new DateOnly(2028, 2, 29), "USD", "TWD",
            31.5m, lines.Sum(l => l.NetAmount), lines.Sum(l => l.TaxAmount), lines.Sum(l => l.GrossAmount),
            lines.Sum(l => l.BaseNetAmount), lines.Sum(l => l.BaseTaxAmount), lines.Sum(l => l.BaseGrossAmount), lines);
    }

    private static Dictionary<long, SalesCreditOriginalInvoice> Originals(decimal open100 = 33075m, decimal open101 = 33075m) =>
        new()
        {
            [100] = new(100, 7, "USD", "TWD", 1000m, 50m, 31500m, 1575m, 1100, open100),
            [101] = new(101, 7, "USD", "TWD", 1000m, 50m, 31500m, 1575m, 1101, open101)
        };

    private static readonly Dictionary<long, SalesCreditTotals> None = new();

    private static SalesCreditFailure? Check(FinanceSalesReturnSnapshot snapshot,
        Dictionary<long, SalesCreditOriginalInvoice>? originals = null,
        Dictionary<long, SalesCreditTotals>? priorByLine = null,
        Dictionary<long, SalesCreditTotals>? priorByInvoice = null) =>
        SalesCreditPostingRules.CheckAllocations(Command, snapshot, originals ?? Originals(), priorByLine ?? None,
            priorByInvoice ?? None);

    [Theory]
    [InlineData(0, 500, 7, 3, "sha256:r")]
    [InlineData(9, 0, 7, 3, "sha256:r")]
    [InlineData(9, 500, 0, 3, "sha256:r")]
    [InlineData(9, 500, 7, 0, "sha256:r")]
    [InlineData(9, 500, 7, 3, "")]
    [InlineData(9, 500, 7, 3, "sha256:r ")]
    public void MalformedCommandsAreNotWellFormed(long company, long ret, long customer, int version, string fingerprint)
    {
        // FI-SALES-CR-U01
        Assert.False(SalesCreditPostingRules.IsWellFormed(new(company, ret, customer, version, fingerprint)));
        Assert.False(SalesCreditPostingRules.IsWellFormed(null));
        Assert.True(SalesCreditPostingRules.IsWellFormed(Command));
    }

    [Fact]
    public void SnapshotChecksRunIdentityStateClaimThenAmounts()
    {
        // FI-SALES-CR-U02：閏日、兩張發票三行的已開票銷退通過；失敗依 404 → 422 狀態 → 409 → 422 金額。
        Assert.Null(SalesCreditPostingRules.CheckSnapshot(Command, Snapshot()));
        Assert.Null(SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { State = FinanceSalesSourceState.Posted }));
        Assert.Equal(new SalesCreditFailure(404, SalesPostingErrors.SourceNotFound),
            SalesCreditPostingRules.CheckSnapshot(Command, null));
        Assert.Equal(new SalesCreditFailure(404, SalesPostingErrors.SourceNotFound),
            SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { CompanyId = 8 }));
        Assert.Equal(new SalesCreditFailure(404, SalesPostingErrors.SourceNotFound),
            SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { ReturnId = 501 }));
        foreach (var state in new[] { FinanceSalesSourceState.Draft, FinanceSalesSourceState.Pending,
                     FinanceSalesSourceState.Rejected, FinanceSalesSourceState.Deleted })
            Assert.Equal(new SalesCreditFailure(422, SalesPostingErrors.SourceNotPostable),
                SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { State = state, Lines = [] }));
        Assert.Equal(new SalesCreditFailure(409, SalesPostingErrors.SourceMismatch),
            SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { CustomerId = 8 }));
        Assert.Equal(new SalesCreditFailure(409, SalesPostingErrors.SourceMismatch),
            SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { SourceVersion = 2 }));
        Assert.Equal(new SalesCreditFailure(409, SalesPostingErrors.SourceMismatch),
            SalesCreditPostingRules.CheckSnapshot(Command, Snapshot() with { Fingerprint = "SHA256:r" }));
    }

    public static TheoryData<string, FinanceSalesReturnSnapshot, int?> InvalidSnapshots()
    {
        var s = Snapshot();
        var l = s.Lines[1];
        FinanceSalesReturnSnapshot WithLine2(FinanceSalesReturnLine changed) => s with { Lines = [s.Lines[0], changed, s.Lines[2]] };
        return new()
        {
            { "no lines", s with { Lines = [] }, null },
            { "header net≠Σ lines", s with { NetAmount = s.NetAmount + 1m, GrossAmount = s.GrossAmount + 1m }, null },
            { "header base tax≠Σ lines", s with { BaseTaxAmount = s.BaseTaxAmount - 1m, BaseGrossAmount = s.BaseGrossAmount - 1m }, null },
            { "header unbalanced", s with { GrossAmount = s.GrossAmount + 0.0001m }, null },
            { "lowercase currency", s with { Currency = "usd" }, null },
            { "zero quantity", WithLine2(l with { Quantity = 0m }), 2 },
            { "negative quantity", WithLine2(l with { Quantity = -1m }), 2 },
            { "7-dp quantity", WithLine2(l with { Quantity = 1.0000001m }), 2 },
            { "5-dp amount", WithLine2(l with { NetAmount = 100.00001m, GrossAmount = 105.00001m }), 2 },
            { "line net+tax≠gross", WithLine2(l with { GrossAmount = 105.0001m }), 2 },
            { "line base net+tax≠gross", WithLine2(l with { BaseGrossAmount = l.BaseGrossAmount + 1m }), 2 },
            { "zero line gross", WithLine2(l with { NetAmount = 0m, TaxAmount = 0m, GrossAmount = 0m }), 2 },
            { "invoice id 0", WithLine2(l with { InvoiceId = 0 }), 2 },
            { "invoice line id 0", WithLine2(l with { InvoiceLineId = 0 }), 2 },
            { "missing cap", WithLine2(l with { InvoiceLine = null! }), 2 },
            { "zero cap quantity", WithLine2(l with { InvoiceLine = Cap with { Quantity = 0m } }), 2 },
            { "negative cap tax", WithLine2(l with { InvoiceLine = Cap with { TaxAmount = -1m } }), 2 },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshots))]
    public void InvalidReturnAmountsFailWithLine(string reason, FinanceSalesReturnSnapshot snapshot, int? line)
    {
        // FI-SALES-CR-U03（FI-SALES-006 銷退部分）
        var failure = SalesCreditPostingRules.CheckSnapshot(Command, snapshot);
        Assert.True(failure is not null, reason);
        Assert.Equal(new SalesCreditFailure(422, SalesPostingErrors.InvalidAmounts, line), failure);
    }

    [Fact]
    public void DuplicateInvoiceLineInOneReturnFailsAtSecondOccurrence()
    {
        // FI-SALES-CR-U04（FI-SALES-018 重複發票行）
        var s = Snapshot(Line(100, 1001, 1m, 100m, 5m), Line(101, 2001, 1m, 100m, 5m), Line(100, 1001, 1m, 100m, 5m));
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.DuplicateLine, 3),
            SalesCreditPostingRules.CheckSnapshot(Command, s));
    }

    [Fact]
    public void TwoInvoicesWithinCapsAndOpenBalancePass()
    {
        // FI-SALES-CR-U05（FI-SALES-004）：一張銷退兩張發票，未超上限。
        Assert.Null(Check(Snapshot()));
    }

    [Fact]
    public void OriginalMustBePostedAndMatchCustomerAndCurrency()
    {
        // FI-SALES-CR-U06（FI-SALES-018）
        var originals = Originals();
        originals.Remove(101);
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.OriginalNotPosted, 3), Check(Snapshot(), originals));

        originals = Originals();
        originals[101] = originals[101] with { CustomerId = 8 };
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.OriginalMismatch, 3), Check(Snapshot(), originals));

        originals = Originals();
        originals[100] = originals[100] with { Currency = "EUR" };
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.OriginalMismatch, 1), Check(Snapshot(), originals));

        originals = Originals();
        originals[100] = originals[100] with { BaseCurrency = "HKD" };
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.OriginalMismatch, 1), Check(Snapshot(), originals));
    }

    [Fact]
    public void CumulativeLineCapsIncludePriorCreditsAndAllowExactRemainder()
    {
        // FI-SALES-CR-U07（FI-SALES-007）：先前已退 8 件／800／40，剩餘 2 件／200／10 恰好可退；多 0.000001 件或 0.0001 元即拒。
        var prior = new Dictionary<long, SalesCreditTotals> { [1001] = new(8m, 800m, 40m, 25200m, 1260m) };
        var byInvoice = new Dictionary<long, SalesCreditTotals> { [100] = new(8m, 800m, 40m, 25200m, 1260m) };
        var exact = Snapshot(Line(100, 1001, 2m, 200m, 10m));
        Assert.Null(Check(exact, priorByLine: prior, priorByInvoice: byInvoice));

        var l = exact.Lines[0];
        foreach (var over in new[]
                 {
                     l with { Quantity = 2.000001m },
                     l with { NetAmount = 200.0001m, GrossAmount = 210.0001m, BaseNetAmount = 6300.0001m, BaseGrossAmount = 6615.0001m },
                     l with { TaxAmount = 10.0001m, GrossAmount = 210.0001m, BaseTaxAmount = 315.0001m, BaseGrossAmount = 6615.0001m },
                     l with { BaseNetAmount = 6300.0001m, BaseGrossAmount = 6615.0001m },
                     l with { BaseTaxAmount = 315.0001m, BaseGrossAmount = 6615.0001m }
                 })
            Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.CreditExceedsLine, 1),
                Check(Snapshot(over), priorByLine: prior, priorByInvoice: byInvoice));
    }

    [Fact]
    public void CumulativeInvoiceTotalsCapAcrossLines()
    {
        // FI-SALES-CR-U08：每行未超行上限，但發票 100 累計淨額超過 FI 已存發票淨額 1000 → 於越界行拒絕。
        var byInvoice = new Dictionary<long, SalesCreditTotals> { [100] = new(5m, 800m, 40m, 25200m, 1260m) };
        var s = Snapshot(Line(100, 1001, 1m, 100m, 5m), Line(100, 1002, 1m, 100m, 5m), Line(100, 1003, 1m, 100m, 5m));
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.CreditExceedsInvoice, 3),
            Check(s, priorByInvoice: byInvoice));
        Assert.Null(Check(Snapshot(Line(100, 1001, 1m, 100m, 5m), Line(100, 1002, 1m, 100m, 5m)), priorByInvoice: byInvoice));
    }

    [Fact]
    public void OpenReceivableCannotGoBelowZero()
    {
        // FI-SALES-CR-U09（FI-SALES-019）：發票 100 已收款，剩餘未清 9922.5；第 1+2 行本位幣總額 9922.5 恰好可退；少 0.0001 即拒。
        Assert.Null(Check(Snapshot(), Originals(open100: 9922.5m)));
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.CreditExceedsOpen, 2),
            Check(Snapshot(), Originals(open100: 9922.4999m)));
        Assert.Equal(new SalesCreditFailure(422, SalesCreditPostingErrors.CreditExceedsOpen, 1),
            Check(Snapshot(), Originals(open100: 0m)));
    }

    [Fact]
    public void CreditJournalDebitsRevenueAndTaxAndCreditsReceivable()
    {
        // FI-SALES-CR-U10（BR-FI-012）：借收入（銷貨退回抵銷）／借銷項稅，貸應收；零稅略過稅腳。
        var map = new SalesInvoiceAccountMap(4, "1131", "4101", "2141");
        var s = Snapshot();
        Assert.Equal(
            new (string, decimal, decimal)[] { ("4101", s.BaseNetAmount, 0m), ("2141", s.BaseTaxAmount, 0m), ("1131", 0m, s.BaseGrossAmount) },
            SalesCreditPostingRules.JournalLegs(map, s));
        var zeroTax = Snapshot(new FinanceSalesReturnLine(100, 1001, 1m, 100m, 0m, 100m, 3150m, 0m, 3150m, Cap));
        Assert.Equal(new (string, decimal, decimal)[] { ("4101", 3150m, 0m), ("1131", 0m, 3150m) }, SalesCreditPostingRules.JournalLegs(map, zeroTax));
    }
}
