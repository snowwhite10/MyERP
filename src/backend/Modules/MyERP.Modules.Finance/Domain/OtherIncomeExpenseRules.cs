using System.Globalization;
using System.Text;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// FI other-source kind. Only <see cref="OtherCollection"/> (income) and <see cref="OtherPayment"/> (expense) are
/// reportable; <see cref="Reversal"/> and <see cref="Compensation"/> are not yet specified and make an in-range Posted
/// read incomplete rather than guessed (BR-FI-132).
/// </summary>
public enum OtherIncomeExpenseKind
{
    OtherCollection,
    OtherPayment,
    Reversal,
    Compensation
}

/// <summary>Saved state of an other-source document. Only <see cref="Posted"/> enters the read (BR-FI-129).</summary>
public enum OtherIncomeExpenseSourceState
{
    Posted,
    Draft,
    Pending,
    Rejected,
    RolledBack,
    Deleted
}

/// <summary>
/// The bound read of BR-FI-130: one company, inclusive <see cref="From"/>..<see cref="Through"/> business dates (at
/// most <see cref="OtherIncomeExpenseRules.MaxRangeDays"/> days), positive committed <see cref="CutoffSequence"/>, base
/// currency with <see cref="BaseCurrencyDecimals"/> (0–4) and an optional kind filter (null = both kinds).
/// </summary>
public sealed record OtherIncomeExpenseScope(long CompanyId, DateOnly From, DateOnly Through, long CutoffSequence,
    string? BaseCurrency, int BaseCurrencyDecimals, OtherIncomeExpenseKind? KindFilter);

/// <summary>
/// One saved FI other-source document in the frozen set. <see cref="GrossBase"/> is the saved positive gross in
/// company base currency; category and mapped account are saved snapshots (null／blank only when missing);
/// <see cref="JournalId"/> is the immutable GL link and is null only when missing.
/// </summary>
public sealed record OtherIncomeExpenseSourceRecord(
    long SourceId,
    long CompanyId,
    OtherIncomeExpenseKind Kind,
    string? DocumentReference,
    long? CategoryId,
    string? CategoryLabel,
    string? AccountCode,
    string? AccountName,
    long? JournalId,
    DateOnly BusinessDate,
    DateTimeOffset PostingTime,
    OtherIncomeExpenseSourceState State,
    long CommitSequence,
    string? BaseCurrency,
    decimal GrossBase);

/// <summary>Why a frozen set cannot produce a complete read (HTTP 422, BR-FI-130／132).</summary>
public enum OtherIncomeExpenseIncompleteReason
{
    CurrencyMismatch,
    MissingCategorySnapshot,
    MissingAccountSnapshot,
    MissingJournal,
    UnsupportedSourceType,
    Overflow
}

/// <summary>One incomplete item; <see cref="SourceId"/> is null only for a full-set total overflow.</summary>
public sealed record OtherIncomeExpenseIncompleteItem(OtherIncomeExpenseIncompleteReason Reason, long? SourceId);

/// <summary>
/// One report line (BR-FI-129). <see cref="SignedBase"/> is <c>+GrossBase</c> for a collection and
/// <c>−GrossBase</c> for a payment.
/// </summary>
public sealed record OtherIncomeExpenseRow(
    DateOnly BusinessDate,
    DateTimeOffset PostingTime,
    OtherIncomeExpenseKind Kind,
    long SourceId,
    string DocumentReference,
    long CategoryId,
    string CategoryLabel,
    string AccountCode,
    string AccountName,
    long JournalId,
    string BaseCurrency,
    decimal GrossBase,
    decimal SignedBase);

/// <summary>
/// Full-set totals over every report row (not a page): positive <see cref="Income"/> and <see cref="Expense"/>,
/// <c>Net = Income − Expense</c> (equal to the sum of signed amounts) and row counts.
/// </summary>
public sealed record OtherIncomeExpenseTotals(decimal Income, decimal Expense, decimal Net, int RowCount,
    int IncomeCount, int ExpenseCount);

/// <summary>
/// The complete read; rows are ordered by (business date, posting time, kind, source ID). Exclusion counts make an
/// all-excluded read explicit; <see cref="ReplayedDuplicateCount"/> counts identical replays that added no line.
/// </summary>
public sealed record OtherIncomeExpenseReport(
    long CompanyId,
    DateOnly From,
    DateOnly Through,
    long CutoffSequence,
    string BaseCurrency,
    OtherIncomeExpenseKind? KindFilter,
    IReadOnlyList<OtherIncomeExpenseRow> Rows,
    OtherIncomeExpenseTotals Totals,
    int ExcludedNotPostedCount,
    int ExcludedOutOfRangeCount,
    int ReplayedDuplicateCount);

