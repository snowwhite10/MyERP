using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// Description of one frozen FI report read (BR-FI-130, ADR-094) that every continuation page reuses.
/// <see cref="MemberIds"/> are the row identities in report order (principal ID for AR／AP detail, source ID for other
/// income／expense); <see cref="ContentDigest"/> is the 64-character lowercase hex SHA-256 of every row's content;
/// exactly one of <see cref="ArApTotals"/>／<see cref="OtherTotals"/> is set, matching <c>Binding.Kind</c>.
/// <see cref="ExpiresAt"/> = <see cref="CreatedAt"/> + TTL. Carries no authority.
/// </summary>
public sealed record FiReportManifest(
    FiReportCursorBinding Binding,
    long CutoffSequence,
    IReadOnlyList<long> MemberIds,
    string ContentDigest,
    ArApDetailTotals? ArApTotals,
    OtherIncomeExpenseTotals? OtherTotals,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public enum FiReportManifestError
{
    Required,
    OutOfRange,
    InvalidFormat,
    NotAllowed,
    /// <summary>More than <see cref="FiReportManifestRules.MaxMembers"/> rows (HTTP 422: narrow the filter).</summary>
    TooLarge,
    /// <summary>Report, re-read or stored manifest disagree (HTTP 500 internal error, never a partial page).</summary>
    Inconsistent
}

/// <summary>
/// One error. <see cref="Collection"/> is <c>binding</c>, <c>request</c>, <c>clock</c>, <c>report</c> (create),
/// <c>manifest</c> (stored manifest) or <c>reread</c> (continuation re-read).
/// </summary>
public sealed record FiReportManifestFieldError(string Collection, string Field, FiReportManifestError Error);

public enum FiReportManifestStatus
{
    Ok,
    /// <summary>HTTP 400.</summary>
    Rejected,
    /// <summary>HTTP 422.</summary>
    TooLarge,
    /// <summary>HTTP 500.</summary>
    Inconsistent
}

internal static class FiReportManifestStatusOf
{
    public static FiReportManifestStatus Of(IReadOnlyList<FiReportManifestFieldError> errors) =>
        errors.Count == 0 ? FiReportManifestStatus.Ok
        : errors.Any(e => e.Error is not (FiReportManifestError.TooLarge or FiReportManifestError.Inconsistent))
            ? FiReportManifestStatus.Rejected
        : errors.Any(e => e.Error == FiReportManifestError.TooLarge) ? FiReportManifestStatus.TooLarge
        : FiReportManifestStatus.Inconsistent;
}

public sealed record FiReportManifestResult(FiReportManifest? Manifest, IReadOnlyList<FiReportManifestFieldError> Errors)
{
    public FiReportManifestStatus Status => FiReportManifestStatusOf.Of(Errors);
}

public sealed record FiReportManifestPageResult<TPage>(TPage? Page, IReadOnlyList<FiReportManifestFieldError> Errors)
    where TPage : class
{
    public FiReportManifestStatus Status => FiReportManifestStatusOf.Of(Errors);
}

/// <summary>CSV text of a manifest-verified export (no BOM); null with no rows unless <see cref="Status"/> is Ok.</summary>
public sealed record FiReportManifestExportResult(string? Content, int RowCount,
    IReadOnlyList<FiReportManifestFieldError> Errors)
{
    public FiReportManifestStatus Status => FiReportManifestStatusOf.Of(Errors);
}

/// <summary>
/// Pure FI report manifest (BR-FI-130／131, FI-RPT-005／006 U level, ADR-094, WU-BI-001a3). <c>Create*</c> describes a
/// complete report read under an ADR-093 binding: cutoff, ordered member IDs, content digest, full-set totals and
/// expiry from the caller's IClock time and injected TTL (1–60 minutes). <c>Page*</c> takes the stored manifest and a
/// re-read at the same cutoff, checks scope, members, digest and totals, then slices with the bound page size; any
/// disagreement is <see cref="FiReportManifestStatus.Inconsistent"/> with no page. Expiry and token checks stay with
/// <see cref="FiReportCursorTokenRules.Validate"/>; SQL storage, permissions, API and screens stay with WU-BI-001a.
/// </summary>
public static class FiReportManifestRules
{
    public const int MaxMembers = 10_000;
    public const string DigestDomain = "MyERP.FI.ReportManifest.v1\n";
    public static readonly TimeSpan MinTtl = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromMinutes(60);

    public static FiReportManifestResult CreateArAp(FiReportCursorBinding? binding, ArApDetailReport? report,
        DateTimeOffset createdAt, TimeSpan ttl)
    {
        var errors = CreateInputErrors(binding, report is null, report?.Rows.Count ?? 0, createdAt, ttl, arAp: true);
        if (errors.Count > 0) return new(null, errors);

        ArApScopeErrors(binding!, report!, "report", errors);
        ArApContentErrors(report!, "report", errors);
        if (errors.Count > 0) return new(null, errors);

        return new(new FiReportManifest(binding!, report!.CutoffSequence,
            report.Rows.Select(r => r.PrincipalId).ToList(), DigestArAp(binding!.Kind, report.Rows), report.Totals,
            null, createdAt, createdAt + ttl), []);
    }

    public static FiReportManifestResult CreateOtherIncomeExpense(FiReportCursorBinding? binding,
        OtherIncomeExpenseReport? report, DateTimeOffset createdAt, TimeSpan ttl)
    {
        var errors = CreateInputErrors(binding, report is null, report?.Rows.Count ?? 0, createdAt, ttl, arAp: false);
        if (errors.Count > 0) return new(null, errors);

        OtherScopeErrors(binding!, report!, "report", errors);
        OtherContentErrors(report!, "report", errors);
        if (errors.Count > 0) return new(null, errors);

        return new(new FiReportManifest(binding!, report!.CutoffSequence,
            report.Rows.Select(r => r.SourceId).ToList(), DigestOther(report.Rows), null, report.Totals, createdAt,
            createdAt + ttl), []);
    }

    public static FiReportManifestPageResult<ArApDetailPage> PageArAp(FiReportManifest? manifest,
        ArApDetailReport? reread, int pageNumber)
    {
        var errors = PageInputErrors(manifest, reread is null, pageNumber);
        if (errors.Count > 0) return new(null, errors);

        ArApRereadErrors(manifest!, reread!, errors);
        if (errors.Count > 0) return new(null, errors);

        return new(ArApDetailAsOfRules.Page(reread, pageNumber, manifest!.Binding.PageSize).Page, []);
    }

    /// <summary>
    /// Export of a frozen AR／AP read (FI-RPT-014, ADR-095): the re-read at the manifest cutoff must match the stored
    /// manifest exactly (scope, members, digest, totals) before <see cref="ArApDetailAsOfRules.Export"/> renders every
    /// member regardless of the bound page size. Any disagreement is <see cref="FiReportManifestStatus.Inconsistent"/>
    /// with no content — never a partial file. Expiry stays with <see cref="FiReportCursorTokenRules.Validate"/>.
    /// </summary>
    public static FiReportManifestExportResult ExportArAp(FiReportManifest? manifest, ArApDetailReport? reread,
        IReadOnlyList<string?>? headers)
    {
        var errors = ExportInputErrors(manifest, reread is null, headers, ArApDetailAsOfRules.ExportColumnCount);
        if (errors.Count > 0) return new(null, 0, errors);

        ArApRereadErrors(manifest!, reread!, errors);
        if (errors.Count > 0) return new(null, 0, errors);

        var export = ArApDetailAsOfRules.Export(reread, headers);
        // Unreachable after the checks above; mapped defensively so a rule drift never yields a partial file.
        foreach (var e in export.Errors) Inconsistent(errors, "reread", e.Field);
        return errors.Count > 0 ? new(null, 0, errors) : new(export.Content, export.RowCount, []);
    }

    /// <summary>Stored manifest self-consistency, then the re-read's scope, cutoff, content, members, digest and totals.</summary>
    private static void ArApRereadErrors(FiReportManifest manifest, ArApDetailReport reread,
        List<FiReportManifestFieldError> errors)
    {
        ManifestErrors(manifest, arAp: true, errors);
        if (errors.Count > 0) return;

        ArApScopeErrors(manifest.Binding, reread, "reread", errors);
        if (reread.CutoffSequence != manifest.CutoffSequence) Inconsistent(errors, "reread", "cutoffSequence");
        ArApContentErrors(reread, "reread", errors);
        MemberErrors(manifest, reread.Rows.Select(r => r.PrincipalId), errors);
        if (DigestArAp(manifest.Binding.Kind, reread.Rows) != manifest.ContentDigest)
            Inconsistent(errors, "reread", "contentDigest");
        if (reread.Totals != manifest.ArApTotals && !Has(errors, "reread", "totals"))
            Inconsistent(errors, "reread", "totals");
    }

    public static FiReportManifestPageResult<OtherIncomeExpensePage> PageOtherIncomeExpense(
        FiReportManifest? manifest, OtherIncomeExpenseReport? reread, int pageNumber)
    {
        var errors = PageInputErrors(manifest, reread is null, pageNumber);
        if (errors.Count > 0) return new(null, errors);

        OtherRereadErrors(manifest!, reread!, errors);
        if (errors.Count > 0) return new(null, errors);

        return new(OtherIncomeExpenseRules.Page(reread, pageNumber, manifest!.Binding.PageSize).Page, []);
    }

    /// <summary>
    /// Export of a frozen other income／expense read (FI-RPT-014, ADR-095, WU-BI-001a5): same contract as
    /// <see cref="ExportArAp"/> — the re-read must match the stored manifest exactly before
    /// <see cref="OtherIncomeExpenseRules.Export"/> renders every member regardless of the bound page size; any
    /// disagreement is <see cref="FiReportManifestStatus.Inconsistent"/> with no content — never a partial file.
    /// </summary>
    public static FiReportManifestExportResult ExportOtherIncomeExpense(FiReportManifest? manifest,
        OtherIncomeExpenseReport? reread, IReadOnlyList<string?>? headers)
    {
        var errors = ExportInputErrors(manifest, reread is null, headers, OtherIncomeExpenseRules.ExportColumnCount);
        if (errors.Count > 0) return new(null, 0, errors);

        OtherRereadErrors(manifest!, reread!, errors);
        if (errors.Count > 0) return new(null, 0, errors);

        var export = OtherIncomeExpenseRules.Export(reread, headers);
        // Unreachable after the checks above; mapped defensively so a rule drift never yields a partial file.
        foreach (var e in export.Errors) Inconsistent(errors, "reread", e.Field);
        return errors.Count > 0 ? new(null, 0, errors) : new(export.Content, export.RowCount, []);
    }

    /// <summary>Stored manifest self-consistency, then the re-read's scope, cutoff, content, members, digest and totals.</summary>
    private static void OtherRereadErrors(FiReportManifest manifest, OtherIncomeExpenseReport reread,
        List<FiReportManifestFieldError> errors)
    {
        ManifestErrors(manifest, arAp: false, errors);
        if (errors.Count > 0) return;

        OtherScopeErrors(manifest.Binding, reread, "reread", errors);
        if (reread.CutoffSequence != manifest.CutoffSequence) Inconsistent(errors, "reread", "cutoffSequence");
        OtherContentErrors(reread, "reread", errors);
        MemberErrors(manifest, reread.Rows.Select(r => r.SourceId), errors);
        if (DigestOther(reread.Rows) != manifest.ContentDigest) Inconsistent(errors, "reread", "contentDigest");
        if (reread.Totals != manifest.OtherTotals && !Has(errors, "reread", "totals"))
            Inconsistent(errors, "reread", "totals");
    }

    // ---- input validation ----

    private static List<FiReportManifestFieldError> CreateInputErrors(FiReportCursorBinding? binding,
        bool reportMissing, int rowCount, DateTimeOffset createdAt, TimeSpan ttl, bool arAp)
    {
        var errors = BindingErrors(binding, "binding", FiReportManifestError.NotAllowed, arAp);
        if (reportMissing) errors.Add(new("request", "report", FiReportManifestError.Required));
        if (ttl < MinTtl || ttl > MaxTtl) errors.Add(new("request", "ttl", FiReportManifestError.OutOfRange));
        if (createdAt == default) errors.Add(new("clock", "createdAt", FiReportManifestError.Required));
        else if (createdAt.UtcDateTime > DateTime.MaxValue - MaxTtl)
            errors.Add(new("clock", "createdAt", FiReportManifestError.OutOfRange));
        if (rowCount > MaxMembers) errors.Add(new("report", "rows", FiReportManifestError.TooLarge));
        return errors;
    }

    /// <summary>Binding validity from ADR-093 plus the report family; <paramref name="kindError"/> varies by caller.</summary>
    private static List<FiReportManifestFieldError> BindingErrors(FiReportCursorBinding? binding, string collection,
        FiReportManifestError kindError, bool arAp)
    {
        var tokenErrors = new List<FiReportCursorFieldError>();
        FiReportCursorTokenRules.ValidateBinding(binding, tokenErrors);
        var errors = tokenErrors.Select(e => new FiReportManifestFieldError(collection, e.Field, e.Error switch
        {
            FiReportCursorError.Required => FiReportManifestError.Required,
            FiReportCursorError.InvalidFormat => FiReportManifestError.InvalidFormat,
            FiReportCursorError.NotAllowed => FiReportManifestError.NotAllowed,
            _ => FiReportManifestError.OutOfRange,
        })).ToList();
        if (binding is not null && Enum.IsDefined(binding.Kind) && IsArAp(binding.Kind) != arAp)
            errors.Add(new(collection, "kind", kindError));
        return errors;
    }

    /// <summary>Export request input (400) in fixed order; any null header is Required, a wrong count OutOfRange.</summary>
    private static List<FiReportManifestFieldError> ExportInputErrors(FiReportManifest? manifest, bool rereadMissing,
        IReadOnlyList<string?>? headers, int columnCount)
    {
        var errors = new List<FiReportManifestFieldError>();
        if (manifest is null) errors.Add(new("request", "manifest", FiReportManifestError.Required));
        if (rereadMissing) errors.Add(new("request", "reread", FiReportManifestError.Required));
        if (headers is null || headers.Any(h => h is null))
            errors.Add(new("request", "headers", FiReportManifestError.Required));
        else if (headers.Count != columnCount)
            errors.Add(new("request", "headers", FiReportManifestError.OutOfRange));
        return errors;
    }

    private static List<FiReportManifestFieldError> PageInputErrors(FiReportManifest? manifest, bool rereadMissing,
        int pageNumber)
    {
        var errors = new List<FiReportManifestFieldError>();
        if (manifest is null) errors.Add(new("request", "manifest", FiReportManifestError.Required));
        if (rereadMissing) errors.Add(new("request", "reread", FiReportManifestError.Required));
        if (pageNumber is < 1 or > FiReportCursorTokenRules.MaxPageNumber)
            errors.Add(new("request", "pageNumber", FiReportManifestError.OutOfRange));
        return errors;
    }

    /// <summary>A stored manifest that is not self-consistent is corrupt: every finding is Inconsistent.</summary>
    private static void ManifestErrors(FiReportManifest m, bool arAp, List<FiReportManifestFieldError> errors)
    {
        foreach (var e in BindingErrors(m.Binding, "manifest", FiReportManifestError.Inconsistent, arAp))
            Inconsistent(errors, "manifest", e.Field == "kind" ? "kind" : "binding");
        if (m.CutoffSequence <= 0) Inconsistent(errors, "manifest", "cutoffSequence");
        if (m.ContentDigest is not { Length: 64 } d || !d.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            Inconsistent(errors, "manifest", "contentDigest");

        var ids = m.MemberIds ?? [];
        int? rowCount = arAp ? m.ArApTotals?.RowCount : m.OtherTotals?.RowCount;
        if (m.MemberIds is null || ids.Count > MaxMembers || ids.Any(x => x <= 0)
            || ids.Distinct().Count() != ids.Count || (rowCount is { } n && n != ids.Count))
            Inconsistent(errors, "manifest", "memberIds");
        if ((arAp ? m.ArApTotals is null || m.OtherTotals is not null : m.OtherTotals is null || m.ArApTotals is not null)
            && !Has(errors, "manifest", "kind"))
            Inconsistent(errors, "manifest", "totals");

        if (m.CreatedAt == default) Inconsistent(errors, "manifest", "createdAt");
        else if (m.ExpiresAt - m.CreatedAt is var ttl && (ttl < MinTtl || ttl > MaxTtl))
            Inconsistent(errors, "manifest", "expiresAt");
    }

    // ---- consistency ----

    private static void ArApScopeErrors(FiReportCursorBinding b, ArApDetailReport r, string collection,
        List<FiReportManifestFieldError> errors)
    {
        var direction = b.Kind == FiReportKind.ApDetail ? ArApDirection.Payable : ArApDirection.Receivable;
        if (r.CompanyId != b.CompanyId) Inconsistent(errors, collection, "companyId");
        if (r.Direction != direction) Inconsistent(errors, collection, "direction");
        if (r.PartnerId != b.PartnerId) Inconsistent(errors, collection, "partnerId");
        if (r.AsOf != b.AsOf) Inconsistent(errors, collection, "asOf");
        if (!string.Equals(r.BaseCurrency, b.Currency, StringComparison.Ordinal))
            Inconsistent(errors, collection, "currency");
        if (r.CutoffSequence <= 0) Inconsistent(errors, collection, "cutoffSequence");
    }

    private static void OtherScopeErrors(FiReportCursorBinding b, OtherIncomeExpenseReport r, string collection,
        List<FiReportManifestFieldError> errors)
    {
        if (r.CompanyId != b.CompanyId) Inconsistent(errors, collection, "companyId");
        if (r.From != b.From) Inconsistent(errors, collection, "from");
        if (r.Through != b.Through) Inconsistent(errors, collection, "through");
        if (r.KindFilter != b.KindFilter) Inconsistent(errors, collection, "kindFilter");
        if (!string.Equals(r.BaseCurrency, b.Currency, StringComparison.Ordinal))
            Inconsistent(errors, collection, "currency");
        if (r.CutoffSequence <= 0) Inconsistent(errors, collection, "cutoffSequence");
    }

    private static void ArApContentErrors(ArApDetailReport r, string collection, List<FiReportManifestFieldError> errors)
    {
        if (r.Rows.Select(x => x.PrincipalId).Distinct().Count() != r.Rows.Count)
            Inconsistent(errors, collection, "memberIds");
        static bool RowAgrees(ArApDetailRow x)
        {
            try
            {
                return x.OpenBase == checked(x.OriginalBase - x.CreditBase - x.SettlementBase)
                       && x.IsOutstanding == x.OpenBase > 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        if (!r.Rows.All(RowAgrees))
        {
            Inconsistent(errors, collection, "rows");
            return;
        }

        try
        {
            decimal original = 0, credit = 0, settled = 0, open = 0;
            foreach (var x in r.Rows)
            {
                original = checked(original + x.OriginalBase);
                credit = checked(credit + x.CreditBase);
                settled = checked(settled + x.SettlementBase);
                open = checked(open + x.OpenBase);
            }

            if (new ArApDetailTotals(original, credit, settled, open, r.Rows.Count,
                    r.Rows.Count(x => x.IsOutstanding)) != r.Totals)
                Inconsistent(errors, collection, "totals");
        }
        catch (OverflowException)
        {
            Inconsistent(errors, collection, "totals");
        }
    }

    private static void OtherContentErrors(OtherIncomeExpenseReport r, string collection,
        List<FiReportManifestFieldError> errors)
    {
        if (r.Rows.Select(x => x.SourceId).Distinct().Count() != r.Rows.Count)
            Inconsistent(errors, collection, "memberIds");

        // Page re-validates signed rows and totals the same way export does (ADR-092).
        foreach (var e in OtherIncomeExpenseRules.Page(r, 1, 1).Errors)
            Inconsistent(errors, collection, e.Field);
    }

    private static void MemberErrors(FiReportManifest m, IEnumerable<long> rereadIds,
        List<FiReportManifestFieldError> errors)
    {
        if (!m.MemberIds.SequenceEqual(rereadIds)) Inconsistent(errors, "reread", "memberIds");
    }

    private static void Inconsistent(List<FiReportManifestFieldError> errors, string collection, string field)
    {
        var e = new FiReportManifestFieldError(collection, field, FiReportManifestError.Inconsistent);
        if (!errors.Contains(e)) errors.Add(e);
    }

    private static bool Has(List<FiReportManifestFieldError> errors, string collection, string field) =>
        errors.Any(e => e.Collection == collection && e.Field == field);

    private static bool IsArAp(FiReportKind kind) => kind is FiReportKind.ArDetail or FiReportKind.ApDetail;

    // ---- digest ----

    private static string DigestArAp(FiReportKind kind, IEnumerable<ArApDetailRow> rows)
    {
        var w = new DigestWriter(kind);
        foreach (var r in rows)
        {
            w.Row();
            w.Text(r.Direction.ToString()).Number(r.CompanyId).Number(r.PartnerId).Text(r.PartnerLabel)
                .Number(r.PrincipalId).Text(r.SourceModule).Text(r.SourceType).Number(r.SourceId)
                .Text(r.DocumentReference).Number(r.JournalId).Date(r.BusinessDate).Instant(r.PostingTime)
                .Text(r.BaseCurrency).Text(r.DocumentCurrency).Amount(r.OriginalBase).Amount(r.CreditBase)
                .Amount(r.SettlementBase).Amount(r.OpenBase).Number(r.AllocationCount)
                .Number(r.AllocationIds.Count);
            foreach (var id in r.AllocationIds) w.Number(id);
            w.Text(r.IsOutstanding ? "1" : "0");
        }

        return w.Finish();
    }

    private static string DigestOther(IEnumerable<OtherIncomeExpenseRow> rows)
    {
        var w = new DigestWriter(FiReportKind.OtherIncomeExpense);
        foreach (var r in rows)
        {
            w.Row();
            w.Date(r.BusinessDate).Instant(r.PostingTime).Text(r.Kind.ToString()).Number(r.SourceId)
                .Text(r.DocumentReference).Number(r.CategoryId).Text(r.CategoryLabel).Text(r.AccountCode)
                .Text(r.AccountName).Number(r.JournalId).Text(r.BaseCurrency).Amount(r.GrossBase).Amount(r.SignedBase);
        }

        return w.Finish();
    }

    /// <summary>
    /// Length-prefixed canonical fields (<c>len:value;</c>) so no text can shift a field boundary; amounts drop trailing
    /// zeros (SQL scale does not change the digest) and instants use UTC ticks (offset does not).
    /// </summary>
    private sealed class DigestWriter
    {
        private readonly StringBuilder _sb = new();

        public DigestWriter(FiReportKind kind) => _sb.Append(DigestDomain).Append(kind).Append('\n');

        public void Row() => _sb.Append('\n');

        public DigestWriter Text(string? value)
        {
            value ??= "";
            _sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
            return this;
        }

        public DigestWriter Number(long value) => Text(value.ToString(CultureInfo.InvariantCulture));

        public DigestWriter Amount(decimal value) => Text(value.ToString("G29", CultureInfo.InvariantCulture));

        public DigestWriter Date(DateOnly value) => Text(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        public DigestWriter Instant(DateTimeOffset value) => Number(value.UtcTicks);

        public string Finish() =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_sb.ToString())));
    }
}
