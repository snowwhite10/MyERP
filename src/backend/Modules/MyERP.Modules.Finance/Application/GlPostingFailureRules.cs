namespace MyERP.Modules.Finance.Application;

/// <summary>Whether the failure happened before COMMIT was sent or while committing (result unknown).</summary>
public enum GlPostingFailurePhase { BeforeCommit, Commit }

/// <summary>
/// One failed posting attempt as seen by the writer: the SQL error number (null for a non-SQL exception) and
/// whether the connection was found broken. The writer has already rolled back (or lost) the transaction.
/// </summary>
public sealed record GlPostingFailure(GlPostingFailurePhase Phase, int? SqlErrorNumber, bool ConnectionLost = false);

public enum GlPostingFailureAction { RetryPosting, ResolveFromCommitted, Report }

/// <summary>
/// <see cref="GlPostingFailureAction.RetryPosting"/>: rerun the whole posting path (permanent-key lock, fresh
/// decision and preflight). <see cref="GlPostingFailureAction.ResolveFromCommitted"/>: read the committed source
/// key in a fresh transaction and pass it to <see cref="GlPostingTimeoutRules.Resolve"/>; if that returns
/// RetryPosting, ask <see cref="GlPostingFailureRules.RetryOrExhausted"/> first. Only Report carries an outcome.
/// </summary>
public sealed record GlPostingFailureResolution(GlPostingFailureAction Action, GlPostingOutcome? Outcome);

/// <summary>
/// Pure BR-FI-028 classification of SQL failures during GL posting and the shared retry budget. No I/O and no
/// SqlClient dependency: the writer passes the error number. Never reports success or <c>changed=true</c>.
/// </summary>
public static class GlPostingFailureRules
{
    public const int DefaultMaxAttempts = 3;
    public const int MaxAttemptsLimit = 10;

    private const int Deadlock = 1205;
    private const int LockTimeout = 1222;

    // Command timeout (-2) and transport / connection failures: the server may or may not have committed.
    private static readonly HashSet<int> TransportErrors = [-2, -1, 2, 53, 64, 121, 233, 10053, 10054, 10060];

    private static readonly GlPostingFailureResolution Retry = new(GlPostingFailureAction.RetryPosting, null);
    private static readonly GlPostingFailureResolution Resolve = new(GlPostingFailureAction.ResolveFromCommitted, null);

    /// <summary>
    /// Classifies one failed attempt. <c>attemptsMade</c> counts posting attempts already made, including the
    /// failed one (≥ 1); <c>maxAttempts</c> is 1..<see cref="MaxAttemptsLimit"/>.
    /// </summary>
    public static GlPostingFailureResolution Classify(GlPostingFailure? failure, int attemptsMade,
        int maxAttempts = DefaultMaxAttempts)
    {
        if (!ValidBudget(attemptsMade, maxAttempts) || failure is null || !Enum.IsDefined(failure.Phase))
            return Fault(GlPostingErrors.InvalidDecision);

        // Unknown commit result: never retry blindly or report failure; the committed lookup decides (no budget
        // check here because the work may already be committed).
        if (failure.Phase == GlPostingFailurePhase.Commit || failure.ConnectionLost
            || failure.SqlErrorNumber is { } n && TransportErrors.Contains(n))
            return Resolve;

        return failure.SqlErrorNumber switch
        {
            // Another writer committed the same permanent key (or a sibling key): replay from committed state.
            2601 or 2627 => Resolve,
            // Whole transaction rolled back by the writer; safe to rerun from the start within the budget.
            Deadlock or LockTimeout => RetryOrExhausted(attemptsMade, maxAttempts),
            _ => Fault(GlPostingErrors.WriteFailed),
        };
    }

    /// <summary>Shared budget for every rerun, including a RetryPosting returned by the timeout resolution.</summary>
    public static GlPostingFailureResolution RetryOrExhausted(int attemptsMade, int maxAttempts = DefaultMaxAttempts)
    {
        if (!ValidBudget(attemptsMade, maxAttempts))
            return Fault(GlPostingErrors.InvalidDecision);
        return attemptsMade < maxAttempts
            ? Retry
            : new(GlPostingFailureAction.Report,
                new(GlPostingOutcomeKind.Fault, false, 503, GlPostingErrors.RetryExhausted));
    }

    private static bool ValidBudget(int attemptsMade, int maxAttempts) =>
        attemptsMade >= 1 && maxAttempts is >= 1 and <= MaxAttemptsLimit;

    private static GlPostingFailureResolution Fault(string code) =>
        new(GlPostingFailureAction.Report, new(GlPostingOutcomeKind.Fault, false, 500, code));
}
