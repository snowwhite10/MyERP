using System.Globalization;
using System.Text;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// One company's exchange-difference read at a durable committed cutoff (BR-FI-123). <see cref="CutoffSequence"/> is
/// the committed sequence every source and GL leg of the set was read at; amounts are in <see cref="BaseCurrency"/>
/// with <see cref="BaseCurrencyDecimals"/> (0–4).
/// </summary>
public sealed record FxDifferenceReconciliationScope(long CompanyId, long CutoffSequence, string? BaseCurrency,
    int BaseCurrencyDecimals);

/// <summary>Settlement allocation (realised difference) or period-end valuation (unrealised difference).</summary>
public enum FxDifferenceSourceKind
{
    Settlement,
    Valuation
}

public enum FxDifferenceCategory
{
    Realised,
    Unrealised
}

/// <summary>
/// Saved state of a source row. Only <see cref="Posted"/> counts; the others are history kept for review and are
/// labelled, never counted as Posted (BR-FI-123).
/// </summary>
public enum FxDifferenceSourceState
{
    Posted,
    Draft,
    Rejected,
    RolledBack,
    Deleted
}

/// <summary>
/// One saved FX source in the frozen set. <see cref="SignedDifference"/> is the saved realised difference (settlement)
/// or unrealised difference (valuation), positive is gain (BR-FI-116／118). <see cref="OverlayReleased"/> is the
/// settlement's released prior overlay in carrying-amount sign (ADR-085, zero for a valuation). <see cref="Rate"/> is
/// the saved settlement or closing rate (<c>1 document = rate base</c>). A Posted settlement and a Posted non-zero
/// valuation have a journal; a zero valuation and every non-Posted source have none.
/// </summary>
public sealed record FxDifferenceSourceRecord(
    long SourceId,
    long CompanyId,
    FxDifferenceSourceKind Kind,
    long ExposureId,
    FxExposureSide Side,
    FxDifferenceSourceState State,
    long CommitSequence,
    string? Reference,
    string? DocumentCurrency,
    decimal Rate,
    decimal OriginalAmount,
    decimal SignedDifference,
    decimal OverlayReleased,
    long? JournalId);

/// <summary>
/// One Posted GL line in the same cutoff: every line tagged with an FX source plus every line on a mapped FX gain/loss
/// account (<see cref="SourceId"/> null for a manual journal). Exactly one of debit/credit is positive.
/// </summary>
public sealed record FxDifferenceGlLeg(
    long GlLineId,
    long JournalId,
    long CompanyId,
    long? SourceId,
    FxJournalLineRole Role,
    long AccountId,
    long CommitSequence,
    decimal Debit,
    decimal Credit);

public enum FxDifferenceKind
{
    Missing,
    JournalMismatch,
    UnexpectedRole,
    Unbalanced,
    AmountDifference,
    OverlayDifference,
    UnexpectedLeg,
    LegOnNonPostedSource,
    Unmatched
}

/// <summary>
/// One reconciliation difference. Amounts are profit-and-loss signed (credit − debit; gain positive) except
/// <see cref="FxDifferenceKind.Unbalanced"/>, where <see cref="Expected"/> is the debit and <see cref="Actual"/> the
/// credit total. <see cref="Difference"/> = actual − expected. IDs are ascending.
/// </summary>
public sealed record FxDifferenceReconciliationDifference(
    FxDifferenceKind Kind,
    long? SourceId,
    long? ExposureId,
    IReadOnlyList<long> GlLineIds,
    IReadOnlyList<long> JournalIds,
    decimal Expected,
    decimal Actual,
    decimal Difference);

