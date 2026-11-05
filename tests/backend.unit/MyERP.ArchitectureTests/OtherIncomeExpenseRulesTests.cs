using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-003／004／005／007／008／011／013／014（純規則部分）：FI 其他收支 Posted 來源讀取。只納入截止點已提交
/// Posted 的 OtherCollection／OtherPayment 且業務日在含首尾 from..through（≤ 366 天）內；收入為正、支出為負，
/// 合計為正收入、正支出與淨額 income − expense；(businessDate, postingTime, kind, sourceId) 排序，分頁與匯出同一
/// 集合；類別／科目快照或傳票缺漏、幣別不符、未定義的沖銷來源與溢位整體不完整並列來源 ID（REQ_F4116.19.3、
/// BR-FI-129～132、ADR-092）。不涉及 SQL 讀取、cutoff token、權限或畫面。
/// </summary>
public sealed class OtherIncomeExpenseRulesTests
{
    private const long Company = 7, Cutoff = 900;
    private static readonly DateOnly From = new(2026, 10, 1), Through = new(2026, 10, 31);
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8));

    private static OtherIncomeExpenseScope Scope(DateOnly? from = null, DateOnly? through = null,
        OtherIncomeExpenseKind? kind = null, int decimals = 2, string? currency = "TWD", long cutoff = Cutoff) =>
        new(Company, from ?? From, through ?? Through, cutoff, currency, decimals, kind);

    private static OtherIncomeExpenseSourceRecord S(long id, decimal gross,
        OtherIncomeExpenseKind kind = OtherIncomeExpenseKind.OtherCollection, DateOnly? date = null, int minute = 0,
        OtherIncomeExpenseSourceState state = OtherIncomeExpenseSourceState.Posted, string? currency = "TWD",
        long? journal = -1, long? category = -1, string? categoryLabel = "利息收入", string? accountCode = "7101",
        string? accountName = "利息收入科目", long sequence = 100, string? reference = "OC-1", long company = Company) =>
        new(id, company, kind, reference, category == -1 ? 40 + id : category, categoryLabel, accountCode, accountName,
            journal == -1 ? (state == OtherIncomeExpenseSourceState.Posted ? 8000 + id : null) : journal,
            date ?? new DateOnly(2026, 10, 1), T0.AddMinutes(minute), state, sequence, currency, gross);

    private static OtherIncomeExpenseResult Run(IReadOnlyList<OtherIncomeExpenseSourceRecord?>? sources,
        OtherIncomeExpenseScope? scope = null) => OtherIncomeExpenseRules.Read(scope ?? Scope(), sources);

    private static OtherIncomeExpenseReport Ok(IReadOnlyList<OtherIncomeExpenseSourceRecord> sources,
        OtherIncomeExpenseScope? scope = null)
    {
        var r = Run(sources, scope);
        Assert.True(r.Status == OtherIncomeExpenseStatus.Complete,
            string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    private static void HasError(OtherIncomeExpenseResult r, string collection, int? index, string field,
        OtherIncomeExpenseError error)
    {
        Assert.Equal(OtherIncomeExpenseStatus.Invalid, r.Status);
        Assert.Null(r.Report);
        Assert.Contains(new OtherIncomeExpenseFieldError(collection, index, field, error), r.Errors);
    }

    private static readonly string[] Headers =
        ["業務日", "過帳時間", "類型", "來源", "單號", "類別", "類別名稱", "科目", "科目名稱", "傳票", "幣別", "總額", "報表金額"];

    // ---- Normal (FI-RPT-003) ----

    [Fact]
    public void Collection_is_positive_income_and_payment_negative_expense_with_signed_net()
    {
        var report = Ok([
            S(1, 1000m),
            S(2, 250.5m, OtherIncomeExpenseKind.OtherPayment, minute: 5, categoryLabel: "雜項支出", accountCode: "7201",
                accountName: "雜項支出科目", reference: "OP-1"),
        ]);

        Assert.Equal([1L, 2L], report.Rows.Select(x => x.SourceId));
        var income = report.Rows[0];
        Assert.Equal((OtherIncomeExpenseKind.OtherCollection, 1000m, 1000m), (income.Kind, income.GrossBase, income.SignedBase));
        Assert.Equal((41L, "利息收入", "7101", "利息收入科目", 8001L, "OC-1", "TWD"),
            (income.CategoryId, income.CategoryLabel, income.AccountCode, income.AccountName, income.JournalId,
                income.DocumentReference, income.BaseCurrency));
        Assert.Equal((new DateOnly(2026, 10, 1), T0), (income.BusinessDate, income.PostingTime));
        var expense = report.Rows[1];
        Assert.Equal((OtherIncomeExpenseKind.OtherPayment, 250.5m, -250.5m), (expense.Kind, expense.GrossBase, expense.SignedBase));
        Assert.Equal(new OtherIncomeExpenseTotals(1000m, 250.5m, 749.5m, 2, 1, 1), report.Totals);
        Assert.Equal(report.Rows.Sum(x => x.SignedBase), report.Totals.Net);
        Assert.Equal((Company, Cutoff, From, Through, "TWD", (OtherIncomeExpenseKind?)null),
            (report.CompanyId, report.CutoffSequence, report.From, report.Through, report.BaseCurrency, report.KindFilter));
    }

    [Fact]
    public void Expense_greater_than_income_yields_negative_net()
    {
        var report = Ok([S(1, 100m), S(2, 300m, OtherIncomeExpenseKind.OtherPayment)]);
        Assert.Equal(new OtherIncomeExpenseTotals(100m, 300m, -200m, 2, 1, 1), report.Totals);
    }

    [Fact]
    public void Rows_are_ordered_by_business_date_posting_time_kind_and_source_id()
    {
        var d2 = new DateOnly(2026, 10, 2);
        var report = Ok([
            S(9, 1m, date: d2),
            S(5, 1m, OtherIncomeExpenseKind.OtherPayment, minute: 1),
            S(7, 1m, OtherIncomeExpenseKind.OtherPayment),
            S(6, 1m),
            S(3, 1m, minute: 1),
        ]);
        Assert.Equal([6L, 7L, 3L, 5L, 9L], report.Rows.Select(x => x.SourceId));
    }

    [Fact]
    public void Kind_filter_keeps_only_that_kind_and_its_totals()
    {
        var report = Ok([S(1, 80m, OtherIncomeExpenseKind.OtherPayment), S(2, 20m, OtherIncomeExpenseKind.OtherPayment)],
            Scope(kind: OtherIncomeExpenseKind.OtherPayment));
        Assert.Equal(new OtherIncomeExpenseTotals(0m, 100m, -100m, 2, 0, 2), report.Totals);
        Assert.Equal(OtherIncomeExpenseKind.OtherPayment, report.KindFilter);
    }

    [Fact]
    public void Source_of_other_kind_under_a_kind_filter_is_a_filter_mismatch()
    {
        var r = Run([S(1, 1m)], Scope(kind: OtherIncomeExpenseKind.OtherPayment));
        HasError(r, "sources", 0, "kind", OtherIncomeExpenseError.FilterMismatch);
    }

    // ---- Boundary (FI-RPT-004) ----

    [Fact]
    public void Empty_set_returns_a_valid_report_with_zero_totals()
    {
        var report = Ok([]);
        Assert.Empty(report.Rows);
        Assert.Equal(new OtherIncomeExpenseTotals(0m, 0m, 0m, 0, 0, 0), report.Totals);
        Assert.Equal((0, 0, 0), (report.ExcludedNotPostedCount, report.ExcludedOutOfRangeCount, report.ReplayedDuplicateCount));
    }

    [Fact]
    public void From_and_through_are_inclusive_and_days_outside_are_excluded_and_counted()
    {
        var report = Ok([
            S(1, 1m, date: From), S(2, 1m, date: Through),
            S(3, 1m, date: From.AddDays(-1)), S(4, 1m, date: Through.AddDays(1)),
        ]);
        Assert.Equal([1L, 2L], report.Rows.Select(x => x.SourceId));
        Assert.Equal(2, report.ExcludedOutOfRangeCount);
    }

    [Fact]
    public void Single_day_and_leap_day_ranges_are_valid()
    {
        var leap = new DateOnly(2028, 2, 29);
        var report = Ok([S(1, 5m, date: leap)], Scope(from: leap, through: leap));
        Assert.Single(report.Rows);
    }

    [Fact]
    public void Range_of_366_days_is_accepted_and_367_days_is_rejected()
    {
        var from = new DateOnly(2027, 3, 1);
        Assert.Equal(OtherIncomeExpenseStatus.Complete,
            Run([], Scope(from: from, through: from.AddDays(365))).Status);
        var r = Run([], Scope(from: from, through: from.AddDays(366)));
        HasError(r, "scope", null, "through", OtherIncomeExpenseError.RangeTooLong);
    }

    [Fact]
    public void Through_before_from_is_out_of_range()
    {
        HasError(Run([], Scope(from: Through, through: From)), "scope", null, "through",
            OtherIncomeExpenseError.OutOfRange);
    }

    [Fact]
    public void Range_across_year_end_is_accepted()
    {
        var report = Ok([S(1, 1m, date: new DateOnly(2026, 12, 31)), S(2, 2m, date: new DateOnly(2027, 1, 1))],
            Scope(from: new DateOnly(2026, 12, 1), through: new DateOnly(2027, 1, 31)));
        Assert.Equal(3m, report.Totals.Income);
    }

    [Fact]
    public void Amount_scale_beyond_base_decimals_and_non_positive_or_sql_overflow_gross_are_input_errors()
    {
        HasError(Run([S(1, 1.005m)]), "sources", 0, "grossBase", OtherIncomeExpenseError.InvalidScale);
        HasError(Run([S(1, 0m)]), "sources", 0, "grossBase", OtherIncomeExpenseError.OutOfRange);
        HasError(Run([S(1, -1m)]), "sources", 0, "grossBase", OtherIncomeExpenseError.OutOfRange);
        HasError(Run([S(1, 1000000000000000m)], Scope(decimals: 0)), "sources", 0, "grossBase",
            OtherIncomeExpenseError.OutOfRange);
        Assert.Equal(OtherIncomeExpenseStatus.Complete, Run([S(1, 1.0050m)], Scope(decimals: 4)).Status);
    }

    [Fact]
    public void Full_set_total_beyond_sql_decimal_is_incomplete_overflow()
    {
        var r = Run([S(1, 999999999999999m), S(2, 1m)], Scope(decimals: 0));
        Assert.Equal(OtherIncomeExpenseStatus.Incomplete, r.Status);
        Assert.Null(r.Report);
        Assert.Contains(new OtherIncomeExpenseIncompleteItem(OtherIncomeExpenseIncompleteReason.Overflow, null), r.Incomplete);
    }

    [Fact]
    public void Maximum_income_and_expense_net_to_zero_without_overflow()
    {
        var report = Ok([S(1, 999999999999999m), S(2, 999999999999999m, OtherIncomeExpenseKind.OtherPayment)],
            Scope(decimals: 0));
        Assert.Equal(0m, report.Totals.Net);
    }

    // ---- Duplicate / conflict (FI-RPT-005) ----

    [Fact]
    public void Identical_replayed_source_adds_no_line_and_is_counted()
    {
        var report = Ok([S(1, 10m), S(1, 10m), S(2, 5m)]);
        Assert.Equal([1L, 2L], report.Rows.Select(x => x.SourceId));
        Assert.Equal(15m, report.Totals.Income);
        Assert.Equal(1, report.ReplayedDuplicateCount);
    }

    [Fact]
    public void Same_source_id_with_different_content_is_a_duplicate_error()
    {
        HasError(Run([S(1, 10m), S(1, 11m)]), "sources", 1, "sourceId", OtherIncomeExpenseError.Duplicate);
    }

    [Fact]
    public void Source_committed_after_the_cutoff_is_an_input_error_never_silently_dropped()
    {
        HasError(Run([S(1, 1m, sequence: Cutoff + 1)]), "sources", 0, "commitSequence",
            OtherIncomeExpenseError.AfterCutoff);
        Assert.Equal(OtherIncomeExpenseStatus.Complete, Run([S(1, 1m, sequence: Cutoff)]).Status);
    }

    // ---- Partial result / incomplete (FI-RPT-007) ----

    [Fact]
    public void Missing_category_account_snapshot_or_journal_fails_the_whole_read_with_source_ids()
    {
        var r = Run([
            S(1, 1m),
            S(2, 1m, category: null),
            S(3, 1m, categoryLabel: " "),
            S(4, 1m, accountCode: null),
            S(5, 1m, accountName: ""),
            S(6, 1m, journal: null),
        ]);
        Assert.Equal(OtherIncomeExpenseStatus.Incomplete, r.Status);
        Assert.Null(r.Report);
        Assert.Equal(
        [
            new(OtherIncomeExpenseIncompleteReason.MissingCategorySnapshot, 2),
            new(OtherIncomeExpenseIncompleteReason.MissingCategorySnapshot, 3),
            new(OtherIncomeExpenseIncompleteReason.MissingAccountSnapshot, 4),
            new(OtherIncomeExpenseIncompleteReason.MissingAccountSnapshot, 5),
            new OtherIncomeExpenseIncompleteItem(OtherIncomeExpenseIncompleteReason.MissingJournal, 6),
        ], r.Incomplete);
    }

    [Fact]
    public void Incomplete_items_list_every_reason_ordered_by_source_then_reason_with_total_overflow_last()
    {
        var r = Run([
            S(9, 999999999999999m, journal: null),
            S(3, 1m, currency: "USD", accountName: null),
        ], Scope(decimals: 0));
        Assert.Equal(
        [
            new(OtherIncomeExpenseIncompleteReason.CurrencyMismatch, 3),
            new(OtherIncomeExpenseIncompleteReason.MissingAccountSnapshot, 3),
            new(OtherIncomeExpenseIncompleteReason.MissingJournal, 9),
            new OtherIncomeExpenseIncompleteItem(OtherIncomeExpenseIncompleteReason.Overflow, null),
        ], r.Incomplete);
    }

    [Fact]
    public void Base_currency_mismatch_is_incomplete()
    {
        var r = Run([S(1, 1m, currency: "USD")]);
        Assert.Equal([new OtherIncomeExpenseIncompleteItem(OtherIncomeExpenseIncompleteReason.CurrencyMismatch, 1)],
            r.Incomplete);
    }

    [Theory]
    [InlineData(OtherIncomeExpenseKind.Reversal)]
    [InlineData(OtherIncomeExpenseKind.Compensation)]
    public void Posted_reversal_or_compensation_in_range_is_an_unsupported_source_not_a_guess(
        OtherIncomeExpenseKind kind)
    {
        var r = Run([S(1, 1m), S(2, 1m, kind)]);
        Assert.Equal([new OtherIncomeExpenseIncompleteItem(OtherIncomeExpenseIncompleteReason.UnsupportedSourceType, 2)],
            r.Incomplete);
        // Also under a kind filter: its effect on that kind is unknown.
        r = Run([S(2, 1m, kind)], Scope(kind: OtherIncomeExpenseKind.OtherCollection));
        Assert.Equal(OtherIncomeExpenseStatus.Incomplete, r.Status);
    }

    [Fact]
    public void Unsupported_source_outside_range_or_not_posted_is_excluded_not_incomplete()
    {
        var report = Ok([
            S(1, 1m, OtherIncomeExpenseKind.Reversal, date: Through.AddDays(1)),
            S(2, 1m, OtherIncomeExpenseKind.Compensation, state: OtherIncomeExpenseSourceState.Draft),
        ]);
        Assert.Empty(report.Rows);
        Assert.Equal((1, 1), (report.ExcludedNotPostedCount, report.ExcludedOutOfRangeCount));
    }

    [Fact]
    public void Excluded_history_with_missing_snapshots_never_makes_the_read_incomplete()
    {
        var report = Ok([
            S(1, 1m, state: OtherIncomeExpenseSourceState.Rejected, category: null, accountCode: null),
            S(2, 1m, date: From.AddDays(-1), journal: null, currency: "USD"),
        ]);
        Assert.Empty(report.Rows);
    }

    // ---- State / all excluded (FI-RPT-008／011) ----

    [Theory]
    [InlineData(OtherIncomeExpenseSourceState.Draft)]
    [InlineData(OtherIncomeExpenseSourceState.Pending)]
    [InlineData(OtherIncomeExpenseSourceState.Rejected)]
    [InlineData(OtherIncomeExpenseSourceState.RolledBack)]
    [InlineData(OtherIncomeExpenseSourceState.Deleted)]
    public void Non_posted_sources_are_excluded_and_counted(OtherIncomeExpenseSourceState state)
    {
        var report = Ok([S(1, 1m, state: state), S(2, 3m)]);
        Assert.Equal([2L], report.Rows.Select(x => x.SourceId));
        Assert.Equal(1, report.ExcludedNotPostedCount);
        Assert.Equal(3m, report.Totals.Income);
    }

    [Fact]
    public void All_sources_excluded_returns_exact_zero_and_exclusion_counts()
    {
        var report = Ok([
            S(1, 1m, state: OtherIncomeExpenseSourceState.Pending),
            S(2, 1m, OtherIncomeExpenseKind.OtherPayment, date: Through.AddDays(3)),
        ]);
        Assert.Empty(report.Rows);
        Assert.Equal(new OtherIncomeExpenseTotals(0m, 0m, 0m, 0, 0, 0), report.Totals);
        Assert.Equal((1, 1), (report.ExcludedNotPostedCount, report.ExcludedOutOfRangeCount));
    }

    // ---- Error / input validation (FI-RPT-013) ----

    [Fact]
    public void Null_scope_and_null_sources_are_required()
    {
        var r = OtherIncomeExpenseRules.Read(null, null);
        HasError(r, "scope", null, "scope", OtherIncomeExpenseError.Required);
        HasError(r, "request", null, "sources", OtherIncomeExpenseError.Required);
    }

    [Fact]
    public void Invalid_scope_fields_are_reported_together()
    {
        var r = Run([], new OtherIncomeExpenseScope(0, default, default, 0, "twd", 5, (OtherIncomeExpenseKind)99));
        HasError(r, "scope", null, "companyId", OtherIncomeExpenseError.Required);
        HasError(r, "scope", null, "from", OtherIncomeExpenseError.Required);
        HasError(r, "scope", null, "through", OtherIncomeExpenseError.Required);
        HasError(r, "scope", null, "cutoffSequence", OtherIncomeExpenseError.Required);
        HasError(r, "scope", null, "baseCurrency", OtherIncomeExpenseError.InvalidFormat);
        HasError(r, "scope", null, "baseCurrencyDecimals", OtherIncomeExpenseError.OutOfRange);
        HasError(r, "scope", null, "kindFilter", OtherIncomeExpenseError.OutOfRange);
    }

    [Theory]
    [InlineData(OtherIncomeExpenseKind.Reversal)]
    [InlineData(OtherIncomeExpenseKind.Compensation)]
    public void Kind_filter_must_be_collection_or_payment(OtherIncomeExpenseKind kind)
    {
        HasError(Run([], Scope(kind: kind)), "scope", null, "kindFilter", OtherIncomeExpenseError.OutOfRange);
    }

    [Fact]
    public void Invalid_source_fields_are_reported_by_index()
    {
        var bad = new OtherIncomeExpenseSourceRecord(0, 0, (OtherIncomeExpenseKind)99, " ", 0, new string('x', 201),
            new string('1', 33), new string('n', 201), 0, default, default, (OtherIncomeExpenseSourceState)99, 0, "TW",
            1m);
        var r = Run([S(1, 1m), bad, null, S(4, 1m, company: 8), S(5, 1m, reference: new string('r', 65))]);
        foreach (var (field, error) in new[]
                 {
                     ("sourceId", OtherIncomeExpenseError.Required), ("companyId", OtherIncomeExpenseError.Required),
                     ("kind", OtherIncomeExpenseError.OutOfRange), ("documentReference", OtherIncomeExpenseError.Required),
                     ("categoryId", OtherIncomeExpenseError.Required), ("categoryLabel", OtherIncomeExpenseError.OutOfRange),
                     ("accountCode", OtherIncomeExpenseError.OutOfRange), ("accountName", OtherIncomeExpenseError.OutOfRange),
                     ("journalId", OtherIncomeExpenseError.Required), ("businessDate", OtherIncomeExpenseError.Required),
                     ("postingTime", OtherIncomeExpenseError.Required), ("state", OtherIncomeExpenseError.OutOfRange),
                     ("commitSequence", OtherIncomeExpenseError.Required), ("baseCurrency", OtherIncomeExpenseError.InvalidFormat),
                 })
            HasError(r, "sources", 1, field, error);
        HasError(r, "sources", 2, "source", OtherIncomeExpenseError.Required);
        HasError(r, "sources", 3, "companyId", OtherIncomeExpenseError.CrossCompany);
        HasError(r, "sources", 4, "documentReference", OtherIncomeExpenseError.OutOfRange);
    }

    [Fact]
    public void Input_errors_take_precedence_over_incomplete_items()
    {
        var r = Run([S(1, 1m, journal: null), S(2, 0m)]);
        Assert.Equal(OtherIncomeExpenseStatus.Invalid, r.Status);
        Assert.Empty(r.Incomplete);
    }

    // ---- Paging (FI-RPT-006, U part) ----

    [Fact]
    public void Pages_cover_every_row_once_in_order_and_carry_full_set_totals()
    {
        var report = Ok(Enumerable.Range(1, 5).Select(i => S(i, i, minute: i)).ToList());
        var seen = new List<long>();
        for (var page = 1; page <= 3; page++)
        {
            var p = OtherIncomeExpenseRules.Page(report, page, 2);
            Assert.True(p.IsValid);
            seen.AddRange(p.Page!.Rows.Select(x => x.SourceId));
            Assert.Equal(report.Totals, p.Page.Totals);
            Assert.Equal((Company, Cutoff, From, Through, 5), (p.Page.CompanyId, p.Page.CutoffSequence, p.Page.From,
                p.Page.Through, p.Page.TotalRows));
            Assert.Equal(page < 3, p.Page.HasNext);
        }

        Assert.Equal([1L, 2L, 3L, 4L, 5L], seen);
    }

    [Fact]
    public void Page_past_end_is_empty_and_huge_page_number_does_not_overflow()
    {
        var report = Ok([S(1, 1m)]);
        var p = OtherIncomeExpenseRules.Page(report, int.MaxValue, OtherIncomeExpenseRules.MaxPageSize);
        Assert.True(p.IsValid);
        Assert.Empty(p.Page!.Rows);
        Assert.False(p.Page.HasNext);
    }

    [Fact]
    public void Page_arguments_are_validated()
    {
        var report = Ok([]);
        var p = OtherIncomeExpenseRules.Page(null, 0, OtherIncomeExpenseRules.MaxPageSize + 1);
        Assert.False(p.IsValid);
        Assert.Equal(3, p.Errors.Count);
        Assert.False(OtherIncomeExpenseRules.Page(report, 1, 0).IsValid);
        Assert.True(OtherIncomeExpenseRules.Page(report, 1, OtherIncomeExpenseRules.MaxPageSize).IsValid);
    }

    // ---- Export (FI-RPT-014, U part) ----

    [Fact]
    public void Export_renders_the_same_rows_in_report_order_with_totals_and_escaped_text()
    {
        var report = Ok([
            S(2, 30m, OtherIncomeExpenseKind.OtherPayment, minute: 1, categoryLabel: "=HYPERLINK(1)",
                reference: "OP,\"2\""),
            S(1, 100.25m),
        ]);
        var e = OtherIncomeExpenseRules.Export(report, Headers);
        Assert.True(e.IsValid);
        Assert.Equal(2, e.RowCount);
        var lines = e.Content!.Split("\r\n");
        Assert.Equal("CompanyId,7", lines[0]);
        Assert.Equal("CutoffSequence,900", lines[1]);
        Assert.Equal("From,2026-10-01", lines[2]);
        Assert.Equal("Through,2026-10-31", lines[3]);
        Assert.Equal(string.Join(',', Headers), lines[4]);
        Assert.Equal("2026-10-01,2026-10-01T09:00:00.0000000+08:00,OtherCollection,1,OC-1,41,利息收入,7101,利息收入科目,8001,TWD,100.25,100.25",
            lines[5]);
        Assert.Equal("2026-10-01,2026-10-01T09:01:00.0000000+08:00,OtherPayment,2,\"OP,\"\"2\"\"\",42,'=HYPERLINK(1),7101,利息收入科目,8002,TWD,30,-30",
            lines[6]);
        Assert.Equal("Income,100.25", lines[7]);
        Assert.Equal("Expense,30", lines[8]);
        Assert.Equal("Net,70.25", lines[9]);
        Assert.Equal(10, lines.Length);
    }

    [Fact]
    public void Export_of_an_empty_report_has_header_and_zero_totals()
    {
        var e = OtherIncomeExpenseRules.Export(Ok([]), Headers);
        Assert.True(e.IsValid);
        Assert.Equal(0, e.RowCount);
        var lines = e.Content!.Split("\r\n");
        Assert.Equal(["Income,0", "Expense,0", "Net,0"], lines[^3..]);
        Assert.Equal(8, lines.Length);
    }

    [Fact]
    public void Export_rejects_missing_report_or_wrong_headers_without_a_partial_file()
    {
        var report = Ok([S(1, 1m)]);
        var e = OtherIncomeExpenseRules.Export(null, null);
        Assert.False(e.IsValid);
        Assert.Null(e.Content);
        Assert.Equal(2, e.Errors.Count);
        Assert.False(OtherIncomeExpenseRules.Export(report, Headers[..^1]).IsValid);
        var withNull = Headers.Cast<string?>().ToArray();
        withNull[3] = null;
        Assert.Contains(new OtherIncomeExpenseFieldError("headers", 3, "header", OtherIncomeExpenseError.Required),
            OtherIncomeExpenseRules.Export(report, withNull).Errors);
    }

    [Fact]
    public void Export_and_page_reject_a_report_whose_totals_do_not_match_its_rows()
    {
        var report = Ok([S(1, 10m), S(2, 4m, OtherIncomeExpenseKind.OtherPayment)]);
        var tampered = report with { Totals = report.Totals with { Net = 14m } };
        var e = OtherIncomeExpenseRules.Export(tampered, Headers);
        Assert.False(e.IsValid);
        Assert.Contains(new OtherIncomeExpenseFieldError("report", null, "totals", OtherIncomeExpenseError.Inconsistent),
            e.Errors);
        Assert.False(OtherIncomeExpenseRules.Page(tampered, 1, 10).IsValid);
        var badRow = report with { Rows = [report.Rows[0] with { SignedBase = -10m }, report.Rows[1]] };
        Assert.False(OtherIncomeExpenseRules.Export(badRow, Headers).IsValid);
    }

    [Fact]
    public void Export_over_the_row_limit_is_rejected_whole()
    {
        var rows = Enumerable.Range(1, OtherIncomeExpenseRules.MaxExportRows + 1)
            .Select(i => S(i, 1m, date: From.AddDays(i % 28), minute: i % 60)).ToList();
        var report = Ok(rows);
        var e = OtherIncomeExpenseRules.Export(report, Headers);
        Assert.False(e.IsValid);
        Assert.Contains(new OtherIncomeExpenseFieldError("report", null, "rows", OtherIncomeExpenseError.OutOfRange),
            e.Errors);
    }
}
