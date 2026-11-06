using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>WU-FI-004a3x2: offline BR-FI-028 retry / committed-lookup loop (FI-GL-RETRY-001–014).</summary>
public sealed class GlPostingRetryLoopTests
{
    private static readonly GlPostingSource Candidate = new(1, "PUR", "GRN", 900, 3, "FP-ABC");
    private static readonly GlPostingOutcome Posted = GlPostingOutcomeRules.Committed(77);
    private static readonly GlPostingFailure Deadlock = new(GlPostingFailurePhase.BeforeCommit, 1205);
    private static readonly GlPostingFailure CommitLost = new(GlPostingFailurePhase.Commit, null, ConnectionLost: true);
    private static readonly GlPostingFailure Timeout = new(GlPostingFailurePhase.BeforeCommit, -2);

    /// <summary>Scripted attempts/lookups that record call order.</summary>
    private sealed class Script(params GlPostingAttemptResult?[] attempts)
    {
        private readonly Queue<GlPostingAttemptResult?> _attempts = new(attempts);
        public Queue<Func<GlCommittedLookup?>> Lookups { get; } = new();
        public List<int> AttemptNumbers { get; } = [];
        public int LookupCalls { get; private set; }

        public Task<GlPostingAttemptResult?> Attempt(int number, CancellationToken ct)
        {
            AttemptNumbers.Add(number);
            return Task.FromResult(_attempts.Dequeue());
        }

        public Task<GlCommittedLookup?> Lookup(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LookupCalls++;
            return Task.FromResult(Lookups.Dequeue()());
        }

        public Task<GlPostingOutcome> Run(int maxAttempts = GlPostingFailureRules.DefaultMaxAttempts) =>
            RunWith(TestContext.Current.CancellationToken, maxAttempts);

        public Task<GlPostingOutcome> RunWith(CancellationToken ct,
            int maxAttempts = GlPostingFailureRules.DefaultMaxAttempts) =>
            GlPostingRetryLoop.RunAsync(1, Candidate, Attempt, Lookup, maxAttempts, ct);
    }

    private static GlPostingAttemptResult Ok(GlPostingOutcome outcome) => GlPostingAttemptResult.Completed(outcome);
    private static GlPostingAttemptResult Fail(GlPostingFailure failure) => GlPostingAttemptResult.Failed(failure);
    private static GlCommittedLookup Found(GlPostingSource source, long journalId = 44) => new(true, new(source, journalId));

    private static void AssertFault(GlPostingOutcome outcome, int status, string code)
    {
        Assert.Equal(GlPostingOutcomeKind.Fault, outcome.Kind);
        Assert.Equal(status, outcome.HttpStatus);
        Assert.Equal(code, outcome.ErrorCode);
        Assert.False(outcome.Changed);
        Assert.False(outcome.WritesAudit);
        Assert.Null(outcome.JournalId);
    }

    [Fact] // FI-GL-RETRY-001
    public async Task FirstSuccessfulAttemptIsReturnedWithoutLookup()
    {
        var script = new Script(Ok(Posted));

        var outcome = await script.Run();

        Assert.Same(Posted, outcome);
        Assert.Equal([1], script.AttemptNumbers);
        Assert.Equal(0, script.LookupCalls);
    }

    [Theory] // FI-GL-RETRY-002
    [InlineData(1205)]
    [InlineData(1222)]
    [InlineData(GlPostingFailureRules.ApplockNotGranted)]
    public async Task RetryableRollbackRerunsTheWholeAttemptWithNextNumber(int sqlError)
    {
        var script = new Script(Fail(new(GlPostingFailurePhase.BeforeCommit, sqlError)), Ok(Posted));

        var outcome = await script.Run();

        Assert.Same(Posted, outcome);
        Assert.Equal([1, 2], script.AttemptNumbers);
        Assert.Equal(0, script.LookupCalls);
    }

