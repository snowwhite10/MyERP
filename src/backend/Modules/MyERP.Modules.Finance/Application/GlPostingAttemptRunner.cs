using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// One posting request built by an FI-owned source adapter from a saved source: the permanent source identity,
/// saved business date and currency, and the MD-checked line draft with the FI mapping version it was built from.
/// </summary>
public sealed record GlPostingRequest(long CompanyId, GlPostingSource Source, DateOnly BusinessDate,
    string? BaseCurrency, GlJournalLineDraftResult? Draft, int ExpectedMappingVersion);

/// <summary>
/// Authorities read inside the attempt transaction after both posting locks are held: the committed source key
/// (null if none), the company base currency, the company periods, the current FI mapping version and the current
/// MD account snapshots in draft line order (null entries for missing accounts).
/// </summary>
public sealed record GlPostingAuthority(GlCommittedSource? Committed, string? CompanyBaseCurrency,
    IReadOnlyList<GlPeriodWindow>? Periods, int CurrentMappingVersion, IReadOnlyList<AccountInfo?>? CurrentAccounts);

/// <summary>
/// One SQL posting transaction (WU-FI-004a3x1b). Disposing without a successful <see cref="CommitAsync"/> must roll
/// back; a failing step must already have rolled back (as <c>GlPostingWriter</c> does).
/// </summary>
public interface IGlPostingAttemptSession : IAsyncDisposable
{
    /// <summary>Company period lock (Shared) then permanent source lock (Exclusive), as <c>GlPostingWriter.LockAsync</c>.</summary>
    Task LockAsync(CancellationToken cancellationToken);

    /// <summary>Reads committed authorities in the same transaction; null is a malformed adapter result.</summary>
    Task<GlPostingAuthority?> ReadAuthorityAsync(CancellationToken cancellationToken);

    /// <summary>Inserts a Ready plan (source key → journal → lines) and returns the uncommitted journal ID.</summary>
    Task<long> WriteAsync(GlPostingWritePlan plan, CancellationToken cancellationToken);

    /// <summary>Commits; the runner always passes <see cref="CancellationToken.None"/>.</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>I/O side of GL posting supplied by FI infrastructure; it never decides outcomes or retries.</summary>
public interface IGlPostingTransactionPort
{
    /// <summary>Opens a ReadCommitted transaction for the request's company; null is a malformed adapter result.</summary>
    Task<IGlPostingAttemptSession?> BeginAsync(GlPostingRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the committed source key in a fresh transaction (after an unknown commit or duplicate key).</summary>
    Task<GlCommittedLookup?> LookupCommittedAsync(GlPostingSource source, CancellationToken cancellationToken);

    /// <summary>SQL error number of the failure (e.g. <c>GlPostingWriter.SqlErrorNumber</c>), null if not SQL.</summary>
    int? SqlErrorNumber(Exception exception);

    /// <summary>Whether the failure left the connection broken (commit result may be unknown).</summary>
    bool IsConnectionLost(Exception exception);
}

/// <summary>
/// WU-FI-004a3x1a: the single BR-FI-027/028 attempt order for <c>IGlPostingService</c>. Each attempt opens a
/// transaction, takes both locks, reads authorities, decides (004a3n), builds a fresh plan (004a3r), writes, commits
/// and maps the result (004a3s); failures are described with the step's phase and retried only through
/// <see cref="GlPostingRetryLoop"/>. Only this attempt's successful commit is Posted / <c>changed=true</c>.
/// </summary>
public static class GlPostingAttemptRunner
{
    private static readonly GlPostingOutcome InvalidDecision =
        new(GlPostingOutcomeKind.Fault, false, 500, GlPostingErrors.InvalidDecision);

    public static Task<GlPostingOutcome> PostAsync(GlPostingRequest? request, IGlPostingTransactionPort port,
        int maxAttempts = GlPostingFailureRules.DefaultMaxAttempts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);
        cancellationToken.ThrowIfCancellationRequested();
        if (Precheck(request) is { } rejected)
            return Task.FromResult(rejected);

