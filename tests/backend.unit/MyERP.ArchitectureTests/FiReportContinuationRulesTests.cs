using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-006（純規則部分）：FI 報表續頁純協調器（WU-BI-001a8、ADR-098）。把 ADR-097 actor scope 摘要、ADR-093 token、
/// ADR-094 manifest 與 ADR-096 錯誤對應串成一次續頁：先重算摘要並驗證 token，通過後才載入 manifest、再檢查過期，
/// 之後才重讀並以 manifest 切頁；有下一頁才簽發下一頁 token。任何失敗只回 <see cref="FiReportProblem"/>，不回部分頁。
/// 不涉及 SQL manifest 表、權限檢查、API 或畫面。
/// </summary>
public sealed partial class FiReportContinuationRulesTests
{
    private const long Company = 7, Cutoff = 900, Partner = 31, Actor = 501, ManifestId = 55;
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly DateOnly AsOf = new(2026, 10, 31), From = new(2026, 10, 1), Through = new(2026, 10, 31);
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 8, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Now = Created.AddMinutes(5);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private static FiReportActorScope Scope(IReadOnlyCollection<long>? roles = null,
        FiReportPartnerScope? partners = null, long actor = Actor) =>
        new(actor, roles ?? [3, 9], partners ?? FiReportPartnerScope.Partners([Partner, 40]));

    private static string DigestOf(FiReportKind kind, FiReportActorScope? scope = null)
    {
        scope ??= Scope();
        var r = FiReportActorScopeRules.Digest(scope.ActorUserId, Company, kind, scope.RoleIds, scope.PartnerScope);
        Assert.True(r.IsValid, string.Join("; ", r.Errors));
        return r.Digest!;
    }

    private static FiReportContinuationRequest ArRequest(string? token, int pageSize = 2,
        FiReportKind kind = FiReportKind.ArDetail) =>
        new(token, Company, kind, null, AsOf, null, null, null, "TWD", pageSize);

    private static FiReportContinuationRequest OtherRequest(string? token, int pageSize = 2) =>
        new(token, Company, FiReportKind.OtherIncomeExpense, null, null, From, Through, null, "TWD", pageSize);

    private static FiReportCursorBinding Bind(FiReportContinuationRequest r, string digest) =>
        new(r.CompanyId, digest, r.Kind, r.PartnerId, r.AsOf, r.From, r.Through, r.KindFilter, r.Currency, r.PageSize);

    private static string Token(FiReportCursorBinding binding, int page = 2, long cutoff = Cutoff,
        long manifestId = ManifestId)
    {
        var r = FiReportCursorTokenRules.Issue(binding, new FiReportCursorPosition(cutoff, manifestId, page), Key);
        Assert.Equal(FiReportCursorIssueStatus.Issued, r.Status);
        return r.Token!;
    }

    private static ArApPrincipalRecord P(long id, decimal original, int minute = 0,
        ArApDirection direction = ArApDirection.Receivable) =>
        new(id, Company, direction, Partner, "客戶甲", "SD", "Invoice", 500 + id, "INV-" + id, 8000 + id,
            new DateOnly(2026, 10, 1), T0.AddMinutes(minute), ArApSourceState.Posted, 100, "TWD", "TWD", original);

    private static ArApDetailReport ArReport(IReadOnlyList<ArApPrincipalRecord> principals,
        ArApDirection direction = ArApDirection.Receivable)
    {
        var r = ArApDetailAsOfRules.Read(new ArApDetailScope(Company, direction, null, AsOf, Cutoff, "TWD", 2, true),
            principals, []);
        Assert.True(r.Status == ArApDetailStatus.Complete, string.Join("; ", r.Errors) + string.Join("; ", r.Incomplete));
        return r.Report!;
    }