/// <summary>
/// One report row per source, ordered by exposure then source ID. <see cref="GlDifference"/> sums the source's
/// realised (settlement) or unrealised (valuation) gain/loss legs as credit − debit;
/// <see cref="ExpectedOverlayReversal"/> is the P&amp;L reversal implied by the released overlay
/// (<c>−signed(side, overlay)</c>) and <see cref="GlOverlayReversal"/> the overlay reversal legs as credit − debit.
/// </summary>
public sealed record FxDifferenceRow(
    long SourceId,
    FxDifferenceSourceKind Kind,
    FxDifferenceCategory Category,
    FxDifferenceSourceState State,
    bool CountsAsPosted,
    long ExposureId,
    FxExposureSide Side,
    string Reference,
    string DocumentCurrency,
    string BaseCurrency,
    decimal Rate,
    decimal OriginalAmount,
    decimal SignedDifference,
    decimal GlDifference,
    decimal ExpectedOverlayReversal,
    decimal GlOverlayReversal,
    long? JournalId,
    IReadOnlyList<long> GlLineIds,
    IReadOnlyList<FxDifferenceKind> Differences);

/// <summary>
/// The frozen read. Posted totals count only Posted sources; GL totals sum every leg of the set by role (including
/// unmatched and non-Posted-tagged legs) and are informational, so offsetting differences never net to a pass.
/// </summary>
public sealed record FxDifferenceReconciliationReport(
    long CompanyId,
    long CutoffSequence,
    IReadOnlyList<FxDifferenceRow> Rows,
    IReadOnlyList<FxDifferenceReconciliationDifference> Differences,
    decimal PostedRealisedNet,
    decimal PostedUnrealisedNet,
    decimal PostedOverlayReversalNet,
    decimal GlRealisedNet,
    decimal GlUnrealisedNet,
    decimal GlOverlayReversalNet,
    int PostedCount,
    int DraftCount,
    int RejectedCount,
    int RolledBackCount,
    int DeletedCount)
{
    public int TotalCount => Rows.Count;
    public bool BlocksSignOff => Differences.Count > 0;
}

public enum FxDifferenceError
{
    Required,
    OutOfRange,
    InvalidScale,
    InvalidFormat,
    Duplicate,
    CrossCompany,
    AfterCutoff,
    SameCurrency,
    NotApplicable,
    NotSingleSided
}

/// <summary>
/// One input error. <see cref="Collection"/> is <c>scope</c>, <c>request</c>, <c>sources</c>, <c>glLegs</c>,
/// <c>totals</c>, <c>report</c> or <c>headers</c>; <see cref="Index"/> is the item position for lists.
/// </summary>
public sealed record FxDifferenceFieldError(string Collection, int? Index, string Field, FxDifferenceError Error);

public sealed record FxDifferenceReconciliationResult(FxDifferenceReconciliationReport? Report,
    IReadOnlyList<FxDifferenceFieldError> Errors)
{
    public bool IsValid => Report is not null;
}

/// <summary>One page of the report rows; every page carries the same cutoff and total.</summary>
public sealed record FxDifferencePage(int PageNumber, int PageSize, int TotalRows, long CompanyId,
    long CutoffSequence, IReadOnlyList<FxDifferenceRow> Rows, bool HasNext);

public sealed record FxDifferencePageResult(FxDifferencePage? Page, IReadOnlyList<FxDifferenceFieldError> Errors)
{
    public bool IsValid => Page is not null;
}

/// <summary>CSV text without BOM (the API adds UTF-8 BOM and streams it); null when rejected.</summary>
public sealed record FxDifferenceExportResult(string? Content, int RowCount, IReadOnlyList<FxDifferenceFieldError> Errors)
{
    public bool IsValid => Content is not null;
}

/// <summary>
/// Pure FX difference read／export reconciliation (BR-FI-123, ADR-090, WU-FI-014j). Reconciles each saved settlement
/// allocation and valuation with the Posted GL legs tagged to it at one durable cutoff: missing journal, legs in
/// another journal, roles foreign to the source kind, unbalanced legs, gain/loss and overlay reversal amounts, legs on
/// zero or non-Posted sources and untagged/unknown legs. Draft, rejected, rolled-back and deleted history is labelled
/// and never counted as Posted. <see cref="Page"/> and <see cref="Export"/> slice and render the same report rows; text
/// cells escape formula-leading values. Queries and changes nothing: SQL reads, manifest token, permissions, API and
/// screens stay with 014b／014c.
/// </summary>
public static class FxDifferenceReconciliationRules
{
    public const int MaxCurrencyDecimals = 4;
    public const int MaxRateDecimals = 8;
    public const int MaxReferenceLength = 64;
    public const int MaxPageSize = 200;
    public const int MaxExportRows = 10000;
    public const int ExportColumnCount = 19;

