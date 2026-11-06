using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Result of one full posting attempt (lock → authority reads → decision → plan → write → commit). Exactly one of
/// <see cref="Outcome"/> (a final result, never ProceedToWrite) or <see cref="Failure"/> (the transaction was rolled
/// back or its commit result is unknown) is set.
/// </summary>
public sealed record GlPostingAttemptResult(GlPostingOutcome? Outcome, GlPostingFailure? Failure)
{
    public static GlPostingAttemptResult Completed(GlPostingOutcome outcome) => new(outcome, null);
    public static GlPostingAttemptResult Failed(GlPostingFailure failure) => new(null, failure);
}

/// <summary>Committed source-key lookup in a fresh transaction; <see cref="Completed"/> false means it did not finish.</summary>
public sealed record GlCommittedLookup(bool Completed, GlCommittedSource? Committed);

/// <summary>
/// BR-FI-028 retry loop shared by <c>IGlPostingService</c> (WU-FI-004a3x) and source adapters. It owns no I/O:
/// the attempt delegate runs one whole posting transaction and maps exceptions to <see cref="GlPostingFailure"/>;
/// the lookup delegate reads the committed source key. Every failure is classified by
/// <see cref="GlPostingFailureRules"/>, every unknown commit result by <see cref="GlPostingTimeoutRules"/>, and all
/// reruns share one attempt budget. Only an attempt's own outcome can be Posted / <c>changed=true</c>.
/// </summary>
public static class GlPostingRetryLoop
{
    private static readonly GlPostingOutcome InvalidDecision =
        new(GlPostingOutcomeKind.Fault, false, 500, GlPostingErrors.InvalidDecision);

    private static readonly GlPostingOutcome ResultUnknown =
        new(GlPostingOutcomeKind.Fault, false, 503, GlPostingErrors.ResultUnknown);

    /// <param name="companyId">Current company; passed to the timeout resolution.</param>
    /// <param name="candidate">The source being posted; compared with the committed key after an unknown result.</param>
    /// <param name="attempt">Runs attempt number n (1-based). Exceptions it throws propagate unchanged.</param>
    /// <param name="lookupCommitted">Reads the committed key; a non-cancellation exception counts as not completed.</param>
    /// <param name="maxAttempts">Shared budget 1..<see cref="GlPostingFailureRules.MaxAttemptsLimit"/>; otherwise 500.</param>
    /// <param name="cancellationToken">Checked before every attempt and lookup; cancellation propagates.</param>
    public static async Task<GlPostingOutcome> RunAsync(long companyId, GlPostingSource? candidate,
        Func<int, CancellationToken, Task<GlPostingAttemptResult?>> attempt,
        Func<CancellationToken, Task<GlCommittedLookup?>> lookupCommitted,
        int maxAttempts = GlPostingFailureRules.DefaultMaxAttempts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(lookupCommitted);
        if (maxAttempts is < 1 or > GlPostingFailureRules.MaxAttemptsLimit)
            return InvalidDecision;

        for (var attemptsMade = 1; ; attemptsMade++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await attempt(attemptsMade, cancellationToken).ConfigureAwait(false);

            if (result is null || (result.Outcome is null) == (result.Failure is null))
                return InvalidDecision;
            if (result.Outcome is { } outcome)
                return outcome.Kind == GlPostingOutcomeKind.ProceedToWrite ? InvalidDecision : outcome;

            var resolution = GlPostingFailureRules.Classify(result.Failure, attemptsMade, maxAttempts);
            if (resolution.Action == GlPostingFailureAction.ResolveFromCommitted)
            {
                var lookup = await LookupAsync(lookupCommitted, cancellationToken).ConfigureAwait(false);
                if (lookup is null)
                    return ResultUnknown;
                var timeout = GlPostingTimeoutRules.Resolve(companyId, candidate, lookup.Completed, lookup.Committed);
                if (timeout.Action == GlPostingTimeoutAction.Report)
                    return timeout.Outcome ?? InvalidDecision;
                resolution = GlPostingFailureRules.RetryOrExhausted(attemptsMade, maxAttempts);
            }

            if (resolution.Action != GlPostingFailureAction.RetryPosting)
                return resolution.Outcome ?? InvalidDecision;
        }
    }

    private static async Task<GlCommittedLookup?> LookupAsync(
        Func<CancellationToken, Task<GlCommittedLookup?>> lookupCommitted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await lookupCommitted(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The lookup itself failed: the commit result stays unknown (503), never success or a blind retry.
            return null;
        }
    }
}
