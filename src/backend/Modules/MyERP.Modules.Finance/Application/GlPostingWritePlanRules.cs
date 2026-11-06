using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

public enum GlPostingWritePlanStatus { Ready, NotReady, InvalidInput, InvalidDraft }

/// <summary>
/// Unsaved rows for one atomic GL write. Identity, foreign keys (<c>SourceKeyId</c>/<c>JournalId</c>),
/// audit and version fields are left for the SQL writer and interceptor. A plan is not a Posted result.
/// </summary>
public sealed record GlPostingWritePlan(GlPostingWritePlanStatus Status, GlSourceKey? SourceKey,
    GlJournal? Journal, IReadOnlyList<GlJournalLine> Lines);

/// <summary>
/// Pure mapping of a <see cref="GlPostingDecisionStatus.Ready"/> decision and its rechecked draft into
/// the exact source-key, journal and line values to insert (BR-FI-027/029). Any failure returns no rows.
/// The caller must still hold the permanent source lock and period lock in the same SQL transaction.
/// </summary>
public static class GlPostingWritePlanRules
{
    private static readonly GlPostingWritePlan EmptyNotReady = Empty(GlPostingWritePlanStatus.NotReady);
    private static readonly GlPostingWritePlan EmptyInvalidInput = Empty(GlPostingWritePlanStatus.InvalidInput);
    private static readonly GlPostingWritePlan EmptyInvalidDraft = Empty(GlPostingWritePlanStatus.InvalidDraft);

    public static GlPostingWritePlan Build(long companyId, GlPostingDecision? decision, GlPostingSource? source,
        DateOnly businessDate, string? baseCurrency, GlJournalLineDraftResult? draft)
    {
        if (decision is not { Status: GlPostingDecisionStatus.Ready, PeriodId: > 0, JournalId: null })
            return EmptyNotReady;
        if (companyId <= 0 || source is null || source.CompanyId != companyId ||
            GlSourceReplayRules.Decide(source, null).Status != GlSourceReplayStatus.New ||
            baseCurrency is not { Length: 3 } || !baseCurrency.All(static c => c is >= 'A' and <= 'Z'))
            return EmptyInvalidInput;
        if (!IsWritableDraft(draft))
            return EmptyInvalidDraft;

        var key = new GlSourceKey
        {
            CompanyId = companyId,
            SourceModule = source.Module,
            SourceType = source.SourceType,
            SourceId = source.SourceId,
            SourceVersion = checked((int)source.SourceVersion),
            Fingerprint = source.Fingerprint,
        };
        var journal = new GlJournal
        {
            CompanyId = companyId,
            PeriodId = decision.PeriodId.Value,
            BusinessDate = businessDate,
            BaseCurrency = baseCurrency,
            Status = GlJournalStatus.Posted,
            TotalDebit = draft!.TotalDebit,
            TotalCredit = draft.TotalCredit,
        };
        var lines = draft.Lines.Select(line => new GlJournalLine
        {
            CompanyId = companyId,
            Sequence = line.Sequence,
            AccountId = line.AccountId,
            AccountCode = line.AccountCode,
            AccountName = line.AccountName,
            AccountVersion = line.AccountVersion,
            MappingVersion = line.MappingVersion,
            Debit = line.Debit,
            Credit = line.Credit,
        }).ToList();
        return new(GlPostingWritePlanStatus.Ready, key, journal, lines.AsReadOnly());
    }

    private static bool IsWritableDraft(GlJournalLineDraftResult? draft)
    {
        if (draft is not { Status: GlJournalLineDraftStatus.Allowed, Lines: { Count: >= 2 } lines })
            return false;
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index] is not { } line || line.Sequence != index + 1)
                return false;
        }

        var balance = GlJournalBalanceRules.Validate(
            lines.Select(static line => new GlJournalBalanceLeg(line.AccountId, line.Debit, line.Credit)).ToList());
        return balance.Status == GlJournalBalanceStatus.Allowed &&
            balance.TotalDebit == draft.TotalDebit && balance.TotalCredit == draft.TotalCredit;
    }

    private static GlPostingWritePlan Empty(GlPostingWritePlanStatus status) =>
        new(status, null, null, Array.Empty<GlJournalLine>());
}
