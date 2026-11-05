using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Application;

public enum GlPostingPreflightStatus { Allowed, HeaderRejected, DraftRejected }

public sealed record GlPostingPreflightResult(GlPostingPreflightStatus Status, long? PeriodId = null,
    GlJournalHeaderStatus? HeaderStatus = null, GlJournalDraftRecheckStatus? DraftStatus = null,
    int? InvalidLine = null);

/// <summary>
/// Pure final gate for a caller-supplied posting snapshot. The SQL writer must fetch every
/// authority inside its transaction and hold the company period lock before calling this gate.
/// </summary>
public static class GlPostingPreflightRules
{
    public static GlPostingPreflightResult Validate(long companyId, GlPostingSource? source,
        DateOnly savedBusinessDate, string? baseCurrency, string? companyBaseCurrency,
        IReadOnlyList<GlPeriodWindow>? periods, GlJournalLineDraftResult? draft,
        int expectedMappingVersion, int currentMappingVersion, IReadOnlyList<AccountInfo?>? currentAccounts)
    {
        var header = GlJournalHeaderRules.Validate(companyId, source, savedBusinessDate,
            baseCurrency, companyBaseCurrency, periods, draft);
        if (header.Status != GlJournalHeaderStatus.Allowed || header.PeriodId is null)
            return new(GlPostingPreflightStatus.HeaderRejected, HeaderStatus: header.Status);

        var recheck = GlJournalDraftRecheckRules.Validate(companyId, expectedMappingVersion,
            draft, currentMappingVersion, currentAccounts);
        if (recheck.Status != GlJournalDraftRecheckStatus.Allowed)
            return new(GlPostingPreflightStatus.DraftRejected, DraftStatus: recheck.Status,
                InvalidLine: recheck.InvalidLine);

        return new(GlPostingPreflightStatus.Allowed, header.PeriodId);
    }
}
