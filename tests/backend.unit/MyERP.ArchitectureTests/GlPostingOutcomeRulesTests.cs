using System.Text.RegularExpressions;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlPostingOutcomeRulesTests
{
    private static readonly Regex ErrorCodePattern = new(@"^[A-Z][A-Z0-9_]*(\.[A-Z0-9_]+){2,}$");
    private static readonly GlPostingDecision Ready = new(GlPostingDecisionStatus.Ready, PeriodId: 7);

    private static GlPostingDecision Header(GlJournalHeaderStatus status) =>
        new(GlPostingDecisionStatus.PostingRejected,
            Preflight: new(GlPostingPreflightStatus.HeaderRejected, HeaderStatus: status));

    private static GlPostingDecision Draft(GlJournalDraftRecheckStatus status, int? line = null) =>
        new(GlPostingDecisionStatus.PostingRejected,
            Preflight: new(GlPostingPreflightStatus.DraftRejected, DraftStatus: status, InvalidLine: line));

    private static GlPostingDecision Replay(GlSourceReplayStatus? status) =>
        new(GlPostingDecisionStatus.ReplayRejected, ReplayStatus: status);

    private static void AssertRejected(GlPostingOutcome outcome, int httpStatus, string errorCode)
    {
        Assert.Equal(httpStatus >= 500 ? GlPostingOutcomeKind.Fault : GlPostingOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(httpStatus, outcome.HttpStatus);
        Assert.Equal(errorCode, outcome.ErrorCode);
        Assert.False(outcome.Changed);
        Assert.False(outcome.WritesAudit);
        Assert.Null(outcome.JournalId);
        Assert.Matches(ErrorCodePattern, outcome.ErrorCode!);
    }

    [Fact]
    public void ReadyDecisionWithReadyPlanProceedsWithoutClaimingChange()
    {
        var outcome = GlPostingOutcomeRules.FromDecision(Ready, GlPostingWritePlanStatus.Ready);

        Assert.Equal(GlPostingOutcomeKind.ProceedToWrite, outcome.Kind);
        Assert.False(outcome.Changed);
        Assert.False(outcome.WritesAudit);
        Assert.Null(outcome.HttpStatus);
        Assert.Null(outcome.ErrorCode);
        Assert.Null(outcome.JournalId);
    }

    [Fact]
    public void CommittedWriteIsTheOnlyChangedOutcomeAndWritesAudit()
    {
        var outcome = GlPostingOutcomeRules.Committed(55);

        Assert.Equal((GlPostingOutcomeKind.Posted, true, true, 200, (string?)null, 55L),
            (outcome.Kind, outcome.Changed, outcome.WritesAudit, outcome.HttpStatus, outcome.ErrorCode, outcome.JournalId));
        AssertRejected(GlPostingOutcomeRules.Committed(0), 500, GlPostingErrors.InvalidCommit);
        AssertRejected(GlPostingOutcomeRules.Committed(-1), 500, GlPostingErrors.InvalidCommit);
    }

    [Fact]
    public void ExactReplayIsUnchangedWithSavedJournalAndNoAudit()
    {
        var outcome = GlPostingOutcomeRules.FromDecision(
            new(GlPostingDecisionStatus.Unchanged, JournalId: 44), null);

        Assert.Equal((GlPostingOutcomeKind.Unchanged, false, false, 200, (string?)null, 44L),
            (outcome.Kind, outcome.Changed, outcome.WritesAudit, outcome.HttpStatus, outcome.ErrorCode, outcome.JournalId));
    }

    [Fact]
    public void UnchangedWithoutPositiveJournalIsInternalFault()
    {
        AssertRejected(GlPostingOutcomeRules.FromDecision(new(GlPostingDecisionStatus.Unchanged), null),
            500, GlPostingErrors.InvalidSavedReceipt);
        AssertRejected(GlPostingOutcomeRules.FromDecision(new(GlPostingDecisionStatus.Unchanged, JournalId: 0), null),
            500, GlPostingErrors.InvalidSavedReceipt);
    }

    [Fact]
    public void ReplayRejectionsMapToConflictBadRequestOrFault()
    {
        AssertRejected(GlPostingOutcomeRules.FromDecision(Replay(GlSourceReplayStatus.Conflict), null),
            409, GlPostingErrors.SourceConflict);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Replay(GlSourceReplayStatus.InvalidSource), null),
            400, GlPostingErrors.InvalidSource);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Replay(GlSourceReplayStatus.IdentityMismatch), null),
            500, GlPostingErrors.SourceIdentityMismatch);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Replay(GlSourceReplayStatus.InvalidSavedReceipt), null),
            500, GlPostingErrors.InvalidSavedReceipt);
        foreach (var status in new GlSourceReplayStatus?[] { null, GlSourceReplayStatus.New, GlSourceReplayStatus.Unchanged })
            AssertRejected(GlPostingOutcomeRules.FromDecision(Replay(status), null), 500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void HeaderRejectionsMapToBadRequestNotFoundOrUnprocessable()
    {
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.InvalidInput), null), 400, GlPostingErrors.InvalidInput);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.InvalidSource), null), 400, GlPostingErrors.InvalidSource);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.WrongCompany), null), 404, GlPostingErrors.SourceNotFound);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.InvalidCurrency), null), 422, GlPostingErrors.BaseCurrencyInvalid);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.CurrencyMismatch), null), 422, GlPostingErrors.CurrencyMismatch);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.MissingPeriod), null), 422, GlPostingErrors.PeriodMissing);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.ClosedPeriod), null), 422, GlPostingErrors.PeriodClosed);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.AmbiguousPeriod), null), 422, GlPostingErrors.PeriodAmbiguous);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.InvalidDraft), null), 500, GlPostingErrors.InvalidDraft);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Header(GlJournalHeaderStatus.Allowed), null), 500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void DraftRejectionsKeepInvalidLineAndMapVersionDriftToConflict()
    {
        var account = GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.AccountRejected, 2), null);
        AssertRejected(account, 422, GlPostingErrors.AccountInvalid);
        Assert.Equal(2, account.InvalidLine);
        var balance = GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.InvalidBalance, 1), null);
        AssertRejected(balance, 422, GlPostingErrors.Unbalanced);
        Assert.Equal(1, balance.InvalidLine);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.MappingChanged), null), 409, GlPostingErrors.MappingChanged);
        var snapshot = GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.SnapshotChanged, 3), null);
        AssertRejected(snapshot, 409, GlPostingErrors.AccountChanged);
        Assert.Equal(3, snapshot.InvalidLine);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.SnapshotConflict, 2), null), 500, GlPostingErrors.InvalidDraft);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.InvalidInput), null), 500, GlPostingErrors.InvalidDraft);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Draft(GlJournalDraftRecheckStatus.Allowed), null), 500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void MalformedDecisionsAreInternalFaults()
    {
        foreach (var decision in new GlPostingDecision?[]
        {
            null,
            new(GlPostingDecisionStatus.PostingRejected),
            new(GlPostingDecisionStatus.PostingRejected, Preflight: new(GlPostingPreflightStatus.Allowed, PeriodId: 7)),
            new(GlPostingDecisionStatus.PostingRejected, Preflight: new(GlPostingPreflightStatus.HeaderRejected)),
            new(GlPostingDecisionStatus.PostingRejected, Preflight: new(GlPostingPreflightStatus.DraftRejected)),
            new((GlPostingDecisionStatus)99),
        })
            AssertRejected(GlPostingOutcomeRules.FromDecision(decision, GlPostingWritePlanStatus.Ready),
                500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void ReadyDecisionWithoutWritablePlanIsInternalFault()
    {
        AssertRejected(GlPostingOutcomeRules.FromDecision(Ready, null), 500, GlPostingErrors.InvalidWritePlan);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Ready, GlPostingWritePlanStatus.NotReady), 500, GlPostingErrors.InvalidWritePlan);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Ready, GlPostingWritePlanStatus.InvalidInput), 500, GlPostingErrors.InvalidWritePlan);
        AssertRejected(GlPostingOutcomeRules.FromDecision(Ready, GlPostingWritePlanStatus.InvalidDraft), 500, GlPostingErrors.InvalidDraft);
    }

    [Fact]
    public void ReadyPlanCannotRescueAMalformedReadyDecision()
    {
        foreach (var decision in new GlPostingDecision[]
        {
            new(GlPostingDecisionStatus.Ready),
            new(GlPostingDecisionStatus.Ready, PeriodId: 0),
            new(GlPostingDecisionStatus.Ready, PeriodId: 7, JournalId: 44),
        })
            AssertRejected(GlPostingOutcomeRules.FromDecision(decision, GlPostingWritePlanStatus.Ready),
                500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void InternalDraftFaultKeepsLineForLogsButNeverExposesJournal()
    {
        var outcome = GlPostingOutcomeRules.FromDecision(
            new(GlPostingDecisionStatus.PostingRejected, JournalId: 44,
                Preflight: new(GlPostingPreflightStatus.DraftRejected,
                    DraftStatus: GlJournalDraftRecheckStatus.SnapshotConflict, InvalidLine: 4)), null);
        AssertRejected(outcome, 500, GlPostingErrors.InvalidDraft);
        Assert.Equal(4, outcome.InvalidLine);
    }

    [Fact]
    public void EveryRejectedHeaderAndDraftStatusHasAMappedNonSuccessOutcome()
    {
        foreach (var status in Enum.GetValues<GlJournalHeaderStatus>().Where(s => s != GlJournalHeaderStatus.Allowed))
        {
            var outcome = GlPostingOutcomeRules.FromDecision(Header(status), null);
            Assert.NotEqual(GlPostingErrors.InvalidDecision, outcome.ErrorCode);
            Assert.InRange(outcome.HttpStatus!.Value, 400, 599);
        }
        foreach (var status in Enum.GetValues<GlJournalDraftRecheckStatus>().Where(s => s != GlJournalDraftRecheckStatus.Allowed))
        {
            var outcome = GlPostingOutcomeRules.FromDecision(Draft(status), null);
            Assert.NotEqual(GlPostingErrors.InvalidDecision, outcome.ErrorCode);
            Assert.InRange(outcome.HttpStatus!.Value, 400, 599);
        }
    }

    [Fact]
    public void AllErrorCodesFollowTheSharedFormatAndAreDistinct()
    {
        var codes = typeof(GlPostingErrors).GetFields()
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.Matches(ErrorCodePattern, code));
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
    }
}
