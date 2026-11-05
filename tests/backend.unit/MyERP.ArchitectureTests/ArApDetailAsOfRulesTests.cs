using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-001／002／004／007／008／011（純規則部分）：FI AR/AP 明細 as-of 讀取。只納入截止點已提交 Posted 且業務日
/// ≤ asOf 的本金，扣除業務日 ≤ asOf 的已提交貸項／結算分配；openBase = original − credit − settlement；負餘額、
/// 來源／傳票缺漏、幣別不符與溢位整體不完整並列出 ID；(businessDate, postingTime, principalId) 排序，分頁與全集合
/// 合計使用同一集合（REQ_F4116.19.1～2、BR-FI-125～128、ADR-091）。不涉及 SQL 讀取、cutoff token、權限或畫面。
/// </summary>
public sealed class ArApDetailAsOfRulesTests
{
    private const long Company = 7, Cutoff = 900, Partner = 31;
    private static readonly DateOnly AsOf = new(2026, 10, 31);
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8));

    private static ArApDetailScope Scope(ArApDirection direction = ArApDirection.Receivable, long? partner = null,
        bool includeZero = false, DateOnly? asOf = null, int decimals = 2) =>
        new(Company, direction, partner, asOf ?? AsOf, Cutoff, "TWD", decimals, includeZero);

    private static ArApPrincipalRecord P(long id, decimal original, DateOnly? date = null, int minute = 0,
        ArApSourceState state = ArApSourceState.Posted, long partner = Partner,
        ArApDirection direction = ArApDirection.Receivable, string baseCurrency = "TWD", long? journal = -1,
        long sequence = 100, string reference = "INV-1") =>
        new(id, Company, direction, partner, "客戶甲", "SD", "Invoice", 5000 + id, reference,
            journal == -1 ? (state == ArApSourceState.Posted ? 8000 + id : null) : journal,
            date ?? new DateOnly(2026, 10, 1), T0.AddMinutes(minute), state, sequence, baseCurrency, "USD", original);

    private static ArApAllocationRecord A(long id, long principal, decimal amount,
        ArApAllocationKind kind = ArApAllocationKind.Settlement, DateOnly? date = null,
        ArApSourceState state = ArApSourceState.Posted, string baseCurrency = "TWD", long? journal = -1,
        long sequence = 200) =>
        new(id, Company, principal, kind, date ?? new DateOnly(2026, 10, 15), state, sequence, baseCurrency, amount,
            journal == -1 ? (state == ArApSourceState.Posted ? 9000 + id : null) : journal);

    private static ArApDetailResult Run(IReadOnlyList<ArApPrincipalRecord?>? principals,
        IReadOnlyList<ArApAllocationRecord?>? allocations, ArApDetailScope? scope = null) =>
        ArApDetailAsOfRules.Read(scope ?? Scope(), principals, allocations);

    private static ArApDetailReport Ok(IReadOnlyList<ArApPrincipalRecord> principals,
        IReadOnlyList<ArApAllocationRecord> allocations, ArApDetailScope? scope = null)
    {
        var r = Run(principals, allocations, scope);
        Assert.True(r.Status == ArApDetailStatus.Complete,
            string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    // ---- Normal ----

    [Fact]
    public void Receivable_with_dated_credit_and_collection_yields_exact_as_of_amounts_and_totals()
    {
        var report = Ok(
            [P(1, 1000m), P(2, 500m, minute: 5)],
            [A(10, 1, 100m, ArApAllocationKind.Credit), A(11, 1, 250.5m), A(12, 2, 500m)],
            Scope(includeZero: true));

        Assert.Equal([1L, 2L], report.Rows.Select(x => x.PrincipalId));
        var row = report.Rows[0];
        Assert.Equal((1000m, 100m, 250.5m, 649.5m), (row.OriginalBase, row.CreditBase, row.SettlementBase, row.OpenBase));
        Assert.Equal([10L, 11L], row.AllocationIds);
        Assert.Equal(2, row.AllocationCount);
        Assert.True(row.IsOutstanding);
        Assert.Equal(ArApDirection.Receivable, row.Direction);
        Assert.Equal((Partner, "客戶甲", "SD", "Invoice", 5001L, "INV-1", 8001L),
            (row.PartnerId, row.PartnerLabel, row.SourceModule, row.SourceType, row.SourceId, row.DocumentReference,
                row.JournalId));
        Assert.Equal(("TWD", "USD"), (row.BaseCurrency, row.DocumentCurrency));
        Assert.False(report.Rows[1].IsOutstanding);
        Assert.Equal(new ArApDetailTotals(1500m, 100m, 750.5m, 649.5m, 2, 1), report.Totals);
        Assert.Equal(report.Totals.Original - report.Totals.Credit - report.Totals.Settled, report.Totals.Open);
        Assert.Equal((Company, Cutoff, AsOf, "TWD"),
            (report.CompanyId, report.CutoffSequence, report.AsOf, report.BaseCurrency));
    }

    [Fact]
    public void Payable_direction_uses_the_same_formula()
    {
        var report = Ok(
            [P(1, 800m, direction: ArApDirection.Payable)],
            [A(10, 1, 50m, ArApAllocationKind.Credit), A(11, 1, 300m)],
            Scope(ArApDirection.Payable));
        Assert.Equal(ArApDirection.Payable, report.Direction);
        Assert.Equal(450m, report.Rows.Single().OpenBase);
    }

    [Fact]
    public void Rows_are_ordered_by_business_date_then_posting_time_then_principal_id()
    {
        var report = Ok(
            [
                P(5, 10m, new DateOnly(2026, 10, 2)), P(4, 10m, new DateOnly(2026, 10, 1), minute: 9),
                P(3, 10m, new DateOnly(2026, 10, 1), minute: 1), P(2, 10m, new DateOnly(2026, 10, 1), minute: 1),
                P(1, 10m, new DateOnly(2026, 9, 30), minute: 50)
            ], []);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], report.Rows.Select(x => x.PrincipalId));
    }

    // ---- As-of / state filtering (not errors) ----

    [Fact]
    public void Principal_after_as_of_and_non_posted_principals_are_excluded_and_counted()
    {
        var report = Ok(
            [
                P(1, 100m), P(2, 100m, new DateOnly(2026, 11, 1)), P(3, 100m, state: ArApSourceState.Draft),
                P(4, 100m, state: ArApSourceState.Pending), P(5, 100m, state: ArApSourceState.Rejected),
                P(6, 100m, state: ArApSourceState.RolledBack), P(7, 100m, state: ArApSourceState.Deleted)
            ], []);
        Assert.Equal([1L], report.Rows.Select(x => x.PrincipalId));
        Assert.Equal((5, 1), (report.ExcludedNotPostedCount, report.ExcludedAfterAsOfCount));
        Assert.Equal(100m, report.Totals.Original);
    }

    [Fact]
    public void Principal_dated_exactly_as_of_is_included_and_allocation_on_as_of_counts()
    {
        var report = Ok([P(1, 100m, AsOf)], [A(10, 1, 40m, date: AsOf), A(11, 1, 60m, date: AsOf.AddDays(1))]);
        var row = report.Rows.Single();
        Assert.Equal((40m, 60m), (row.SettlementBase, row.OpenBase));
        Assert.Equal([10L], row.AllocationIds);
        Assert.Equal(1, report.ExcludedAllocationCount);
    }

    [Fact]
    public void Non_posted_allocations_are_ignored_and_counted()
    {
        var report = Ok([P(1, 100m)],
            [
                A(10, 1, 10m, state: ArApSourceState.Draft), A(11, 1, 10m, state: ArApSourceState.Rejected),
                A(12, 1, 10m, state: ArApSourceState.RolledBack), A(13, 1, 10m, state: ArApSourceState.Deleted),
                A(14, 1, 10m, state: ArApSourceState.Pending)
            ]);
        Assert.Equal(100m, report.Rows.Single().OpenBase);
        Assert.Equal(5, report.ExcludedAllocationCount);
    }

    [Fact]
    public void Zero_open_rows_are_omitted_unless_history_is_requested_and_never_count_as_outstanding()
    {
        IReadOnlyList<ArApPrincipalRecord> ps = [P(1, 100m), P(2, 200m, minute: 1)];
        IReadOnlyList<ArApAllocationRecord> al = [A(10, 1, 100m)];

        var open = Ok(ps, al);
        Assert.Equal([2L], open.Rows.Select(x => x.PrincipalId));
        Assert.Equal(new ArApDetailTotals(200m, 0m, 0m, 200m, 1, 1), open.Totals);
        Assert.Equal(1, open.ExcludedZeroOpenCount);

        var history = Ok(ps, al, Scope(includeZero: true));
        Assert.Equal([1L, 2L], history.Rows.Select(x => x.PrincipalId));
        Assert.Equal(new ArApDetailTotals(300m, 0m, 100m, 200m, 2, 1), history.Totals);
        Assert.Equal(0, history.ExcludedZeroOpenCount);
    }

    [Fact]
    public void Empty_set_is_complete_with_zero_totals_and_cutoff()
    {
        var report = Ok([], []);
        Assert.Empty(report.Rows);
        Assert.Equal(new ArApDetailTotals(0m, 0m, 0m, 0m, 0, 0), report.Totals);
        Assert.Equal(Cutoff, report.CutoffSequence);
    }

    [Fact]
    public void All_sources_excluded_returns_exact_zero_counts_not_unqualified_success()
    {
        var report = Ok([P(1, 100m, state: ArApSourceState.Draft), P(2, 100m, new DateOnly(2026, 12, 1))], []);
        Assert.Empty(report.Rows);
        Assert.Equal((0, 1, 1), (report.Totals.RowCount, report.ExcludedNotPostedCount, report.ExcludedAfterAsOfCount));
    }

    [Fact]
    public void Leap_day_as_of_is_inclusive()
    {
        var leap = new DateOnly(2028, 2, 29);
        var report = Ok([P(1, 100m, leap), P(2, 100m, new DateOnly(2028, 3, 1))], [], Scope(asOf: leap));
        Assert.Equal([1L], report.Rows.Select(x => x.PrincipalId));
    }

    // ---- Incomplete (422) ----

    [Fact]
    public void Negative_open_is_incomplete_with_principal_and_source_ids()
    {
        var r = Run([P(1, 100m), P(2, 50m)], [A(10, 1, 60m, ArApAllocationKind.Credit), A(11, 1, 50m)]);
        Assert.Equal(ArApDetailStatus.Incomplete, r.Status);
        Assert.Null(r.Report);
        var item = Assert.Single(r.Incomplete);
        Assert.Equal((ArApIncompleteReason.NegativeOpen, 1L, 5001L, (long?)null),
            (item.Reason, item.PrincipalId, item.SourceId, item.AllocationId));
    }

    [Fact]
    public void Missing_journal_on_posted_principal_or_allocation_is_incomplete()
    {
        var r = Run([P(1, 100m, journal: null), P(2, 100m)], [A(10, 2, 10m, journal: null)]);
        Assert.Equal(ArApDetailStatus.Incomplete, r.Status);
        Assert.Equal(
            [(ArApIncompleteReason.MissingJournal, 1L, (long?)null), (ArApIncompleteReason.MissingJournal, 2L, 10L)],
            r.Incomplete.Select(x => (x.Reason, x.PrincipalId!.Value, x.AllocationId)));
    }

    [Fact]
    public void Allocation_on_unknown_principal_is_missing_principal()
    {
        var r = Run([P(1, 100m)], [A(10, 99, 10m)]);
        var item = Assert.Single(r.Incomplete);
        Assert.Equal((ArApIncompleteReason.MissingPrincipal, 99L, (long?)null, 10L),
            (item.Reason, item.PrincipalId!.Value, item.SourceId, item.AllocationId!.Value));
    }

    [Fact]
    public void Posted_allocation_on_non_posted_principal_is_incomplete()
    {
        var r = Run([P(1, 100m, state: ArApSourceState.RolledBack)], [A(10, 1, 10m)]);
        Assert.Equal(ArApIncompleteReason.AllocationOnNonPostedPrincipal, Assert.Single(r.Incomplete).Reason);
    }

    [Fact]
    public void Allocation_effective_before_its_principal_is_incomplete_even_when_principal_is_after_as_of()
    {
        var r = Run([P(1, 100m, new DateOnly(2026, 11, 5))], [A(10, 1, 10m, date: new DateOnly(2026, 10, 20))]);
        Assert.Equal(ArApIncompleteReason.AllocationBeforePrincipal, Assert.Single(r.Incomplete).Reason);

        var sameDay = Run([P(1, 100m, new DateOnly(2026, 10, 20))], [A(10, 1, 10m, date: new DateOnly(2026, 10, 20))]);
        Assert.Equal(ArApDetailStatus.Complete, sameDay.Status);
    }

    [Fact]
    public void Base_currency_mismatch_is_a_named_incomplete_result()
    {
        var r = Run([P(1, 100m, baseCurrency: "USD"), P(2, 100m)], [A(10, 2, 10m, baseCurrency: "HKD")]);
        Assert.Equal(
            [(ArApIncompleteReason.CurrencyMismatch, 1L, (long?)null), (ArApIncompleteReason.CurrencyMismatch, 2L, 10L)],
            r.Incomplete.Select(x => (x.Reason, x.PrincipalId!.Value, x.AllocationId)));
    }

    [Fact]
    public void Excluded_principals_and_later_allocations_do_not_make_the_read_incomplete()
    {
        var report = Ok(
            [P(1, 100m, state: ArApSourceState.Draft, journal: null, baseCurrency: "USD"), P(2, 100m)],
            [A(10, 2, 10m, date: new DateOnly(2026, 11, 2), journal: null, baseCurrency: "USD")]);
        Assert.Equal(100m, report.Rows.Single().OpenBase);
    }

    [Fact]
    public void Credit_and_settlement_sum_beyond_sql_amount_is_overflow()
    {
        const decimal max = 999999999999999.99m;
        var r = Run([P(1, max)], [A(10, 1, max, ArApAllocationKind.Credit), A(11, 1, max)]);
        Assert.Contains(r.Incomplete, x => x is { Reason: ArApIncompleteReason.Overflow, PrincipalId: 1 });
    }

    [Fact]
    public void Totals_beyond_sql_amount_are_overflow_without_ids()
    {
        const decimal max = 999999999999999.99m;
        var r = Run([P(1, max), P(2, max)], []);
        var item = Assert.Single(r.Incomplete);
        Assert.Equal((ArApIncompleteReason.Overflow, (long?)null), (item.Reason, item.PrincipalId));
    }

    [Fact]
    public void Several_incomplete_items_are_all_reported_in_principal_then_allocation_order()
    {
        var r = Run([P(2, 10m), P(1, 100m, journal: null)], [A(11, 2, 30m), A(10, 2, 5m, journal: null), A(12, 50, 1m)]);
        Assert.Equal(
            [
                (ArApIncompleteReason.MissingJournal, 1L, (long?)null),
                (ArApIncompleteReason.MissingJournal, 2L, 10L),
                (ArApIncompleteReason.NegativeOpen, 2L, null),
                (ArApIncompleteReason.MissingPrincipal, 50L, 12L)
            ],
            r.Incomplete.Select(x => (x.Reason, x.PrincipalId!.Value, x.AllocationId)));
    }

    // ---- Input errors (400) ----

    [Fact]
    public void Null_inputs_are_required()
    {
        var r = ArApDetailAsOfRules.Read(null, null, null);
        Assert.Equal(ArApDetailStatus.Invalid, r.Status);
        Assert.Equal([("scope", "scope"), ("request", "principals"), ("request", "allocations")],
            r.Errors.Select(e => (e.Collection, e.Field)));
    }

    [Fact]
    public void Scope_fields_are_validated()
    {
        var r = Run([], [], new ArApDetailScope(0, (ArApDirection)9, 0, default, 0, "twd", 5, false));
        Assert.Equal(
            [
                ("companyId", ArApDetailError.Required), ("direction", ArApDetailError.OutOfRange),
                ("partnerId", ArApDetailError.OutOfRange), ("asOf", ArApDetailError.Required),
                ("cutoffSequence", ArApDetailError.Required), ("baseCurrency", ArApDetailError.InvalidFormat),
                ("baseCurrencyDecimals", ArApDetailError.OutOfRange)
            ],
            r.Errors.Select(e => (e.Field, e.Error)));
    }

    [Fact]
    public void Principal_fields_are_validated_in_order()
    {
        var bad = new ArApPrincipalRecord(0, 0, (ArApDirection)5, 0, " ", "", null, 0, new string('R', 65), 0,
            default, default, (ArApSourceState)9, 0, "usd", "x", 0m);
        var r = Run([bad, null], []);
        Assert.Equal(
            [
                (0, "principalId", ArApDetailError.Required), (0, "companyId", ArApDetailError.Required),
                (0, "direction", ArApDetailError.OutOfRange), (0, "partnerId", ArApDetailError.Required),
                (0, "partnerLabel", ArApDetailError.Required), (0, "sourceModule", ArApDetailError.Required),
                (0, "sourceType", ArApDetailError.Required), (0, "sourceId", ArApDetailError.Required),
                (0, "documentReference", ArApDetailError.OutOfRange), (0, "journalId", ArApDetailError.Required),
                (0, "businessDate", ArApDetailError.Required), (0, "postingTime", ArApDetailError.Required),
                (0, "state", ArApDetailError.OutOfRange), (0, "commitSequence", ArApDetailError.Required),
                (0, "baseCurrency", ArApDetailError.InvalidFormat),
                (0, "documentCurrency", ArApDetailError.InvalidFormat),
                (0, "originalBase", ArApDetailError.OutOfRange), (1, "principal", ArApDetailError.Required)
            ],
            r.Errors.Select(e => (e.Index!.Value, e.Field, e.Error)));
        Assert.All(r.Errors, e => Assert.Equal("principals", e.Collection));
    }

    [Fact]
    public void Allocation_fields_are_validated_in_order()
    {
        var bad = new ArApAllocationRecord(0, 0, 0, (ArApAllocationKind)7, default, (ArApSourceState)9, 0, null, -1m, 0);
        var r = Run([], [bad, null]);
        Assert.Equal(
            [
                (0, "allocationId", ArApDetailError.Required), (0, "companyId", ArApDetailError.Required),
                (0, "principalId", ArApDetailError.Required), (0, "kind", ArApDetailError.OutOfRange),
                (0, "businessDate", ArApDetailError.Required), (0, "state", ArApDetailError.OutOfRange),
                (0, "commitSequence", ArApDetailError.Required), (0, "baseCurrency", ArApDetailError.InvalidFormat),
                (0, "amountBase", ArApDetailError.OutOfRange), (0, "journalId", ArApDetailError.Required),
                (1, "allocation", ArApDetailError.Required)
            ],
            r.Errors.Select(e => (e.Index!.Value, e.Field, e.Error)));
    }

    [Fact]
    public void Rows_committed_after_the_cutoff_are_input_errors_not_silently_dropped()
    {
        var r = Run([P(1, 100m, sequence: Cutoff + 1)], [A(10, 1, 1m, sequence: Cutoff + 1)]);
        Assert.Equal([("principals", "commitSequence"), ("allocations", "commitSequence")],
            r.Errors.Select(e => (e.Collection, e.Field)));
        Assert.All(r.Errors, e => Assert.Equal(ArApDetailError.AfterCutoff, e.Error));
        Assert.Equal(ArApDetailStatus.Complete, Run([P(1, 100m, sequence: Cutoff)], []).Status);
    }

    [Fact]
    public void Duplicate_ids_other_company_and_filter_mismatch_are_rejected()
    {
        var other = P(3, 10m) with { CompanyId = 8 };
        var r = Run(
            [P(1, 10m), P(1, 10m) with { SourceId = 7777 }, other, P(4, 10m, partner: 99), P(5, 10m, direction: ArApDirection.Payable)],
            [A(10, 1, 1m), A(10, 1, 1m), A(11, 1, 1m) with { CompanyId = 8 }],
            Scope(partner: Partner));
        Assert.Equal(
            [
                ("principals", 1, "principalId", ArApDetailError.Duplicate),
                ("principals", 2, "companyId", ArApDetailError.CrossCompany),
                ("principals", 3, "partnerId", ArApDetailError.FilterMismatch),
                ("principals", 4, "direction", ArApDetailError.FilterMismatch),
                ("allocations", 1, "allocationId", ArApDetailError.Duplicate),
                ("allocations", 2, "companyId", ArApDetailError.CrossCompany)
            ],
            r.Errors.Select(e => (e.Collection, e.Index!.Value, e.Field, e.Error)));
    }

    [Fact]
    public void Same_fi_source_on_two_principals_is_a_duplicate_not_a_second_row()
    {
        var replay = P(2, 10m) with { SourceId = 5001 };
        var r = Run([P(1, 10m), replay, P(3, 10m) with { SourceId = 5001, SourceType = "CreditNote" }], []);
        var e = Assert.Single(r.Errors);
        Assert.Equal((1, "sourceId", ArApDetailError.Duplicate), (e.Index!.Value, e.Field, e.Error));
    }

    [Fact]
    public void Negative_open_is_reported_even_when_zero_open_history_is_off_and_zero_rows_need_no_history()
    {
        var r = Run([P(1, 100m), P(2, 100m)], [A(10, 1, 100m), A(11, 2, 100.01m)]);
        Assert.Equal((ArApIncompleteReason.NegativeOpen, 2L),
            (Assert.Single(r.Incomplete).Reason, r.Incomplete[0].PrincipalId!.Value));
    }

    [Fact]
    public void Unfiltered_partner_scope_accepts_every_visible_partner()
    {
        var report = Ok([P(1, 10m, partner: 31), P(2, 20m, partner: 32, minute: 1)], []);
        Assert.Equal([31L, 32L], report.Rows.Select(x => x.PartnerId));
        Assert.Null(report.PartnerId);
    }

    [Fact]
    public void Amount_scale_and_sql_range_are_checked_against_base_decimals()
    {
        var r = Run([P(1, 10.005m), P(2, 1000000000000000m)], [A(10, 1, 0.001m), A(11, 1, 0m)]);
        Assert.Equal(
            [
                ("principals", "originalBase", ArApDetailError.InvalidScale),
                ("principals", "originalBase", ArApDetailError.OutOfRange),
                ("allocations", "amountBase", ArApDetailError.InvalidScale),
                ("allocations", "amountBase", ArApDetailError.OutOfRange)
            ],
            r.Errors.Select(e => (e.Collection, e.Field, e.Error)));

        var zeroDecimals = Run([P(1, 10.5m)], [], Scope(decimals: 0));
        Assert.Equal(ArApDetailError.InvalidScale, Assert.Single(zeroDecimals.Errors).Error);
    }

    [Fact]
    public void Input_errors_take_precedence_over_incomplete_reasons()
    {
        var r = Run([P(1, 10m, journal: null), P(2, 10m, sequence: 0)], []);
        Assert.Equal(ArApDetailStatus.Invalid, r.Status);
        Assert.Empty(r.Incomplete);
    }

    // ---- Paging over the same set ----

    [Fact]
    public void Pages_slice_the_same_rows_and_carry_full_set_totals()
    {
        var principals = Enumerable.Range(1, 5).Select(i => P(i, 10m * i, minute: i)).ToList();
        var report = Ok(principals, []);

        var pages = Enumerable.Range(1, 3).Select(n => ArApDetailAsOfRules.Page(report, n, 2).Page!).ToList();
        Assert.Equal(report.Rows.Select(x => x.PrincipalId), pages.SelectMany(p => p.Rows).Select(x => x.PrincipalId));
        Assert.Equal([true, true, false], pages.Select(p => p.HasNext));
        Assert.All(pages, p =>
        {
            Assert.Equal(report.Totals, p.Totals);
            Assert.Equal((Company, Cutoff, AsOf), (p.CompanyId, p.CutoffSequence, p.AsOf));
        });
        Assert.Equal(150m, pages[0].Totals.Open);
    }

    [Fact]
    public void Page_past_the_end_is_empty_and_large_page_numbers_do_not_overflow()
    {
        var report = Ok([P(1, 10m)], []);
        var page = ArApDetailAsOfRules.Page(report, int.MaxValue, ArApDetailAsOfRules.MaxPageSize).Page!;
        Assert.Empty(page.Rows);
        Assert.False(page.HasNext);
    }

    [Fact]
    public void Page_arguments_are_validated()
    {
        var r = ArApDetailAsOfRules.Page(null, 0, ArApDetailAsOfRules.MaxPageSize + 1);
        Assert.False(r.IsValid);
        Assert.Equal(["report", "pageNumber", "pageSize"], r.Errors.Select(e => e.Field));
        Assert.True(ArApDetailAsOfRules.Page(Ok([], []), 1, 1).IsValid);
    }

    // ---- Invariants ----

    [Fact]
    public void Splitting_allocations_differently_yields_the_same_open_balance_and_totals_identity()
    {
        var principals = Enumerable.Range(1, 40).Select(i => P(i, 1000m + i, minute: i)).ToList();
        var allocations = new List<ArApAllocationRecord>();
        long id = 100;
        foreach (var p in principals)
            for (var k = 0; k < (int)(p.PrincipalId % 4); k++)
                allocations.Add(A(++id, p.PrincipalId, 0.01m * (k + 1) + p.PrincipalId,
                    k % 2 == 0 ? ArApAllocationKind.Settlement : ArApAllocationKind.Credit));

        var report = Ok(principals, allocations, Scope(includeZero: true));
        foreach (var row in report.Rows)
        {
            var sum = allocations.Where(a => a.PrincipalId == row.PrincipalId).Sum(a => a.AmountBase);
            Assert.Equal(row.OriginalBase - sum, row.OpenBase);
            Assert.Equal(row.OriginalBase - row.CreditBase - row.SettlementBase, row.OpenBase);
        }

        var t = report.Totals;
        Assert.Equal(t.Original - t.Credit - t.Settled, t.Open);
        Assert.Equal(report.Rows.Sum(x => x.OpenBase), t.Open);
        Assert.Equal(allocations.Count, report.Rows.Sum(x => x.AllocationCount));
    }
}
