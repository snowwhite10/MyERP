namespace MyERP.Modules.Finance.Domain;

public enum ArApDirection
{
    Receivable,
    Payable
}

/// <summary>Saved state of a principal or allocation. Only <see cref="Posted"/> enters the as-of view (BR-FI-125).</summary>
public enum ArApSourceState
{
    Posted,
    Draft,
    Pending,
    Rejected,
    RolledBack,
    Deleted
}

/// <summary>Credit (credit note／debit note) or settlement (collection／payment) allocation against a principal.</summary>
public enum ArApAllocationKind
{
    Credit,
    Settlement
}

/// <summary>
/// The bound read of BR-FI-127: one company, direction, optional partner filter (null = every partner the reader
/// already scoped as visible), inclusive <see cref="AsOf"/> business date, positive committed
/// <see cref="CutoffSequence"/> the set was frozen at, base currency with <see cref="BaseCurrencyDecimals"/> (0–4) and
/// the zero-open history option.
/// </summary>
public sealed record ArApDetailScope(long CompanyId, ArApDirection Direction, long? PartnerId, DateOnly AsOf,
    long CutoffSequence, string? BaseCurrency, int BaseCurrencyDecimals, bool IncludeZeroOpen);

/// <summary>
/// One saved FI AR／AP principal in the frozen set. <see cref="OriginalBase"/> is the saved positive gross in company
/// base currency (never recomputed from today's rate, BR-FI-126); <see cref="JournalId"/> is the immutable GL link and
/// is null only when missing.
/// </summary>
public sealed record ArApPrincipalRecord(
    long PrincipalId,
    long CompanyId,
    ArApDirection Direction,
    long PartnerId,
    string? PartnerLabel,
    string? SourceModule,
    string? SourceType,
    long SourceId,
    string? DocumentReference,
    long? JournalId,
    DateOnly BusinessDate,
    DateTimeOffset PostingTime,
    ArApSourceState State,
    long CommitSequence,
    string? BaseCurrency,
    string? DocumentCurrency,
    decimal OriginalBase);

/// <summary>One saved credit or settlement allocation; <see cref="AmountBase"/> is the saved positive base amount.</summary>
public sealed record ArApAllocationRecord(
    long AllocationId,
    long CompanyId,
    long PrincipalId,
    ArApAllocationKind Kind,
    DateOnly BusinessDate,
    ArApSourceState State,
    long CommitSequence,
    string? BaseCurrency,
    decimal AmountBase,
    long? JournalId);

/// <summary>Why a frozen set cannot produce a complete as-of read (HTTP 422, BR-FI-125／126／128).</summary>
public enum ArApIncompleteReason
{
    CurrencyMismatch,
    MissingJournal,
    MissingPrincipal,
    AllocationOnNonPostedPrincipal,
    AllocationBeforePrincipal,
    NegativeOpen,
    Overflow
}

/// <summary>
/// One incomplete item. <see cref="PrincipalId"/> is null only for a full-set total overflow; <see cref="SourceId"/>
/// is the principal's FI source when the principal is in the set; <see cref="AllocationId"/> is set for allocation items.
/// </summary>
public sealed record ArApIncompleteItem(ArApIncompleteReason Reason, long? PrincipalId, long? SourceId,
    long? AllocationId);

/// <summary>
/// One detail row (BR-FI-128). <c>OpenBase = OriginalBase − CreditBase − SettlementBase</c>; only a positive open
/// balance <see cref="IsOutstanding"/>. <see cref="AllocationIds"/> are the counted allocations, ascending.
/// </summary>
public sealed record ArApDetailRow(
    ArApDirection Direction,
    long CompanyId,
    long PartnerId,
    string PartnerLabel,
    long PrincipalId,
    string SourceModule,
    string SourceType,
    long SourceId,
    string DocumentReference,
    long JournalId,
    DateOnly BusinessDate,
    DateTimeOffset PostingTime,
    string BaseCurrency,
    string DocumentCurrency,
    decimal OriginalBase,
    decimal CreditBase,
    decimal SettlementBase,
    decimal OpenBase,
    IReadOnlyList<long> AllocationIds,
    int AllocationCount,
    bool IsOutstanding);

