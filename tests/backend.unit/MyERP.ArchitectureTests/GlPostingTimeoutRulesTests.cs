using System.Text.RegularExpressions;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

public sealed class GlPostingTimeoutRulesTests
{
    private static readonly Regex ErrorCodePattern = new(@"^[A-Z][A-Z0-9_]*(\.[A-Z0-9_]+){2,}$");
    private static readonly GlPostingSource Candidate = new(1, "PUR", "GRN", 900, 3, "FP-ABC");

    private static GlCommittedSource Saved(GlPostingSource source, long journalId = 44) => new(source, journalId);

    private static void AssertReported(GlPostingTimeoutResolution resolution, int httpStatus, string errorCode)
    {
        Assert.Equal(GlPostingTimeoutAction.Report, resolution.Action);
        var outcome = Assert.IsType<GlPostingOutcome>(resolution.Outcome);
        Assert.Equal(httpStatus >= 500 ? GlPostingOutcomeKind.Fault : GlPostingOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(httpStatus, outcome.HttpStatus);
        Assert.Equal(errorCode, outcome.ErrorCode);
        Assert.False(outcome.Changed);
        Assert.False(outcome.WritesAudit);
        Assert.Null(outcome.JournalId);
        Assert.Matches(ErrorCodePattern, outcome.ErrorCode!);
    }

    [Fact]
    public void NoCommittedSourceAllowsRetryThroughTheFullPostingPathWithoutAResult()
    {
        var resolution = GlPostingTimeoutRules.Resolve(1, Candidate, lookupCompleted: true, committed: null);

        Assert.Equal(GlPostingTimeoutAction.RetryPosting, resolution.Action);
        Assert.Null(resolution.Outcome);
    }

    [Fact]
    public void SameCommittedSourceReportsOriginalJournalWithoutClaimingChangeOrAuditing()
    {
        var resolution = GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate with { }, 44));

        Assert.Equal(GlPostingTimeoutAction.Report, resolution.Action);
        var outcome = Assert.IsType<GlPostingOutcome>(resolution.Outcome);
        Assert.Equal((GlPostingOutcomeKind.Unchanged, false, false, 200, (string?)null, 44L),
            (outcome.Kind, outcome.Changed, outcome.WritesAudit, outcome.HttpStatus, outcome.ErrorCode, outcome.JournalId));
    }

    [Fact]
    public void DifferentCommittedVersionOrFingerprintIsConflict()
    {
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate with { SourceVersion = 4 })),
            409, GlPostingErrors.SourceConflict);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate with { Fingerprint = "FP-XYZ" })),
            409, GlPostingErrors.SourceConflict);
    }

    [Fact]
    public void InvalidSavedReceiptOrIdentityMismatchIsInternalFaultNeverSuccess()
    {
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate, 0)),
            500, GlPostingErrors.InvalidSavedReceipt);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate with { Fingerprint = " FP" })),
            500, GlPostingErrors.InvalidSavedReceipt);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate with { SourceId = 901 })),
            500, GlPostingErrors.SourceIdentityMismatch);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, true, Saved(Candidate with { CompanyId = 2 })),
            500, GlPostingErrors.SourceIdentityMismatch);
    }

    [Fact]
    public void IncompleteLookupIsUnknownResultEvenIfARowWasSupplied()
    {
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, lookupCompleted: false, committed: null),
            503, GlPostingErrors.ResultUnknown);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate, false, Saved(Candidate)),
            503, GlPostingErrors.ResultUnknown);
    }

    [Fact]
    public void InvalidRequestIsRejectedBeforeTheLookupIsTrusted()
    {
        AssertReported(GlPostingTimeoutRules.Resolve(0, Candidate, true, Saved(Candidate)), 400, GlPostingErrors.InvalidInput);
        AssertReported(GlPostingTimeoutRules.Resolve(-1, Candidate, false, null), 400, GlPostingErrors.InvalidInput);
        AssertReported(GlPostingTimeoutRules.Resolve(2, Candidate, true, Saved(Candidate)), 404, GlPostingErrors.SourceNotFound);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate with { Module = "pur " }, true, null),
            400, GlPostingErrors.InvalidSource);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate with { SourceVersion = 0 }, false, null),
            400, GlPostingErrors.InvalidSource);
        AssertReported(GlPostingTimeoutRules.Resolve(1, Candidate with { SourceVersion = (long)int.MaxValue + 1 }, true,
            Saved(Candidate with { SourceVersion = (long)int.MaxValue + 1 })), 400, GlPostingErrors.InvalidSource);
        AssertReported(GlPostingTimeoutRules.Resolve(2, Candidate, false, null), 404, GlPostingErrors.SourceNotFound);
    }

    [Fact]
    public void MissingCandidateIsInternalFault()
    {
        AssertReported(GlPostingTimeoutRules.Resolve(1, null, true, null), 500, GlPostingErrors.InvalidDecision);
        AssertReported(GlPostingTimeoutRules.Resolve(1, null, true, Saved(Candidate)), 500, GlPostingErrors.InvalidDecision);
    }

    [Fact]
    public void OnlyAnEmptyCompletedLookupPermitsRetry()
    {
        var lookups = new (bool Completed, GlCommittedSource? Saved)[]
        {
            (true, null), (false, null), (true, Saved(Candidate)), (true, Saved(Candidate, -1)),
            (true, Saved(Candidate with { SourceVersion = 4 })), (false, Saved(Candidate)),
        };
        var retries = lookups.Where(l => GlPostingTimeoutRules.Resolve(1, Candidate, l.Completed, l.Saved).Action
            == GlPostingTimeoutAction.RetryPosting).ToList();

        Assert.Equal([(true, (GlCommittedSource?)null)], retries);
        Assert.All(lookups.Except(retries), l =>
            Assert.NotNull(GlPostingTimeoutRules.Resolve(1, Candidate, l.Completed, l.Saved).Outcome));
    }

    [Fact]
    public void RecoveryNeverReportsPostedOrChanged()
    {
        foreach (var completed in new[] { true, false })
        foreach (var saved in new GlCommittedSource?[] { null, Saved(Candidate), Saved(Candidate, 0),
                     Saved(Candidate with { Fingerprint = "FP-XYZ" }) })
        {
            var outcome = GlPostingTimeoutRules.Resolve(1, Candidate, completed, saved).Outcome;
            Assert.NotEqual(GlPostingOutcomeKind.Posted, outcome?.Kind);
            Assert.False(outcome?.Changed ?? false);
            Assert.False(outcome?.WritesAudit ?? false);
        }
    }
}
