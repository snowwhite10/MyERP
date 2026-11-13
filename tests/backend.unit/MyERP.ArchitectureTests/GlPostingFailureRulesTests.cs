using System.Text.RegularExpressions;
using MyERP.Modules.Finance.Application;

namespace MyERP.ArchitectureTests;

public sealed class GlPostingFailureRulesTests
{
    private static readonly Regex ErrorCodePattern = new(@"^[A-Z][A-Z0-9_]*(\.[A-Z0-9_]+){2,}$");

    private static GlPostingFailure BeforeCommit(int? number, bool connectionLost = false) =>
        new(GlPostingFailurePhase.BeforeCommit, number, connectionLost);

    private static void AssertReported(GlPostingFailureResolution resolution, int httpStatus, string errorCode)
    {
        Assert.Equal(GlPostingFailureAction.Report, resolution.Action);
        var outcome = Assert.IsType<GlPostingOutcome>(resolution.Outcome);
        Assert.Equal(GlPostingOutcomeKind.Fault, outcome.Kind);
        Assert.Equal(httpStatus, outcome.HttpStatus);
        Assert.Equal(errorCode, outcome.ErrorCode);
        Assert.False(outcome.Changed);
        Assert.False(outcome.WritesAudit);
        Assert.Null(outcome.JournalId);
        Assert.Matches(ErrorCodePattern, outcome.ErrorCode!);
    }

    private static void AssertAction(GlPostingFailureResolution resolution, GlPostingFailureAction action)
    {
        Assert.Equal(action, resolution.Action);
        Assert.Null(resolution.Outcome);
    }