    private const decimal SqlAmountMaximum = 999999999999999.9999m;
    private const decimal SqlRateMaximum = 99999999999.99999999m;

    private static readonly FxJournalLineRole[] SettlementRoles =
    [
        FxJournalLineRole.Control, FxJournalLineRole.Clearing, FxJournalLineRole.RealisedGain,
        FxJournalLineRole.RealisedLoss, FxJournalLineRole.OverlayReversalControl,
        FxJournalLineRole.OverlayReversalGain, FxJournalLineRole.OverlayReversalLoss
    ];

    private static readonly FxJournalLineRole[] ValuationRoles =
    [
        FxJournalLineRole.Control, FxJournalLineRole.UnrealisedGain, FxJournalLineRole.UnrealisedLoss
    ];

    public static FxDifferenceReconciliationResult Reconcile(
        FxDifferenceReconciliationScope? scope,
        IReadOnlyList<FxDifferenceSourceRecord?>? sources,
        IReadOnlyList<FxDifferenceGlLeg?>? glLegs)
    {
        var errors = new List<FxDifferenceFieldError>();

        long? company = null;
        long? cutoff = null;
        string? baseCurrency = null;
        int? scale = null;
        if (scope is null)
        {
            errors.Add(new("scope", null, "scope", FxDifferenceError.Required));
        }
        else
        {
            void Fail(string field, FxDifferenceError e) => errors.Add(new("scope", null, field, e));
            if (scope.CompanyId <= 0) Fail("companyId", FxDifferenceError.Required);
            else company = scope.CompanyId;
            if (scope.CutoffSequence <= 0) Fail("cutoffSequence", FxDifferenceError.Required);
            else cutoff = scope.CutoffSequence;
            if (IsCurrencyCode(scope.BaseCurrency)) baseCurrency = scope.BaseCurrency;
            else Fail("baseCurrency", FxDifferenceError.InvalidFormat);
            if (scope.BaseCurrencyDecimals is >= 0 and <= MaxCurrencyDecimals) scale = scope.BaseCurrencyDecimals;
            else Fail("baseCurrencyDecimals", FxDifferenceError.OutOfRange);
        }

        if (sources is null) errors.Add(new("request", null, "sources", FxDifferenceError.Required));
        if (glLegs is null) errors.Add(new("request", null, "glLegs", FxDifferenceError.Required));

        FxDifferenceError? AmountError(decimal value, int? decimals) =>
            Math.Abs(value) > SqlAmountMaximum ? FxDifferenceError.OutOfRange
            : decimals is { } s && decimal.Round(value, s) != value ? FxDifferenceError.InvalidScale
            : null;

        FxDifferenceError? SequenceError(long sequence) =>
            sequence <= 0 ? FxDifferenceError.Required
            : cutoff is { } c && sequence > c ? FxDifferenceError.AfterCutoff
            : null;

        var seenSources = new HashSet<long>();
        for (var i = 0; i < (sources?.Count ?? 0); i++)
        {
            var s = sources![i];
            void Fail(string field, FxDifferenceError e) => errors.Add(new("sources", i, field, e));
            if (s is null)
            {
                Fail("source", FxDifferenceError.Required);
                continue;
            }

            if (s.SourceId <= 0) Fail("sourceId", FxDifferenceError.Required);
            else if (!seenSources.Add(s.SourceId)) Fail("sourceId", FxDifferenceError.Duplicate);

            if (s.CompanyId <= 0) Fail("companyId", FxDifferenceError.Required);
            else if (company is { } c && s.CompanyId != c) Fail("companyId", FxDifferenceError.CrossCompany);

            var kindValid = Enum.IsDefined(s.Kind);
            if (!kindValid) Fail("kind", FxDifferenceError.OutOfRange);
            if (s.ExposureId <= 0) Fail("exposureId", FxDifferenceError.Required);
            if (!Enum.IsDefined(s.Side)) Fail("side", FxDifferenceError.OutOfRange);
            var stateValid = Enum.IsDefined(s.State);
            if (!stateValid) Fail("state", FxDifferenceError.OutOfRange);
            if (SequenceError(s.CommitSequence) is { } qe) Fail("commitSequence", qe);

            if (string.IsNullOrWhiteSpace(s.Reference)) Fail("reference", FxDifferenceError.Required);
            else if (s.Reference.Length > MaxReferenceLength) Fail("reference", FxDifferenceError.OutOfRange);

            if (!IsCurrencyCode(s.DocumentCurrency)) Fail("documentCurrency", FxDifferenceError.InvalidFormat);
            else if (baseCurrency is not null && s.DocumentCurrency == baseCurrency)
                Fail("documentCurrency", FxDifferenceError.SameCurrency);

            if (s.Rate <= 0 || s.Rate > SqlRateMaximum) Fail("rate", FxDifferenceError.OutOfRange);
            else if (decimal.Round(s.Rate, MaxRateDecimals) != s.Rate) Fail("rate", FxDifferenceError.InvalidScale);

            if (s.OriginalAmount <= 0) Fail("originalAmount", FxDifferenceError.OutOfRange);
            else if (AmountError(s.OriginalAmount, MaxCurrencyDecimals) is { } oe) Fail("originalAmount", oe);

            if (AmountError(s.SignedDifference, scale) is { } de) Fail("signedDifference", de);

            if (kindValid && s.Kind == FxDifferenceSourceKind.Valuation && s.OverlayReleased != 0)
                Fail("overlayReleased", FxDifferenceError.NotApplicable);
            else if (AmountError(s.OverlayReleased, scale) is { } ve) Fail("overlayReleased", ve);

            if (s.JournalId is <= 0) Fail("journalId", FxDifferenceError.Required);
            else if (kindValid && stateValid)
            {
                var expectsJournal = ExpectsJournal(s);
                if (expectsJournal && s.JournalId is null) Fail("journalId", FxDifferenceError.Required);
                else if (!expectsJournal && s.JournalId is not null) Fail("journalId", FxDifferenceError.NotApplicable);
            }
        }

        var seenLines = new HashSet<long>();
        for (var i = 0; i < (glLegs?.Count ?? 0); i++)
        {
            var g = glLegs![i];
            void Fail(string field, FxDifferenceError e) => errors.Add(new("glLegs", i, field, e));
            if (g is null)
            {
                Fail("glLeg", FxDifferenceError.Required);
                continue;
            }

            if (g.GlLineId <= 0) Fail("glLineId", FxDifferenceError.Required);
            else if (!seenLines.Add(g.GlLineId)) Fail("glLineId", FxDifferenceError.Duplicate);
            if (g.JournalId <= 0) Fail("journalId", FxDifferenceError.Required);

            if (g.CompanyId <= 0) Fail("companyId", FxDifferenceError.Required);
            else if (company is { } c && g.CompanyId != c) Fail("companyId", FxDifferenceError.CrossCompany);

            if (g.SourceId is <= 0) Fail("sourceId", FxDifferenceError.OutOfRange);
            if (!Enum.IsDefined(g.Role)) Fail("role", FxDifferenceError.OutOfRange);
            if (g.AccountId <= 0) Fail("accountId", FxDifferenceError.Required);
            if (SequenceError(g.CommitSequence) is { } qe) Fail("commitSequence", qe);

            FxDifferenceError? SideError(decimal v) => v < 0 ? FxDifferenceError.OutOfRange : AmountError(v, scale);
            var debitError = SideError(g.Debit);
            var creditError = SideError(g.Credit);
            if (debitError is { } dbe) Fail("debit", dbe);
            if (creditError is { } cre) Fail("credit", cre);
            if (debitError is null && creditError is null && (g.Debit > 0) == (g.Credit > 0))
                Fail("credit", FxDifferenceError.NotSingleSided);
        }

        if (errors.Count > 0) return new(null, errors);

        var allSources = sources!.Select(x => x!).ToList();
        var allLegs = glLegs!.Select(x => x!).ToList();
        var posted = allSources.Where(x => x.State == FxDifferenceSourceState.Posted).ToList();

        var totals = new (string Field, decimal Value)[]
        {
            ("postedRealisedNet", posted.Where(IsSettlement).Sum(x => x.SignedDifference)),
            ("postedUnrealisedNet", posted.Where(x => !IsSettlement(x)).Sum(x => x.SignedDifference)),
            ("postedOverlayReversalNet", posted.Sum(ExpectedReversal)),
            ("glRealisedNet", ProfitEffect(allLegs.Where(IsRealised))),
            ("glUnrealisedNet", ProfitEffect(allLegs.Where(IsUnrealised))),
            ("glOverlayReversalNet", ProfitEffect(allLegs.Where(IsOverlayReversal))),
        };
        foreach (var (field, value) in totals)
            if (Math.Abs(value) > SqlAmountMaximum) errors.Add(new("totals", null, field, FxDifferenceError.OutOfRange));
        if (errors.Count > 0) return new(null, errors);

        var sourceIds = allSources.Select(x => x.SourceId).ToHashSet();
        var legsBySource = allLegs
            .Where(g => g.SourceId is { } id && sourceIds.Contains(id))
            .GroupBy(g => g.SourceId!.Value)
            .ToDictionary(grp => grp.Key, grp => grp.OrderBy(g => g.GlLineId).ToList());

        var rows = new List<FxDifferenceRow>(allSources.Count);
        var differences = new List<FxDifferenceReconciliationDifference>();
        foreach (var s in allSources.OrderBy(x => x.ExposureId).ThenBy(x => x.SourceId))
        {
            var legs = legsBySource.GetValueOrDefault(s.SourceId) ?? [];
            var found = Compare(s, legs);
            differences.AddRange(found);
            var glDifference = ProfitEffect(legs.Where(IsSettlement(s) ? IsRealised : IsUnrealised));
            rows.Add(new(s.SourceId, s.Kind,
                IsSettlement(s) ? FxDifferenceCategory.Realised : FxDifferenceCategory.Unrealised,
                s.State, s.State == FxDifferenceSourceState.Posted, s.ExposureId, s.Side, s.Reference!,
                s.DocumentCurrency!, baseCurrency!, s.Rate, s.OriginalAmount, s.SignedDifference, glDifference,
                ExpectedReversal(s), ProfitEffect(legs.Where(IsOverlayReversal)), s.JournalId,
                legs.Select(g => g.GlLineId).ToList(), found.Select(d => d.Kind).ToList()));
        }

        foreach (var g in allLegs.Where(g => g.SourceId is not { } id || !sourceIds.Contains(id))
                     .OrderBy(g => g.GlLineId))
        {
            var amount = g.Credit - g.Debit;
            differences.Add(new(FxDifferenceKind.Unmatched, g.SourceId, null, [g.GlLineId], [g.JournalId],
                0m, amount, amount));
        }

        int Count(FxDifferenceSourceState state) => allSources.Count(x => x.State == state);
        var report = new FxDifferenceReconciliationReport(company!.Value, cutoff!.Value, rows, differences,
            totals[0].Value, totals[1].Value, totals[2].Value, totals[3].Value, totals[4].Value, totals[5].Value,
            Count(FxDifferenceSourceState.Posted), Count(FxDifferenceSourceState.Draft),
            Count(FxDifferenceSourceState.Rejected), Count(FxDifferenceSourceState.RolledBack),
            Count(FxDifferenceSourceState.Deleted));
        return new(report, []);
    }

