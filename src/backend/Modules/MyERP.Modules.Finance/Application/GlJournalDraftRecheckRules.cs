using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

public enum GlJournalDraftRecheckStatus
{
    Allowed,
    InvalidInput,
    MappingChanged,
    AccountRejected,
    SnapshotChanged,
    InvalidBalance
}

public sealed record GlJournalDraftRecheckResult(GlJournalDraftRecheckStatus Status,
    int? InvalidLine = null, GlAccountSnapshotStatus? AccountStatus = null,
    GlJournalBalanceStatus? BalanceStatus = null);

/// <summary>
/// Compares a complete draft with fresh MD contract results. The caller must read MD and
/// mapping authority inside the posting transaction and hold the company period lock.
/// </summary>
public static class GlJournalDraftRecheckRules
{
    public static GlJournalDraftRecheckResult Validate(long companyId, int expectedMappingVersion,
        GlJournalLineDraftResult? draft, int currentMappingVersion,
        IReadOnlyList<AccountInfo?>? currentAccounts)
    {
        if (companyId <= 0 || expectedMappingVersion <= 0 || draft?.Status != GlJournalLineDraftStatus.Allowed ||
            draft.Lines is null || draft.Lines.Count < 2)
            return new(GlJournalDraftRecheckStatus.InvalidInput);

        if (currentMappingVersion <= 0 || currentMappingVersion != expectedMappingVersion)
            return new(GlJournalDraftRecheckStatus.MappingChanged);

        if (currentAccounts is null || currentAccounts.Count != draft.Lines.Count)
            return new(GlJournalDraftRecheckStatus.InvalidInput);

        for (var index = 0; index < draft.Lines.Count; index++)
        {
            var line = draft.Lines[index];
            if (line is null || line.Sequence != index + 1 || line.MappingVersion != expectedMappingVersion ||
                line.AccountId <= 0 || line.AccountVersion <= 0 || string.IsNullOrEmpty(line.AccountCode))
                return new(GlJournalDraftRecheckStatus.InvalidInput, index + 1);

            var result = GlAccountSnapshotRules.Validate(companyId, line.AccountId, line.AccountCode,
                expectedMappingVersion, currentMappingVersion, currentAccounts[index]);
            if (result.Status != GlAccountSnapshotStatus.Allowed)
                return new(GlJournalDraftRecheckStatus.AccountRejected, index + 1, result.Status);

            if (result.Snapshot!.Name != line.AccountName ||
                result.Snapshot.AccountVersion != line.AccountVersion)
                return new(GlJournalDraftRecheckStatus.SnapshotChanged, index + 1);
        }

        var balance = GlJournalBalanceRules.Validate(draft.Lines.Select(
            line => new GlJournalBalanceLeg(line.AccountId, line.Debit, line.Credit)).ToArray());
        if (balance.Status != GlJournalBalanceStatus.Allowed)
            return new(GlJournalDraftRecheckStatus.InvalidBalance, balance.InvalidLine,
                BalanceStatus: balance.Status);
        if (balance.TotalDebit != draft.TotalDebit || balance.TotalCredit != draft.TotalCredit)
            return new(GlJournalDraftRecheckStatus.InvalidBalance);

        return new(GlJournalDraftRecheckStatus.Allowed);
    }
}