public enum OtherIncomeExpenseError
{
    Required,
    OutOfRange,
    InvalidScale,
    InvalidFormat,
    Duplicate,
    CrossCompany,
    AfterCutoff,
    FilterMismatch,
    /// <summary>Range over <see cref="OtherIncomeExpenseRules.MaxRangeDays"/> days; the API maps it to 422 (BR-FI-131).</summary>
    RangeTooLong,
    Inconsistent
}

/// <summary>
/// One input error (HTTP 400, except <see cref="OtherIncomeExpenseError.RangeTooLong"/> → 422). <see cref="Collection"/>
/// is <c>scope</c>, <c>request</c>, <c>sources</c>, <c>headers</c> or <c>report</c>; <see cref="Index"/> is the item
/// position for lists.
/// </summary>
public sealed record OtherIncomeExpenseFieldError(string Collection, int? Index, string Field,
    OtherIncomeExpenseError Error);

public enum OtherIncomeExpenseStatus
{
    Complete,
    Incomplete,
    Invalid
}

public sealed record OtherIncomeExpenseResult(OtherIncomeExpenseReport? Report,
    IReadOnlyList<OtherIncomeExpenseIncompleteItem> Incomplete, IReadOnlyList<OtherIncomeExpenseFieldError> Errors)
{
    public OtherIncomeExpenseStatus Status => Errors.Count > 0 ? OtherIncomeExpenseStatus.Invalid
        : Incomplete.Count > 0 ? OtherIncomeExpenseStatus.Incomplete
        : OtherIncomeExpenseStatus.Complete;
}

/// <summary>One page of the report rows; every page carries the same cutoff, range and full-set totals.</summary>
public sealed record OtherIncomeExpensePage(int PageNumber, int PageSize, int TotalRows, long CompanyId,
    long CutoffSequence, DateOnly From, DateOnly Through, IReadOnlyList<OtherIncomeExpenseRow> Rows,
    OtherIncomeExpenseTotals Totals, bool HasNext);

public sealed record OtherIncomeExpensePageResult(OtherIncomeExpensePage? Page,
    IReadOnlyList<OtherIncomeExpenseFieldError> Errors)
{
    public bool IsValid => Page is not null;
}

/// <summary>CSV text without BOM (the API adds UTF-8 BOM and streams it); null when rejected.</summary>
public sealed record OtherIncomeExpenseExportResult(string? Content, int RowCount,
    IReadOnlyList<OtherIncomeExpenseFieldError> Errors)
{
    public bool IsValid => Content is not null;
}

