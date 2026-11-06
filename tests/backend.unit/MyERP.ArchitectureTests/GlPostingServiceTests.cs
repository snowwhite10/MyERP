using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-FI-004a3x1b2a: offline <see cref="GlPostingService"/> composition — current-company scope before any
/// transaction, then the single <see cref="GlPostingAttemptRunner"/> order over the injected port (FI-GL-SVC-001–007).
/// </summary>
public sealed class GlPostingServiceTests
{
    private static readonly GlPostingSource Source = new(3, "SD", "Invoice", 12, 1, "sha256:abc");
    private static readonly GlJournalLineDraftResult Draft = new(GlJournalLineDraftStatus.Allowed,
        [new(1, 7, "1002", "Cash", 2, 4, 8, 0), new(2, 8, "2001", "Payable", 2, 4, 0, 8)], 8, 8);
    private static readonly GlPostingRequest Request = new(3, Source, new(2024, 2, 29), "TWD", Draft, 4);

    private static AccountInfo Account(long id, string code, string name) =>
        new(id, 3, 2, code, name, null, AccountType.Asset, BalanceDirection.Debit, true, true);

    private static readonly GlPostingAuthority Authority = new(null, "TWD",
        [new GlPeriodWindow(7, 3, new(2024, 2, 1), new(2024, 2, 29), true)], 4,
        [Account(7, "1002", "Cash"), Account(8, "2001", "Payable")]);

    private sealed class Company(long? companyId) : ICurrentCompany
    {
        public long? CompanyId { get; } = companyId;
    }

    private sealed class SqlFailure(int number) : Exception("fake sql")
    {
        public int Number { get; } = number;
    }

    /// <summary>Each queued entry is one attempt: null succeeds, otherwise the write throws it.</summary>
    private sealed class Port(params Exception?[] writeFailures) : IGlPostingTransactionPort
    {
        private readonly Queue<Exception?> _failures = new(writeFailures);
        public List<string> Calls { get; } = [];

        public Task<IGlPostingAttemptSession?> BeginAsync(GlPostingRequest request, CancellationToken ct)
        {
            Calls.Add($"begin:{request.CompanyId}");
            return Task.FromResult<IGlPostingAttemptSession?>(new Session(this, _failures.Dequeue()));
        }

        public Task<GlCommittedLookup?> LookupCommittedAsync(GlPostingSource source, CancellationToken ct)
        {
            Calls.Add("lookup");
            return Task.FromResult<GlCommittedLookup?>(null);
        }

        public int? SqlErrorNumber(Exception exception) => (exception as SqlFailure)?.Number;

        public bool IsConnectionLost(Exception exception) => false;

        private sealed class Session(Port port, Exception? writeFailure) : IGlPostingAttemptSession
        {
            public Task LockAsync(CancellationToken ct)
            {
                port.Calls.Add("lock");
                return Task.CompletedTask;
            }

            public Task<GlPostingAuthority?> ReadAuthorityAsync(CancellationToken ct)
            {
                port.Calls.Add("read");
                return Task.FromResult<GlPostingAuthority?>(Authority);
            }

            public Task<long> WriteAsync(GlPostingWritePlan plan, CancellationToken ct)
            {
                port.Calls.Add("write");
                return writeFailure is null ? Task.FromResult(501L) : throw writeFailure;
            }

            public Task CommitAsync(CancellationToken ct)
            {
                port.Calls.Add("commit");
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                port.Calls.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }

    private static void AssertRejectedWithoutTransaction(GlPostingOutcome outcome, Port port, int status, string code)
    {
        Assert.Equal(new GlPostingOutcome(GlPostingOutcomeKind.Rejected, false, status, code), outcome);
        Assert.False(outcome.WritesAudit);
        Assert.Empty(port.Calls);
    }

    [Fact] // FI-GL-SVC-001
    public async Task CurrentCompanyRequestRunsOneFullAttemptAndPostsAfterCommit()
    {
        var port = new Port((Exception?)null);
        var outcome = await new GlPostingService(port, new Company(3))
            .PostAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(new GlPostingOutcome(GlPostingOutcomeKind.Posted, true, 200, JournalId: 501), outcome);
        Assert.True(outcome.WritesAudit);
        Assert.Equal(["begin:3", "lock", "read", "write", "commit", "dispose"], port.Calls);
    }

