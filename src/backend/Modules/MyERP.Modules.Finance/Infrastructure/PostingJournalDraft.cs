using MyERP.Modules.Finance.Application;
using MyERP.Modules.MasterData.Contracts;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// Shared by SD invoice/return and PUR purchase invoice posting (WU-FI-002a2b/002a3b/003a2b): checks each mapped
/// account through MD's public contract and builds the checked GL draft. On failure returns the 1-based journal leg
/// that is invalid.
/// </summary>
internal static class PostingJournalDraft
{
    public static async Task<(GlJournalLineDraftResult? Draft, int? InvalidLine)> BuildAsync(IChartOfAccounts chart,
        long companyId, int mappingVersion, IReadOnlyList<(string Code, decimal Debit, decimal Credit)> legs,
        CancellationToken cancellationToken)
    {
        var candidates = new List<GlJournalLineCandidate>(legs.Count);
        for (var index = 0; index < legs.Count; index++)
        {
            var (code, debit, credit) = legs[index];
            var account = string.IsNullOrEmpty(code) ? null : await chart.FindAsync(code, cancellationToken);
            var snapshot = GlAccountSnapshotRules.Validate(companyId, account?.Id ?? 0, code, mappingVersion,
                mappingVersion, account);
            if (snapshot.Status != GlAccountSnapshotStatus.Allowed)
                return (null, index + 1);
            candidates.Add(new(snapshot, debit, credit));
        }

        var draft = GlJournalLineDraftRules.Build(companyId, mappingVersion, candidates);
        return draft.Status == GlJournalLineDraftStatus.Allowed ? (draft, null) : (null, draft.InvalidLine);
    }
}
