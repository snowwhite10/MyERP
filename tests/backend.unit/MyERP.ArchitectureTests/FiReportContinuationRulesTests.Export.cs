using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-014（純規則部分）：FI 報表凍結匯出協調器（WU-BI-001a11、ADR-101）。匯出必帶首頁或續頁 token，沿用續頁的
/// 順序：接線檢查（含 headers）→ 重算摘要 → 不含過期的 token 驗證（失敗不載入）→ 載入 manifest 後驗過期（409）→
/// binding／cutoff 一致（否則 500、不重讀）→ 重讀 → ADR-095 匯出全部成員（不受 pageSize 影響；不一致 500、不回部分檔案）。
/// 不涉及 SQL、EXPORT 權限、API 或畫面。
/// </summary>
public sealed partial class FiReportContinuationRulesTests
{
    private static readonly string[] ArHeaders =
        Enumerable.Range(1, ArApDetailAsOfRules.ExportColumnCount).Select(i => "AR" + i).ToArray();

    private static readonly string[] OtherHeaders =
        Enumerable.Range(1, OtherIncomeExpenseRules.ExportColumnCount).Select(i => "OT" + i).ToArray();

    private static FiReportExportResult ExportAr(FiReportContinuationRequest? request, Store store,
        IReadOnlyList<string?>? headers = null, FiReportActorScope? scope = null, byte[]? key = null,
        DateTimeOffset? now = null) =>
        FiReportContinuationRules.ExportArAp(request, scope ?? Scope(), key ?? Key, now ?? Now, headers ?? ArHeaders,
            store.Load, store.ReadAr);

    private static (FiReportContinuationRequest Request, Store Store, FiReportManifest Manifest) OtherSetup(
        int page = 2, int pageSize = 2)
    {
        var probe = OtherRequest(null, pageSize);
        var binding = Bind(probe, DigestOf(FiReportKind.OtherIncomeExpense));
        var manifest = OtherManifest(binding);
        return (OtherRequest(Token(binding, page), pageSize), new Store(manifest), manifest);
    }

    private static void AssertExportProblem(FiReportExportResult r, int status, string code)
    {
        Assert.False(r.IsSuccess);
        Assert.Null(r.Content);
        Assert.Equal(0, r.RowCount);
        Assert.NotNull(r.Problem);
        Assert.Equal((status, code), (r.Problem!.Status, r.Problem.ErrorCode));
        if (status == 500) Assert.Empty(r.Problem.FieldErrors);
    }

    // ---- Normal ----

    [Fact]
    public void Export_with_page_two_token_returns_every_member_ignoring_page_size()
    {
        var (request, store, manifest) = ArSetup();

        var r = ExportAr(request, store);
        var direct = FiReportManifestRules.ExportArAp(manifest, FiveAr(), ArHeaders);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Null(r.Problem);
        Assert.Equal(5, r.RowCount);
        Assert.Equal(direct.Content, r.Content);
        Assert.All(Enumerable.Range(1, 5), i => Assert.Contains("INV-" + i, r.Content));
        Assert.Contains(string.Join(",", ArHeaders), r.Content);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Same(manifest, Assert.Single(store.Reread));
    }