/// <summary>
/// Pure FI other income／expense read (REQ_F4116.19.3, BR-FI-129～132, ADR-092, WU-BI-001a1). From one frozen
/// committed set it keeps Posted OtherCollection／OtherPayment sources dated within the inclusive range, signs
/// collections positive and payments negative, and totals positive income, positive expense and net with checked
/// decimals. Missing category／account snapshot or journal, base currency mismatch, unspecified reversal／compensation
/// sources and overflow make the whole read incomplete with source IDs — never a partial report. An identical replayed
/// source adds no line. <see cref="Page"/> and <see cref="Export"/> slice and render the same rows and reject a report
/// whose totals do not match its rows. Queries and changes nothing: SQL reads, cutoff token, permissions, API and
/// screens stay with WU-BI-001a.
/// </summary>
public static class OtherIncomeExpenseRules
{
    public const int MaxCurrencyDecimals = 4;
    public const int MaxRangeDays = 366;
    public const int MaxPageSize = 200;
    public const int MaxExportRows = 10000;
    public const int ExportColumnCount = 13;
    public const int MaxLabelLength = 200;
    public const int MaxAccountCodeLength = 32;
    public const int MaxReferenceLength = 64;

    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static OtherIncomeExpenseResult Read(OtherIncomeExpenseScope? scope,
        IReadOnlyList<OtherIncomeExpenseSourceRecord?>? sources)
    {
        var errors = new List<OtherIncomeExpenseFieldError>();

        long? company = null;
        long? cutoff = null;
        int? scale = null;
        if (scope is null)
        {
            errors.Add(new("scope", null, "scope", OtherIncomeExpenseError.Required));
        }
        else
        {
            void Fail(string field, OtherIncomeExpenseError e) => errors.Add(new("scope", null, field, e));
            if (scope.CompanyId <= 0) Fail("companyId", OtherIncomeExpenseError.Required);
            else company = scope.CompanyId;
            if (scope.From == default) Fail("from", OtherIncomeExpenseError.Required);
            if (scope.Through == default) Fail("through", OtherIncomeExpenseError.Required);
            else if (scope.From != default)
            {
                var days = scope.Through.DayNumber - scope.From.DayNumber + 1;
                if (days < 1) Fail("through", OtherIncomeExpenseError.OutOfRange);
                else if (days > MaxRangeDays) Fail("through", OtherIncomeExpenseError.RangeTooLong);
            }

            if (scope.CutoffSequence <= 0) Fail("cutoffSequence", OtherIncomeExpenseError.Required);
            else cutoff = scope.CutoffSequence;
            if (!IsCurrencyCode(scope.BaseCurrency)) Fail("baseCurrency", OtherIncomeExpenseError.InvalidFormat);
            if (scope.BaseCurrencyDecimals is >= 0 and <= MaxCurrencyDecimals) scale = scope.BaseCurrencyDecimals;
            else Fail("baseCurrencyDecimals", OtherIncomeExpenseError.OutOfRange);
            if (scope.KindFilter is { } k && !IsReportable(k))
                Fail("kindFilter", OtherIncomeExpenseError.OutOfRange);
        }

        if (sources is null) errors.Add(new("request", null, "sources", OtherIncomeExpenseError.Required));

        static OtherIncomeExpenseError? LengthError(string? value, int max) =>
            value is not null && value.Length > max ? OtherIncomeExpenseError.OutOfRange : null;

        var byId = new Dictionary<long, OtherIncomeExpenseSourceRecord>();
        var unique = new List<OtherIncomeExpenseSourceRecord>();
        var replayed = 0;
        for (var i = 0; i < (sources?.Count ?? 0); i++)
        {
            var s = sources![i];
            void Fail(string field, OtherIncomeExpenseError e) => errors.Add(new("sources", i, field, e));
            if (s is null)
            {
                Fail("source", OtherIncomeExpenseError.Required);
                continue;
            }

            if (s.SourceId <= 0) Fail("sourceId", OtherIncomeExpenseError.Required);
            else if (byId.TryGetValue(s.SourceId, out var earlier))
            {
                // An identical replay adds no line (BR-FI-130); a different payload under one ID is a corrupt set.
                if (earlier == s) replayed++;
                else Fail("sourceId", OtherIncomeExpenseError.Duplicate);
                continue;
            }

            if (s.CompanyId <= 0) Fail("companyId", OtherIncomeExpenseError.Required);
            else if (company is { } c && s.CompanyId != c) Fail("companyId", OtherIncomeExpenseError.CrossCompany);
            if (!Enum.IsDefined(s.Kind)) Fail("kind", OtherIncomeExpenseError.OutOfRange);
            else if (scope?.KindFilter is { } filter && IsReportable(s.Kind) && s.Kind != filter)
                Fail("kind", OtherIncomeExpenseError.FilterMismatch);
            if (string.IsNullOrWhiteSpace(s.DocumentReference)) Fail("documentReference", OtherIncomeExpenseError.Required);
            else if (LengthError(s.DocumentReference, MaxReferenceLength) is { } re) Fail("documentReference", re);
            if (s.CategoryId is <= 0) Fail("categoryId", OtherIncomeExpenseError.Required);
            if (LengthError(s.CategoryLabel, MaxLabelLength) is { } ce) Fail("categoryLabel", ce);
            if (LengthError(s.AccountCode, MaxAccountCodeLength) is { } ae) Fail("accountCode", ae);
            if (LengthError(s.AccountName, MaxLabelLength) is { } ne) Fail("accountName", ne);
            if (s.JournalId is <= 0) Fail("journalId", OtherIncomeExpenseError.Required);
            if (s.BusinessDate == default) Fail("businessDate", OtherIncomeExpenseError.Required);
            if (s.PostingTime == default) Fail("postingTime", OtherIncomeExpenseError.Required);
            if (!Enum.IsDefined(s.State)) Fail("state", OtherIncomeExpenseError.OutOfRange);
            if (s.CommitSequence <= 0) Fail("commitSequence", OtherIncomeExpenseError.Required);
            else if (cutoff is { } cut && s.CommitSequence > cut) Fail("commitSequence", OtherIncomeExpenseError.AfterCutoff);
            if (!IsCurrencyCode(s.BaseCurrency)) Fail("baseCurrency", OtherIncomeExpenseError.InvalidFormat);
            if (s.GrossBase <= 0 || s.GrossBase > SqlAmountMaximum) Fail("grossBase", OtherIncomeExpenseError.OutOfRange);
            else if (scale is { } sc && decimal.Round(s.GrossBase, sc) != s.GrossBase)
                Fail("grossBase", OtherIncomeExpenseError.InvalidScale);

            if (s.SourceId > 0)
            {
                byId[s.SourceId] = s;
                unique.Add(s);
            }
        }

        if (errors.Count > 0) return new(null, [], errors);

        var b = scope!;
        var notPosted = unique.Count(x => x.State != OtherIncomeExpenseSourceState.Posted);
        var outOfRange = unique.Count(x => x.State == OtherIncomeExpenseSourceState.Posted
                                           && (x.BusinessDate < b.From || x.BusinessDate > b.Through));
        var included = unique.Where(x => x.State == OtherIncomeExpenseSourceState.Posted
                                         && x.BusinessDate >= b.From && x.BusinessDate <= b.Through).ToList();

        var incomplete = new List<OtherIncomeExpenseIncompleteItem>();
        foreach (var s in included)
        {
            void Incomplete(OtherIncomeExpenseIncompleteReason reason) => incomplete.Add(new(reason, s.SourceId));
            if (!IsReportable(s.Kind))
            {
                Incomplete(OtherIncomeExpenseIncompleteReason.UnsupportedSourceType);
                continue;
            }

            if (s.BaseCurrency != b.BaseCurrency) Incomplete(OtherIncomeExpenseIncompleteReason.CurrencyMismatch);
            if (s.CategoryId is null || string.IsNullOrWhiteSpace(s.CategoryLabel))
                Incomplete(OtherIncomeExpenseIncompleteReason.MissingCategorySnapshot);
            if (string.IsNullOrWhiteSpace(s.AccountCode) || string.IsNullOrWhiteSpace(s.AccountName))
                Incomplete(OtherIncomeExpenseIncompleteReason.MissingAccountSnapshot);
            if (s.JournalId is null) Incomplete(OtherIncomeExpenseIncompleteReason.MissingJournal);
        }

        var rows = included.Where(x => IsReportable(x.Kind))
            .OrderBy(x => x.BusinessDate).ThenBy(x => x.PostingTime).ThenBy(x => x.Kind).ThenBy(x => x.SourceId)
            .Select(x => new OtherIncomeExpenseRow(x.BusinessDate, x.PostingTime, x.Kind, x.SourceId,
                x.DocumentReference!, x.CategoryId ?? 0, x.CategoryLabel ?? "", x.AccountCode ?? "",
                x.AccountName ?? "", x.JournalId ?? 0, x.BaseCurrency!, x.GrossBase,
                x.Kind == OtherIncomeExpenseKind.OtherCollection ? x.GrossBase : -x.GrossBase))
            .ToList();

        var totals = Totalise(rows);
        if (new[] { totals.Income, totals.Expense, totals.Net }.Any(x => Math.Abs(x) > SqlAmountMaximum))
            incomplete.Add(new(OtherIncomeExpenseIncompleteReason.Overflow, null));

        if (incomplete.Count > 0)
            return new(null, incomplete.OrderBy(x => x.SourceId ?? long.MaxValue).ThenBy(x => x.Reason).ToList(), []);

        return new(new OtherIncomeExpenseReport(company!.Value, b.From, b.Through, cutoff!.Value, b.BaseCurrency!,
            b.KindFilter, rows, totals, notPosted, outOfRange, replayed), [], []);
    }

