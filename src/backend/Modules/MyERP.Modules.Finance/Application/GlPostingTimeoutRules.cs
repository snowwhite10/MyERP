using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

public enum GlPostingTimeoutAction { Report, RetryPosting }

/// <summary>
/// <see cref="GlPostingTimeoutAction.RetryPosting"/> carries no outcome: the caller must rerun the whole posting
/// path (permanent-key lock, fresh decision and preflight). <see cref="GlPostingTimeoutAction.Report"/> always has one.
/// </summary>
public sealed record GlPostingTimeoutResolution(GlPostingTimeoutAction Action, GlPostingOutcome? Outcome);

/// <summary>
/// Pure BR-FI-028 resolution of a posting attempt whose commit result is unknown (timeout / lost connection).
/// The caller reads the committed source key in a fresh transaction; this rule never calls an in-flight or
/// unproven result a success, never reports <c>changed=true</c> and never asks for a second audit row.
/// </summary>
public static class GlPostingTimeoutRules
{
    private static readonly GlPostingTimeoutResolution Retry = new(GlPostingTimeoutAction.RetryPosting, null);

    public static GlPostingTimeoutResolution Resolve(long companyId, GlPostingSource? candidate,
        bool lookupCompleted, GlCommittedSource? committed)
    {
        if (companyId <= 0)
            return Report(Outcome(new(GlPostingDecisionStatus.PostingRejected,
                Preflight: new(GlPostingPreflightStatus.HeaderRejected, HeaderStatus: GlJournalHeaderStatus.InvalidInput))));
        if (candidate is null)
            return Report(Outcome(null));
        if (candidate.CompanyId != companyId)
            return Report(Outcome(new(GlPostingDecisionStatus.PostingRejected,
                Preflight: new(GlPostingPreflightStatus.HeaderRejected, HeaderStatus: GlJournalHeaderStatus.WrongCompany))));

        // Validate the candidate before trusting the lookup: an invalid source is the caller's error either way.
        var replay = GlSourceReplayRules.Decide(candidate, lookupCompleted ? committed : null);
        if (replay.Status == GlSourceReplayStatus.InvalidSource)
            return Report(Outcome(new(GlPostingDecisionStatus.ReplayRejected, ReplayStatus: replay.Status)));
        if (!lookupCompleted)
            return Report(new(GlPostingOutcomeKind.Fault, false, 503, GlPostingErrors.ResultUnknown));

        return replay.Status switch
        {
            // No committed key: the attempt rolled back or is still in flight. Only a full retry under the
            // permanent-key lock is safe; it waits for any in-flight writer and then replays or posts.
            GlSourceReplayStatus.New => Retry,
            GlSourceReplayStatus.Unchanged =>
                Report(Outcome(new(GlPostingDecisionStatus.Unchanged, JournalId: replay.JournalId))),
            _ => Report(Outcome(new(GlPostingDecisionStatus.ReplayRejected, ReplayStatus: replay.Status))),
        };
    }

    private static GlPostingOutcome Outcome(GlPostingDecision? decision) =>
        GlPostingOutcomeRules.FromDecision(decision, null);

    private static GlPostingTimeoutResolution Report(GlPostingOutcome outcome) =>
        new(GlPostingTimeoutAction.Report, outcome);
}