    /// <summary>Slices the report rows (1-based page); a page past the end is empty without a next page.</summary>
    public static FxDifferencePageResult Page(FxDifferenceReconciliationReport? report, int pageNumber, int pageSize)
    {
        var errors = new List<FxDifferenceFieldError>();
        if (report is null) errors.Add(new("report", null, "report", FxDifferenceError.Required));
        if (pageNumber < 1) errors.Add(new("request", null, "pageNumber", FxDifferenceError.OutOfRange));
        if (pageSize is < 1 or > MaxPageSize) errors.Add(new("request", null, "pageSize", FxDifferenceError.OutOfRange));
        if (errors.Count > 0) return new(null, errors);

        var total = report!.Rows.Count;
        var start = (long)(pageNumber - 1) * pageSize;
        var rows = start >= total ? [] : report.Rows.Skip((int)start).Take(pageSize).ToList();
        return new(new FxDifferencePage(pageNumber, pageSize, total, report.CompanyId, report.CutoffSequence, rows,
            start + pageSize < total), []);
    }

    /// <summary>
    /// Renders every report row in report order after a company and cutoff line and the caller's translated header
    /// row. Text cells (headers, reference, codes, enum names) are escaped; numbers use invariant culture. More than
    /// <see cref="MaxExportRows"/> rows rejects the whole export (never a truncated file).
    /// </summary>
    public static FxDifferenceExportResult Export(FxDifferenceReconciliationReport? report,
        IReadOnlyList<string?>? headers)
    {
        var errors = new List<FxDifferenceFieldError>();
        if (report is null) errors.Add(new("report", null, "report", FxDifferenceError.Required));
        if (headers is null) errors.Add(new("request", null, "headers", FxDifferenceError.Required));
        else if (headers.Count != ExportColumnCount)
            errors.Add(new("request", null, "headers", FxDifferenceError.OutOfRange));
        else
            for (var i = 0; i < headers.Count; i++)
                if (headers[i] is null) errors.Add(new("headers", i, "header", FxDifferenceError.Required));
        if (report is not null && report.Rows.Count > MaxExportRows)
            errors.Add(new("report", null, "rows", FxDifferenceError.OutOfRange));
        if (errors.Count > 0) return new(null, 0, errors);

        var sb = new StringBuilder();
        sb.Append("CompanyId,").Append(Number(report!.CompanyId)).Append("\r\n");
        sb.Append("CutoffSequence,").Append(Number(report.CutoffSequence)).Append("\r\n");
        sb.AppendJoin(',', headers!.Select(h => EscapeCsvText(h!)));
        foreach (var r in report.Rows)
        {
            sb.Append("\r\n");
            sb.AppendJoin(',', new[]
            {
                Number(r.SourceId), EscapeCsvText(r.Kind.ToString()), EscapeCsvText(r.Category.ToString()),
                EscapeCsvText(r.State.ToString()), r.CountsAsPosted ? "true" : "false", Number(r.ExposureId),
                EscapeCsvText(r.Side.ToString()), EscapeCsvText(r.Reference), EscapeCsvText(r.DocumentCurrency),
                EscapeCsvText(r.BaseCurrency), Number(r.Rate), Number(r.OriginalAmount), Number(r.SignedDifference),
                Number(r.GlDifference), Number(r.ExpectedOverlayReversal), Number(r.GlOverlayReversal),
                r.JournalId is { } j ? Number(j) : "",
                EscapeCsvText(string.Join(';', r.GlLineIds.Select(x => Number(x)))),
                EscapeCsvText(string.Join(';', r.Differences)),
            });
        }

        return new(sb.ToString(), report.Rows.Count, []);
    }

