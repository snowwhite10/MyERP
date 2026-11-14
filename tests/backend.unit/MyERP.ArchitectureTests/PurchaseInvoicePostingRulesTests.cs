using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-PUR-POST-U01～U06（WU-FI-003a2b，ADR-117）：PUR 採購發票過帳命令與已存快照的純規則
/// （BR-FI-015、017、021；FI-PUR-002／004／006／008／014／015 的離線部分）。
/// </summary>
public sealed class PurchaseInvoicePostingRulesTests
{
    private static readonly PurchaseInvoicePostingCommand Command = new(9, 100, 7, 2, "sha256:p");

    private static readonly FinancePurchaseInvoiceLine[] UsdLines =
    [
        new(1, 60m, 3m, 63m, 1890m, 94.5m, 1984.5m),
        new(2, 40m, 0m, 40m, 1260m, 0m, 1260m)
    ];

    private static FinancePurchaseInvoiceSnapshot Snapshot(
        FinancePurchaseSourceState state = FinancePurchaseSourceState.Matched) =>
        new(9, 100, 2, "sha256:p", 7, state, new DateOnly(2028, 2, 29), "USD", "TWD", 31.5m,
            100m, 3m, 103m, 3150m, 94.5m, 3244.5m, UsdLines);

    private static FinancePurchaseInvoiceSnapshot WithLine(int index, FinancePurchaseInvoiceLine line)
    {
        var lines = UsdLines.ToArray();
        lines[index] = line;
        return Snapshot() with { Lines = lines };
    }

    [Theory]
    [InlineData(0, 100, 7, 2, "sha256:p")]
    [InlineData(9, 0, 7, 2, "sha256:p")]
    [InlineData(9, 100, 0, 2, "sha256:p")]
    [InlineData(9, 100, 7, 0, "sha256:p")]
    [InlineData(9, 100, 7, -1, "sha256:p")]
    [InlineData(9, 100, 7, 2, "")]
    [InlineData(9, 100, 7, 2, "sha256:p ")]
    [InlineData(9, 100, 7, 2, "sha256 p")]
    [InlineData(9, 100, 7, 2, "sha256:é")]
    public void MalformedCommandsAreNotWellFormed(long company, long invoice, long supplier, int version, string fingerprint)
    {
        // FI-PUR-POST-U01（FI-PUR-014 偽造／格式錯誤 → 400）
        Assert.False(PurchaseInvoicePostingRules.IsWellFormed(new(company, invoice, supplier, version, fingerprint)));
    }

    [Fact]
    public void FingerprintLengthAndNullCommandBoundaries()
    {
        Assert.False(PurchaseInvoicePostingRules.IsWellFormed(null));
        Assert.True(PurchaseInvoicePostingRules.IsWellFormed(Command));
        Assert.True(PurchaseInvoicePostingRules.IsWellFormed(Command with { Fingerprint = new string('a', 128) }));
        Assert.False(PurchaseInvoicePostingRules.IsWellFormed(Command with { Fingerprint = new string('a', 129) }));
    }

    [Fact]
    public void SnapshotChecksRunIdentityStateClaimThenAmounts()
    {
        // FI-PUR-POST-U02：閏日、外幣、含零稅行的正常快照通過；各類失敗依 404 → 422 → 409 → 422 次序命名。
        Assert.Null(PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot()));
        Assert.Equal((404, PurchasePostingErrors.SourceNotFound, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, null));
        Assert.Equal((404, PurchasePostingErrors.SourceNotFound, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { CompanyId = 8 }));
        Assert.Equal((404, PurchasePostingErrors.SourceNotFound, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { InvoiceId = 101 }));