    [Fact] // FI-GL-SVC-002
    public async Task OtherCompanyRequestIsNotFoundWithoutOpeningTransaction()
    {
        foreach (var request in new[] { Request with { CompanyId = 4, Source = Source with { CompanyId = 4 } },
                     Request with { CompanyId = 4 } })
        {
            var port = new Port();
            var outcome = await new GlPostingService(port, new Company(3))
                .PostAsync(request, TestContext.Current.CancellationToken);
            AssertRejectedWithoutTransaction(outcome, port, 404, GlPostingErrors.SourceNotFound);
        }
    }

    [Fact] // FI-GL-SVC-003
    public async Task MissingOrInvalidCompanyContextFailsClosedAsNotFound()
    {
        foreach (var companyId in new long?[] { null, 0, -3 })
        {
            var port = new Port();
            var outcome = await new GlPostingService(port, new Company(companyId))
                .PostAsync(Request, TestContext.Current.CancellationToken);
            AssertRejectedWithoutTransaction(outcome, port, 404, GlPostingErrors.SourceNotFound);
        }
    }

    [Fact] // FI-GL-SVC-004
    public async Task InvalidRequestIsBadRequestBeforeCompanyScopeAndTransaction()
    {
        var invalid = new GlPostingRequest?[]
        {
            null, Request with { Source = null! }, Request with { CompanyId = 0 },
            Request with { Source = Source with { Fingerprint = " " } },
        };
        foreach (var companyId in new long?[] { 3, null })
        {
            foreach (var request in invalid)
            {
                var port = new Port();
                var outcome = await new GlPostingService(port, new Company(companyId))
                    .PostAsync(request, TestContext.Current.CancellationToken);
                Assert.Equal(400, outcome.HttpStatus);
                Assert.False(outcome.Changed);
                Assert.Empty(port.Calls);
            }
        }
    }

    [Fact] // FI-GL-SVC-005
    public async Task DeadlockIsRetriedWithinDefaultBudgetAndExhaustionIs503()
    {
        var retried = new Port(new SqlFailure(1205), null);
        var posted = await new GlPostingService(retried, new Company(3))
            .PostAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal(GlPostingOutcomeKind.Posted, posted.Kind);
        Assert.Equal(2, retried.Calls.Count(static call => call.StartsWith("begin:", StringComparison.Ordinal)));
        Assert.Equal(1, retried.Calls.Count(static call => call == "commit"));

        var exhausted = new Port(new SqlFailure(1205), new SqlFailure(1205), new SqlFailure(1205));
        var outcome = await new GlPostingService(exhausted, new Company(3))
            .PostAsync(Request, TestContext.Current.CancellationToken);
        Assert.Equal(503, outcome.HttpStatus);
        Assert.Equal(GlPostingErrors.RetryExhausted, outcome.ErrorCode);
        Assert.False(outcome.Changed);
        Assert.Equal(GlPostingFailureRules.DefaultMaxAttempts,
            exhausted.Calls.Count(static call => call.StartsWith("begin:", StringComparison.Ordinal)));
        Assert.DoesNotContain("commit", exhausted.Calls);
    }

    [Fact] // FI-GL-SVC-006
    public async Task CancelledCallerTokenThrowsWithoutOpeningTransaction()
    {
        var port = new Port();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GlPostingService(port, new Company(3)).PostAsync(Request, cancelled.Token));
        Assert.Empty(port.Calls);
    }

    [Fact] // FI-GL-SVC-007
    public void ServiceRequiresPortAndCompanyAndIsTheFinancePostingEntryPoint()
    {
        Assert.Throws<ArgumentNullException>(() => new GlPostingService(null!, new Company(3)));
        Assert.Throws<ArgumentNullException>(() => new GlPostingService(new Port(), null!));
        Assert.True(typeof(IGlPostingService).IsAssignableFrom(typeof(GlPostingService)));
        Assert.Equal("MyERP.Modules.Finance.Application", typeof(IGlPostingService).Namespace);
    }
}
