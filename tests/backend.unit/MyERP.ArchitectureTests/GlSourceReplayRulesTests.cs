using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlSourceReplayRulesTests
{
    private static readonly GlPostingSource Source = new(10, "PUR", "Invoice", 20, 3, "sha256:abc");
    private static readonly GlCommittedSource Saved = new(Source, 99);

    [Fact]
    public void NewSourceAndExactCommittedReplayHaveDistinctResults()
    {
        Assert.Equal(new GlSourceReplayDecision(GlSourceReplayStatus.New, null),
            GlSourceReplayRules.Decide(Source, null));
        Assert.Equal(new GlSourceReplayDecision(GlSourceReplayStatus.Unchanged, 99),
            GlSourceReplayRules.Decide(Source, Saved));
    }

    [Fact]
    public void SamePermanentKeyWithChangedVersionOrFingerprintConflicts()
    {
        Assert.Equal(GlSourceReplayStatus.Conflict,
            GlSourceReplayRules.Decide(Source with { SourceVersion = 4 }, Saved).Status);
        Assert.Equal(GlSourceReplayStatus.Conflict,
            GlSourceReplayRules.Decide(Source with { Fingerprint = "sha256:def" }, Saved).Status);
    }

    [Fact]
    public void ARecordForAnotherPermanentKeyIsNotConsideredAReplay()
    {
        GlPostingSource[] different =
        [
            Source with { CompanyId = 11 }, Source with { Module = "SD" },
            Source with { SourceType = "CreditNote" }, Source with { SourceId = 21 }
        ];
        foreach (var candidate in different)
            Assert.Equal(GlSourceReplayStatus.IdentityMismatch,
                GlSourceReplayRules.Decide(candidate, Saved).Status);
    }

    [Fact]
    public void InvalidIdentityAndUnpersistedReceiptNeverProduceSuccess()
    {
        GlPostingSource[] invalid =
        [
            Source with { CompanyId = 0 }, Source with { SourceId = -1 },
            Source with { SourceVersion = 0 }, Source with { Module = " " },
            Source with { SourceType = "" }, Source with { Fingerprint = "\t" },
            Source with { Module = " PUR" }, Source with { SourceType = "Invoice " },
            Source with { Fingerprint = "sha256:abc " }
        ];
        foreach (var candidate in invalid)
            Assert.Equal(GlSourceReplayStatus.InvalidSource,
                GlSourceReplayRules.Decide(candidate, null).Status);
        Assert.Equal(GlSourceReplayStatus.InvalidSavedReceipt,
            GlSourceReplayRules.Decide(Source, Saved with { JournalId = 0 }).Status);
    }

    [Fact]
    public void ExactOrdinalFingerprintAndIdentityAreRequired()
    {
        Assert.Equal(GlSourceReplayStatus.Conflict,
            GlSourceReplayRules.Decide(Source with { Fingerprint = "SHA256:ABC" }, Saved).Status);
        Assert.Equal(GlSourceReplayStatus.IdentityMismatch,
            GlSourceReplayRules.Decide(Source with { Module = "pur" }, Saved).Status);
    }
}
