using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-005／006（純規則部分）：FI 報表首頁純協調器（WU-BI-001a9、ADR-099）。無 token 的首頁請求：重算 actor scope 摘要 →
/// 綁定驗證（用戶端欄位 400）→ 凍結讀取 → ADR-094 建立 manifest（超量 422、不一致 500）→ 保存取得 manifest ID（失敗 500、
/// 不回頁）→ 第 1 頁 → 有下一頁才簽首 token；首 token 可直接餵續頁協調器走完全部頁。
/// </summary>
public sealed partial class FiReportContinuationRulesTests
{
    private const long SavedId = 88;

    /// <summary>Records every read／save so tests can prove nothing is read or saved before validation passes.</summary>
    private sealed class FirstStore(Func<FiReportCursorBinding, ArApDetailReport?>? ar = null,
        Func<FiReportCursorBinding, OtherIncomeExpenseReport?>? other = null, long? id = SavedId)
    {
        public List<FiReportCursorBinding> Reads { get; } = [];
        public List<FiReportManifest> Saved { get; } = [];

        public ArApDetailReport? ReadAr(FiReportCursorBinding b)
        {
            Reads.Add(b);
            return ar is null ? FiveAr() : ar(b);
        }

        public OtherIncomeExpenseReport? ReadOther(FiReportCursorBinding b)
        {
            Reads.Add(b);
            return other is null ? ThreeOther() : other(b);
        }

        public long? Save(FiReportManifest m)
        {
            Saved.Add(m);
            return id;
        }

        /// <summary>Continuation-side load: returns the saved manifest only for the ID the save returned.</summary>
        public FiReportManifest? Load(long manifestId) =>
            Saved.Count > 0 && manifestId == id ? Saved[^1] : null;
    }

    private static FiReportContinuationResult<ArApDetailPage> FirstAr(FiReportContinuationRequest request,
        FirstStore store, FiReportActorScope? scope = null, byte[]? key = null, DateTimeOffset? now = null,
        TimeSpan? ttl = null) =>
        FiReportContinuationRules.FirstArAp(request, scope ?? Scope(), key ?? Key, now ?? Created, ttl ?? Ttl,
            store.ReadAr, store.Save);

    private static FiReportContinuationResult<OtherIncomeExpensePage> FirstOther(FiReportContinuationRequest request,
        FirstStore store) =>
        FiReportContinuationRules.FirstOtherIncomeExpense(request, Scope(), Key, Created, Ttl, store.ReadOther,
            store.Save);

    // ---- Normal ----

    [Fact]
    public void First_ar_page_saves_a_manifest_and_returns_page_one_with_a_token_for_page_two()
    {
        var store = new FirstStore();
        var request = ArRequest(null);

        var r = FirstAr(request, store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(1, r.Page!.PageNumber);
        Assert.Equal([1L, 2L], r.Page.Rows.Select(x => x.PrincipalId));
        var expectedBinding = Bind(request, DigestOf(FiReportKind.ArDetail));
        Assert.Equal(expectedBinding, Assert.Single(store.Reads));
        var saved = Assert.Single(store.Saved);
        Assert.Equal(expectedBinding, saved.Binding);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], saved.MemberIds);
        Assert.Equal(Created, saved.CreatedAt);
        Assert.Equal(Created + Ttl, saved.ExpiresAt);
        Assert.Equal(saved.ArApTotals, r.Page.Totals);