/// <summary>Full-set totals over every report row (not a page); <c>Open = Original − Credit − Settled</c>.</summary>
public sealed record ArApDetailTotals(decimal Original, decimal Credit, decimal Settled, decimal Open, int RowCount,
    int OutstandingCount);

/// <summary>
/// The complete as-of read. Rows are ordered by (business date, posting time, principal ID). Exclusion counts make an
/// all-excluded read explicit: non-Posted and after-as-of principals, zero-open rows omitted when history is off, and
/// non-Posted or later allocations.
/// </summary>
public sealed record ArApDetailReport(
    long CompanyId,
    ArApDirection Direction,
    long? PartnerId,
    DateOnly AsOf,
    long CutoffSequence,
    string BaseCurrency,
    bool IncludeZeroOpen,
    IReadOnlyList<ArApDetailRow> Rows,
    ArApDetailTotals Totals,
    int ExcludedNotPostedCount,
    int ExcludedAfterAsOfCount,
    int ExcludedZeroOpenCount,
    int ExcludedAllocationCount);

public enum ArApDetailError
{
    Required,
    OutOfRange,
    InvalidScale,
    InvalidFormat,
    Duplicate,
    CrossCompany,
    AfterCutoff,
    FilterMismatch
}

/// <summary>
/// One input error (HTTP 400). <see cref="Collection"/> is <c>scope</c>, <c>request</c>, <c>principals</c>,
/// <c>allocations</c> or <c>report</c>; <see cref="Index"/> is the item position for lists.
/// </summary>
public sealed record ArApDetailFieldError(string Collection, int? Index, string Field, ArApDetailError Error);

public enum ArApDetailStatus
{
    Complete,
    Incomplete,
    Invalid
}

public sealed record ArApDetailResult(ArApDetailReport? Report, IReadOnlyList<ArApIncompleteItem> Incomplete,
    IReadOnlyList<ArApDetailFieldError> Errors)
{
    public ArApDetailStatus Status => Errors.Count > 0 ? ArApDetailStatus.Invalid
        : Incomplete.Count > 0 ? ArApDetailStatus.Incomplete
        : ArApDetailStatus.Complete;
}

/// <summary>One page of the report rows; every page carries the same cutoff, as-of date and full-set totals.</summary>
public sealed record ArApDetailPage(int PageNumber, int PageSize, long CompanyId, long CutoffSequence, DateOnly AsOf,
    IReadOnlyList<ArApDetailRow> Rows, ArApDetailTotals Totals, bool HasNext);

public sealed record ArApDetailPageResult(ArApDetailPage? Page, IReadOnlyList<ArApDetailFieldError> Errors)
{
    public bool IsValid => Page is not null;
}

