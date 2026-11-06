using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-014（純規則部分）：首頁僅供匯出的 token（WU-BI-001a12、ADR-102）。首頁成功一律帶 ExportToken（含單頁與空報表），
/// NextToken 規則不變；ExportToken 可交給 Export* 匯出已保存 manifest 的全部成員，交給 Continue* 則 400 且不載入；
/// 續頁結果與所有錯誤結果不帶 ExportToken。
/// </summary>
public sealed partial class FiReportContinuationRulesTests
{
    private static void AssertExportToken(string? token, FiReportCursorBinding binding, DateTimeOffset expiresAt)
    {
        var r = FiReportCursorTokenRules.Validate(token, binding, Key, Created, expiresAt, FiReportCursorPurpose.Export);
        Assert.Equal(FiReportCursorValidationStatus.Valid, r.Status);
        Assert.Equal(new FiReportCursorPosition(Cutoff, SavedId, 1), r.Position);
    }

    [Fact]
    public void Single_page_first_page_has_an_export_token_but_no_next_token()
    {
        var store = new FirstStore();
        var request = ArRequest(null, pageSize: 5);

        var r = FirstAr(request, store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Null(r.NextToken);
        var saved = Assert.Single(store.Saved);
        AssertExportToken(r.ExportToken, Bind(request, DigestOf(FiReportKind.ArDetail)), saved.ExpiresAt);
    }

    [Fact]
    public void Single_page_export_token_exports_all_five_rows_without_a_new_manifest()
    {
        var store = new FirstStore();
        var request = ArRequest(null, pageSize: 5);
        var first = FirstAr(request, store);
        var reread = new List<FiReportManifest>();

        var r = FiReportContinuationRules.ExportArAp(request with { Token = first.ExportToken }, Scope(), Key, Now,
            ArHeaders, store.Load, m =>
            {
                reread.Add(m);
                return FiveAr();
            });

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(5, r.RowCount);
        Assert.Equal(FiReportManifestRules.ExportArAp(store.Saved[0], FiveAr(), ArHeaders).Content, r.Content);
        Assert.Single(store.Saved);
        Assert.Single(store.Reads);
        Assert.Same(store.Saved[0], Assert.Single(reread));
    }

    [Fact]
    public void Multi_page_first_page_has_both_tokens_and_the_export_token_exports_every_member()
    {
        var store = new FirstStore();
        var request = ArRequest(null);

        var first = FirstAr(request, store);

        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.NotNull(first.NextToken);
        Assert.NotNull(first.ExportToken);
        Assert.NotEqual(first.NextToken, first.ExportToken);
        AssertExportToken(first.ExportToken, store.Saved[0].Binding, store.Saved[0].ExpiresAt);

        var r = FiReportContinuationRules.ExportArAp(request with { Token = first.ExportToken }, Scope(), Key, Now,
            ArHeaders, store.Load, _ => FiveAr());
        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(5, r.RowCount);
    }

    [Fact]
    public void Empty_report_still_gets_an_export_token_for_a_header_only_file()
    {
        var store = new FirstStore(_ => ArReport([]));
        var request = ArRequest(null);
        var first = FirstAr(request, store);

        var r = FiReportContinuationRules.ExportArAp(request with { Token = first.ExportToken }, Scope(), Key, Now,
            ArHeaders, store.Load, _ => ArReport([]));

        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.Null(first.NextToken);
        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(0, r.RowCount);
    }

    [Fact]
    public void Other_income_first_page_export_token_exports_every_member()
    {
        var store = new FirstStore();
        var request = OtherRequest(null, pageSize: 3);
        var first = FirstOther(request, store);

        var r = FiReportContinuationRules.ExportOtherIncomeExpense(request with { Token = first.ExportToken }, Scope(),
            Key, Now, OtherHeaders, store.Load, _ => ThreeOther());

        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.Null(first.NextToken);
        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal(3, r.RowCount);
    }

    [Fact]
    public void Single_entry_first_page_carries_the_export_token_and_continuations_do_not()
    {
        var entry = new EntryStore();
        var request = ArRequest(null);

        var first = EntryAr(request, entry);
        var direct = FirstAr(request, new FirstStore());
        var next = EntryAr(request with { Token = first.NextToken }, entry);

        Assert.True(first.IsSuccess, first.Problem?.ErrorCode);
        Assert.Equal(direct.ExportToken, first.ExportToken);
        Assert.NotNull(first.ExportToken);
        Assert.True(next.IsSuccess, next.Problem?.ErrorCode);
        Assert.Null(next.ExportToken);
    }

    [Fact]
    public void Export_token_given_to_continue_is_400_and_nothing_is_loaded_or_reread()
    {
        var store = new FirstStore();
        var request = ArRequest(null, pageSize: 5);
        var first = FirstAr(request, store);
        var loaded = new List<long>();
        var reread = new List<FiReportManifest>();

        var r = FiReportContinuationRules.ContinueArAp(request with { Token = first.ExportToken }, Scope(), Key, Now,
            id =>
            {
                loaded.Add(id);
                return store.Load(id);
            }, m =>
            {
                reread.Add(m);
                return FiveAr();
            });

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Equal(["Invalid"], r.Problem!.FieldErrors["token"]);
        Assert.Empty(loaded);
        Assert.Empty(reread);
    }

    [Fact]
    public void Export_token_through_the_single_entry_is_400_and_nothing_is_loaded()
    {
        var entry = new EntryStore();
        var request = ArRequest(null, pageSize: 5);
        var first = EntryAr(request, entry);

        var r = EntryAr(request with { Token = first.ExportToken }, entry);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(entry.Loaded);
        Assert.Single(entry.Saved);
    }

    [Fact]
    public void Export_token_with_a_changed_filter_or_scope_is_400_and_nothing_is_loaded()
    {
        var store = new Store(null);
        var request = ArRequest(null, pageSize: 5);
        var first = FirstAr(request, new FirstStore());
        var withToken = request with { Token = first.ExportToken };

        AssertExportProblem(ExportAr(withToken with { Currency = "USD" }, store), 400,
            FiReportProblemRules.TokenInvalid);
        AssertExportProblem(ExportAr(withToken, store, scope: Scope(roles: [3])), 400,
            FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Export_token_after_manifest_expiry_is_409_and_nothing_is_reread()
    {
        var first = new FirstStore();
        var request = ArRequest(null, pageSize: 5);
        var page = FirstAr(request, first);
        var store = new Store(first.Saved[0]);

        var r = ExportAr(request with { Token = page.ExportToken }, store, now: first.Saved[0].ExpiresAt);

        AssertExportProblem(r, 409, FiReportProblemRules.ManifestExpired);
        Assert.Equal([SavedId], store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Export_token_naming_another_cutoff_than_the_stored_manifest_is_500()
    {
        var first = new FirstStore();
        var request = ArRequest(null, pageSize: 5);
        var page = FirstAr(request, first);
        var store = new Store(first.Saved[0] with { CutoffSequence = Cutoff + 1 });

        var r = ExportAr(request with { Token = page.ExportToken }, store);

        AssertExportProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("manifest.cutoffSequence:Inconsistent", r.Problem!.InternalDetails);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Failed_first_page_has_no_export_token()
    {
        var r = FirstAr(ArRequest(null), new FirstStore(id: null));

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Null(r.ExportToken);
    }

    [Fact]
    public void Repeated_first_page_issues_the_same_export_token()
    {
        var a = FirstAr(ArRequest(null), new FirstStore());
        var b = FirstAr(ArRequest(null), new FirstStore());

        Assert.Equal(a.ExportToken, b.ExportToken);
    }
}
