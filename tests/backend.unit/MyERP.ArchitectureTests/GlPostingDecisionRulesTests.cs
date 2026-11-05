using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.ArchitectureTests;

public sealed class GlPostingDecisionRulesTests
{
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 1, "sha256:abc");
    private static readonly GlCommittedSource Receipt = new(Source, 44);
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "Cash", 2, 4, 8, 0), new(2, 8, "2001", "Payable", 2, 4, 0, 8)], 8, 8);
    private static AccountInfo Account(long id, string code, string name) =>
        new(id, 3, 2, code, name, null, AccountType.Asset, BalanceDirection.Debit, true, true);

    private static GlPostingDecision Decide(GlPostingSource? source = null, GlCommittedSource? saved = null,
        IReadOnlyList<GlPeriodWindow>? periods = null, int mappingVersion = 4, long companyId = 3) =>
        GlPostingDecisionRules.Decide(companyId, source ?? Source, saved, new(2024, 2, 29), "TWD", "TWD",
            periods ?? [new(7, 3, new(2024, 2, 1), new(2024, 2, 29), true)], Draft, 4,
            mappingVersion, [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]);

    [Fact]
    public void NewSourceRequiresCompletePreflightBeforeReady()
    {
        Assert.Equal(new GlPostingDecision(GlPostingDecisionStatus.Ready, PeriodId: 7), Decide());
        var closed = Decide(periods: []);
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, closed.Status);
        Assert.Equal(GlJournalHeaderStatus.MissingPeriod, closed.Preflight?.HeaderStatus);
        Assert.Null(closed.PeriodId);
    }

    [Fact]
    public void CommittedExactReplayReturnsUnchangedEvenWhenPeriodOrMappingChanged()
    {
        var result = Decide(saved: Receipt, periods: [], mappingVersion: 5);
        Assert.Equal(new GlPostingDecision(GlPostingDecisionStatus.Unchanged, JournalId: 44), result);
        Assert.Null(result.PeriodId);
        Assert.Null(result.Preflight);
    }

    [Fact]
    public void ChangedFingerprintOrVersionRejectsWithoutPreflightOrSavedJournalId()
    {
        foreach (var changed in new[] { Source with { Fingerprint = "sha256:other" }, Source with { SourceVersion = 2 } })
        {
            var result = Decide(source: changed, saved: Receipt);
            Assert.Equal(GlPostingDecisionStatus.ReplayRejected, result.Status);
            Assert.Equal(GlSourceReplayStatus.Conflict, result.ReplayStatus);
            Assert.Null(result.JournalId);
            Assert.Null(result.PeriodId);
            Assert.Null(result.Preflight);
        }
    }

    [Fact]
    public void ForeignCompanyCannotUseCommittedReceiptToRevealJournalId()
    {
        var result = Decide(saved: Receipt, companyId: 4);
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, result.Status);
        Assert.Equal(GlJournalHeaderStatus.WrongCompany, result.Preflight?.HeaderStatus);
        Assert.Null(result.JournalId);
    }

    [Fact]
    public void InvalidSourceAndInvalidSavedReceiptNeverBecomeReady()
    {
        var invalid = Decide(source: Source with { Fingerprint = "bad\nkey" });
        Assert.Equal(GlSourceReplayStatus.InvalidSource, invalid.ReplayStatus);
        Assert.Equal(GlPostingDecisionStatus.ReplayRejected, invalid.Status);

        var receipt = Decide(saved: Receipt with { JournalId = 0 });
        Assert.Equal(GlSourceReplayStatus.InvalidSavedReceipt, receipt.ReplayStatus);
        Assert.Equal(GlPostingDecisionStatus.ReplayRejected, receipt.Status);
    }

    [Fact]
    public void NewSourceWithChangedMappingCannotReturnReadyPeriod()
    {
        var result = Decide(mappingVersion: 5);
        Assert.Equal(GlPostingDecisionStatus.PostingRejected, result.Status);
        Assert.Equal(GlJournalDraftRecheckStatus.MappingChanged, result.Preflight?.DraftStatus);
        Assert.Null(result.PeriodId);
    }

    [Fact]
    public void DifferentSavedSourceIdentityDoesNotLeakItsJournalId()
    {
        var result = Decide(saved: Receipt with { Source = Source with { SourceId = 13 } });
        Assert.Equal(GlPostingDecisionStatus.ReplayRejected, result.Status);
        Assert.Equal(GlSourceReplayStatus.IdentityMismatch, result.ReplayStatus);
        Assert.Null(result.JournalId);
    }
}