    [Theory]
    [InlineData(1205, 1)]
    [InlineData(1205, 2)]
    [InlineData(1222, 1)]
    [InlineData(1222, 2)]
    [InlineData(51021, 1)]
    [InlineData(51021, 2)]
    public void DeadlockOrLockTimeoutBeforeCommitRetriesTheWholePostingWithinBudget(int number, int attemptsMade)
    {
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number), attemptsMade), GlPostingFailureAction.RetryPosting);
    }

    [Theory]
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(51021)]
    public void ExhaustedRetryBudgetIsServiceUnavailableNeverSuccess(int number)
    {
        AssertReported(GlPostingFailureRules.Classify(BeforeCommit(number), GlPostingFailureRules.DefaultMaxAttempts),
            503, GlPostingErrors.RetryExhausted);
        AssertReported(GlPostingFailureRules.Classify(BeforeCommit(number), 5, maxAttempts: 5), 503, GlPostingErrors.RetryExhausted);
        AssertReported(GlPostingFailureRules.Classify(BeforeCommit(number), 1, maxAttempts: 1), 503, GlPostingErrors.RetryExhausted);
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number), 4, maxAttempts: 5), GlPostingFailureAction.RetryPosting);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(53)]
    [InlineData(64)]
    [InlineData(121)]
    [InlineData(233)]
    [InlineData(10053)]
    [InlineData(10054)]
    [InlineData(10060)]
    public void CommandTimeoutOrTransportErrorIsUnknownAndGoesToCommittedLookupEvenWhenBudgetIsSpent(int number)
    {
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number), 1), GlPostingFailureAction.ResolveFromCommitted);
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number), 3), GlPostingFailureAction.ResolveFromCommitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1205)]
    [InlineData(2627)]
    [InlineData(547)]
    public void AnyFailureDuringCommitOrWithLostConnectionIsUnknownNotAFailure(int? number)
    {
        AssertAction(GlPostingFailureRules.Classify(new(GlPostingFailurePhase.Commit, number), 1),
            GlPostingFailureAction.ResolveFromCommitted);
        AssertAction(GlPostingFailureRules.Classify(new(GlPostingFailurePhase.Commit, number), 3),
            GlPostingFailureAction.ResolveFromCommitted);
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number, connectionLost: true), 3),
            GlPostingFailureAction.ResolveFromCommitted);
    }

    [Theory]
    [InlineData(2601)]
    [InlineData(2627)]
    public void UniqueKeyViolationReadsTheCommittedSourceKeyInsteadOfReportingError(int number)
    {
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number), 1), GlPostingFailureAction.ResolveFromCommitted);
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(number), 3), GlPostingFailureAction.ResolveFromCommitted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(547)]
    [InlineData(8115)]
    [InlineData(50000)]
    [InlineData(51022)] // 過帳 applock 被取消或錯誤（非逾時／死結），不重跑
    public void OtherErrorsBeforeCommitAreRolledBackInternalFaultsWithoutRetry(int? number)
    {
        AssertReported(GlPostingFailureRules.Classify(BeforeCommit(number), 1), 500, GlPostingErrors.WriteFailed);
    }

    [Fact]
    public void RetryAfterUnknownResolutionSharesTheSameBudget()
    {
        AssertAction(GlPostingFailureRules.RetryOrExhausted(1), GlPostingFailureAction.RetryPosting);
        AssertAction(GlPostingFailureRules.RetryOrExhausted(2), GlPostingFailureAction.RetryPosting);
        AssertReported(GlPostingFailureRules.RetryOrExhausted(3), 503, GlPostingErrors.RetryExhausted);
        AssertReported(GlPostingFailureRules.RetryOrExhausted(9, maxAttempts: 4), 503, GlPostingErrors.RetryExhausted);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(1, 0)]
    [InlineData(1, -3)]
    [InlineData(1, 11)]
    public void InvalidBudgetIsInternalFaultBeforeAnyClassification(int attemptsMade, int maxAttempts)
    {
        AssertReported(GlPostingFailureRules.Classify(BeforeCommit(1205), attemptsMade, maxAttempts), 500, GlPostingErrors.InvalidDecision);
        AssertReported(GlPostingFailureRules.Classify(new(GlPostingFailurePhase.Commit, -2), attemptsMade, maxAttempts),
            500, GlPostingErrors.InvalidDecision);
        AssertReported(GlPostingFailureRules.RetryOrExhausted(attemptsMade, maxAttempts), 500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void MissingFailureOrUndefinedPhaseIsInternalFault()
    {
        AssertReported(GlPostingFailureRules.Classify(null, 1), 500, GlPostingErrors.InvalidDecision);
        AssertReported(GlPostingFailureRules.Classify(new((GlPostingFailurePhase)99, 1205), 1), 500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void MaximumAllowedBudgetIsAccepted()
    {
        Assert.Equal(10, GlPostingFailureRules.MaxAttemptsLimit);
        AssertAction(GlPostingFailureRules.Classify(BeforeCommit(1205), 9, maxAttempts: 10), GlPostingFailureAction.RetryPosting);
        AssertReported(GlPostingFailureRules.Classify(BeforeCommit(1205), 10, maxAttempts: 10), 503, GlPostingErrors.RetryExhausted);
    }

    [Fact]
    public void NoClassificationEverReportsSuccessOrChange()
    {
        int?[] numbers = [null, -2, 0, 547, 1205, 1222, 51021, 51022, 2601, 2627, 10054];
        foreach (var phase in new[] { GlPostingFailurePhase.BeforeCommit, GlPostingFailurePhase.Commit })
        foreach (var number in numbers)
        foreach (var lost in new[] { false, true })
        for (var attempts = 1; attempts <= 4; attempts++)
        {
            var resolution = GlPostingFailureRules.Classify(new(phase, number, lost), attempts);
            if (resolution.Action == GlPostingFailureAction.Report)
            {
                var outcome = Assert.IsType<GlPostingOutcome>(resolution.Outcome);
                Assert.Equal(GlPostingOutcomeKind.Fault, outcome.Kind);
                Assert.False(outcome.Changed);
                Assert.Null(outcome.JournalId);
            }
            else
            {
                Assert.Null(resolution.Outcome);
            }
        }
    }

    // WU-FI-002a2a（ADR-107）：加入呼叫者交易時，失敗只回報一次；呼叫者交易已失效，不重試、不查詢已提交結果。
    [Theory]
    [InlineData(GlPostingFailurePhase.BeforeCommit, 1205, false)]
    [InlineData(GlPostingFailurePhase.BeforeCommit, 1222, false)]
    [InlineData(GlPostingFailurePhase.BeforeCommit, 51021, false)]
    [InlineData(GlPostingFailurePhase.BeforeCommit, -2, false)]
    [InlineData(GlPostingFailurePhase.BeforeCommit, 10054, false)]
    [InlineData(GlPostingFailurePhase.BeforeCommit, null, true)]
    [InlineData(GlPostingFailurePhase.Commit, null, false)]
    public void JoinedLockContentionOrLostTransactionIsRetryableTransactionAborted(GlPostingFailurePhase phase,
        int? number, bool lost)
    {
        var outcome = GlPostingFailureRules.ClassifyJoined(new(phase, number, lost));
        AssertReported(new(GlPostingFailureAction.Report, outcome), 503, GlPostingErrors.TransactionAborted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(547)]
    [InlineData(2601)]
    [InlineData(2627)]
    [InlineData(51022)]
    public void JoinedOtherFailuresAreWriteFailed(int? number)
    {
        AssertReported(new(GlPostingFailureAction.Report, GlPostingFailureRules.ClassifyJoined(BeforeCommit(number))),
            500, GlPostingErrors.WriteFailed);
    }

    [Fact]
    public void JoinedMissingFailureOrUndefinedPhaseIsInternalFault()
    {
        AssertReported(new(GlPostingFailureAction.Report, GlPostingFailureRules.ClassifyJoined(null)), 500,
            GlPostingErrors.InvalidDecision);
        AssertReported(new(GlPostingFailureAction.Report,
            GlPostingFailureRules.ClassifyJoined(new((GlPostingFailurePhase)99, 1205))), 500, GlPostingErrors.InvalidDecision);
    }
}
