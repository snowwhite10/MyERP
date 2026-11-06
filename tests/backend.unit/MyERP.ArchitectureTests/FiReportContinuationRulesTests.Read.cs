using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-005／006（純規則部分）：FI 報表單一入口分派（WU-BI-001a10、ADR-100）。端點只呼叫一次
/// <c>ReadArAp／ReadOtherIncomeExpense</c>：token 為 null → 首頁協調器；否則（含空白）→ 續頁協調器。證明首頁不呼叫
/// load／reread、續頁不呼叫 read／save，接線錯誤在任何 I/O 前就以 500 回報，且兩條路徑回應形狀一致。
/// </summary>
public sealed partial class FiReportContinuationRulesTests
{
    /// <summary>All four I/O callbacks in one spy, so each test can assert which side of the dispatch ran.</summary>
    private sealed class EntryStore(long? id = SavedId, Func<FiReportCursorBinding, ArApDetailReport?>? ar = null)
    {
        public List<FiReportCursorBinding> Reads { get; } = [];
        public List<FiReportManifest> Saved { get; } = [];
        public List<long> Loaded { get; } = [];
        public List<FiReportManifest> Reread { get; } = [];

        public ArApDetailReport? ReadAr(FiReportCursorBinding b)
        {
            Reads.Add(b);
            return ar is null ? FiveAr() : ar(b);
        }

        public OtherIncomeExpenseReport? ReadOther(FiReportCursorBinding b)
        {
            Reads.Add(b);
            return ThreeOther();
        }

        public long? Save(FiReportManifest m)
        {
            Saved.Add(m);
            return id;
        }

        public FiReportManifest? Load(long manifestId)
        {
            Loaded.Add(manifestId);
            return Saved.Count > 0 && manifestId == id ? Saved[^1] : null;
        }

        public ArApDetailReport? RereadAr(FiReportManifest m)
        {
            Reread.Add(m);
            return FiveAr();
        }

        public OtherIncomeExpenseReport? RereadOther(FiReportManifest m)
        {
            Reread.Add(m);
            return ThreeOther();
        }

        public int Calls => Reads.Count + Saved.Count + Loaded.Count + Reread.Count;
    }

    private static FiReportContinuationResult<ArApDetailPage> EntryAr(FiReportContinuationRequest? request,
        EntryStore store, DateTimeOffset? now = null, FiReportActorScope? scope = null) =>
        FiReportContinuationRules.ReadArAp(request, scope ?? Scope(), Key, now ?? Created, Ttl, store.ReadAr,
            store.Save, store.Load, store.RereadAr);

    private static FiReportContinuationResult<OtherIncomeExpensePage> EntryOther(FiReportContinuationRequest request,
        EntryStore store) =>
        FiReportContinuationRules.ReadOtherIncomeExpense(request, Scope(), Key, Created, Ttl, store.ReadOther,
            store.Save, store.Load, store.RereadOther);

    // ---- Normal ----

    [Fact]
    public void Entry_without_token_runs_the_first_page_and_never_loads_or_rereads()
    {
        var store = new EntryStore();
        var request = ArRequest(null);

        var r = EntryAr(request, store);
        var direct = FirstAr(request, new FirstStore());

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(direct.Page!.Rows.Select(x => x.PrincipalId), r.Page!.Rows.Select(x => x.PrincipalId));
        Assert.Equal(direct.Page.Totals, r.Page.Totals);
        Assert.Equal(direct.NextToken, r.NextToken);
        Assert.Single(store.Reads);
        Assert.Single(store.Saved);
        Assert.Empty(store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Entry_with_token_runs_the_continuation_and_never_reads_or_saves()
    {
        var (request, continuation, manifest) = ArSetup();
        var store = new EntryStore(ManifestId);
        store.Saved.Add(manifest);

        var r = EntryAr(request, store, Now);
        var direct = ContinueAr(request, continuation);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(2, r.Page!.PageNumber);
        Assert.Equal(direct.Page!.Rows.Select(x => x.PrincipalId), r.Page.Rows.Select(x => x.PrincipalId));
        Assert.Equal(direct.Page.Totals, r.Page.Totals);
        Assert.Equal(direct.NextToken, r.NextToken);
        Assert.Empty(store.Reads);
        Assert.Single(store.Saved); // only the pre-seeded manifest; nothing new saved
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Single(store.Reread);
    }

    [Fact]
    public void Entry_walks_every_ar_page_through_one_call_site()
    {
        var store = new EntryStore();
        var request = ArRequest(null);
        var seen = new List<long>();
        string? token = null;
        var pages = 0;

        do
        {
            var r = EntryAr(request with { Token = token }, store, pages == 0 ? Created : Now);
            Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
            Assert.Equal(pages + 1, r.Page!.PageNumber);
            seen.AddRange(r.Page.Rows.Select(x => x.PrincipalId));
            token = r.NextToken;
            pages++;
        } while (token is not null);

        Assert.Equal(3, pages);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], seen);
        Assert.Single(store.Reads);
        Assert.Single(store.Saved);
        Assert.Equal([SavedId, SavedId], store.Loaded);
        Assert.Equal(2, store.Reread.Count);
    }