/// <summary>
/// Pure FI AR／AP detail as-of read (REQ_F4116.19.1～2, BR-FI-125～128, ADR-091, WU-BI-001a0). From one frozen
/// committed set it keeps Posted principals dated on or before <c>asOf</c>, subtracts Posted credit／settlement
/// allocations dated on or before <c>asOf</c>, and computes open base with checked decimals. Negative open, missing
/// principal or journal linkage, allocations on non-Posted or later principals, base currency mismatch and overflow
/// make the whole read incomplete with IDs — never a partial report. <see cref="Page"/> slices the same rows with the
/// full-set totals. Queries and changes nothing: SQL reads, cutoff token, permissions, API and screens stay with
/// WU-BI-001a.
/// </summary>
public static class ArApDetailAsOfRules
{
    public const int MaxCurrencyDecimals = 4;
    public const int MaxPageSize = 200;
    public const int MaxLabelLength = 200;
    public const int MaxSourceCodeLength = 32;
    public const int MaxReferenceLength = 64;

    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static ArApDetailResult Read(
        ArApDetailScope? scope,
        IReadOnlyList<ArApPrincipalRecord?>? principals,
        IReadOnlyList<ArApAllocationRecord?>? allocations)
    {
        var errors = new List<ArApDetailFieldError>();

        long? company = null;
        long? cutoff = null;
        int? scale = null;
        ArApDirection? direction = null;
        if (scope is null)
        {
            errors.Add(new("scope", null, "scope", ArApDetailError.Required));
        }
        else
        {
            void Fail(string field, ArApDetailError e) => errors.Add(new("scope", null, field, e));
            if (scope.CompanyId <= 0) Fail("companyId", ArApDetailError.Required);
            else company = scope.CompanyId;
            if (Enum.IsDefined(scope.Direction)) direction = scope.Direction;
            else Fail("direction", ArApDetailError.OutOfRange);
            if (scope.PartnerId is <= 0) Fail("partnerId", ArApDetailError.OutOfRange);
            if (scope.AsOf == default) Fail("asOf", ArApDetailError.Required);
            if (scope.CutoffSequence <= 0) Fail("cutoffSequence", ArApDetailError.Required);
            else cutoff = scope.CutoffSequence;
            if (!IsCurrencyCode(scope.BaseCurrency)) Fail("baseCurrency", ArApDetailError.InvalidFormat);
            if (scope.BaseCurrencyDecimals is >= 0 and <= MaxCurrencyDecimals) scale = scope.BaseCurrencyDecimals;
            else Fail("baseCurrencyDecimals", ArApDetailError.OutOfRange);
        }

        if (principals is null) errors.Add(new("request", null, "principals", ArApDetailError.Required));
        if (allocations is null) errors.Add(new("request", null, "allocations", ArApDetailError.Required));

        ArApDetailError? AmountError(decimal value) =>
            value <= 0 || value > SqlAmountMaximum ? ArApDetailError.OutOfRange
            : scale is { } s && decimal.Round(value, s) != value ? ArApDetailError.InvalidScale
            : null;

        ArApDetailError? SequenceError(long sequence) =>
            sequence <= 0 ? ArApDetailError.Required
            : cutoff is { } c && sequence > c ? ArApDetailError.AfterCutoff
            : null;

        ArApDetailError? TextError(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? ArApDetailError.Required
            : value.Length > max ? ArApDetailError.OutOfRange
            : null;

        var seenPrincipals = new HashSet<long>();
        // A replayed FI source must map to one principal; a second principal for it would double-count (FI-RPT-005).
        var seenSources = new HashSet<(string, string, long)>();
        for (var i = 0; i < (principals?.Count ?? 0); i++)
        {
            var p = principals![i];
            void Fail(string field, ArApDetailError e) => errors.Add(new("principals", i, field, e));
            if (p is null)
            {
                Fail("principal", ArApDetailError.Required);
                continue;
            }

            if (p.PrincipalId <= 0) Fail("principalId", ArApDetailError.Required);
            else if (!seenPrincipals.Add(p.PrincipalId)) Fail("principalId", ArApDetailError.Duplicate);
            if (p.CompanyId <= 0) Fail("companyId", ArApDetailError.Required);
            else if (company is { } c && p.CompanyId != c) Fail("companyId", ArApDetailError.CrossCompany);
            if (!Enum.IsDefined(p.Direction)) Fail("direction", ArApDetailError.OutOfRange);
            else if (direction is { } d && p.Direction != d) Fail("direction", ArApDetailError.FilterMismatch);
            if (p.PartnerId <= 0) Fail("partnerId", ArApDetailError.Required);
            else if (scope?.PartnerId is { } partner && p.PartnerId != partner)
                Fail("partnerId", ArApDetailError.FilterMismatch);
            if (TextError(p.PartnerLabel, MaxLabelLength) is { } le) Fail("partnerLabel", le);
            if (TextError(p.SourceModule, MaxSourceCodeLength) is { } me) Fail("sourceModule", me);
            if (TextError(p.SourceType, MaxSourceCodeLength) is { } te) Fail("sourceType", te);
            if (p.SourceId <= 0) Fail("sourceId", ArApDetailError.Required);
            else if (!string.IsNullOrWhiteSpace(p.SourceModule) && !string.IsNullOrWhiteSpace(p.SourceType)
                     && !seenSources.Add((p.SourceModule, p.SourceType, p.SourceId)))
                Fail("sourceId", ArApDetailError.Duplicate);
            if (TextError(p.DocumentReference, MaxReferenceLength) is { } re) Fail("documentReference", re);
            if (p.JournalId is <= 0) Fail("journalId", ArApDetailError.Required);
            if (p.BusinessDate == default) Fail("businessDate", ArApDetailError.Required);
            if (p.PostingTime == default) Fail("postingTime", ArApDetailError.Required);
            if (!Enum.IsDefined(p.State)) Fail("state", ArApDetailError.OutOfRange);
            if (SequenceError(p.CommitSequence) is { } qe) Fail("commitSequence", qe);
            if (!IsCurrencyCode(p.BaseCurrency)) Fail("baseCurrency", ArApDetailError.InvalidFormat);
            if (!IsCurrencyCode(p.DocumentCurrency)) Fail("documentCurrency", ArApDetailError.InvalidFormat);
            if (AmountError(p.OriginalBase) is { } oe) Fail("originalBase", oe);
        }

        var seenAllocations = new HashSet<long>();
        for (var i = 0; i < (allocations?.Count ?? 0); i++)
        {
            var a = allocations![i];
            void Fail(string field, ArApDetailError e) => errors.Add(new("allocations", i, field, e));
            if (a is null)
            {
                Fail("allocation", ArApDetailError.Required);
                continue;
            }

            if (a.AllocationId <= 0) Fail("allocationId", ArApDetailError.Required);
            else if (!seenAllocations.Add(a.AllocationId)) Fail("allocationId", ArApDetailError.Duplicate);
            if (a.CompanyId <= 0) Fail("companyId", ArApDetailError.Required);
            else if (company is { } c && a.CompanyId != c) Fail("companyId", ArApDetailError.CrossCompany);
            if (a.PrincipalId <= 0) Fail("principalId", ArApDetailError.Required);
            if (!Enum.IsDefined(a.Kind)) Fail("kind", ArApDetailError.OutOfRange);
            if (a.BusinessDate == default) Fail("businessDate", ArApDetailError.Required);
            if (!Enum.IsDefined(a.State)) Fail("state", ArApDetailError.OutOfRange);
            if (SequenceError(a.CommitSequence) is { } qe) Fail("commitSequence", qe);
            if (!IsCurrencyCode(a.BaseCurrency)) Fail("baseCurrency", ArApDetailError.InvalidFormat);
            if (AmountError(a.AmountBase) is { } ae) Fail("amountBase", ae);
            if (a.JournalId is <= 0) Fail("journalId", ArApDetailError.Required);
        }

        if (errors.Count > 0) return new(null, [], errors);

        var s = scope!;
        var allPrincipals = principals!.Select(x => x!).ToList();
        var byId = allPrincipals.ToDictionary(x => x.PrincipalId);
        bool Included(ArApPrincipalRecord p) => p.State == ArApSourceState.Posted && p.BusinessDate <= s.AsOf;

        var incomplete = new List<ArApIncompleteItem>();
        foreach (var p in allPrincipals.Where(Included))
        {
            if (p.BaseCurrency != s.BaseCurrency)
                incomplete.Add(new(ArApIncompleteReason.CurrencyMismatch, p.PrincipalId, p.SourceId, null));
            if (p.JournalId is null)
                incomplete.Add(new(ArApIncompleteReason.MissingJournal, p.PrincipalId, p.SourceId, null));
        }

        var counted = new Dictionary<long, List<ArApAllocationRecord>>();
        var excludedAllocations = 0;
        foreach (var a in allocations!.Select(x => x!))
        {
            if (a.State != ArApSourceState.Posted || a.BusinessDate > s.AsOf)
            {
                excludedAllocations++;
                continue;
            }

            void Incomplete(ArApIncompleteReason reason, long? sourceId) =>
                incomplete.Add(new(reason, a.PrincipalId, sourceId, a.AllocationId));

            if (!byId.TryGetValue(a.PrincipalId, out var p))
            {
                Incomplete(ArApIncompleteReason.MissingPrincipal, null);
                continue;
            }

            if (p.State != ArApSourceState.Posted)
            {
                Incomplete(ArApIncompleteReason.AllocationOnNonPostedPrincipal, p.SourceId);
                continue;
            }

            if (a.BusinessDate < p.BusinessDate) Incomplete(ArApIncompleteReason.AllocationBeforePrincipal, p.SourceId);
            if (a.BaseCurrency != s.BaseCurrency) Incomplete(ArApIncompleteReason.CurrencyMismatch, p.SourceId);
            if (a.JournalId is null) Incomplete(ArApIncompleteReason.MissingJournal, p.SourceId);
            if (!Included(p)) continue;

            if (!counted.TryGetValue(p.PrincipalId, out var list)) counted[p.PrincipalId] = list = [];
            list.Add(a);
        }

        var rows = new List<ArApDetailRow>();
        var excludedZero = 0;
        foreach (var p in allPrincipals.Where(Included)
                     .OrderBy(x => x.BusinessDate).ThenBy(x => x.PostingTime).ThenBy(x => x.PrincipalId))
        {
            var list = (counted.GetValueOrDefault(p.PrincipalId) ?? []).OrderBy(x => x.AllocationId).ToList();
            var credit = list.Where(x => x.Kind == ArApAllocationKind.Credit).Sum(x => x.AmountBase);
            var settlement = list.Where(x => x.Kind == ArApAllocationKind.Settlement).Sum(x => x.AmountBase);
            var open = checked(p.OriginalBase - credit - settlement);
            if (credit > SqlAmountMaximum || settlement > SqlAmountMaximum || credit + settlement > SqlAmountMaximum)
                incomplete.Add(new(ArApIncompleteReason.Overflow, p.PrincipalId, p.SourceId, null));
            if (open < 0) incomplete.Add(new(ArApIncompleteReason.NegativeOpen, p.PrincipalId, p.SourceId, null));

            if (open == 0 && !s.IncludeZeroOpen)
            {
                excludedZero++;
                continue;
            }

            rows.Add(new(p.Direction, p.CompanyId, p.PartnerId, p.PartnerLabel!, p.PrincipalId, p.SourceModule!,
                p.SourceType!, p.SourceId, p.DocumentReference!, p.JournalId ?? 0, p.BusinessDate, p.PostingTime,
                p.BaseCurrency!, p.DocumentCurrency!, p.OriginalBase, credit, settlement, open,
                list.Select(x => x.AllocationId).ToList(), list.Count, open > 0));
        }

        var totals = new ArApDetailTotals(rows.Sum(x => x.OriginalBase), rows.Sum(x => x.CreditBase),
            rows.Sum(x => x.SettlementBase), rows.Sum(x => x.OpenBase), rows.Count, rows.Count(x => x.IsOutstanding));
        if (new[] { totals.Original, totals.Credit, totals.Settled, totals.Open }.Any(x => Math.Abs(x) > SqlAmountMaximum))
            incomplete.Add(new(ArApIncompleteReason.Overflow, null, null, null));

        if (incomplete.Count > 0)
            return new(null, incomplete
                .OrderBy(x => x.PrincipalId ?? long.MaxValue).ThenBy(x => x.Reason).ThenBy(x => x.AllocationId ?? 0)
                .ToList(), []);

        var report = new ArApDetailReport(company!.Value, direction!.Value, s.PartnerId, s.AsOf, cutoff!.Value,
            s.BaseCurrency!, s.IncludeZeroOpen, rows, totals,
            allPrincipals.Count(x => x.State != ArApSourceState.Posted),
            allPrincipals.Count(x => x.State == ArApSourceState.Posted && x.BusinessDate > s.AsOf),
            excludedZero, excludedAllocations);
        return new(report, [], []);
    }

    /// <summary>Slices the report rows (1-based page); a page past the end is empty without a next page.</summary>
    public static ArApDetailPageResult Page(ArApDetailReport? report, int pageNumber, int pageSize)
    {
        var errors = new List<ArApDetailFieldError>();
        if (report is null) errors.Add(new("report", null, "report", ArApDetailError.Required));
        if (pageNumber < 1) errors.Add(new("request", null, "pageNumber", ArApDetailError.OutOfRange));
        if (pageSize is < 1 or > MaxPageSize) errors.Add(new("request", null, "pageSize", ArApDetailError.OutOfRange));
        if (errors.Count > 0) return new(null, errors);

        var total = report!.Rows.Count;
        var start = (long)(pageNumber - 1) * pageSize;
        var rows = start >= total ? [] : report.Rows.Skip((int)start).Take(pageSize).ToList();
        return new(new ArApDetailPage(pageNumber, pageSize, report.CompanyId, report.CutoffSequence, report.AsOf, rows,
            report.Totals, start + pageSize < total), []);
    }

    private static bool IsCurrencyCode(string? code) =>
        code is { Length: 3 } && code.All(ch => ch is >= 'A' and <= 'Z');
}