    private static ArApDetailReport FiveAr() =>
        ArReport([P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4)]);

    private static OtherIncomeExpenseSourceRecord S(long id, decimal gross,
        OtherIncomeExpenseKind kind = OtherIncomeExpenseKind.OtherCollection, int minute = 0) =>
        new(id, Company, kind, "OC-" + id, 40 + id, "利息收入", "7101", "利息收入科目", 8000 + id,
            new DateOnly(2026, 10, 1), T0.AddMinutes(minute), OtherIncomeExpenseSourceState.Posted, 100, "TWD", gross);

    private static OtherIncomeExpenseReport ThreeOther()
    {
        var r = OtherIncomeExpenseRules.Read(new OtherIncomeExpenseScope(Company, From, Through, Cutoff, "TWD", 2, null),
            [S(1, 1000m), S(2, 250.5m, OtherIncomeExpenseKind.OtherPayment, 1), S(3, 10m, minute: 2)]);
        Assert.True(r.Status == OtherIncomeExpenseStatus.Complete, string.Join("; ", r.Errors));
        return r.Report!;
    }

    private static FiReportManifest ArManifest(FiReportCursorBinding binding, ArApDetailReport? report = null,
        DateTimeOffset? created = null)
    {
        var r = FiReportManifestRules.CreateArAp(binding, report ?? FiveAr(), created ?? Created, Ttl);
        Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
        return r.Manifest!;
    }

    private static FiReportManifest OtherManifest(FiReportCursorBinding binding)
    {
        var r = FiReportManifestRules.CreateOtherIncomeExpense(binding, ThreeOther(), Created, Ttl);
        Assert.True(r.Status == FiReportManifestStatus.Ok, string.Join("; ", r.Errors));
        return r.Manifest!;
    }

    /// <summary>Records every load／re-read call so tests can prove nothing is read before validation passes.</summary>
    private sealed class Store(FiReportManifest? manifest, Func<FiReportManifest, ArApDetailReport?>? arReread = null,
        Func<FiReportManifest, OtherIncomeExpenseReport?>? otherReread = null)
    {
        public List<long> Loaded { get; } = [];
        public List<FiReportManifest> Reread { get; } = [];

        public FiReportManifest? Load(long id)
        {
            Loaded.Add(id);
            return manifest;
        }

        public ArApDetailReport? ReadAr(FiReportManifest m)
        {
            Reread.Add(m);
            return arReread is null ? FiveAr() : arReread(m);
        }

        public OtherIncomeExpenseReport? ReadOther(FiReportManifest m)
        {
            Reread.Add(m);
            return otherReread is null ? ThreeOther() : otherReread(m);
        }
    }

    private static FiReportContinuationResult<ArApDetailPage> ContinueAr(FiReportContinuationRequest request,
        Store store, FiReportActorScope? scope = null, byte[]? key = null, DateTimeOffset? now = null) =>
        FiReportContinuationRules.ContinueArAp(request, scope ?? Scope(), key ?? Key, now ?? Now, store.Load,
            store.ReadAr);

    private static (FiReportContinuationRequest Request, Store Store, FiReportManifest Manifest) ArSetup(int page = 2,
        int pageSize = 2)
    {
        var probe = ArRequest(null, pageSize);
        var binding = Bind(probe, DigestOf(FiReportKind.ArDetail));
        var manifest = ArManifest(binding);
        return (ArRequest(Token(binding, page), pageSize), new Store(manifest), manifest);
    }

    private static void AssertProblem<T>(FiReportContinuationResult<T> r, int status, string code) where T : class
    {
        Assert.False(r.IsSuccess);
        Assert.Null(r.Page);
        Assert.Null(r.NextToken);
        Assert.Null(r.ExportToken);
        Assert.NotNull(r.Problem);
        Assert.Equal((status, code), (r.Problem!.Status, r.Problem.ErrorCode));
        if (status == 500) Assert.Empty(r.Problem.FieldErrors);
    }

    // ---- Normal ----

    [Fact]
    public void Ar_page_two_returns_manifest_rows_full_totals_and_a_token_for_page_three()
    {
        var (request, store, manifest) = ArSetup();

        var r = ContinueAr(request, store);

        Assert.True(r.IsSuccess);
        Assert.Null(r.Problem);
        Assert.Equal(2, r.Page!.PageNumber);
        Assert.Equal([3L, 4L], r.Page.Rows.Select(x => x.PrincipalId));
        Assert.Equal(manifest.ArApTotals, r.Page.Totals);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Same(manifest, Assert.Single(store.Reread));

        var next = FiReportCursorTokenRules.Validate(r.NextToken, manifest.Binding, Key, Now, manifest.ExpiresAt);
        Assert.Equal(FiReportCursorValidationStatus.Valid, next.Status);
        Assert.Equal(new FiReportCursorPosition(Cutoff, ManifestId, 3), next.Position);
    }

    [Fact]
    public void Walking_every_continuation_covers_remaining_members_once_and_last_page_has_no_token()
    {
        var (request, store, manifest) = ArSetup();
        var seen = new List<long>();
        var token = request.Token;
        var pages = 0;
        while (token is not null)
        {
            var r = ContinueAr(request with { Token = token }, store);
            Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
            Assert.Equal(manifest.ArApTotals, r.Page!.Totals);
            Assert.Equal(r.Page.HasNext, r.NextToken is not null);
            seen.AddRange(r.Page.Rows.Select(x => x.PrincipalId));
            token = r.NextToken;
            pages++;
        }

        Assert.Equal(2, pages);
        Assert.Equal([3L, 4L, 5L], seen);
    }

    [Fact]
    public void Exactly_full_last_page_issues_no_next_token()
    {
        var (request, store, _) = ArSetup(page: 5, pageSize: 1);

        var r = ContinueAr(request, store);

        Assert.True(r.IsSuccess);
        Assert.Equal([5L], r.Page!.Rows.Select(x => x.PrincipalId));
        Assert.False(r.Page.HasNext);
        Assert.Null(r.NextToken);
    }

    [Fact]
    public void Page_beyond_the_last_is_an_empty_page_with_full_totals_and_no_token()
    {
        var (request, store, manifest) = ArSetup(page: 9);

        var r = ContinueAr(request, store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Empty(r.Page!.Rows);
        Assert.Equal(manifest.ArApTotals, r.Page.Totals);
        Assert.Null(r.NextToken);
    }

    [Fact]
    public void Loads_exactly_the_signed_manifest_id_and_carries_it_into_the_next_token()
    {
        var probe = ArRequest(null);
        var binding = Bind(probe, DigestOf(FiReportKind.ArDetail));
        var manifest = ArManifest(binding);
        var store = new Store(manifest);

        var r = ContinueAr(probe with { Token = Token(binding, manifestId: 77) }, store);

        Assert.Equal([77L], store.Loaded);
        var next = FiReportCursorTokenRules.Validate(r.NextToken, binding, Key, Now, manifest.ExpiresAt);
        Assert.Equal(new FiReportCursorPosition(Cutoff, 77, 3), next.Position);
    }

    [Fact]
    public void Ap_detail_continuation_uses_the_payable_direction()
    {
        var probe = ArRequest(null, kind: FiReportKind.ApDetail);
        var binding = Bind(probe, DigestOf(FiReportKind.ApDetail));
        var report = ArReport([P(1, 10m, direction: ArApDirection.Payable), P(2, 20m, 1, ArApDirection.Payable),
            P(3, 30m, 2, ArApDirection.Payable)], ArApDirection.Payable);
        var store = new Store(ArManifest(binding, report), _ => report);

        var r = ContinueAr(probe with { Token = Token(binding) }, store);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal([3L], r.Page!.Rows.Select(x => x.PrincipalId));
        Assert.Null(r.NextToken);
    }

    [Fact]
    public void Other_income_expense_page_two_returns_last_row_and_no_token()
    {
        var probe = OtherRequest(null);
        var binding = Bind(probe, DigestOf(FiReportKind.OtherIncomeExpense));
        var manifest = OtherManifest(binding);
        var store = new Store(manifest);

        var r = FiReportContinuationRules.ContinueOtherIncomeExpense(probe with { Token = Token(binding) }, Scope(),
            Key, Now, store.Load, store.ReadOther);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal([3L], r.Page!.Rows.Select(x => x.SourceId));
        Assert.Equal(manifest.OtherTotals, r.Page.Totals);
        Assert.Null(r.NextToken);
    }

    [Fact]
    public void Other_income_expense_page_with_more_rows_issues_next_token()
    {
        var probe = OtherRequest(null, pageSize: 1);
        var binding = Bind(probe, DigestOf(FiReportKind.OtherIncomeExpense));
        var manifest = OtherManifest(binding);
        var store = new Store(manifest);

        var r = FiReportContinuationRules.ContinueOtherIncomeExpense(probe with { Token = Token(binding) }, Scope(),
            Key, Now, store.Load, store.ReadOther);

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
        Assert.Equal([2L], r.Page!.Rows.Select(x => x.SourceId));
        var next = FiReportCursorTokenRules.Validate(r.NextToken, binding, Key, Now, manifest.ExpiresAt);
        Assert.Equal(new FiReportCursorPosition(Cutoff, ManifestId, 3), next.Position);
    }

    [Fact]
    public void Same_scope_given_in_another_order_still_continues()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request, store, Scope([9, 3, 9], FiReportPartnerScope.Partners([40, Partner, 40])));

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
    }

    // ---- Boundary: expiry ----

    [Fact]
    public void One_tick_before_expiry_continues()
    {
        var (request, store, manifest) = ArSetup();

        var r = ContinueAr(request, store, now: manifest.ExpiresAt.AddTicks(-1));

        Assert.True(r.IsSuccess, r.Problem?.ErrorCode);
    }

    [Fact]
    public void At_expiry_is_409_and_nothing_is_reread()
    {
        var (request, store, manifest) = ArSetup();

        var r = ContinueAr(request, store, now: manifest.ExpiresAt);

        AssertProblem(r, 409, FiReportProblemRules.ManifestExpired);
        Assert.Empty(r.Problem!.FieldErrors);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Gone_manifest_is_409_and_nothing_is_reread()
    {
        var (request, _, _) = ArSetup();
        var store = new Store(null);

        var r = ContinueAr(request, store);

        AssertProblem(r, 409, FiReportProblemRules.ManifestExpired);
        Assert.Equal([ManifestId], store.Loaded);
        Assert.Empty(store.Reread);
    }

    // ---- Token and request errors: nothing is loaded ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_token_is_400_token_required_and_nothing_is_loaded(string? token)
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request with { Token = token }, store);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Equal(["Required"], r.Problem!.FieldErrors["token"]);
        Assert.Empty(store.Loaded);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Tampered_token_is_400_token_invalid_and_nothing_is_loaded()
    {
        var (request, store, _) = ArSetup();
        var t = request.Token!;
        var tampered = (t[0] == 'A' ? 'B' : 'A') + t[1..];

        var r = ContinueAr(request with { Token = tampered }, store);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Equal(["Invalid"], r.Problem!.FieldErrors["token"]);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Token_signed_with_another_key_is_400()
    {
        var (request, store, _) = ArSetup();
        var other = Enumerable.Repeat((byte)7, 32).ToArray();

        var r = ContinueAr(request, store, key: other);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Changed_filter_is_400_token_invalid_and_nothing_is_loaded()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request with { AsOf = AsOf.AddDays(-1) }, store);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Invalid_client_filter_is_400_validation_failed_on_that_field()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request with { PageSize = 0, Currency = "twd" }, store);

        AssertProblem(r, 400, FiReportProblemRules.ValidationFailed);
        Assert.Equal(["OutOfRange"], r.Problem!.FieldErrors["pageSize"]);
        Assert.Equal(["InvalidFormat"], r.Problem.FieldErrors["currency"]);
        Assert.Empty(store.Loaded);
    }

    // ---- Permission: actor scope change ----

    public static TheoryData<string> ScopeChanges => ["partnerRemoved", "allPartners", "roleRevoked", "otherActor"];

    [Theory]
    [MemberData(nameof(ScopeChanges))]
    public void Changed_actor_scope_is_400_token_invalid_and_nothing_is_loaded(string change)
    {
        var (request, store, _) = ArSetup();
        var scope = change switch
        {
            "partnerRemoved" => Scope(partners: FiReportPartnerScope.Partners([Partner])),
            "allPartners" => Scope(partners: FiReportPartnerScope.All),
            "roleRevoked" => Scope(roles: [3]),
            _ => Scope(actor: Actor + 1),
        };

        var r = ContinueAr(request, store, scope);

        AssertProblem(r, 400, FiReportProblemRules.TokenInvalid);
        Assert.Equal(["Invalid"], r.Problem!.FieldErrors["token"]);
        Assert.Empty(store.Loaded);
    }

    // ---- Server faults: 500 without field detail ----

    [Fact]
    public void Short_key_is_500_and_nothing_is_loaded()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request, store, key: new byte[31]);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("key.key:KeyInvalid", r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Default_clock_is_500()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request, store, now: default(DateTimeOffset));

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Invalid_actor_scope_is_500_with_internal_detail_only()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request, store, new FiReportActorScope(0, [], null));

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Equal(["actorScope.actorUserId:OutOfRange", "actorScope.roleIds:Required",
            "actorScope.partnerScope:Required"], r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Non_positive_company_is_500()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request with { CompanyId = 0 }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Ar_ap_entry_with_other_income_kind_is_500()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request with { Kind = FiReportKind.OtherIncomeExpense }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("request.kind:NotAllowed", r.Problem!.InternalDetails);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Other_income_entry_with_ar_kind_is_500()
    {
        var (request, store, _) = ArSetup();

        var r = FiReportContinuationRules.ContinueOtherIncomeExpense(request, Scope(), Key, Now, store.Load,
            store.ReadOther);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Undefined_kind_is_500()
    {
        var (request, store, _) = ArSetup();

        var r = ContinueAr(request with { Kind = (FiReportKind)99 }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Loaded);
    }

    [Fact]
    public void Null_inputs_are_500()
    {
        var (request, store, _) = ArSetup();

        AssertProblem(FiReportContinuationRules.ContinueArAp(null, Scope(), Key, Now, store.Load, store.ReadAr), 500,
            FiReportProblemRules.InternalError);
        AssertProblem(FiReportContinuationRules.ContinueArAp(request, null, Key, Now, store.Load, store.ReadAr), 500,
            FiReportProblemRules.InternalError);
        AssertProblem(FiReportContinuationRules.ContinueArAp(request, Scope(), Key, Now, null, store.ReadAr), 500,
            FiReportProblemRules.InternalError);
        AssertProblem(FiReportContinuationRules.ContinueArAp(request, Scope(), Key, Now, store.Load, null), 500,
            FiReportProblemRules.InternalError);
        AssertProblem(FiReportContinuationRules.ContinueOtherIncomeExpense(null, Scope(), Key, Now, store.Load,
            store.ReadOther), 500, FiReportProblemRules.InternalError);
        Assert.Empty(store.Loaded);
    }

    // ---- Stored manifest／re-read disagree: 500, no partial page ----

    [Fact]
    public void Loaded_manifest_for_another_binding_is_500_and_nothing_is_reread()
    {
        var (request, _, _) = ArSetup();
        var otherBinding = Bind(ArRequest(null, pageSize: 3), DigestOf(FiReportKind.ArDetail));
        var store = new Store(ArManifest(otherBinding));

        var r = ContinueAr(request, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("manifest.binding:Inconsistent", r.Problem!.InternalDetails);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Token_cutoff_other_than_manifest_cutoff_is_500()
    {
        var probe = ArRequest(null);
        var binding = Bind(probe, DigestOf(FiReportKind.ArDetail));
        var store = new Store(ArManifest(binding));

        var r = ContinueAr(probe with { Token = Token(binding, cutoff: Cutoff + 1) }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("manifest.cutoffSequence:Inconsistent", r.Problem!.InternalDetails);
        Assert.Empty(store.Reread);
    }

    [Fact]
    public void Reread_with_a_late_row_is_500_with_no_page_or_token()
    {
        var probe = ArRequest(null);
        var binding = Bind(probe, DigestOf(FiReportKind.ArDetail));
        var late = ArReport([P(1, 100m), P(2, 200m, 1), P(3, 300m, 2), P(4, 400m, 3), P(5, 500.25m, 4), P(6, 1m, 5)]);
        var store = new Store(ArManifest(binding), _ => late);

        var r = ContinueAr(probe with { Token = Token(binding) }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Contains("reread.memberIds:Inconsistent", r.Problem!.InternalDetails);
    }

    [Fact]
    public void Missing_reread_is_500()
    {
        var probe = ArRequest(null);
        var binding = Bind(probe, DigestOf(FiReportKind.ArDetail));
        var store = new Store(ArManifest(binding), _ => null);

        var r = ContinueAr(probe with { Token = Token(binding) }, store);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
        Assert.Single(store.Reread);
    }

    [Fact]
    public void Other_reread_that_differs_is_500()
    {
        var probe = OtherRequest(null);
        var binding = Bind(probe, DigestOf(FiReportKind.OtherIncomeExpense));
        var changed = OtherIncomeExpenseRules.Read(
            new OtherIncomeExpenseScope(Company, From, Through, Cutoff, "TWD", 2, null),
            [S(1, 1000m), S(2, 250.5m, OtherIncomeExpenseKind.OtherPayment, 1), S(3, 11m, minute: 2)]).Report!;
        var store = new Store(OtherManifest(binding), otherReread: _ => changed);

        var r = FiReportContinuationRules.ContinueOtherIncomeExpense(probe with { Token = Token(binding) }, Scope(),
            Key, Now, store.Load, store.ReadOther);

        AssertProblem(r, 500, FiReportProblemRules.InternalError);
    }

    [Fact]
    public void Repeated_continuation_is_deterministic_and_leaves_the_manifest_unchanged()
    {
        var (request, store, manifest) = ArSetup();
        var before = manifest with { };
        var members = manifest.MemberIds.ToList();

        var first = ContinueAr(request, store);
        var second = ContinueAr(request, store);

        Assert.Equal(before, manifest);
        Assert.Equal(members, manifest.MemberIds);
        Assert.Equal(first.NextToken, second.NextToken);
        Assert.Equal(first.Page!.Rows.Select(x => x.PrincipalId), second.Page!.Rows.Select(x => x.PrincipalId));
        Assert.Equal(first.Page.Totals, second.Page.Totals);
    }
}