        return GlPostingRetryLoop.RunAsync(request!.CompanyId, request.Source,
            (_, token) => AttemptAsync(request, port, token),
            token => port.LookupCommittedAsync(request.Source, token),
            maxAttempts, cancellationToken);
    }

    /// <summary>Rejects input that must never reach the lock (it would surface as a 500 lock argument failure).</summary>
    private static GlPostingOutcome? Precheck(GlPostingRequest? request)
    {
        if (request?.Source is not { } source || request.CompanyId <= 0)
            return Reject(400, GlPostingErrors.InvalidInput);
        if (source.CompanyId != request.CompanyId)
            return Reject(404, GlPostingErrors.SourceNotFound);
        return GlSourceReplayRules.Decide(source, null).Status == GlSourceReplayStatus.New
            ? null
            : Reject(400, GlPostingErrors.InvalidSource);
    }

    private static async Task<GlPostingAttemptResult?> AttemptAsync(GlPostingRequest request,
        IGlPostingTransactionPort port, CancellationToken cancellationToken)
    {
        IGlPostingAttemptSession? session = null;
        var phase = GlPostingFailurePhase.BeforeCommit;
        try
        {
            session = await port.BeginAsync(request, cancellationToken).ConfigureAwait(false);
            if (session is null)
                return GlPostingAttemptResult.Completed(InvalidDecision);

            await session.LockAsync(cancellationToken).ConfigureAwait(false);
            var authority = await session.ReadAuthorityAsync(cancellationToken).ConfigureAwait(false);
            if (authority is null)
                return GlPostingAttemptResult.Completed(InvalidDecision);

            var decision = GlPostingDecisionRules.Decide(request.CompanyId, request.Source, authority.Committed,
                request.BusinessDate, request.BaseCurrency, authority.CompanyBaseCurrency, authority.Periods,
                request.Draft, request.ExpectedMappingVersion, authority.CurrentMappingVersion,
                authority.CurrentAccounts);
            if (decision.Status != GlPostingDecisionStatus.Ready)
                return GlPostingAttemptResult.Completed(GlPostingOutcomeRules.FromDecision(decision, null));

            // A new plan every attempt: a written plan carries IDs and the writer refuses to reuse it.
            var plan = GlPostingWritePlanRules.Build(request.CompanyId, decision, request.Source,
                request.BusinessDate, authority.CompanyBaseCurrency, request.Draft);
            var gate = GlPostingOutcomeRules.FromDecision(decision, plan.Status);
            if (gate.Kind != GlPostingOutcomeKind.ProceedToWrite)
                return GlPostingAttemptResult.Completed(gate);

            var journalId = await session.WriteAsync(plan, cancellationToken).ConfigureAwait(false);
            if (journalId <= 0)
                return GlPostingAttemptResult.Completed(GlPostingOutcomeRules.Committed(journalId));

            // Never cancel a commit half-way: a cancelled commit would leave the result unknown to the caller.
            phase = GlPostingFailurePhase.Commit;
            await session.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return GlPostingAttemptResult.Completed(GlPostingOutcomeRules.Committed(journalId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return GlPostingAttemptResult.Failed(
                new GlPostingFailure(phase, port.SqlErrorNumber(ex), port.IsConnectionLost(ex)));
        }
        finally
        {
            if (session is not null)
                await DisposeQuietlyAsync(session).ConfigureAwait(false);
        }
    }

    private static async Task DisposeQuietlyAsync(IGlPostingAttemptSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The attempt result is already decided; a failed rollback/close cannot change it.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Uncommitted work on a broken connection is discarded by the server; a committed result stays committed.
        }
    }

    private static GlPostingOutcome Reject(int status, string code) =>
        new(GlPostingOutcomeKind.Rejected, false, status, code);
}