    /// <summary>Slices the report rows (1-based page); a page past the end is empty without a next page.</summary>
    public static OtherIncomeExpensePageResult Page(OtherIncomeExpenseReport? report, int pageNumber, int pageSize)
    {
        var errors = ReportErrors(report);
        if (pageNumber < 1) errors.Add(new("request", null, "pageNumber", OtherIncomeExpenseError.OutOfRange));
        if (pageSize is < 1 or > MaxPageSize)
            errors.Add(new("request", null, "pageSize", OtherIncomeExpenseError.OutOfRange));
        if (errors.Count > 0) return new(null, errors);

        var total = report!.Rows.Count;
        var start = (long)(pageNumber - 1) * pageSize;
        var rows = start >= total ? [] : report.Rows.Skip((int)start).Take(pageSize).ToList();
        return new(new OtherIncomeExpensePage(pageNumber, pageSize, total, report.CompanyId, report.CutoffSequence,
            report.From, report.Through, rows, report.Totals, start + pageSize < total), []);
    }

    /// <summary>
    /// Renders company, cutoff and range lines, the caller's translated header row, every report row in report order
    /// and the full-set income／expense／net trailer. Text cells are escaped with
    /// <see cref="FxDifferenceReconciliationRules.EscapeCsvText"/>; numbers and dates use invariant culture. More than
    /// <see cref="MaxExportRows"/> rows or inconsistent totals reject the whole export (never a partial file).
    /// </summary>
    public static OtherIncomeExpenseExportResult Export(OtherIncomeExpenseReport? report,
        IReadOnlyList<string?>? headers)
    {
        var errors = ReportErrors(report);
        if (headers is null) errors.Add(new("request", null, "headers", OtherIncomeExpenseError.Required));
        else if (headers.Count != ExportColumnCount)
            errors.Add(new("request", null, "headers", OtherIncomeExpenseError.OutOfRange));
        else
            for (var i = 0; i < headers.Count; i++)
                if (headers[i] is null) errors.Add(new("headers", i, "header", OtherIncomeExpenseError.Required));
        if (report is not null && report.Rows.Count > MaxExportRows)
            errors.Add(new("report", null, "rows", OtherIncomeExpenseError.OutOfRange));
        if (errors.Count > 0) return new(null, 0, errors);

        static string Text(string value) => FxDifferenceReconciliationRules.EscapeCsvText(value);
        var sb = new StringBuilder();
        sb.Append("CompanyId,").Append(Number(report!.CompanyId)).Append("\r\n");
        sb.Append("CutoffSequence,").Append(Number(report.CutoffSequence)).Append("\r\n");
        sb.Append("From,").Append(Date(report.From)).Append("\r\n");
        sb.Append("Through,").Append(Date(report.Through)).Append("\r\n");
        sb.AppendJoin(',', headers!.Select(h => Text(h!)));
        foreach (var r in report.Rows)
        {
            sb.Append("\r\n");
            sb.AppendJoin(',', new[]
            {
                Date(r.BusinessDate), r.PostingTime.ToString("O", CultureInfo.InvariantCulture), Text(r.Kind.ToString()),
                Number(r.SourceId), Text(r.DocumentReference), Number(r.CategoryId), Text(r.CategoryLabel),
                Text(r.AccountCode), Text(r.AccountName), Number(r.JournalId), Text(r.BaseCurrency),
                Number(r.GrossBase), Number(r.SignedBase),
            });
        }

        sb.Append("\r\nIncome,").Append(Number(report.Totals.Income));
        sb.Append("\r\nExpense,").Append(Number(report.Totals.Expense));
        sb.Append("\r\nNet,").Append(Number(report.Totals.Net));
        return new(sb.ToString(), report.Rows.Count, []);
    }

