using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>WU-FI-004a3x1a: offline single-attempt GL posting orchestration over a scripted port (FI-GL-ATTEMPT-001–014).</summary>
public sealed class GlPostingAttemptRunnerTests
{
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 1, "sha256:abc");
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "Cash", 2, 4, 8, 0), new(2, 8, "2001", "Payable", 2, 4, 0, 8)], 8, 8);
    private static readonly GlPostingRequest Request = new(3, Source, new(2024, 2, 29), "TWD", Draft, 4);
    private static readonly GlPeriodWindow OpenFebruary = new(7, 3, new(2024, 2, 1), new(2024, 2, 29), true);

    private static AccountInfo Account(long id, string code, string name, int version = 2) =>
        new(id, 3, version, code, name, null, AccountType.Asset, BalanceDirection.Debit, true, true);

    private static GlPostingAuthority Authority(GlCommittedSource? committed = null,
        IReadOnlyList<GlPeriodWindow>? periods = null, int mappingVersion = 4, int accountVersion = 2) =>
        new(committed, "TWD", periods ?? [OpenFebruary], mappingVersion,
            [Account(7, "1002", "Cash", accountVersion), Account(8, "2001", "Payable")]);

    private sealed class FakeSqlException(int? number, bool connectionLost = false) : Exception("fake sql")
    {
        public int? Number { get; } = number;
        public bool ConnectionLost { get; } = connectionLost;
    }

    /// <summary>Behaviour of one attempt: which step throws (if any), authority and written journal id.</summary>
    private sealed record Step(Exception? Begin = null, Exception? Lock = null, Exception? Read = null,
        Exception? Write = null, Exception? Commit = null, Exception? Dispose = null,
        GlPostingAuthority? Authority = null, bool NullAuthority = false, bool NullSession = false, long JournalId = 501,
        Action? OnLock = null);

    private sealed class Port(params Step[] steps) : IGlPostingTransactionPort
    {
        private readonly Queue<Step> _steps = new(steps);
        public List<string> Calls { get; } = [];
        public List<GlPostingWritePlan> Plans { get; } = [];
        public List<GlPostingSource> LookupSources { get; } = [];
        public List<bool> CommitTokenCancellable { get; } = [];
        public Queue<Func<GlCommittedLookup?>> Lookups { get; } = new();

        public Task<IGlPostingAttemptSession?> BeginAsync(GlPostingRequest request, CancellationToken ct)
        {
            var step = _steps.Dequeue();
            Calls.Add("begin");
            if (step.Begin is not null)
                throw step.Begin;
            return Task.FromResult<IGlPostingAttemptSession?>(step.NullSession ? null : new Session(this, step));
        }

        public Task<GlCommittedLookup?> LookupCommittedAsync(GlPostingSource source, CancellationToken ct)
        {
            Calls.Add("lookup");
            LookupSources.Add(source);
            return Task.FromResult(Lookups.Dequeue()());
        }

        public int? SqlErrorNumber(Exception exception) => (exception as FakeSqlException)?.Number;

        public bool IsConnectionLost(Exception exception) => exception is FakeSqlException { ConnectionLost: true };

        private sealed class Session(Port port, Step step) : IGlPostingAttemptSession
        {
            public Task LockAsync(CancellationToken ct)
            {
                port.Calls.Add("lock");
                step.OnLock?.Invoke();
                ct.ThrowIfCancellationRequested();
                return step.Lock is null ? Task.CompletedTask : throw step.Lock;
            }

            public Task<GlPostingAuthority?> ReadAuthorityAsync(CancellationToken ct)
            {
                port.Calls.Add("read");
                if (step.Read is not null)
                    throw step.Read;
                return Task.FromResult(step.NullAuthority ? null : step.Authority ?? Authority());
            }

            public Task<long> WriteAsync(GlPostingWritePlan plan, CancellationToken ct)
            {
                port.Calls.Add("write");
                port.Plans.Add(plan);
                return step.Write is null ? Task.FromResult(step.JournalId) : throw step.Write;
            }

            public Task CommitAsync(CancellationToken ct)
            {
                port.Calls.Add("commit");
                port.CommitTokenCancellable.Add(ct.CanBeCanceled);
                return step.Commit is null ? Task.CompletedTask : throw step.Commit;
            }

            public ValueTask DisposeAsync()
            {
                port.Calls.Add("dispose");
                return step.Dispose is null ? ValueTask.CompletedTask : throw step.Dispose;
            }
        }
    }

    private static Task<GlPostingOutcome> Post(Port port, GlPostingRequest? request = null,
        int maxAttempts = GlPostingFailureRules.DefaultMaxAttempts, CancellationToken? token = null) =>
        GlPostingAttemptRunner.PostAsync(request ?? Request, port, maxAttempts,
            token ?? TestContext.Current.CancellationToken);

    private static void AssertNotPosted(GlPostingOutcome outcome, GlPostingOutcomeKind kind, int status, string? code)
    {
        Assert.Equal(kind, outcome.Kind);
        Assert.Equal(status, outcome.HttpStatus);
        Assert.Equal(code, outcome.ErrorCode);
        Assert.False(outcome.Changed);
        Assert.False(outcome.WritesAudit);
    }

    private static readonly string[] FullAttempt = ["begin", "lock", "read", "write", "commit", "dispose"];
    private static readonly string[] ReadOnlyAttempt = ["begin", "lock", "read", "dispose"];

    [Fact] // FI-GL-ATTEMPT-001
    public async Task NewSourceRunsLockReadWriteCommitInOrderAndPostsOnlyAfterCommit()
    {
        var port = new Port(new Step());
        var outcome = await Post(port);

        Assert.Equal(new GlPostingOutcome(GlPostingOutcomeKind.Posted, true, 200, JournalId: 501), outcome);
        Assert.True(outcome.WritesAudit);
        Assert.Equal(FullAttempt, port.Calls);
        var plan = Assert.Single(port.Plans);
        Assert.Equal(GlPostingWritePlanStatus.Ready, plan.Status);
        Assert.Equal(7, plan.Journal!.PeriodId);
        Assert.Equal("TWD", plan.Journal.BaseCurrency);
        Assert.Equal(new DateOnly(2024, 2, 29), plan.Journal.BusinessDate);
        Assert.Equal(("SD", "Invoice", 12L, 1, "sha256:abc"),
            (plan.SourceKey!.SourceModule, plan.SourceKey.SourceType, plan.SourceKey.SourceId,
                plan.SourceKey.SourceVersion, plan.SourceKey.Fingerprint));
        Assert.Equal([1, 2], plan.Lines.Select(static line => line.Sequence));
        Assert.Equal([false], port.CommitTokenCancellable);
    }

    [Fact] // FI-GL-ATTEMPT-002
    public async Task ExactCommittedReplayReturnsSavedJournalWithoutWriteOrCommit()
    {
        var port = new Port(new Step(Authority: Authority(new GlCommittedSource(Source, 44), periods: [], mappingVersion: 9)));
        var outcome = await Post(port);

        AssertNotPosted(outcome, GlPostingOutcomeKind.Unchanged, 200, null);
        Assert.Equal(44, outcome.JournalId);
        Assert.Equal(ReadOnlyAttempt, port.Calls);
    }

    [Fact] // FI-GL-ATTEMPT-003
    public async Task ChangedCommittedSourceIsConflictWithoutWrite()
    {
        foreach (var saved in new[] { Source with { Fingerprint = "sha256:old" }, Source with { SourceVersion = 2 } })
        {
            var port = new Port(new Step(Authority: Authority(new GlCommittedSource(saved, 44))));
            var outcome = await Post(port);

            AssertNotPosted(outcome, GlPostingOutcomeKind.Rejected, 409, GlPostingErrors.SourceConflict);
            Assert.Null(outcome.JournalId);
            Assert.Equal(ReadOnlyAttempt, port.Calls);
        }
    }

    public static TheoryData<string, int, string> RejectedAuthorities => new()
    {
        { "missing", 422, GlPostingErrors.PeriodMissing },
        { "closed", 422, GlPostingErrors.PeriodClosed },
        { "ambiguous", 422, GlPostingErrors.PeriodAmbiguous },
        { "mapping", 409, GlPostingErrors.MappingChanged },
        { "account", 409, GlPostingErrors.AccountChanged },
    };

    [Theory] // FI-GL-ATTEMPT-004
    [MemberData(nameof(RejectedAuthorities))]
    public async Task AuthorityReadInsideTheTransactionRejectsWithZeroWrites(string change, int status, string code)
    {
        var authority = change switch
        {
            "missing" => Authority(periods: []),
            "closed" => Authority(periods: [OpenFebruary with { IsOpen = false }]),
            "ambiguous" => Authority(periods: [OpenFebruary, OpenFebruary with { Id = 8 }]),
            "mapping" => Authority(mappingVersion: 5),
            _ => Authority(accountVersion: 3),
        };
        var port = new Port(new Step(Authority: authority));
        var outcome = await Post(port);

        AssertNotPosted(outcome, GlPostingOutcomeKind.Rejected, status, code);
        Assert.Equal(ReadOnlyAttempt, port.Calls);
        Assert.Empty(port.Plans);
    }

    [Fact] // FI-GL-ATTEMPT-005
    public async Task InvalidRequestIsRejectedBeforeAnyTransaction()
    {
        var cases = new (GlPostingRequest? Request, int Status, string Code)[]
        {
            (null, 400, GlPostingErrors.InvalidInput),
            (Request with { Source = null! }, 400, GlPostingErrors.InvalidInput),
            (Request with { CompanyId = 0 }, 400, GlPostingErrors.InvalidInput),
            (Request with { CompanyId = 4 }, 404, GlPostingErrors.SourceNotFound),
            (Request with { Source = Source with { Fingerprint = "bad\nkey" } }, 400, GlPostingErrors.InvalidSource),
            (Request with { Source = Source with { SourceId = 0 } }, 400, GlPostingErrors.InvalidSource),
        };
        foreach (var (request, status, code) in cases)
        {
            var port = new Port();
            var outcome = await GlPostingAttemptRunner.PostAsync(request, port,
                cancellationToken: TestContext.Current.CancellationToken);

            AssertNotPosted(outcome, GlPostingOutcomeKind.Rejected, status, code);
            Assert.Empty(port.Calls);
        }
    }

    [Fact] // FI-GL-ATTEMPT-006
    public async Task RetryableLockOrReadFailureRerunsTheWholeAttemptWithAFreshPlan()
    {
        var port = new Port(new Step(Lock: new FakeSqlException(1205)), new Step(Read: new FakeSqlException(1222)),
            new Step(JournalId: 777));
        var outcome = await Post(port);

        Assert.Equal(GlPostingOutcomeKind.Posted, outcome.Kind);
        Assert.Equal(777, outcome.JournalId);
        Assert.Equal(["begin", "lock", "dispose", "begin", "lock", "read", "dispose", .. FullAttempt], port.Calls);
        Assert.Single(port.Plans);
    }

    [Fact] // FI-GL-ATTEMPT-007
    public async Task ApplockNotGrantedOnEveryAttemptExhaustsTheSharedBudget()
    {
        var port = new Port(new Step(Lock: new FakeSqlException(51021)), new Step(Lock: new FakeSqlException(51021)),
            new Step(Lock: new FakeSqlException(51021)));
        var outcome = await Post(port);

        AssertNotPosted(outcome, GlPostingOutcomeKind.Fault, 503, GlPostingErrors.RetryExhausted);
        Assert.Equal(3, port.Calls.Count(static call => call == "begin"));
        Assert.DoesNotContain("write", port.Calls);
        Assert.Equal(3, port.Calls.Count(static call => call == "dispose"));
    }

    [Fact] // FI-GL-ATTEMPT-008
    public async Task DuplicateKeyOnWriteResolvesFromCommittedSourceWithoutCommit()
    {
        var port = new Port(new Step(Write: new FakeSqlException(2627)));
        port.Lookups.Enqueue(() => new(true, new(Source, 44)));
        var outcome = await Post(port);

        AssertNotPosted(outcome, GlPostingOutcomeKind.Unchanged, 200, null);
        Assert.Equal(44, outcome.JournalId);
        Assert.Equal(["begin", "lock", "read", "write", "dispose", "lookup"], port.Calls);
        Assert.Equal([Source], port.LookupSources);
    }

    [Fact] // FI-GL-ATTEMPT-009
    public async Task CommitFailureIsCommitPhaseAndNeverRetriedBlindly()
    {
        // Commit failed without a SQL number (non-transport): only the commit phase sends it to the lookup.
        var port = new Port(new Step(Commit: new InvalidOperationException("commit lost")), new Step(JournalId: 909));
        port.Lookups.Enqueue(() => new(true, null));
        var outcome = await Post(port);

        Assert.Equal(GlPostingOutcomeKind.Posted, outcome.Kind);
        Assert.Equal(909, outcome.JournalId);
        Assert.Equal([.. FullAttempt, "lookup", .. FullAttempt], port.Calls);

        var foundPort = new Port(new Step(Commit: new FakeSqlException(null, connectionLost: true)));
        foundPort.Lookups.Enqueue(() => new(true, new(Source, 501)));
        var found = await Post(foundPort);
        AssertNotPosted(found, GlPostingOutcomeKind.Unchanged, 200, null);
        Assert.Equal(501, found.JournalId);
        Assert.Equal([.. FullAttempt, "lookup"], foundPort.Calls);
    }

    [Fact] // FI-GL-ATTEMPT-010
    public async Task NonRetryableFailuresReportWriteFailedAfterOneAttempt()
    {
        foreach (var step in new[]
        {
            new Step(Begin: new FakeSqlException(547)),
            new Step(Lock: new FakeSqlException(51022)),
            new Step(Read: new InvalidOperationException("bad read")),
            new Step(Write: new FakeSqlException(50000)),
        })
        {
            var port = new Port(step);
            var outcome = await Post(port);

            AssertNotPosted(outcome, GlPostingOutcomeKind.Fault, 500, GlPostingErrors.WriteFailed);
            Assert.Single(port.Calls, static call => call == "begin");
            Assert.DoesNotContain("commit", port.Calls);
            Assert.DoesNotContain("lookup", port.Calls);
        }
    }

    [Fact] // FI-GL-ATTEMPT-011
    public async Task MalformedPortResultsAreInternalFaultsWithoutCommit()
    {
        var cases = new (Step Step, string Code, string[] Calls)[]
        {
            (new Step(NullSession: true), GlPostingErrors.InvalidDecision, ["begin"]),
            (new Step(NullAuthority: true), GlPostingErrors.InvalidDecision, ReadOnlyAttempt),
            (new Step(JournalId: 0), GlPostingErrors.InvalidCommit, ["begin", "lock", "read", "write", "dispose"]),
        };
        foreach (var (step, code, calls) in cases)
        {
            var port = new Port(step);
            var outcome = await Post(port);

            AssertNotPosted(outcome, GlPostingOutcomeKind.Fault, 500, code);
            Assert.Equal(calls, port.Calls);
        }
    }

    [Fact] // FI-GL-ATTEMPT-015
    public async Task MissingOrUnbalancedDraftNeverReachesTheWriter()
    {
        var unbalanced = Draft with { TotalCredit = 9 };
        foreach (var draft in new GlJournalLineDraftResult?[] { null, unbalanced, Draft with { Lines = [] } })
        {
            var port = new Port(new Step());
            var outcome = await Post(port, Request with { Draft = draft });

            Assert.NotEqual(GlPostingOutcomeKind.Posted, outcome.Kind);
            Assert.False(outcome.Changed);
            Assert.True(outcome.HttpStatus is 422 or 500, $"status {outcome.HttpStatus}");
            Assert.Equal(ReadOnlyAttempt, port.Calls);
            Assert.Empty(port.Plans);
        }
    }

    [Fact] // FI-GL-ATTEMPT-012
    public async Task CancellationBeforeCommitPropagatesAndDisposesWithoutCommit()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var port = new Port(new Step(OnLock: cts.Cancel));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Post(port, token: cts.Token));
        Assert.Equal(["begin", "lock", "dispose"], port.Calls);

        var idle = new Port(new Step());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Post(idle, token: cts.Token));
        Assert.Empty(idle.Calls);
    }

    [Fact] // FI-GL-ATTEMPT-013
    public async Task DisposeFailureDoesNotHideTheAttemptResult()
    {
        var posted = await Post(new Port(new Step(Dispose: new InvalidOperationException("rollback lost"))));
        Assert.Equal(GlPostingOutcomeKind.Posted, posted.Kind);

        var rejected = await Post(new Port(new Step(Authority: Authority(periods: []),
            Dispose: new FakeSqlException(10054, connectionLost: true))));
        AssertNotPosted(rejected, GlPostingOutcomeKind.Rejected, 422, GlPostingErrors.PeriodMissing);
    }

    [Fact] // FI-GL-ATTEMPT-014
    public async Task OnlyASuccessfulCommitOfThisAttemptCanBePosted()
    {
        Exception[] failures = [new FakeSqlException(1205), new FakeSqlException(-2), new FakeSqlException(2601),
            new FakeSqlException(547), new InvalidOperationException("x")];
        foreach (var failure in failures)
        {
            foreach (var at in new[] { "begin", "lock", "read", "write", "commit" })
            {
                var step = at switch
                {
                    "begin" => new Step(Begin: failure),
                    "lock" => new Step(Lock: failure),
                    "read" => new Step(Read: failure),
                    "write" => new Step(Write: failure),
                    _ => new Step(Commit: failure),
                };
                var port = new Port(step);
                port.Lookups.Enqueue(() => new(false, null));
                var outcome = await Post(port, maxAttempts: 1);

                Assert.NotEqual(GlPostingOutcomeKind.Posted, outcome.Kind);
                Assert.NotEqual(GlPostingOutcomeKind.ProceedToWrite, outcome.Kind);
                Assert.False(outcome.Changed);
                Assert.False(outcome.WritesAudit);
                Assert.Equal(at == "begin" ? 0 : 1, port.Calls.Count(static call => call == "dispose"));
            }
        }
    }
}
