using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-COLL-WO-U01～U05（WU-FI-005a2c，ADR-122）：收款核銷過帳命令、已存核銷單據、收款餘額與逐行應收的純規則
/// （BR-FI-035、037、040）。
/// </summary>
public sealed class CollectionWriteOffPostingRulesTests
{
    private static readonly DateOnly Date = new(2028, 2, 29);
    private static readonly CollectionWriteOffPostingCommand Command = new(9, 300, 7, 2, "sha256:w");

    private static FinanceCollectionWriteOffSnapshot Snapshot(
        FinanceCollectionWriteOffState state = FinanceCollectionWriteOffState.Approved,
        params FinanceCollectionWriteOffLine[] lines)
    {
        lines = lines.Length == 0 ? [new(12, 100.5m), new(11, 0.0001m)] : lines;
        return new(9, 300, 2, "sha256:w", 7, state, Date, "TWD", "TWD", 100, lines.Sum(x => x.Amount), lines);
    }

    private static CollectionWriteOffReceivable Receivable(long id, decimal open = 500m, long customer = 7,
        string type = "Invoice", string currency = "TWD", DateOnly? date = null) =>
        new(id, customer, type, currency, open, date ?? Date);

    [Theory]
    [InlineData(0, 300, 7, 2, "sha256:w")]
    [InlineData(9, 0, 7, 2, "sha256:w")]
    [InlineData(9, 300, 0, 2, "sha256:w")]
    [InlineData(9, 300, 7, 0, "sha256:w")]
    [InlineData(9, 300, 7, -1, "sha256:w")]
    [InlineData(9, 300, 7, 2, "")]
    [InlineData(9, 300, 7, 2, "sha256:w ")]
    [InlineData(9, 300, 7, 2, "sha256 w")]
    public void MalformedCommandsAreNotWellFormed(long company, long settlement, long customer, int version, string fingerprint)
    {
        // FI-COLL-WO-U01（格式錯誤 → 400）
        Assert.False(CollectionWriteOffPostingRules.IsWellFormed(new(company, settlement, customer, version, fingerprint)));
        Assert.False(CollectionWriteOffPostingRules.IsWellFormed(null));
        Assert.True(CollectionWriteOffPostingRules.IsWellFormed(Command));
    }

    [Fact]
    public void SnapshotChecksRunIdentityStateClaimThenContent()
    {
        // FI-COLL-WO-U02（404 → 422 狀態 → 409 主張 → 422 內容；閏日、四位小數可過帳）
        Assert.Null(CollectionWriteOffPostingRules.CheckSnapshot(Command, Snapshot()));
        Assert.Null(CollectionWriteOffPostingRules.CheckSnapshot(Command, Snapshot(lines: Enumerable
            .Range(1, CollectionWriteOffPostingRules.MaxLines).Select(i => new FinanceCollectionWriteOffLine(i, 0.0001m)).ToArray())));
        Assert.Equal((404, CollectionWriteOffPostingErrors.SourceNotFound), CollectionWriteOffPostingRules.CheckSnapshot(Command, null));
        Assert.Equal((404, CollectionWriteOffPostingErrors.SourceNotFound),
            CollectionWriteOffPostingRules.CheckSnapshot(Command, Snapshot() with { CompanyId = 8, State = FinanceCollectionWriteOffState.Draft }));
        Assert.Equal((404, CollectionWriteOffPostingErrors.SourceNotFound),
            CollectionWriteOffPostingRules.CheckSnapshot(Command, Snapshot() with { SettlementId = 301 }));
        foreach (var state in new[] { FinanceCollectionWriteOffState.Draft, FinanceCollectionWriteOffState.Pending,
                     FinanceCollectionWriteOffState.Rejected, FinanceCollectionWriteOffState.Deleted,
                     FinanceCollectionWriteOffState.Posted })
            Assert.Equal((422, CollectionWriteOffPostingErrors.SourceNotPostable),
                CollectionWriteOffPostingRules.CheckSnapshot(Command, Snapshot(state) with { Fingerprint = "x" }));
        foreach (var claim in new[] { Snapshot() with { CustomerId = 8 }, Snapshot() with { SourceVersion = 3 },
                     Snapshot() with { Fingerprint = "SHA256:w", TotalAmount = 0m } })
            Assert.Equal((409, CollectionWriteOffPostingErrors.SourceMismatch),
                CollectionWriteOffPostingRules.CheckSnapshot(Command, claim));
    }

    public static TheoryData<string> InvalidContents => new()
    {
        "no date", "lowercase currency", "foreign currency", "receipt 0", "total 0", "total fifth decimal", "no lines",
        "null lines", "receivable 0", "duplicate receivable", "zero line", "negative line", "line fifth decimal",
        "sum below total", "sum above total", "overflow", "1001 lines"
    };