    [Fact]
    public void Export_with_last_page_token_still_exports_every_member()
    {
        var (request, store, _) = ArSetup(page: 5, pageSize: 1);

        var r = ExportAr(request, store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(5, r.RowCount);
    }

    [Fact]
    public void Other_income_export_returns_every_member()
    {
        var (request, store, manifest) = OtherSetup();

        var r = FiReportContinuationRules.ExportOtherIncomeExpense(request, Scope(), Key, Now, OtherHeaders,
            store.Load, store.ReadOther);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(3, r.RowCount);
        Assert.Equal(FiReportManifestRules.ExportOtherIncomeExpense(manifest, ThreeOther(), OtherHeaders).Content,
            r.Content);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Single(store.Reread);
    }

    [Fact]
    public void Export_reuses_the_manifest_saved_by_the_single_entry_first_page()
    {
        var entry = new EntryStore();
        var first = EntryAr(ArRequest(null), entry);
        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.NotNull(first.NextToken);

        var r = FiReportContinuationRules.ExportArAp(ArRequest(first.NextToken), Scope(), Key, Now, ArHeaders,
            entry.Load, entry.RereadAr);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(5, r.RowCount);
        Assert.Single(entry.Reads);
        Assert.Single(entry.Saved); // export never creates a new manifest
        Assert.Equal([SavedId], entry.Loaded);
        Assert.Single(entry.Reread);
    }

    [Fact]
    public void Export_just_before_expiry_succeeds()
    {
        var (request, store, manifest) = ArSetup();

        var r = ExportAr(request, store, now: manifest.ExpiresAt.AddTicks(-1));

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
    }

    // ---- Expiry: loaded but never re-read ----

    [Fact]
    public void Export_at_expiry_is_409_and_nothing_is_reread()
    {
        var (request, store, manifest) = ArSetup();

        var r = ExportAr(request, store, now: manifest.ExpiresAt);

        AssertExportProblem(r, 409, FiReportProblemRules.ManifestExpired);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Export_of_gone_manifest_is_409_and_nothing_is_reread()
    {
        var (request, _, _) = ArSetup();
        var store = new Store(null);

        var r = ExportAr(request, store);

        AssertExportProblem(r, 409, FiReportProblemRules.ManifestExpired);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Empty(store.Reread);
    }

    // ---- Token and request errors: nothing is loaded ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    public void Export_without_a_valid_token_is_400_and_nothing_is_loaded(string? token)
    {
        var (request, store, _) = ArSetup();

        var r = ExportAr(request with { Token = token }, store);

        AssertExportProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Export_with_changed_filter_is_400_and_nothing_is_loaded()
    {
        var (request, store, _) = ArSetup();

        var r = ExportAr(request with { Currency = "USD" }, store);

        AssertExportProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Export_after_actor_scope_change_is_400_and_nothing_is_loaded()
    {
        var (request, store, _) = ArSetup();

        var r = ExportAr(request, store, scope: Scope(roles: [3]));

        AssertExportProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Export_with_a_different_key_is_400_and_nothing_is_loaded()
    {
        var (request, store, _) = ArSetup();

        var r = ExportAr(request, store, key: Enumerable.Range(2, 32).Select(i => (byte)i).ToArray());

        AssertExportProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
    }

    // ---- Server wiring faults: 500 before any I/O ----

    [Fact]
    public void Missing_request_actor_and_callbacks_are_listed_in_one_500()
    {
        var r = FiReportContinuationRules.ExportArAp(null, null, Key, Now, ArHeaders, null, null);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["export.request:Required", "export.actor:Required", "export.loadManifest:Required",
            "export.reread:Required"], r.Problem!.InternalDetails);
    }

    public static TheoryData<string[]?, string> BadHeaders => new()
    {
        { null, "export.headers:Required" },
        { ArHeaders[..^1], "export.headers:OutOfRange" },
        { [.. ArHeaders, "extra"], "export.headers:OutOfRange" },
    };

    [Theory]
    [MemberData(nameof(BadHeaders))]
    public void Bad_headers_are_500_before_any_load(string[]? headers, string detail)
    {
        var (request, store, _) = ArSetup();

        var r = FiReportContinuationRules.ExportArAp(request, Scope(), Key, Now, headers, store.Load, store.ReadAr);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal([detail], r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Null_header_entry_is_500_before_any_load()
    {
        var (request, store, _) = ArSetup();
        var headers = ArHeaders.Cast<string?>().ToArray();
        headers[3] = null;

        var r = ExportAr(request, store, headers);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["export.headers:Required"], r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Other_income_column_count_is_checked_against_its_own_export()
    {
        var (request, store, _) = OtherSetup();

        var r = FiReportContinuationRules.ExportOtherIncomeExpense(request, Scope(), Key, Now, ArHeaders, store.Load,
            store.ReadOther);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["export.headers:OutOfRange"], r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Wrong_report_family_is_500_before_any_load()
    {
        var (request, store, _) = OtherSetup();

        var r = ExportAr(request, store);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["request.kind:NotAllowed"], r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Invalid_actor_scope_is_500_before_any_load()
    {
        var (request, store, _) = ArSetup();

        var r = ExportAr(request, store, scope: Scope(actor: 0));

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Loaded);
    }

    // ---- Stored manifest or re-read disagreement: 500, never a partial file ----

    [Fact]
    public void Manifest_with_another_cutoff_is_500_and_nothing_is_reread()
    {
        var (request, _, manifest) = ArSetup();
        var store = new Store(manifest with { CutoffSequence = Cutoff + 1 });

        var r = ExportAr(request, store);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("manifest.cutoffSequence:Inconsistent", r.Problem!.InternalDetails);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Manifest_with_another_binding_is_500_and_nothing_is_reread()
    {
        var (request, _, manifest) = ArSetup();
        var store = new Store(manifest with { Binding = manifest.Binding with { PageSize = 3 } });

        var r = ExportAr(request, store);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("manifest.binding:Inconsistent", r.Problem!.InternalDetails);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Changed_reread_is_500_with_no_content()
    {
        var (request, _, manifest) = ArSetup();
        var store = new Store(manifest,
            arReread: _ => ArReport([P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 999m, 4)]));

        var r = ExportAr(request, store);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Single(store.Reread);
    }

    [Fact]
    public void Missing_reread_is_500_with_no_content()
    {
        var (request, _, manifest) = ArSetup();
        var store = new Store(manifest, arReread: _ => null);

        var r = ExportAr(request, store);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Single(store.Reread);
    }

    [Fact]
    public void Reread_exception_propagates_to_the_endpoint()
    {
        var (request, _, manifest) = ArSetup();
        var store = new Store(manifest, arReread: _ => throw new InvalidOperationException("db"));

        Assert.Throws<InvalidOperationException>(() => ExportAr(request, store));
    }
}
