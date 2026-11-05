namespace MyERP.Modules.Finance.Domain;

/// <summary>A base-currency leg supplied by an FI-owned source adapter.</summary>
public sealed record GlJournalBalanceLeg(long AccountId, decimal Debit, decimal Credit);

public enum GlJournalBalanceStatus
{
    Allowed,
    TooFewLines,
    InvalidAccount,
    InvalidSide,
    InvalidPrecision,
    Overflow,
    Unbalanced
}

public sealed record GlJournalBalanceResult(
    GlJournalBalanceStatus Status, decimal TotalDebit = 0, decimal TotalCredit = 0, int? InvalidLine = null);

/// <summary>Pure SQL decimal(19,4) and balance preflight; no source, account or period authority is implied.</summary>
public static class GlJournalBalanceRules
{
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static GlJournalBalanceResult Validate(IReadOnlyList<GlJournalBalanceLeg>? lines)
    {
        if (lines is null || lines.Count < 2)
            return new(GlJournalBalanceStatus.TooFewLines);

        decimal debitTotal = 0;
        decimal creditTotal = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;
            if (line.AccountId <= 0)
                return new(GlJournalBalanceStatus.InvalidAccount, InvalidLine: lineNumber);
            if (decimal.Round(line.Debit, 4) != line.Debit || decimal.Round(line.Credit, 4) != line.Credit)
                return new(GlJournalBalanceStatus.InvalidPrecision, InvalidLine: lineNumber);
            if (line.Debit > SqlAmountMaximum || line.Credit > SqlAmountMaximum)
                return new(GlJournalBalanceStatus.Overflow, InvalidLine: lineNumber);
            if ((line.Debit > 0) == (line.Credit > 0) || line.Debit < 0 || line.Credit < 0)
                return new(GlJournalBalanceStatus.InvalidSide, InvalidLine: lineNumber);
            try
            {
                debitTotal = checked(debitTotal + line.Debit);
                creditTotal = checked(creditTotal + line.Credit);
            }
            catch (OverflowException)
            {
                return new(GlJournalBalanceStatus.Overflow, InvalidLine: lineNumber);
            }
            if (debitTotal > SqlAmountMaximum || creditTotal > SqlAmountMaximum)
                return new(GlJournalBalanceStatus.Overflow, InvalidLine: lineNumber);
        }

        return debitTotal == creditTotal
            ? new(GlJournalBalanceStatus.Allowed, debitTotal, creditTotal)
            : new(GlJournalBalanceStatus.Unbalanced);
    }
}