    /// <summary>A report handed back for paging／export must still have signed rows and totals that agree.</summary>
    private static List<OtherIncomeExpenseFieldError> ReportErrors(OtherIncomeExpenseReport? report)
    {
        var errors = new List<OtherIncomeExpenseFieldError>();
        if (report is null)
        {
            errors.Add(new("report", null, "report", OtherIncomeExpenseError.Required));
            return errors;
        }

        if (report.Rows.Any(r => !IsReportable(r.Kind)
                                 || r.SignedBase != (r.Kind == OtherIncomeExpenseKind.OtherCollection
                                     ? r.GrossBase
                                     : -r.GrossBase)))
            errors.Add(new("report", null, "rows", OtherIncomeExpenseError.Inconsistent));
        else if (Totalise(report.Rows) != report.Totals)
            errors.Add(new("report", null, "totals", OtherIncomeExpenseError.Inconsistent));
        return errors;
    }

    private static OtherIncomeExpenseTotals Totalise(IReadOnlyList<OtherIncomeExpenseRow> rows)
    {
        decimal income = 0m, expense = 0m;
        int incomeCount = 0, expenseCount = 0;
        foreach (var r in rows)
        {
            if (r.Kind == OtherIncomeExpenseKind.OtherCollection)
            {
                income = checked(income + r.GrossBase);
                incomeCount++;
            }
            else
            {
                expense = checked(expense + r.GrossBase);
                expenseCount++;
            }
        }

        return new(income, expense, checked(income - expense), rows.Count, incomeCount, expenseCount);
    }

    private static bool IsReportable(OtherIncomeExpenseKind kind) =>
        kind is OtherIncomeExpenseKind.OtherCollection or OtherIncomeExpenseKind.OtherPayment;

    private static bool IsCurrencyCode(string? code) =>
        code is { Length: 3 } && code.All(ch => ch is >= 'A' and <= 'Z');

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
