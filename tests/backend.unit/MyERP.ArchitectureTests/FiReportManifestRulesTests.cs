using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-005／006（純規則部分，跨頁不重不漏）：FI 報表 manifest。由一次凍結讀取（AR／AP 明細或其他收支）產生
/// manifest——ADR-093 binding、cutoff、有序成員 ID、SHA-256 內容摘要、全集合合計與筆數、createdAt 與
/// expiresAt = createdAt + TTL（1–60 分鐘）；續頁以 manifest 重新切頁並核對重讀結果的成員、摘要與合計，不符整體
/// Inconsistent（500）不得部分成功；頁碼超出為空頁；成員上限 10,000（BR-FI-130／131、ADR-094）。
/// 不涉及 SQL manifest 表、權限、API 或畫面。
/// </summary>
public sealed class FiReportManifestRulesTests
{
    private const long Company = 7, Cutoff = 900, Partner = 31;
    private static readonly string Digest = new('a', 64);
    private static readonly DateOnly AsOf = new(2026, 10, 31), From = new(2026, 10, 1), Through = new(2026, 10, 31);
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 3, 50, 0, TimeSpan.FromHours(8));
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private static FiReportCursorBinding ArBinding(int pageSize = 2, long? partner = null) =>
        new(Company, Digest, FiReportKind.ArDetail, partner, AsOf, null, null, null, "TWD", pageSize);

    private static FiReportCursorBinding OtherBinding(int pageSize = 2, OtherIncomeExpenseKind? kind = null) =>
        new(Company, Digest, FiReportKind.OtherIncomeExpense, null, null, From, Through, kind, "TWD", pageSize);

    private static ArApPrincipalRecord P(long id, decimal original, int minute = 0, long partner = Partner,
        ArApDirection direction = ArApDirection.Receivable, string label = "客戶甲") =>
        new(id, Company, direction, partner, label, "SD", "Invoice", 500 + id, "INV-" + id, 8000 + id,
            new DateOnly(2026, 10, 1), T0.AddMinutes(minute), ArApSourceState.Posted, 100, "TWD", "TWD", original);

    private static ArApAllocationRecord A(long id, long principal, decimal amount,
        ArApAllocationKind kind = ArApAllocationKind.Settlement) =>
        new(id, Company, principal, kind, new DateOnly(2026, 10, 2), ArApSourceState.Posted, 100, "TWD", amount, 9000 + id);

    private static ArApDetailReport ArReport(IReadOnlyList<ArApPrincipalRecord> principals,
        IReadOnlyList<ArApAllocationRecord>? allocations = null, long? partner = null,
        ArApDirection direction = ArApDirection.Receivable, bool includeZero = true)
    {
        var r = ArApDetailAsOfRules.Read(
            new ArApDetailScope(Company, direction, partner, AsOf, Cutoff, "TWD", 2, includeZero),
            principals, allocations ?? []);
        Assert.True(r.Status == ArApDetailStatus.Complete, string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    private static ArApDetailReport FiveAr() => ArReport([
        P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4),
    ], [A(1, 2, 50m), A(2, 3, 25m, ArApAllocationKind.Credit)]);

    private static OtherIncomeExpenseSourceRecord S(long id, decimal gross,
        OtherIncomeExpenseKind kind = OtherIncomeExpenseKind.OtherCollection, int minute = 0, string label = "利息收入") =>
        new(id, Company, kind, "OC-" + id, 40 + id, label, "7101", "利息收入科目", 8000 + id,
            new DateOnly(2026, 10, 1), T0.AddMinutes(minute), OtherIncomeExpenseSourceState.Posted, 100, "TWD", gross);

    private static OtherIncomeExpenseReport OtherReport(IReadOnlyList<OtherIncomeExpenseSourceRecord> sources,
        OtherIncomeExpenseKind? kind = null)
    {
        var r = OtherIncomeExpenseRules.Read(new OtherIncomeExpenseScope(Company, From, Through, Cutoff, "TWD", 2, kind),
            sources);
        Assert.True(r.Status == OtherIncomeExpenseStatus.Complete,
            string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    private static OtherIncomeExpenseReport ThreeOther() => OtherReport([
        S(1, 1000m), S(2, 250.5m, OtherIncomeExpenseKind.OtherPayment, 1), S(3, 10m, minute: 2),
    ]);

    private static FiReportManifest ArManifest(ArApDetailReport? report = null, FiReportCursorBinding? binding = null)
    {
        var r = FiReportManifestRules.CreateArAp(binding ?? ArBinding(), report ?? FiveAr(), Created, Ttl);
        Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
        return r.Manifest!;
    }

    private static FiReportManifest OtherManifest(OtherIncomeExpenseReport? report = null,
        FiReportCursorBinding? binding = null)
    {
        var r = FiReportManifestRules.CreateOtherIncomeExpense(binding ?? OtherBinding(), report ?? ThreeOther(),
            Created, Ttl);
        Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
        return r.Manifest!;
    }

    private static void Has(IReadOnlyList<FiReportManifestFieldError> errors, string collection, string field,
        FiReportManifestError error) =>
        Assert.Contains(new FiReportManifestFieldError(collection, field, error), errors);

    // ---- Normal ----

    [Fact]
    public void Ar_manifest_binds_cutoff_ordered_members_digest_totals_and_expiry()
    {
        var report = FiveAr();
        var m = ArManifest(report);

        Assert.Equal(ArBinding(), m.Binding);
        Assert.Equal(Cutoff, m.CutoffSequence);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], m.MemberIds);
        Assert.Equal(report.Totals, m.ArApTotals);
        Assert.Null(m.OtherTotals);
        Assert.Equal(64, m.ContentDigest.Length);
        Assert.All(m.ContentDigest, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        Assert.Equal((Created, Created.AddMinutes(15)), (m.CreatedAt, m.ExpiresAt));
    }

    [Fact]
    public void Other_manifest_binds_members_in_report_order_and_signed_totals()
    {
        var report = ThreeOther();
        var m = OtherManifest(report);

        Assert.Equal([1L, 2L, 3L], m.MemberIds);
        Assert.Equal(report.Totals, m.OtherTotals);
        Assert.Equal(new OtherIncomeExpenseTotals(1010m, 250.5m, 759.5m, 3, 2, 1), m.OtherTotals);
        Assert.Null(m.ArApTotals);
        Assert.Equal(OtherBinding(), m.Binding);
    }

    [Fact]
    public void Ap_manifest_uses_payable_report()
    {
        var report = ArReport([P(1, 100m, direction: ArApDirection.Payable)], direction: ArApDirection.Payable);
        var r = FiReportManifestRules.CreateArAp(ArBinding() with { Kind = FiReportKind.ApDetail }, report, Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Ok, r.Status);
    }

    [Fact]
    public void Same_report_gives_same_digest_and_any_row_change_changes_it()
    {
        var a = ArManifest();
        Assert.Equal(a.ContentDigest, ArManifest().ContentDigest);

        var changedLabel = ArReport([
            P(1, 100m, label: "客戶乙"), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4),
        ], [A(1, 2, 50m), A(2, 3, 25m, ArApAllocationKind.Credit)]);
        Assert.NotEqual(a.ContentDigest, ArManifest(changedLabel).ContentDigest);

        var changedAllocation = ArReport([
            P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4),
        ], [A(1, 2, 50m), A(3, 3, 25m, ArApAllocationKind.Credit)]);
        Assert.NotEqual(a.ContentDigest, ArManifest(changedAllocation).ContentDigest);
    }

    [Fact]
    public void Digest_is_independent_of_decimal_scale_and_offset_of_the_same_instant()
    {
        var a = ArManifest(ArReport([P(1, 100m)]));
        var report = ArReport([P(1, 100.00m)]);
        var row = report.Rows[0] with { PostingTime = report.Rows[0].PostingTime.ToOffset(TimeSpan.Zero) };
        var b = ArManifest(report with { Rows = [row] });
        Assert.Equal(a.ContentDigest, b.ContentDigest);
    }

    [Fact]
    public void Text_field_boundaries_are_unambiguous_in_digest()
    {
        var a = OtherManifest(OtherReport([S(1, 10m, label: "ab")]));
        var report = OtherReport([S(1, 10m, label: "ab")]);
        var shifted = report.Rows[0] with { CategoryLabel = "a", AccountCode = "b7101" };
        var b = FiReportManifestRules.CreateOtherIncomeExpense(OtherBinding(), report with { Rows = [shifted] },
            Created, Ttl);
        Assert.NotEqual(a.ContentDigest, b.Manifest!.ContentDigest);
    }

    [Fact]
    public void Ar_and_other_reports_do_not_share_digest_domain()
    {
        var ar = ArManifest(ArReport([]));
        var other = OtherManifest(OtherReport([]));
        Assert.NotEqual(ar.ContentDigest, other.ContentDigest);
    }

    // ---- Paging: no duplicate or missing row across boundaries (FI-RPT-006) ----

    [Fact]
    public void Pages_from_reread_cover_every_member_once_in_order_with_full_totals()
    {
        var m = ArManifest();
        var seen = new List<long>();
        for (var page = 1; page <= 3; page++)
        {
            var r = FiReportManifestRules.PageArAp(m, FiveAr(), page);
            Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
            Assert.Equal(m.ArApTotals, r.Page!.Totals);
            Assert.Equal((page, 2, Cutoff), (r.Page.PageNumber, r.Page.PageSize, r.Page.CutoffSequence));
            Assert.Equal(page < 3, r.Page.HasNext);
            seen.AddRange(r.Page.Rows.Select(x => x.PrincipalId));
        }

        Assert.Equal(m.MemberIds, seen);
    }

    [Fact]
    public void Other_pages_cover_every_member_once()
    {
        var m = OtherManifest();
        var p1 = FiReportManifestRules.PageOtherIncomeExpense(m, ThreeOther(), 1);
        var p2 = FiReportManifestRules.PageOtherIncomeExpense(m, ThreeOther(), 2);
        Assert.Equal(FiReportManifestStatus.Ok, p1.Status);
        Assert.Equal(FiReportManifestStatus.Ok, p2.Status);
        Assert.Equal([1L, 2L, 3L], p1.Page!.Rows.Concat(p2.Page!.Rows).Select(x => x.SourceId));
        Assert.Equal((3, true, false), (p1.Page.TotalRows, p1.Page.HasNext, p2.Page.HasNext));
        Assert.Equal(m.OtherTotals, p2.Page.Totals);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(FiReportCursorTokenRules.MaxPageNumber)]
    public void Page_past_end_is_empty_without_next(int page)
    {
        var r = FiReportManifestRules.PageArAp(ArManifest(), FiveAr(), page);
        Assert.Equal(FiReportManifestStatus.Ok, r.Status);
        Assert.Empty(r.Page!.Rows);
        Assert.False(r.Page.HasNext);
        Assert.Equal(5, r.Page.Totals.RowCount);
    }

    [Fact]
    public void Empty_report_manifest_pages_to_empty_first_page()
    {
        var m = ArManifest(ArReport([]));
        Assert.Empty(m.MemberIds);
        var r = FiReportManifestRules.PageArAp(m, ArReport([]), 1);
        Assert.Equal(FiReportManifestStatus.Ok, r.Status);
        Assert.Empty(r.Page!.Rows);
        Assert.Equal(0, r.Page.Totals.RowCount);
    }

    [Fact]
    public void Page_size_comes_from_manifest_binding()
    {
        var m = ArManifest(binding: ArBinding(pageSize: 200));
        var r = FiReportManifestRules.PageArAp(m, FiveAr(), 1);
        Assert.Equal(5, r.Page!.Rows.Count);
        Assert.False(r.Page.HasNext);
    }

    // ---- Conflict: reread differs from manifest → Inconsistent, never a partial page (FI-RPT-005) ----

    [Fact]
    public void Backdated_source_appearing_in_reread_is_inconsistent_not_shifted_page()
    {
        var m = ArManifest();
        var withLate = ArReport([
            P(1, 100m), P(2, 200m, 1), P(9, 1m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4),
        ], [A(1, 2, 50m), A(2, 3, 25m, ArApAllocationKind.Credit)]);
        var r = FiReportManifestRules.PageArAp(m, withLate, 2);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Assert.Null(r.Page);
        Has(r.Errors, "reread", "memberIds", FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Missing_member_in_reread_is_inconsistent()
    {
        var m = ArManifest();
        var fewer = ArReport([P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3)],
            [A(1, 2, 50m), A(2, 3, 25m, ArApAllocationKind.Credit)]);
        var r = FiReportManifestRules.PageArAp(m, fewer, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Assert.Null(r.Page);
    }

    [Fact]
    public void Same_members_with_changed_content_is_digest_and_totals_inconsistent()
    {
        var m = ArManifest();
        var changed = ArReport([
            P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4),
        ], [A(1, 2, 60m), A(2, 3, 25m, ArApAllocationKind.Credit)]);
        var r = FiReportManifestRules.PageArAp(m, changed, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", "contentDigest", FiReportManifestError.Inconsistent);
        Has(r.Errors, "reread", "totals", FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Reordered_reread_is_inconsistent()
    {
        var m = OtherManifest();
        var report = ThreeOther();
        var swapped = report with { Rows = [report.Rows[1], report.Rows[0], report.Rows[2]] };
        var r = FiReportManifestRules.PageOtherIncomeExpense(m, swapped, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", "memberIds", FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Reread_totals_not_matching_its_rows_is_inconsistent()
    {
        var m = OtherManifest();
        var report = ThreeOther();
        var r = FiReportManifestRules.PageOtherIncomeExpense(m,
            report with { Totals = report.Totals with { Net = 1m } }, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", "totals", FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Ar_row_with_wrong_open_is_inconsistent()
    {
        var m = ArManifest();
        var report = FiveAr();
        var bad = report.Rows[0] with { OpenBase = 99m };
        var r = FiReportManifestRules.PageArAp(m, report with { Rows = [bad, .. report.Rows.Skip(1)] }, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", "rows", FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Ar_row_with_overflowing_amounts_is_inconsistent_not_an_exception()
    {
        var report = FiveAr();
        var bad = report.Rows[0] with { OriginalBase = decimal.MaxValue, CreditBase = -1m, OpenBase = 1m };
        var r = FiReportManifestRules.CreateArAp(ArBinding(), report with { Rows = [bad, .. report.Rows.Skip(1)] },
            Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "report", "rows", FiReportManifestError.Inconsistent);
    }

    public static TheoryData<string, ArApDetailReport> ArHeaderChanges()
    {
        var r = FiveAr();
        return new()
        {
            { "companyId", r with { CompanyId = 8 } },
            { "cutoffSequence", r with { CutoffSequence = Cutoff + 1 } },
            { "asOf", r with { AsOf = AsOf.AddDays(-1) } },
            { "partnerId", r with { PartnerId = Partner } },
            { "direction", r with { Direction = ArApDirection.Payable } },
            { "currency", r with { BaseCurrency = "USD" } },
        };
    }

    [Theory]
    [MemberData(nameof(ArHeaderChanges))]
    public void Reread_with_different_scope_is_inconsistent(string field, ArApDetailReport reread)
    {
        var r = FiReportManifestRules.PageArAp(ArManifest(), reread, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", field, FiReportManifestError.Inconsistent);
    }

    public static TheoryData<string, OtherIncomeExpenseReport> OtherHeaderChanges()
    {
        var r = ThreeOther();
        return new()
        {
            { "from", r with { From = From.AddDays(1) } },
            { "through", r with { Through = Through.AddDays(-1) } },
            { "kindFilter", r with { KindFilter = OtherIncomeExpenseKind.OtherCollection } },
            { "cutoffSequence", r with { CutoffSequence = 1 } },
        };
    }

    [Theory]
    [MemberData(nameof(OtherHeaderChanges))]
    public void Other_reread_with_different_scope_is_inconsistent(string field, OtherIncomeExpenseReport reread)
    {
        var r = FiReportManifestRules.PageOtherIncomeExpense(OtherManifest(), reread, 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", field, FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Ar_manifest_cannot_page_other_report_or_vice_versa()
    {
        var a = FiReportManifestRules.PageOtherIncomeExpense(ArManifest(), ThreeOther(), 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, a.Status);
        Has(a.Errors, "manifest", "kind", FiReportManifestError.Inconsistent);
        var b = FiReportManifestRules.PageArAp(OtherManifest(), FiveAr(), 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, b.Status);
        Has(b.Errors, "manifest", "kind", FiReportManifestError.Inconsistent);
    }

    // ---- Corrupt stored manifest → Inconsistent ----

    public static TheoryData<string, FiReportManifest> CorruptManifests()
    {
        var m = ArManifest();
        return new()
        {
            { "contentDigest", m with { ContentDigest = new string('A', 64) } },
            { "contentDigest", m with { ContentDigest = "abc" } },
            { "memberIds", m with { MemberIds = [1L, 2L, 3L, 4L] } },
            { "memberIds", m with { MemberIds = [1L, 2L, 2L, 4L, 5L] } },
            { "memberIds", m with { MemberIds = [0L, 2L, 3L, 4L, 5L] } },
            { "totals", m with { ArApTotals = null } },
            { "totals", m with { OtherTotals = new OtherIncomeExpenseTotals(0, 0, 0, 0, 0, 0) } },
            { "cutoffSequence", m with { CutoffSequence = 0 } },
            { "expiresAt", m with { ExpiresAt = m.CreatedAt } },
            { "expiresAt", m with { ExpiresAt = m.CreatedAt.AddMinutes(61) } },
            { "createdAt", m with { CreatedAt = default } },
            { "binding", m with { Binding = m.Binding with { PageSize = 0 } } },
        };
    }

    [Theory]
    [MemberData(nameof(CorruptManifests))]
    public void Corrupt_manifest_is_inconsistent(string field, FiReportManifest manifest)
    {
        var r = FiReportManifestRules.PageArAp(manifest, FiveAr(), 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Assert.Null(r.Page);
        Has(r.Errors, "manifest", field, FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Tampered_digest_with_valid_format_is_inconsistent_against_reread()
    {
        var m = ArManifest() with { ContentDigest = new string('0', 64) };
        var r = FiReportManifestRules.PageArAp(m, FiveAr(), 1);
        Assert.Equal(FiReportManifestStatus.Inconsistent, r.Status);
        Has(r.Errors, "reread", "contentDigest", FiReportManifestError.Inconsistent);
    }

    // ---- Request errors on paging → Rejected (400) ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FiReportCursorTokenRules.MaxPageNumber + 1)]
    public void Page_number_out_of_range_is_rejected(int page)
    {
        var r = FiReportManifestRules.PageArAp(ArManifest(), FiveAr(), page);
        Assert.Equal(FiReportManifestStatus.Rejected, r.Status);
        Has(r.Errors, "request", "pageNumber", FiReportManifestError.OutOfRange);
    }

    [Fact]
    public void Missing_manifest_or_reread_is_rejected()
    {
        var a = FiReportManifestRules.PageArAp(null, FiveAr(), 1);
        Assert.Equal(FiReportManifestStatus.Rejected, a.Status);
        Has(a.Errors, "request", "manifest", FiReportManifestError.Required);
        var b = FiReportManifestRules.PageOtherIncomeExpense(OtherManifest(), null, 1);
        Assert.Equal(FiReportManifestStatus.Rejected, b.Status);
        Has(b.Errors, "request", "reread", FiReportManifestError.Required);
    }

    // ---- Create: boundaries ----

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void Ttl_boundaries_are_accepted(int minutes)
    {
        var r = FiReportManifestRules.CreateArAp(ArBinding(), FiveAr(), Created, TimeSpan.FromMinutes(minutes));
        Assert.Equal(FiReportManifestStatus.Ok, r.Status);
        Assert.Equal(Created.AddMinutes(minutes), r.Manifest!.ExpiresAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59_999)]
    [InlineData(3_600_001)]
    [InlineData(-60_000)]
    public void Ttl_outside_one_to_sixty_minutes_is_rejected(int milliseconds)
    {
        var r = FiReportManifestRules.CreateArAp(ArBinding(), FiveAr(), Created, TimeSpan.FromMilliseconds(milliseconds));
        Assert.Equal(FiReportManifestStatus.Rejected, r.Status);
        Assert.Null(r.Manifest);
        Has(r.Errors, "request", "ttl", FiReportManifestError.OutOfRange);
    }

    [Fact]
    public void Default_or_unrepresentable_created_at_is_rejected()
    {
        var a = FiReportManifestRules.CreateArAp(ArBinding(), FiveAr(), default, Ttl);
        Has(a.Errors, "clock", "createdAt", FiReportManifestError.Required);
        var b = FiReportManifestRules.CreateArAp(ArBinding(), FiveAr(), DateTimeOffset.MaxValue, Ttl);
        Assert.Equal(FiReportManifestStatus.Rejected, b.Status);
        Has(b.Errors, "clock", "createdAt", FiReportManifestError.OutOfRange);
    }

    [Fact]
    public void Missing_report_or_binding_is_rejected()
    {
        var a = FiReportManifestRules.CreateArAp(ArBinding(), null, Created, Ttl);
        Has(a.Errors, "request", "report", FiReportManifestError.Required);
        var b = FiReportManifestRules.CreateOtherIncomeExpense(null, ThreeOther(), Created, Ttl);
        Has(b.Errors, "binding", "binding", FiReportManifestError.Required);
        Assert.Equal(FiReportManifestStatus.Rejected, b.Status);
    }

    [Fact]
    public void Invalid_binding_fields_are_rejected_with_field_names()
    {
        var r = FiReportManifestRules.CreateArAp(ArBinding() with { CompanyId = 0, Currency = "twd", From = From },
            FiveAr(), Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Rejected, r.Status);
        Has(r.Errors, "binding", "companyId", FiReportManifestError.OutOfRange);
        Has(r.Errors, "binding", "currency", FiReportManifestError.InvalidFormat);
        Has(r.Errors, "binding", "from", FiReportManifestError.NotAllowed);
    }

    [Fact]
    public void Binding_kind_must_match_report_type()
    {
        var a = FiReportManifestRules.CreateArAp(OtherBinding(), FiveAr(), Created, Ttl);
        Has(a.Errors, "binding", "kind", FiReportManifestError.NotAllowed);
        var b = FiReportManifestRules.CreateOtherIncomeExpense(ArBinding(), ThreeOther(), Created, Ttl);
        Has(b.Errors, "binding", "kind", FiReportManifestError.NotAllowed);
        Assert.Equal(FiReportManifestStatus.Rejected, b.Status);
    }

    [Fact]
    public void Ten_thousand_members_is_allowed_and_one_more_is_too_large()
    {
        var report = ArReport([P(1, 1m)]);
        var row = report.Rows[0];
        ArApDetailReport Of(int n)
        {
            var rows = Enumerable.Range(1, n).Select(i => row with { PrincipalId = i, SourceId = i }).ToList();
            return report with
            {
                Rows = rows,
                Totals = new ArApDetailTotals(n, 0, 0, n, n, n),
            };
        }

        var ok = FiReportManifestRules.CreateArAp(ArBinding(pageSize: 200), Of(FiReportManifestRules.MaxMembers),
            Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Ok, ok.Status);
        Assert.Equal(FiReportManifestRules.MaxMembers, ok.Manifest!.MemberIds.Count);

        var big = FiReportManifestRules.CreateArAp(ArBinding(), Of(FiReportManifestRules.MaxMembers + 1), Created, Ttl);
        Assert.Equal(FiReportManifestStatus.TooLarge, big.Status);
        Assert.Null(big.Manifest);
        Has(big.Errors, "report", "rows", FiReportManifestError.TooLarge);
    }

    [Fact]
    public void Request_error_takes_precedence_over_too_large()
    {
        var report = ArReport([P(1, 1m)]);
        var rows = Enumerable.Range(1, FiReportManifestRules.MaxMembers + 1)
            .Select(i => report.Rows[0] with { PrincipalId = i }).ToList();
        var r = FiReportManifestRules.CreateArAp(ArBinding(), report with { Rows = rows }, Created, TimeSpan.Zero);
        Assert.Equal(FiReportManifestStatus.Rejected, r.Status);
    }

    // ---- Create: report disagrees with binding or itself → Inconsistent ----

    [Fact]
    public void Report_not_matching_binding_is_inconsistent()
    {
        var a = FiReportManifestRules.CreateArAp(ArBinding(partner: 99), FiveAr(), Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Inconsistent, a.Status);
        Has(a.Errors, "report", "partnerId", FiReportManifestError.Inconsistent);

        var b = FiReportManifestRules.CreateArAp(ArBinding() with { Kind = FiReportKind.ApDetail }, FiveAr(), Created, Ttl);
        Has(b.Errors, "report", "direction", FiReportManifestError.Inconsistent);

        var c = FiReportManifestRules.CreateOtherIncomeExpense(
            OtherBinding(kind: OtherIncomeExpenseKind.OtherPayment), ThreeOther(), Created, Ttl);
        Has(c.Errors, "report", "kindFilter", FiReportManifestError.Inconsistent);

        var d = FiReportManifestRules.CreateOtherIncomeExpense(OtherBinding() with { Currency = "USD" }, ThreeOther(),
            Created, Ttl);
        Has(d.Errors, "report", "currency", FiReportManifestError.Inconsistent);
    }

    [Fact]
    public void Partner_filtered_report_with_matching_binding_is_ok()
    {
        var report = ArReport([P(1, 100m)], partner: Partner);
        var r = FiReportManifestRules.CreateArAp(ArBinding(partner: Partner), report, Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Ok, r.Status);
    }

    [Fact]
    public void Report_with_duplicate_members_or_wrong_totals_is_inconsistent()
    {
        var report = FiveAr();
        var dup = FiReportManifestRules.CreateArAp(ArBinding(),
            report with { Rows = [report.Rows[0], report.Rows[0], .. report.Rows.Skip(2)] }, Created, Ttl);
        Assert.Equal(FiReportManifestStatus.Inconsistent, dup.Status);
        Has(dup.Errors, "report", "memberIds", FiReportManifestError.Inconsistent);

        var totals = FiReportManifestRules.CreateArAp(ArBinding(),
            report with { Totals = report.Totals with { OutstandingCount = 0 } }, Created, Ttl);
        Has(totals.Errors, "report", "totals", FiReportManifestError.Inconsistent);

        var other = ThreeOther();
        var signed = other.Rows[1] with { SignedBase = 250.5m };
        var o = FiReportManifestRules.CreateOtherIncomeExpense(OtherBinding(),
            other with { Rows = [other.Rows[0], signed, other.Rows[2]] }, Created, Ttl);
        Has(o.Errors, "report", "rows", FiReportManifestError.Inconsistent);
        Assert.Null(o.Manifest);
    }

    [Fact]
    public void Manifest_carries_no_authority_beyond_binding_digest()
    {
        var props = typeof(FiReportManifest).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain(props, n => n.Contains("Permission", StringComparison.OrdinalIgnoreCase)
                                          || n.Contains("Role", StringComparison.OrdinalIgnoreCase));
    }
}
