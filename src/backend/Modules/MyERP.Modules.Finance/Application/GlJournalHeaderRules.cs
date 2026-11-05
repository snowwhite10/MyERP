using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

public enum GlJournalHeaderStatus
{
    Allowed,
    InvalidInput,
    InvalidSource,
    WrongCompany,
    InvalidCurrency,
    CurrencyMismatch,
    InvalidDraft,
    MissingPeriod,
    ClosedPeriod,
    AmbiguousPeriod
}

public sealed record GlJournalHeaderResult(GlJournalHeaderStatus Status, long? PeriodId = null);

/// <summary>
/// Pure header preflight over caller-supplied saved source, company currency, whole draft and
/// company periods. The posting transaction must reload these authorities under its company lock.
/// </summary>
public static class GlJournalHeaderRules
{
    public static GlJournalHeaderResult Validate(long companyId, GlPostingSource? source,
        DateOnly savedBusinessDate, string? baseCurrency, string? companyBaseCurrency,
        IReadOnlyList<GlPeriodWindow>? periods, GlJournalLineDraftResult? draft)
    {
        if (companyId <= 0 || periods is null)
            return new(GlJournalHeaderStatus.InvalidInput);
        if (source is null || GlSourceReplayRules.Decide(source, null).Status != GlSourceReplayStatus.New)
            return new(GlJournalHeaderStatus.InvalidSource);
        if (source.CompanyId != companyId)
            return new(GlJournalHeaderStatus.WrongCompany);
        if (!IsCurrency(baseCurrency) || !IsCurrency(companyBaseCurrency))
            return new(GlJournalHeaderStatus.InvalidCurrency);
        if (!string.Equals(baseCurrency, companyBaseCurrency, StringComparison.Ordinal))
            return new(GlJournalHeaderStatus.CurrencyMismatch);
        if (draft?.Status != GlJournalLineDraftStatus.Allowed || draft.Lines is null || draft.Lines.Count < 2 ||
            draft.Lines.Any(static line => line is null))
            return new(GlJournalHeaderStatus.InvalidDraft);

        var balance = GlJournalBalanceRules.Validate(draft.Lines.Select(
            line => new GlJournalBalanceLeg(line.AccountId, line.Debit, line.Credit)).ToArray());
        if (balance.Status != GlJournalBalanceStatus.Allowed ||
            balance.TotalDebit != draft.TotalDebit || balance.TotalCredit != draft.TotalCredit)
            return new(GlJournalHeaderStatus.InvalidDraft);

        var period = GlPeriodDateRules.Resolve(companyId, savedBusinessDate, periods);
        return period.Status switch
        {
            GlPeriodResolutionStatus.Found => new(GlJournalHeaderStatus.Allowed, period.PeriodId),
            GlPeriodResolutionStatus.Closed => new(GlJournalHeaderStatus.ClosedPeriod),
            GlPeriodResolutionStatus.Ambiguous => new(GlJournalHeaderStatus.AmbiguousPeriod),
            _ => new(GlJournalHeaderStatus.MissingPeriod)
        };
    }

    private static bool IsCurrency(string? currency) => currency?.Length == 3 &&
        currency.All(static value => value is >= 'A' and <= 'Z');
}