    /// <summary>
    /// Escapes one CSV text cell: a value whose first non-blank character is <c>= + - @</c>, or which starts with tab
    /// or carriage return, is prefixed with an apostrophe so spreadsheets do not evaluate it; cells containing comma,
    /// quote, tab or line breaks are quoted with doubled quotes.
    /// </summary>
    public static string EscapeCsvText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return value;
        var first = value.AsSpan().TrimStart();
        if (value[0] is '\t' or '\r' || (!first.IsEmpty && "=+-@".Contains(first[0]))) value = "'" + value;
        return value.AsSpan().IndexOfAny(",\"\t\r\n") >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static List<FxDifferenceReconciliationDifference> Compare(FxDifferenceSourceRecord s,
        List<FxDifferenceGlLeg> legs)
    {
        var found = new List<FxDifferenceReconciliationDifference>();
        var lineIds = legs.Select(g => g.GlLineId).ToList();
        FxDifferenceReconciliationDifference Diff(FxDifferenceKind kind, decimal expected, decimal actual,
            IEnumerable<FxDifferenceGlLeg>? subset = null, IEnumerable<long>? journals = null) =>
            new(kind, s.SourceId, s.ExposureId, subset?.Select(g => g.GlLineId).ToList() ?? lineIds,
                (journals ?? legs.Select(g => g.JournalId)).Distinct().Order().ToList(),
                expected, actual, actual - expected);

        if (s.State != FxDifferenceSourceState.Posted || s.JournalId is null)
        {
            if (legs.Count > 0)
            {
                var kind = s.State == FxDifferenceSourceState.Posted
                    ? FxDifferenceKind.UnexpectedLeg
                    : FxDifferenceKind.LegOnNonPostedSource;
                found.Add(Diff(kind, 0m, ProfitEffect(legs)));
            }

            return found;
        }

        var settlement = IsSettlement(s);
        if (legs.Count == 0)
        {
            found.Add(Diff(FxDifferenceKind.Missing, s.SignedDifference, 0m, journals: [s.JournalId.Value]));
            return found;
        }

        var foreignJournal = legs.Where(g => g.JournalId != s.JournalId).ToList();
        if (foreignJournal.Count > 0)
            found.Add(Diff(FxDifferenceKind.JournalMismatch, 0m, 0m, foreignJournal,
                legs.Select(g => g.JournalId).Append(s.JournalId.Value)));

        var allowed = settlement ? SettlementRoles : ValuationRoles;
        var foreignRole = legs.Where(g => !allowed.Contains(g.Role)).ToList();
        if (foreignRole.Count > 0) found.Add(Diff(FxDifferenceKind.UnexpectedRole, 0m, 0m, foreignRole));

        var debit = legs.Sum(g => g.Debit);
        var credit = legs.Sum(g => g.Credit);
        if (debit != credit) found.Add(Diff(FxDifferenceKind.Unbalanced, debit, credit));

        var actual = ProfitEffect(legs.Where(settlement ? IsRealised : IsUnrealised));
        if (actual != s.SignedDifference)
            found.Add(Diff(FxDifferenceKind.AmountDifference, s.SignedDifference, actual));

        var expectedReversal = ExpectedReversal(s);
        var actualReversal = ProfitEffect(legs.Where(IsOverlayReversal));
        if (actualReversal != expectedReversal)
            found.Add(Diff(FxDifferenceKind.OverlayDifference, expectedReversal, actualReversal));

        return found;
    }

