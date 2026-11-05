using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Application;

public enum GlPostingDecisionStatus { Ready, Unchanged, ReplayRejected, PostingRejected }

public sealed record GlPostingDecision(GlPostingDecisionStatus Status, long? PeriodId = null,
    long? JournalId = null, GlSourceReplayStatus? ReplayStatus = null,
    GlPostingPreflightResult? Preflight = null);

/// <summary>
/// Pure ordering of a committed source-key lookup and fresh posting preflight. The caller must
/// acquire the permanent source lock, verify the saved source and read authorities in its SQL transaction.
/// </summary>
public static class GlPostingDecisionRules
{
    public static GlPostingDecision Decide(long companyId, GlPostingSource? candidate,
        GlCommittedSource? committed, DateOnly savedBusinessDate, string? baseCurrency,
        string? companyBaseCurrency, IReadOnlyList<GlPeriodWindow>? periods,
        GlJournalLineDraftResult? draft, int expectedMappingVersion, int currentMappingVersion,
        IReadOnlyList<AccountInfo?>? currentAccounts)
    {
        if (companyId <= 0 || candidate?.CompanyId != companyId)
            return new(GlPostingDecisionStatus.PostingRejected,
                Preflight: new(GlPostingPreflightStatus.HeaderRejected,
                    HeaderStatus: companyId <= 0 ? GlJournalHeaderStatus.InvalidInput : GlJournalHeaderStatus.WrongCompany));

        var replay = GlSourceReplayRules.Decide(candidate, committed);
        if (replay.Status == GlSourceReplayStatus.Unchanged)
            return new(GlPostingDecisionStatus.Unchanged, JournalId: replay.JournalId);
        if (replay.Status != GlSourceReplayStatus.New)
            return new(GlPostingDecisionStatus.ReplayRejected, ReplayStatus: replay.Status);

        var preflight = GlPostingPreflightRules.Validate(companyId, candidate, savedBusinessDate,
            baseCurrency, companyBaseCurrency, periods, draft, expectedMappingVersion,
            currentMappingVersion, currentAccounts);
        return preflight.Status == GlPostingPreflightStatus.Allowed
            ? new(GlPostingDecisionStatus.Ready, PeriodId: preflight.PeriodId)
            : new(GlPostingDecisionStatus.PostingRejected, Preflight: preflight);
    }
}