        var next = FiReportCursorTokenRules.Validate(r.NextToken, expectedBinding, Key, Created, saved.ExpiresAt);
        Assert.Equal(FiReportCursorValidationStatus.Valid, next.Status);
        Assert.Equal(new FiReportCursorPosition(Cutoff, SavedId, 2), next.Position);
    }

    [Fact]
    public void First_token_walks_every_continuation_and_covers_all_members_once()
    {
        var store = new FirstStore();
        var request = ArRequest(null);
        var first = FirstAr(request, store);
        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        var seen = first.Page!.Rows.Select(x => x.PrincipalId).ToList();
        var token = first.NextToken;
        var pages = 1;
        while (token is not null)
        {
            var r = FiReportContinuationRules.ContinueArAp(request with { Token = token }, Scope(), Key, Now,
                store.Load, _ => FiveAr());
            Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
            Assert.Equal(first.Page.Totals, r.Page!.Totals);
            seen.AddRange(r.Page.Rows.Select(x => x.PrincipalId));
            token = r.NextToken;
            pages++;
        }

        Assert.Equal(3, pages);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], seen);
    }

    [Fact]
    public void Single_page_report_still_saves_the_manifest_and_issues_no_token()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null, pageSize: 5), store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(5, r.Page!.Rows.Count);
        Assert.False(r.Page.HasNext);
        Assert.Null(r.NextToken);
        Assert.Single(store.Saved);
    }

    [Fact]
    public void Empty_report_returns_an_empty_first_page_with_zero_totals_and_no_token()
    {
        var store = new FirstStore(_ => ArReport([]));

        var r = FirstAr(ArRequest(null), store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Empty(r.Page!.Rows);
        Assert.Equal(0, r.Page.Totals.RowCount);
        Assert.Null(r.NextToken);
        Assert.Empty(Assert.Single(store.Saved).MemberIds);
    }

    [Fact]
    public void First_ap_page_uses_the_payable_direction()
    {
        var report = ArReport([P(1, 10m, direction: ArApDirection.Payable), P(2, 20m, 1, ArApDirection.Payable),
            P(3, 30m, 2, ArApDirection.Payable)], ArApDirection.Payable);
        var store = new FirstStore(_ => report);

        var r = FirstAr(ArRequest(null, kind: FiReportKind.ApDetail), store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal([1L, 2L], r.Page!.Rows.Select(x => x.PrincipalId));
        Assert.NotNull(r.NextToken);
    }

    [Fact]
    public void First_other_income_page_issues_a_token_that_continues_to_the_last_row()
    {
        var store = new FirstStore();
        var request = OtherRequest(null);

        var first = FirstOther(request, store);

        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.Equal([1L, 2L], first.Page!.Rows.Select(x => x.SourceId));
        Assert.Equal(Assert.Single(store.Saved).OtherTotals, first.Page.Totals);

        var next = FiReportContinuationRules.ContinueOtherIncomeExpense(request with { Token = first.NextToken },
            Scope(), Key, Now, store.Load, _ => ThreeOther());
        Assert.True(next.IsSuccess, next.Problem?.ErrorCode);
        Assert.Equal([3L], next.Page!.Rows.Select(x => x.SourceId));
        Assert.Null(next.NextToken);
    }

    [Fact]
    public void Ttl_bounds_one_and_sixty_minutes_are_accepted()
    {
        Assert.True(FirstAr(ArRequest(null), new FirstStore(), ttl: TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.True(FirstAr(ArRequest(null), new FirstStore(), ttl: TimeSpan.FromMinutes(60)).IsSuccess);
    }

    // ---- Client filter errors: 400, nothing read or saved ----

    [Fact]
    public void Invalid_client_filters_are_400_validation_failed_and_nothing_is_read()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null) with { PageSize = 201, Currency = "tw", PartnerId = 0 }, store);

        AssertProblem(r, 400, FiReportProblemRules.ValidationFailed);
        Assert.Equal(["OutOfRange"], r.Problem!.FieldErrors["pageSize"]);
        Assert.Equal(["InvalidFormat"], r.Problem.FieldErrors["currency"]);
        Assert.Equal(["OutOfRange"], r.Problem.FieldErrors["partnerId"]);
        Assert.Empty(store.Reads);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Other_income_filter_on_ar_report_is_400_not_allowed()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null) with { From = From }, store);

        AssertProblem(r, 400, FiReportProblemRules.ValidationFailed);
        Assert.Equal(["NotAllowed"], r.Problem!.FieldErrors["from"]);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Other_income_through_before_from_is_400()
    {
        var store = new FirstStore();

        var r = FiReportContinuationRules.FirstOtherIncomeExpense(OtherRequest(null) with { Through = From.AddDays(-1) },
            Scope(), Key, Created, Ttl, store.ReadOther, store.Save);

        AssertProblem(r, 400, FiReportProblemRules.ValidationFailed);
        Assert.Equal(["OutOfRange"], r.Problem!.FieldErrors["through"]);
        Assert.Empty(store.Reads);
    }

    // ---- Partial result: too many rows → 422 ----

    [Fact]
    public void More_than_max_members_is_422_and_nothing_is_saved()
    {
        var principals = Enumerable.Range(1, FiReportManifestRules.MaxMembers + 1)
            .Select(i => P(i, 1m, i % 1000)).ToList();
        var big = ArReport(principals);
        var store = new FirstStore(_ => big);

        var r = FirstAr(ArRequest(null), store);

        AssertProblem(r, 422, FiReportProblemRules.TooLarge);
        Assert.Single(store.Reads);
        Assert.Empty(store.Saved);
    }

    // ---- Server faults: 500 without field detail, nothing read ----

    [Fact]
    public void Token_on_a_first_page_request_is_500_and_nothing_is_read()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest("abc"), store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("request.token:NotAllowed", r.Problem!.InternalDetails);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Short_key_is_500_before_any_read()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null), store, key: new byte[31]);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("key.key:KeyInvalid", r.Problem!.InternalDetails);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Default_clock_is_500_before_any_read()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null), store, now: default(DateTimeOffset));

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("clock.now:Required", r.Problem!.InternalDetails);
        Assert.Empty(store.Reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(3601)]
    public void Ttl_outside_one_to_sixty_minutes_is_500_before_any_read(int seconds)
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null), store, ttl: TimeSpan.FromSeconds(seconds));

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("request.ttl:OutOfRange", r.Problem!.InternalDetails);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Invalid_actor_scope_is_500_before_any_read()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null), store, new FiReportActorScope(0, [], null));

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("actorScope.actorUserId:OutOfRange", r.Problem!.InternalDetails);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Non_positive_company_is_500_before_any_read()
    {
        var store = new FirstStore();

        var r = FirstAr(ArRequest(null) with { CompanyId = 0 }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Wrong_report_family_or_undefined_kind_is_500_before_any_read()
    {
        var store = new FirstStore();

        AssertProblem(FirstAr(OtherRequest(null), store), 500, FiReportProblemRules.InternalError);
        AssertProblem(FirstOther(ArRequest(null), store), 500, FiReportProblemRules.InternalError);
        AssertProblem(FirstAr(ArRequest(null) with { Kind = (FiReportKind)99 }, store), 500,
            FiReportProblemRules.InternalError);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Null_inputs_are_500_and_nothing_is_read_or_saved()
    {
        var store = new FirstStore();
        var request = ArRequest(null);

        AssertProblem(FiReportContinuationRules.FirstArAp(null, Scope(), Key, Created, Ttl, store.ReadAr, store.Save),
            500, FiReportProblemRules.InternalError);
        AssertProblem(FiReportContinuationRules.FirstArAp(request, null, Key, Created, Ttl, store.ReadAr, store.Save),
            500, FiReportProblemRules.InternalError);
        var r = FiReportContinuationRules.FirstArAp(request, Scope(), Key, Created, Ttl, null, null);
        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["first.read:Required", "first.saveManifest:Required"], r.Problem!.InternalDetails);
        AssertProblem(FiReportContinuationRules.FirstOtherIncomeExpense(null, Scope(), Key, Created, Ttl,
            store.ReadOther, store.Save), 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Reads);
        Assert.Empty(store.Saved);
    }

    // ---- Read／save faults: 500, no page or token ----

    [Fact]
    public void Missing_read_result_is_500_and_nothing_is_saved()
    {
        var store = new FirstStore(_ => null);

        var r = FirstAr(ArRequest(null), store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("request.report:Required", r.Problem!.InternalDetails);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Read_for_another_scope_is_500_and_nothing_is_saved()
    {
        var store = new FirstStore(_ => FiveAr() with { AsOf = AsOf.AddDays(-1) });

        var r = FirstAr(ArRequest(null), store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("report.asOf:Inconsistent", r.Problem!.InternalDetails);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public void Read_with_wrong_totals_is_500_and_nothing_is_saved()
    {
        var good = FiveAr();
        var store = new FirstStore(_ => good with { Totals = good.Totals with { Open = good.Totals.Open + 1 } });

        var r = FirstAr(ArRequest(null), store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Saved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Save_without_a_positive_manifest_id_is_500_with_no_page_or_token(long? id)
    {
        var store = new FirstStore(id: id);

        var r = FirstAr(ArRequest(null), store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("save.manifestId:" + (id is null ? "Required" : "OutOfRange"), r.Problem!.InternalDetails);
        Assert.Single(store.Saved);
    }

    [Fact]
    public void Repeated_first_page_is_deterministic_for_the_same_saved_id()
    {
        var a = FirstAr(ArRequest(null), new FirstStore());
        var b = FirstAr(ArRequest(null), new FirstStore());

        Assert.Equal(a.NextToken, b.NextToken);
        Assert.Equal(a.Page!.Rows.Select(x => x.PrincipalId), b.Page!.Rows.Select(x => x.PrincipalId));
    }
}
