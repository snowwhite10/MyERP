using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

/// <summary>Stable GL posting error codes (also future front-end i18n keys, BR-FI-030).</summary>
public static class GlPostingErrors
{
    /// <summary>400: request/header shape is invalid (non-positive company or malformed input).</summary>
    public const string InvalidInput = "FI.GL_POSTING.INVALID_INPUT";
    /// <summary>400: source identity text, ID, version or fingerprint is not canonical.</summary>
    public const string InvalidSource = "FI.GL_POSTING.INVALID_SOURCE";
    /// <summary>404: source belongs to another company; hidden as not found.</summary>
    public const string SourceNotFound = "FI.GL_POSTING.SOURCE_NOT_FOUND";
    /// <summary>409: the permanent source key was posted with another version or fingerprint.</summary>
    public const string SourceConflict = "FI.GL_POSTING.SOURCE_CONFLICT";
    /// <summary>409: the FI account mapping version changed after the draft was built.</summary>
    public const string MappingChanged = "FI.GL_POSTING.MAPPING_CHANGED";
    /// <summary>409: an MD account snapshot changed after the draft was built.</summary>
    public const string AccountChanged = "FI.GL_POSTING.ACCOUNT_CHANGED";
    /// <summary>422: the company base currency is missing or not a canonical ISO code.</summary>
    public const string BaseCurrencyInvalid = "FI.GL_POSTING.BASE_CURRENCY_INVALID";
    /// <summary>422: the source currency is not the company base currency.</summary>
    public const string CurrencyMismatch = "FI.GL_POSTING.CURRENCY_MISMATCH";
    /// <summary>422: no company period covers the business date.</summary>
    public const string PeriodMissing = "FI.GL_POSTING.PERIOD_MISSING";
    /// <summary>422: the period covering the business date is Closed.</summary>
    public const string PeriodClosed = "FI.GL_POSTING.PERIOD_CLOSED";
    /// <summary>422: more than one period covers the business date.</summary>
    public const string PeriodAmbiguous = "FI.GL_POSTING.PERIOD_AMBIGUOUS";
    /// <summary>422: an account is not currently postable (with line number).</summary>
    public const string AccountInvalid = "FI.GL_POSTING.ACCOUNT_INVALID";
    /// <summary>422: line amounts or totals violate BR-FI-026.</summary>
    public const string Unbalanced = "FI.GL_POSTING.UNBALANCED";
    /// <summary>500: the internally assembled draft is malformed or self-contradictory.</summary>
    public const string InvalidDraft = "FI.GL_POSTING.INVALID_DRAFT";
    /// <summary>500: a saved source receipt is malformed.</summary>
    public const string InvalidSavedReceipt = "FI.GL_POSTING.INVALID_SAVED_RECEIPT";
    /// <summary>500: the committed lookup returned a different source identity.</summary>
    public const string SourceIdentityMismatch = "FI.GL_POSTING.SOURCE_IDENTITY_MISMATCH";
    /// <summary>500: the decision object is missing or internally inconsistent.</summary>
    public const string InvalidDecision = "FI.GL_POSTING.INVALID_DECISION";
    /// <summary>500: a Ready decision did not yield a writable plan.</summary>
    public const string InvalidWritePlan = "FI.GL_POSTING.INVALID_WRITE_PLAN";
    /// <summary>500: the writer reported a commit without a positive journal ID.</summary>
    public const string InvalidCommit = "FI.GL_POSTING.INVALID_COMMIT";
    /// <summary>503: after a timeout the committed lookup did not complete; the result is unknown, retry later.</summary>
    public const string ResultUnknown = "FI.GL_POSTING.RESULT_UNKNOWN";
    /// <summary>503: deadlock / lock-timeout retries used up the budget; nothing was posted, retry later.</summary>
    public const string RetryExhausted = "FI.GL_POSTING.RETRY_EXHAUSTED";
    /// <summary>500: the write failed before commit with a non-retryable error; the transaction was rolled back.</summary>
    public const string WriteFailed = "FI.GL_POSTING.WRITE_FAILED";
}

public enum GlPostingOutcomeKind { ProceedToWrite, Posted, Unchanged, Rejected, Fault }

/// <summary>
/// External result of one posting attempt. Only <see cref="GlPostingOutcomeKind.Posted"/> (after commit) is
/// <c>changed=true</c> and writes audit; <see cref="GlPostingOutcomeKind.ProceedToWrite"/> is not a result.
/// </summary>
public sealed record GlPostingOutcome(GlPostingOutcomeKind Kind, bool Changed, int? HttpStatus,
    string? ErrorCode = null, long? JournalId = null, int? InvalidLine = null)
{
    public bool WritesAudit => Kind == GlPostingOutcomeKind.Posted;
}