    private static bool ExpectsJournal(FxDifferenceSourceRecord s) =>
        s.State == FxDifferenceSourceState.Posted && (IsSettlement(s) || s.SignedDifference != 0);

    private static bool IsSettlement(FxDifferenceSourceRecord s) => s.Kind == FxDifferenceSourceKind.Settlement;

    // Released carrying overlay reverses the original P&L: AR carrying sign is the P&L sign, AP the opposite (ADR-087).
    private static decimal ExpectedReversal(FxDifferenceSourceRecord s) =>
        s.Side == FxExposureSide.Receivable ? -s.OverlayReleased : s.OverlayReleased;

    private static decimal ProfitEffect(IEnumerable<FxDifferenceGlLeg> legs) => legs.Sum(g => g.Credit - g.Debit);

    private static bool IsRealised(FxDifferenceGlLeg g) =>
        g.Role is FxJournalLineRole.RealisedGain or FxJournalLineRole.RealisedLoss;

    private static bool IsUnrealised(FxDifferenceGlLeg g) =>
        g.Role is FxJournalLineRole.UnrealisedGain or FxJournalLineRole.UnrealisedLoss;

    private static bool IsOverlayReversal(FxDifferenceGlLeg g) =>
        g.Role is FxJournalLineRole.OverlayReversalGain or FxJournalLineRole.OverlayReversalLoss;

    private static bool IsCurrencyCode(string? code) =>
        code is { Length: 3 } && code.All(ch => ch is >= 'A' and <= 'Z');

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