    [Theory]
    [MemberData(nameof(InvalidContents))]
    public void UnusableSavedContentIsInvalidSource(string kind)
    {
        // FI-COLL-WO-U03（BR-FI-035：沒有行、重複應收、零／負／五位小數、合計不符 → 422）
        var s = Snapshot();
        var bad = kind switch
        {
            "no date" => s with { BusinessDate = default },
            "lowercase currency" => s with { Currency = "twd", BaseCurrency = "twd" },
            "foreign currency" => s with { Currency = "USD" },
            "receipt 0" => s with { ReceiptId = 0 },
            "total 0" => Snapshot(lines: [new(11, 0m)]),
            "total fifth decimal" => Snapshot(lines: [new(11, 1.00001m)]),
            "no lines" => s with { Lines = [] },
            "null lines" => s with { Lines = null! },
            "receivable 0" => Snapshot(lines: [new(0, 1m)]),
            "duplicate receivable" => Snapshot(lines: [new(11, 1m), new(11, 2m)]),
            "zero line" => Snapshot(lines: [new(11, 1m), new(12, 0m)]),
            "negative line" => Snapshot(lines: [new(11, 3m), new(12, -1m)]),
            "line fifth decimal" => Snapshot(lines: [new(11, 0.00001m), new(12, 1m)]),
            "sum below total" => s with { TotalAmount = s.TotalAmount + 0.0001m },
            "sum above total" => s with { TotalAmount = s.TotalAmount - 0.0001m },
            "overflow" => Snapshot(lines: [new(11, 999_999_999_999_999.9999m), new(12, 1m)]) with { TotalAmount = 1m },
            "1001 lines" => Snapshot(lines: Enumerable.Range(1, CollectionWriteOffPostingRules.MaxLines + 1)
                .Select(i => new FinanceCollectionWriteOffLine(i, 1m)).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.Equal((422, CollectionWriteOffPostingErrors.InvalidSource), CollectionWriteOffPostingRules.CheckSnapshot(Command, bad));
    }

    [Fact]
    public void ReceiptMustBePostedMatchingDatedNoLaterAndCoverTheTotal()
    {
        // FI-COLL-WO-U04（BR-FI-034／035：未過帳 422、他客戶／他幣別／晚於核銷 422、剛好等於未核銷可、超過 0.0001 拒絕）
        var s = Snapshot();
        var ok = new CollectionWriteOffReceiptBalance(7, "TWD", Date, s.TotalAmount);
        Assert.Null(CollectionWriteOffPostingRules.CheckReceipt(s, ok));
        Assert.Null(CollectionWriteOffPostingRules.CheckReceipt(s, ok with { BusinessDate = Date.AddDays(-400) }));
        Assert.Equal((422, CollectionWriteOffPostingErrors.ReceiptNotPosted), CollectionWriteOffPostingRules.CheckReceipt(s, null));
        foreach (var mismatch in new[] { ok with { CustomerId = 8 }, ok with { BaseCurrency = "USD" },
                     ok with { BusinessDate = Date.AddDays(1) } })
            Assert.Equal((422, CollectionWriteOffPostingErrors.ReceiptMismatch), CollectionWriteOffPostingRules.CheckReceipt(s, mismatch));
        Assert.Equal((422, CollectionWriteOffPostingErrors.ReceiptInsufficient),
            CollectionWriteOffPostingRules.CheckReceipt(s, ok with { UnappliedAmount = s.TotalAmount - 0.0001m }));
        Assert.Equal((422, CollectionWriteOffPostingErrors.ReceiptInsufficient),
            CollectionWriteOffPostingRules.CheckReceipt(s, ok with { UnappliedAmount = 0m }));
    }

    [Fact]
    public void EveryBadLineIsReportedInDocumentOrderAndExactOpenAmountIsAllowed()
    {
        // FI-COLL-WO-U05（BR-FI-035／040：逐行原因、不略過；剛好等於未清可、發票當日可）
        FinanceCollectionWriteOffLine[] lines =
        [
            new(1, 10m), new(2, 10m), new(3, 10m), new(4, 10m), new(5, 10m), new(6, 10m), new(7, 10m), new(8, 10m)
        ];
        var s = Snapshot(lines: lines);
        var receivables = new Dictionary<long, CollectionWriteOffReceivable>
        {
            [1] = Receivable(1, open: 10m),
            [3] = Receivable(3, customer: 8),
            [4] = Receivable(4, type: "Return"),
            [5] = Receivable(5, currency: "USD"),
            [6] = Receivable(6, date: Date.AddDays(1)),
            [7] = Receivable(7, open: 9.9999m),
            [8] = Receivable(8, open: 0m, customer: 8)
        };
        Assert.Equal(new CollectionWriteOffLineError[]
        {
            new(2, 2, CollectionWriteOffLineReasons.ReceivableNotFound),
            new(3, 3, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(4, 4, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(5, 5, CollectionWriteOffLineReasons.ReceivableMismatch),
            new(6, 6, CollectionWriteOffLineReasons.ReceivableAfterSettlement),
            new(7, 7, CollectionWriteOffLineReasons.ExceedsOpen),
            new(8, 8, CollectionWriteOffLineReasons.ReceivableMismatch)
        }, CollectionWriteOffPostingRules.CheckLines(s, receivables));
        Assert.Empty(CollectionWriteOffPostingRules.CheckLines(Snapshot(lines: [new(1, 10m)]), receivables));
    }
}