    [Fact]
    public void Entry_walks_every_other_income_page_through_one_call_site()
    {
        var store = new EntryStore();
        var request = OtherRequest(null);

        var first = EntryOther(request, store);
        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.Equal([1L, 2L], first.Page!.Rows.Select(x => x.SourceId));

        var last = EntryOther(request with { Token = first.NextToken }, store);
        Assert.True(last.IsSuccess, last.Problem?.ErrorCode);
        Assert.Equal([3L], last.Page!.Rows.Select(x => x.SourceId));
        Assert.Null(last.NextToken);
        Assert.Single(store.Reads);
        Assert.Single(store.Saved);
        Assert.Equal([SavedId], store.Loaded);
        Assert.Single(store.Reread);
    }

    [Fact]
    public void Entry_single_page_report_has_no_token_and_is_not_continued()
    {
        var store = new EntryStore();

        var r = EntryAr(ArRequest(null, pageSize: 5), store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Null(r.NextToken);
        Assert.Empty(store.Loaded);
    }

    // ---- Dispatch boundary: blank is a token, not "first page" ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_token_goes_to_continuation_and_is_400_token_required_with_no_io(string token)
    {
        var store = new EntryStore();

        var r = EntryAr(ArRequest(token), store);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Equal(["Required"], r.Problem!.FieldErrors["token"]);
        Assert.Equal(0, store.Calls);
    }

    // ---- Errors from each side keep their mapping ----

    [Fact]
    public void First_page_filter_error_is_400_and_nothing_is_read_or_saved()
    {
        var store = new EntryStore();

        var r = EntryAr(ArRequest(null, pageSize: 0), store);

        AssertProblem(r, 400, FiReportProblemRules.ValidationFailed);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public void Continuation_after_manifest_expiry_is_409_and_nothing_is_reread()
    {
        var store = new EntryStore();
        var request = ArRequest(null);
        var first = EntryAr(request, store);
        Assert.NotNull(first.NextToken);

        var r = EntryAr(request with { Token = first.NextToken }, store, Created + Ttl);

        AssertProblem(r, 409, FiReportProblemRules.ManifestExpired);
        Assert.Equal([SavedId], store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Save_failure_on_first_page_is_500_with_no_page_and_no_continuation_io()
    {
        var store = new EntryStore(id: null);

        var r = EntryAr(ArRequest(null), store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Single(store.Saved);
        Assert.Empty(store.Loaded);
        Assert.Empty(store.Reread);
    }

    // ---- Wiring faults: 500 before any I/O, on both paths ----

    [Theory]
    [InlineData(null, "read")]
    [InlineData(null, "saveManifest")]
    [InlineData(null, "loadManifest")]
    [InlineData(null, "reread")]
    [InlineData("token", "read")]
    [InlineData("token", "saveManifest")]
    [InlineData("token", "loadManifest")]
    [InlineData("token", "reread")]
    public void Any_missing_callback_is_500_on_either_path_and_nothing_is_called(string? tokenMarker, string missing)
    {
        var (continued, _, manifest) = ArSetup();
        var request = tokenMarker is null ? ArRequest(null) : continued;
        var store = new EntryStore(ManifestId);
        store.Saved.Add(manifest);

        var r = FiReportContinuationRules.ReadArAp(request, Scope(), Key, Now, Ttl,
            missing == "read" ? null : store.ReadAr,
            missing == "saveManifest" ? null : store.Save,
            missing == "loadManifest" ? null : store.Load,
            missing == "reread" ? null : store.RereadAr);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal([$"read.{missing}:Required"], r.Problem!.InternalDetails);
        Assert.Empty(store.Reads);
        Assert.Single(store.Saved);
        Assert.Empty(store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Null_request_actor_and_callbacks_are_all_listed()
    {
        var r = FiReportContinuationRules.ReadOtherIncomeExpense(null, null, Key, Created, Ttl, null, null, null, null);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["read.request:Required", "read.actor:Required", "read.read:Required",
            "read.saveManifest:Required", "read.loadManifest:Required", "read.reread:Required"],
            r.Problem!.InternalDetails);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wrong_report_family_is_500_on_either_path_with_no_io(bool withToken)
    {
        var store = new EntryStore();
        var request = ArRequest(withToken ? "not-a-real-token" : null);

        var r = FiReportContinuationRules.ReadOtherIncomeExpense(request, Scope(), Key, Created, Ttl, store.ReadOther,
            store.Save, store.Load, store.RereadOther);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(0, store.Calls);
    }
}