        // Posted 只在 FI 已有來源時以重放處理（讀 PUR 前），走到快照檢查即不可再建立應付（BR-FI-015）。
        foreach (var state in new[] { FinancePurchaseSourceState.Draft, FinancePurchaseSourceState.Disputed,
                     FinancePurchaseSourceState.Rejected, FinancePurchaseSourceState.Deleted,
                     FinancePurchaseSourceState.Posted })
            Assert.Equal((422, PurchasePostingErrors.SourceNotPostable, (int?)null),
                PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot(state) with { SupplierId = 8, GrossAmount = 0m }));

        Assert.Equal((409, PurchasePostingErrors.SourceMismatch, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { SupplierId = 8, GrossAmount = 0m }));
        Assert.Equal((409, PurchasePostingErrors.SourceMismatch, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { SourceVersion = 3 }));
        Assert.Equal((409, PurchasePostingErrors.SourceMismatch, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { Fingerprint = "SHA256:p" }));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { GrossAmount = 104m }));
        Assert.Throws<ArgumentNullException>(() => PurchaseInvoicePostingRules.CheckSnapshot(null!, Snapshot()));
    }

    public static TheoryData<string> BadHeaders => new()
    {
        "date", "currency-lower", "currency-short", "base-currency", "rate-zero", "rate-9dp", "rate-max",
        "net+tax", "base-net+tax", "zero-gross", "negative-tax", "five-decimals", "overflow", "same-currency-rate",
        "same-currency-amount"
    };

    [Theory]
    [MemberData(nameof(BadHeaders))]
    public void HeaderAmountsCurrencyAndRateMustReconcile(string kind)
    {
        // FI-PUR-POST-U03（FI-PUR-004／006：net+tax≠gross、小數位、上限、幣別、匯率 → 422 無行號）
        var snapshot = Snapshot();
        var bad = kind switch
        {
            "date" => snapshot with { BusinessDate = default },
            "currency-lower" => snapshot with { Currency = "usd" },
            "currency-short" => snapshot with { Currency = "US" },
            "base-currency" => snapshot with { BaseCurrency = "TW1" },
            "rate-zero" => snapshot with { ExchangeRate = 0m },
            "rate-9dp" => snapshot with { ExchangeRate = 31.500000001m },
            "rate-max" => snapshot with { ExchangeRate = 100_000_000_000m },
            "net+tax" => snapshot with { TaxAmount = 3.0001m },
            "base-net+tax" => snapshot with { BaseGrossAmount = 3244.4999m },
            "zero-gross" => snapshot with { NetAmount = 0m, TaxAmount = 0m, GrossAmount = 0m },
            "negative-tax" => snapshot with { NetAmount = 106m, TaxAmount = -3m },
            "five-decimals" => snapshot with { NetAmount = 99.99999m, GrossAmount = 102.99999m },
            "overflow" => snapshot with { NetAmount = 1_000_000_000_000_000m, GrossAmount = 1_000_000_000_000_003m },
            "same-currency-rate" => SameCurrency() with { ExchangeRate = 1.1m },
            "same-currency-amount" => SameCurrency() with { BaseNetAmount = 99m, BaseGrossAmount = 102m },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, bad));
    }

    private static FinancePurchaseInvoiceSnapshot SameCurrency() =>
        Snapshot() with
        {
            Currency = "TWD", ExchangeRate = 1m, BaseNetAmount = 100m, BaseTaxAmount = 3m, BaseGrossAmount = 103m,
            Lines = [new(1, 100m, 3m, 103m, 100m, 3m, 103m)]
        };

    [Fact]
    public void MaximumDecimalAndBaseCurrencyInvoicePass()
    {
        // FI-PUR-POST-U04：decimal(19,4)／(19,8) 最大有效值、本位幣發票（匯率 1、兩組金額相同）。
        Assert.Null(PurchaseInvoicePostingRules.CheckSnapshot(Command, SameCurrency()));
        const decimal max = 999_999_999_999_999.9999m;
        var big = Snapshot() with
        {
            ExchangeRate = 99_999_999_999.99999999m, NetAmount = max - 1m, TaxAmount = 1m, GrossAmount = max,
            BaseNetAmount = max, BaseTaxAmount = 0m, BaseGrossAmount = max,
            Lines = [new(1, max - 1m, 1m, max, max, 0m, max)]
        };
        Assert.Null(PurchaseInvoicePostingRules.CheckSnapshot(Command, big));
    }

    [Fact]
    public void EachSavedLineMustReconcileAndNameItsLineNumber()
    {
        // FI-PUR-POST-U05（FI-PUR-002／004／012：一行錯誤整張拒絕，回報 PUR 行號；零稅行允許）
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)2),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, WithLine(1, UsdLines[1] with { GrossAmount = 40.0001m })));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)1),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, WithLine(0, UsdLines[0] with { BaseTaxAmount = 94.4m })));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)2),
            PurchaseInvoicePostingRules.CheckSnapshot(Command,
                WithLine(1, new(2, 0m, 0m, 0m, 0m, 0m, 0m))));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)1),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, WithLine(0, UsdLines[0] with { NetAmount = 60.00001m, TaxAmount = 2.99999m })));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)0),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, WithLine(0, UsdLines[0] with { LineNo = 0 })));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)1),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, WithLine(1, UsdLines[1] with { LineNo = 1 })));
        // 本位幣發票的行，本位幣金額必須與原幣相同。
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)1),
            PurchaseInvoicePostingRules.CheckSnapshot(Command,
                SameCurrency() with { Lines = [new(1, 100m, 3m, 103m, 101m, 2m, 103m)] }));
    }

    [Fact]
    public void LinesMustExistAndSumExactlyToTheHeaderWithoutAPlug()
    {
        // FI-PUR-POST-U06（FI-PUR-003：行加總須精確等於表頭，不補差額；無行 → 422）
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { Lines = [] }));
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command, Snapshot() with { Lines = null! }));
        // 原幣行加總與表頭差 0.0001（行本身平衡）。
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command,
                WithLine(1, UsdLines[1] with { NetAmount = 40.0001m, GrossAmount = 40.0001m })));
        // 本位幣行加總差 0.0001：不得以尾差吸收。
        Assert.Equal((422, PurchasePostingErrors.InvalidAmounts, (int?)null),
            PurchaseInvoicePostingRules.CheckSnapshot(Command,
                WithLine(1, UsdLines[1] with { BaseNetAmount = 1259.9999m, BaseGrossAmount = 1259.9999m })));
        // 三行各自四捨五入、最後一行以精確餘額保存 → 通過。
        var thirds = Snapshot() with
        {
            NetAmount = 100m, TaxAmount = 0m, GrossAmount = 100m,
            BaseNetAmount = 3150m, BaseTaxAmount = 0m, BaseGrossAmount = 3150m,
            Lines =
            [
                new(1, 33.3333m, 0m, 33.3333m, 1049.999m, 0m, 1049.999m),
                new(2, 33.3333m, 0m, 33.3333m, 1049.999m, 0m, 1049.999m),
                new(3, 33.3334m, 0m, 33.3334m, 1050.002m, 0m, 1050.002m)
            ]
        };
        Assert.Null(PurchaseInvoicePostingRules.CheckSnapshot(Command, thirds));
    }
}
