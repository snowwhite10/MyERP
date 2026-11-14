using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-COLL-POST-U01～U04（WU-FI-005a2b，ADR-121）：收款過帳命令與已存收款單據的純規則
/// （BR-FI-031、033；FI-AR-COLL-005 的離線部分）。
/// </summary>
public sealed class CollectionReceiptPostingRulesTests
{
    private static readonly CollectionReceiptPostingCommand Command = new(9, 100, 7, 2, "sha256:c");

    private static FinanceCollectionReceiptSnapshot Snapshot(
        FinanceCollectionReceiptState state = FinanceCollectionReceiptState.Approved) =>
        new(9, 100, 2, "sha256:c", 7, state, new DateOnly(2028, 2, 29), "TWD", "TWD", 1234.5678m, 55, "TT-0001");

    [Theory]
    [InlineData(0, 100, 7, 2, "sha256:c")]
    [InlineData(9, 0, 7, 2, "sha256:c")]
    [InlineData(9, 100, 0, 2, "sha256:c")]
    [InlineData(9, 100, -7, 2, "sha256:c")]
    [InlineData(9, 100, 7, 0, "sha256:c")]
    [InlineData(9, 100, 7, 2, "")]
    [InlineData(9, 100, 7, 2, " sha256:c")]
    [InlineData(9, 100, 7, 2, "sha256 c")]
    [InlineData(9, 100, 7, 2, "sha256:é")]
    public void MalformedCommandsAreNotWellFormed(long company, long receipt, long customer, int version, string fingerprint)
    {
        // FI-COLL-POST-U01（偽造／格式錯誤 → 400）
        Assert.False(CollectionReceiptPostingRules.IsWellFormed(new(company, receipt, customer, version, fingerprint)));
    }

    [Fact]
    public void FingerprintLengthAndNullCommandBoundaries()
    {
        Assert.False(CollectionReceiptPostingRules.IsWellFormed(null));
        Assert.True(CollectionReceiptPostingRules.IsWellFormed(Command));
        Assert.True(CollectionReceiptPostingRules.IsWellFormed(Command with { Fingerprint = new string('a', 128) }));
        Assert.False(CollectionReceiptPostingRules.IsWellFormed(Command with { Fingerprint = new string('a', 129) }));
    }

    [Fact]
    public void SnapshotChecksRunIdentityStateClaimThenContent()
    {
        // FI-COLL-POST-U02：閏日、四位小數的正常快照通過；各類失敗依 404 → 422 → 409 → 422 次序命名。
        Assert.Null(CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot()));
        Assert.Equal((404, CollectionPostingErrors.SourceNotFound), CollectionReceiptPostingRules.CheckSnapshot(Command, null));
        Assert.Equal((404, CollectionPostingErrors.SourceNotFound),
            CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot() with { CompanyId = 8 }));
        Assert.Equal((404, CollectionPostingErrors.SourceNotFound),
            CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot() with { ReceiptId = 101 }));
        foreach (var state in new[] { FinanceCollectionReceiptState.Draft, FinanceCollectionReceiptState.Pending,
                     FinanceCollectionReceiptState.Rejected, FinanceCollectionReceiptState.Deleted,
                     FinanceCollectionReceiptState.Posted })
            Assert.Equal((422, CollectionPostingErrors.SourceNotPostable),
                CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot(state) with { CustomerId = 8 }));
        foreach (var stale in new[] { Snapshot() with { CustomerId = 8 }, Snapshot() with { SourceVersion = 3 },
                     Snapshot() with { Fingerprint = "sha256:C" } })
            Assert.Equal((409, CollectionPostingErrors.SourceMismatch),
                CollectionReceiptPostingRules.CheckSnapshot(Command, stale with { Amount = 0m }));
    }

    public static TheoryData<string, FinanceCollectionReceiptSnapshot> UnusableContent => new()
    {
        { "no date", Snapshot() with { BusinessDate = default } },
        { "zero", Snapshot() with { Amount = 0m } },
        { "negative", Snapshot() with { Amount = -1m } },
        { "fifth decimal", Snapshot() with { Amount = 1.00001m } },
        { "overflow", Snapshot() with { Amount = 1_000_000_000_000_000m } },
        { "foreign currency", Snapshot() with { Currency = "USD" } },
        { "lowercase", Snapshot() with { Currency = "twd", BaseCurrency = "twd" } },
        { "blank currency", Snapshot() with { Currency = "", BaseCurrency = "" } },
        { "bank 0", Snapshot() with { BankAccountId = 0 } },
        { "blank reference", Snapshot() with { BankReference = "  " } },
        { "untrimmed reference", Snapshot() with { BankReference = " TT-0001" } },
        { "51 characters", Snapshot() with { BankReference = new string('R', 51) } },
        { "control character", Snapshot() with { BankReference = "TT\t01" } },
        { "null reference", Snapshot() with { BankReference = null! } }
    };

    [Theory]
    [MemberData(nameof(UnusableContent))]
    public void UnusableSavedContentIsInvalidSource(string reason, FinanceCollectionReceiptSnapshot snapshot)
    {
        // FI-COLL-POST-U03（FI-AR-COLL-005 離線部分、BR-FI-031 只收本位幣）
        Assert.Equal((422, CollectionPostingErrors.InvalidSource), CollectionReceiptPostingRules.CheckSnapshot(Command, snapshot));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void BoundaryContentIsAccepted()
    {
        // FI-COLL-POST-U04：DECIMAL(19,4) 上限、最小正額、50 字元與中文銀行參考；鎖鍵不分大小寫。
        Assert.Null(CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot() with { Amount = 999_999_999_999_999.9999m }));
        Assert.Null(CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot() with { Amount = 0.0001m }));
        Assert.Null(CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot() with { BankReference = new string('R', 50) }));
        Assert.Null(CollectionReceiptPostingRules.CheckSnapshot(Command, Snapshot() with { BankReference = "匯款 0001" }));
        Assert.Equal(CollectionReceiptPostingRules.BankReferenceKey("tt-0001"), CollectionReceiptPostingRules.BankReferenceKey("TT-0001"));
    }
}