    [Theory] // FI-GL-RETRY-003
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(GlPostingFailureRules.MaxAttemptsLimit)]
    public async Task RetryBudgetIsExhaustedAfterExactlyMaxAttempts(int max)
    {
        var script = new Script(Enumerable.Repeat<GlPostingAttemptResult?>(Fail(Deadlock), max).ToArray());

        var outcome = await script.Run(max);

        AssertFault(outcome, 503, GlPostingErrors.RetryExhausted);
        Assert.Equal(Enumerable.Range(1, max), script.AttemptNumbers);
    }

    [Fact] // FI-GL-RETRY-004
    public async Task UnknownCommitWithSameCommittedSourceReportsSavedJournalUnchanged()
    {
        var script = new Script(Fail(CommitLost));
        script.Lookups.Enqueue(() => Found(Candidate with { }, 44));

        var outcome = await script.Run();

        Assert.Equal((GlPostingOutcomeKind.Unchanged, false, false, 200, 44L),
            (outcome.Kind, outcome.Changed, outcome.WritesAudit, outcome.HttpStatus, outcome.JournalId));
        Assert.Equal([1], script.AttemptNumbers);
        Assert.Equal(1, script.LookupCalls);
    }

    [Fact] // FI-GL-RETRY-005
    public async Task EmptyCompletedLookupRetriesWithinTheSharedBudget()
    {
        var script = new Script(Fail(Timeout), Ok(Posted));
        script.Lookups.Enqueue(() => new(true, null));

        var outcome = await script.Run();

        Assert.Same(Posted, outcome);
        Assert.Equal([1, 2], script.AttemptNumbers);
        Assert.Equal(1, script.LookupCalls);
    }

    [Fact] // FI-GL-RETRY-005 boundary
    public async Task EmptyCompletedLookupOnLastAttemptIsRetryExhausted()
    {
        var script = new Script(Fail(Deadlock), Fail(Timeout));
        script.Lookups.Enqueue(() => new(true, null));

        var outcome = await script.Run(2);

        AssertFault(outcome, 503, GlPostingErrors.RetryExhausted);
        Assert.Equal([1, 2], script.AttemptNumbers);
        Assert.Equal(1, script.LookupCalls);
    }

    [Theory] // FI-GL-RETRY-006
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task UniqueViolationWithChangedCommittedSourceIsConflict(int sqlError)
    {
        var script = new Script(Fail(new(GlPostingFailurePhase.BeforeCommit, sqlError)));
        script.Lookups.Enqueue(() => Found(Candidate with { Fingerprint = "FP-OTHER" }));

        var outcome = await script.Run();

        Assert.Equal((GlPostingOutcomeKind.Rejected, false, 409, GlPostingErrors.SourceConflict),
            (outcome.Kind, outcome.Changed, outcome.HttpStatus, outcome.ErrorCode));
        Assert.Equal([1], script.AttemptNumbers);
    }

    [Fact] // FI-GL-RETRY-007
    public async Task IncompleteNullOrThrowingLookupIsResultUnknownWithoutRetry()
    {
        Func<GlCommittedLookup?>[] lookups =
        [
            () => new(false, Found(Candidate).Committed),
            () => null,
            () => throw new InvalidOperationException("lookup connection failed"),
        ];
        foreach (var lookup in lookups)
        {
            var script = new Script(Fail(CommitLost), Ok(Posted));
            script.Lookups.Enqueue(lookup);

            var outcome = await script.Run();

            AssertFault(outcome, 503, GlPostingErrors.ResultUnknown);
            Assert.Equal([1], script.AttemptNumbers);
        }
    }

    [Theory] // FI-GL-RETRY-008
    [InlineData(null)]
    [InlineData(547)]
    [InlineData(50000)]
    public async Task NonRetryableFailureBeforeCommitIsWriteFailedAfterOneAttempt(int? sqlError)
    {
        var script = new Script(Fail(new(GlPostingFailurePhase.BeforeCommit, sqlError)), Ok(Posted));

        var outcome = await script.Run();

        AssertFault(outcome, 500, GlPostingErrors.WriteFailed);
        Assert.Equal([1], script.AttemptNumbers);
        Assert.Equal(0, script.LookupCalls);
    }

    [Fact] // FI-GL-RETRY-009
    public async Task MalformedAttemptResultIsInvalidDecisionWithoutRetry()
    {
        GlPostingAttemptResult?[] malformed =
        [
            null,
            new(null, null),
            new(Posted, Deadlock),
            Ok(new(GlPostingOutcomeKind.ProceedToWrite, false, null)),
        ];
        foreach (var result in malformed)
        {
            var script = new Script(result, Ok(Posted));

            var outcome = await script.Run();

            AssertFault(outcome, 500, GlPostingErrors.InvalidDecision);
            Assert.Equal([1], script.AttemptNumbers);
        }
    }

    [Theory] // FI-GL-RETRY-010
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(GlPostingFailureRules.MaxAttemptsLimit + 1)]
    public async Task InvalidBudgetIsRejectedBeforeAnyAttempt(int max)
    {
        var script = new Script(Ok(Posted));

        var outcome = await script.Run(max);

        AssertFault(outcome, 500, GlPostingErrors.InvalidDecision);
        Assert.Empty(script.AttemptNumbers);
    }

    [Fact] // FI-GL-RETRY-011
    public async Task CancellationPropagatesBeforeAttemptAndDuringLookup()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var before = new Script(Ok(Posted));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => before.RunWith(cancelled.Token));
        Assert.Empty(before.AttemptNumbers);

        using var cts = new CancellationTokenSource();
        var during = new Script(Fail(CommitLost));
        during.Lookups.Enqueue(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => during.RunWith(cts.Token));
    }

    [Fact] // FI-GL-RETRY-012
    public async Task ExceptionFromAttemptPropagatesWithoutSilentRetry()
    {
        var calls = 0;
        Task<GlPostingAttemptResult?> Throwing(int n, CancellationToken ct)
        {
            calls++;
            throw new InvalidOperationException("adapter bug");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => GlPostingRetryLoop.RunAsync(1, Candidate,
            Throwing, _ => Task.FromResult<GlCommittedLookup?>(new(true, null)),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact] // FI-GL-RETRY-013
    public async Task RejectedOrFaultOutcomeIsReturnedAsIsWithoutRetry()
    {
        GlPostingOutcome[] outcomes =
        [
            new(GlPostingOutcomeKind.Rejected, false, 422, GlPostingErrors.PeriodClosed),
            new(GlPostingOutcomeKind.Unchanged, false, 200, JournalId: 5),
            new(GlPostingOutcomeKind.Fault, false, 500, GlPostingErrors.InvalidDraft),
        ];
        foreach (var expected in outcomes)
        {
            var script = new Script(Ok(expected), Ok(Posted));

            Assert.Same(expected, await script.Run());
            Assert.Equal([1], script.AttemptNumbers);
        }
    }

    [Fact] // FI-GL-RETRY-014
    public async Task NoFailureSequenceEverClaimsAChangeOrLoopsBeyondTheBudget()
    {
        GlPostingFailure[] failures =
        [
            Deadlock, CommitLost, Timeout, new(GlPostingFailurePhase.BeforeCommit, 2627),
            new(GlPostingFailurePhase.BeforeCommit, 547),
        ];
        Func<GlCommittedLookup?>[] lookups =
        [
            () => new(true, null), () => new(false, null), () => Found(Candidate),
            () => Found(Candidate with { SourceVersion = 9 }),
        ];
        foreach (var failure in failures)
        foreach (var lookup in lookups)
        for (var max = 1; max <= 4; max++)
        {
            var script = new Script(Enumerable.Repeat<GlPostingAttemptResult?>(Fail(failure), max).ToArray());
            for (var i = 0; i < max; i++)
                script.Lookups.Enqueue(lookup);

            var outcome = await script.Run(max);

            Assert.False(outcome.Changed);
            Assert.False(outcome.WritesAudit);
            Assert.NotEqual(GlPostingOutcomeKind.ProceedToWrite, outcome.Kind);
            Assert.NotEqual(GlPostingOutcomeKind.Posted, outcome.Kind);
            Assert.InRange(script.AttemptNumbers.Count, 1, max);
        }
    }
}
