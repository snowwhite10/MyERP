using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

/// <summary>One FI-derived leg with an account checked through MD's public contract.</summary>
public sealed record GlJournalLineCandidate(GlAccountSnapshotResult Account, decimal Debit, decimal Credit);

/// <summary>Scalar account snapshot and amount ready for the later transactional writer.</summary>
public sealed record GlJournalLineDraft(int Sequence, long AccountId, string AccountCode, string AccountName,
    int AccountVersion, int MappingVersion, decimal Debit, decimal Credit);

public enum GlJournalLineDraftStatus
{
    Allowed,
    InvalidInput,
    InvalidAccountSnapshot,
    WrongCompany,
    MappingChanged,
    SnapshotConflict,
    InvalidBalance
}

public sealed record GlJournalLineDraftResult(GlJournalLineDraftStatus Status,
    IReadOnlyList<GlJournalLineDraft> Lines, decimal TotalDebit = 0, decimal TotalCredit = 0,
    int? InvalidLine = null, GlJournalBalanceStatus? BalanceStatus = null);

/// <summary>
/// Pure whole-journal preflight. A failure returns no lines. The posting transaction must
/// reload and recheck MD and FI mapping authority before saving these scalar snapshots.
/// </summary>
public static class GlJournalLineDraftRules
{
    public static GlJournalLineDraftResult Build(long companyId, int mappingVersion,
        IReadOnlyList<GlJournalLineCandidate>? candidates)
    {
        if (companyId <= 0 || mappingVersion <= 0 || candidates is null)
            return Failure(GlJournalLineDraftStatus.InvalidInput);

        var lines = new List<GlJournalLineDraft>(candidates.Count);
        var balanceLegs = new List<GlJournalBalanceLeg>(candidates.Count);
        var accountsById = new Dictionary<long, GlAccountSnapshot>();
        var accountIdsByCode = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var snapshot = candidate.Account.Snapshot;
            if (candidate.Account.Status != GlAccountSnapshotStatus.Allowed || snapshot is null ||
                snapshot.AccountId <= 0 || snapshot.AccountVersion <= 0 ||
                snapshot.Code.Length is < 1 or > 30 || !snapshot.Code.All(static c => c is >= '0' and <= '9') ||
                string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Name.Length > 200)
                return Failure(GlJournalLineDraftStatus.InvalidAccountSnapshot, index + 1);
            if (snapshot.CompanyId != companyId)
                return Failure(GlJournalLineDraftStatus.WrongCompany, index + 1);
            if (snapshot.MappingVersion != mappingVersion)
                return Failure(GlJournalLineDraftStatus.MappingChanged, index + 1);
            if ((accountsById.TryGetValue(snapshot.AccountId, out var earlier) && earlier != snapshot) ||
                (accountIdsByCode.TryGetValue(snapshot.Code, out var earlierId) && earlierId != snapshot.AccountId))
                return Failure(GlJournalLineDraftStatus.SnapshotConflict, index + 1);

            accountsById[snapshot.AccountId] = snapshot;
            accountIdsByCode[snapshot.Code] = snapshot.AccountId;

            lines.Add(new(index + 1, snapshot.AccountId, snapshot.Code, snapshot.Name,
                snapshot.AccountVersion, snapshot.MappingVersion, candidate.Debit, candidate.Credit));
            balanceLegs.Add(new(snapshot.AccountId, candidate.Debit, candidate.Credit));
        }

        var balance = GlJournalBalanceRules.Validate(balanceLegs);
        if (balance.Status != GlJournalBalanceStatus.Allowed)
            return new(GlJournalLineDraftStatus.InvalidBalance, Array.Empty<GlJournalLineDraft>(),
                InvalidLine: balance.InvalidLine, BalanceStatus: balance.Status);

        return new(GlJournalLineDraftStatus.Allowed, lines.AsReadOnly(),
            balance.TotalDebit, balance.TotalCredit);
    }

    private static GlJournalLineDraftResult Failure(GlJournalLineDraftStatus status, int? invalidLine = null) =>
        new(status, Array.Empty<GlJournalLineDraft>(), InvalidLine: invalidLine);
}