/// <summary>
/// Pure BR-FI-028/029/030 mapping of a posting decision and write-plan status to the changed flag,
/// HTTP status and error code shared by the FI HTTP API and source-module adapters. No I/O.
/// </summary>
public static class GlPostingOutcomeRules
{
    private static readonly GlPostingOutcome Proceed = new(GlPostingOutcomeKind.ProceedToWrite, false, null);

    public static GlPostingOutcome Committed(long journalId) => journalId > 0
        ? new(GlPostingOutcomeKind.Posted, true, 200, JournalId: journalId)
        : Fault(GlPostingErrors.InvalidCommit);

    public static GlPostingOutcome FromDecision(GlPostingDecision? decision, GlPostingWritePlanStatus? planStatus) =>
        decision?.Status switch
        {
            GlPostingDecisionStatus.Ready => planStatus switch
            {
                GlPostingWritePlanStatus.Ready when decision.PeriodId > 0 && decision.JournalId is null => Proceed,
                GlPostingWritePlanStatus.Ready => Fault(GlPostingErrors.InvalidDecision),
                GlPostingWritePlanStatus.InvalidDraft => Fault(GlPostingErrors.InvalidDraft),
                _ => Fault(GlPostingErrors.InvalidWritePlan),
            },
            GlPostingDecisionStatus.Unchanged => decision.JournalId > 0
                ? new(GlPostingOutcomeKind.Unchanged, false, 200, JournalId: decision.JournalId)
                : Fault(GlPostingErrors.InvalidSavedReceipt),
            GlPostingDecisionStatus.ReplayRejected => decision.ReplayStatus switch
            {
                GlSourceReplayStatus.Conflict => Reject(409, GlPostingErrors.SourceConflict),
                GlSourceReplayStatus.InvalidSource => Reject(400, GlPostingErrors.InvalidSource),
                GlSourceReplayStatus.IdentityMismatch => Fault(GlPostingErrors.SourceIdentityMismatch),
                GlSourceReplayStatus.InvalidSavedReceipt => Fault(GlPostingErrors.InvalidSavedReceipt),
                _ => Fault(GlPostingErrors.InvalidDecision),
            },
            GlPostingDecisionStatus.PostingRejected => FromPreflight(decision.Preflight),
            _ => Fault(GlPostingErrors.InvalidDecision),
        };

    private static GlPostingOutcome FromPreflight(GlPostingPreflightResult? preflight) => preflight?.Status switch
    {
        GlPostingPreflightStatus.HeaderRejected => preflight.HeaderStatus switch
        {
            GlJournalHeaderStatus.InvalidInput => Reject(400, GlPostingErrors.InvalidInput),
            GlJournalHeaderStatus.InvalidSource => Reject(400, GlPostingErrors.InvalidSource),
            GlJournalHeaderStatus.WrongCompany => Reject(404, GlPostingErrors.SourceNotFound),
            GlJournalHeaderStatus.InvalidCurrency => Reject(422, GlPostingErrors.BaseCurrencyInvalid),
            GlJournalHeaderStatus.CurrencyMismatch => Reject(422, GlPostingErrors.CurrencyMismatch),
            GlJournalHeaderStatus.MissingPeriod => Reject(422, GlPostingErrors.PeriodMissing),
            GlJournalHeaderStatus.ClosedPeriod => Reject(422, GlPostingErrors.PeriodClosed),
            GlJournalHeaderStatus.AmbiguousPeriod => Reject(422, GlPostingErrors.PeriodAmbiguous),
            GlJournalHeaderStatus.InvalidDraft => Fault(GlPostingErrors.InvalidDraft),
            _ => Fault(GlPostingErrors.InvalidDecision),
        },
        GlPostingPreflightStatus.DraftRejected => preflight.DraftStatus switch
        {
            GlJournalDraftRecheckStatus.AccountRejected => Reject(422, GlPostingErrors.AccountInvalid, preflight.InvalidLine),
            GlJournalDraftRecheckStatus.InvalidBalance => Reject(422, GlPostingErrors.Unbalanced, preflight.InvalidLine),
            GlJournalDraftRecheckStatus.MappingChanged => Reject(409, GlPostingErrors.MappingChanged, preflight.InvalidLine),
            GlJournalDraftRecheckStatus.SnapshotChanged => Reject(409, GlPostingErrors.AccountChanged, preflight.InvalidLine),
            GlJournalDraftRecheckStatus.SnapshotConflict or GlJournalDraftRecheckStatus.InvalidInput =>
                Fault(GlPostingErrors.InvalidDraft, preflight.InvalidLine),
            _ => Fault(GlPostingErrors.InvalidDecision),
        },
        _ => Fault(GlPostingErrors.InvalidDecision),
    };

    private static GlPostingOutcome Reject(int status, string code, int? line = null) =>
        new(GlPostingOutcomeKind.Rejected, false, status, code, InvalidLine: line);

    private static GlPostingOutcome Fault(string code, int? line = null) =>
        new(GlPostingOutcomeKind.Fault, false, 500, code, InvalidLine: line);
}
