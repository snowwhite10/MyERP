using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-006／014（錯誤對應 U 層）：把 ADR-093 cursor token 與 ADR-094 manifest 結果對應為 HTTP 狀態、errorCode 與
/// 對外欄位錯誤（ADR-096、WU-BI-001a6）。只有呼叫端可提供的篩選欄位（partnerId、asOf、from、through、kindFilter、
/// currency、pageSize）錯誤回 400 欄位錯誤；token 問題 400 <c>FI.REPORT.TOKEN_INVALID</c> 且不說明竄改或哪個綁定
/// 不符；manifest 過期 409；超過上限 422；伺服器組裝錯誤（金鑰、時鐘、公司、actor、種類、manifest、重讀、表頭、
/// TTL）與 Inconsistent 一律 500 <c>COMMON.SERVER.INTERNAL_ERROR</c>，不對外揭露任何欄位，內部細節只供 traceId 日誌。
/// 不涉及 SQL、權限、API 或畫面。
/// </summary>
public sealed class FiReportProblemRulesTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly string Actor = new('a', 64);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 4, 20, 0, TimeSpan.FromHours(8));

    private static FiReportCursorBinding Ar(long? partner = 55, int pageSize = 50, string actor = "") =>
        new(7, actor.Length == 0 ? Actor : actor, FiReportKind.ArDetail, partner, new DateOnly(2026, 10, 31), null,
            null, null, "TWD", pageSize);

    private static FiReportCursorValidationResult Token(params FiReportCursorFieldError[] errors) =>
        new(errors.Length == 0 ? new FiReportCursorPosition(900, 4321, 2) : null, errors);

    private static FiReportCursorFieldError T(string collection, string field, FiReportCursorError error) =>
        new(collection, field, error);

    private static FiReportManifestFieldError M(string collection, string field, FiReportManifestError error) =>
        new(collection, field, error);

    private static void Internal(FiReportProblem? p)
    {
        Assert.NotNull(p);
        Assert.Equal(500, p.Status);
        Assert.Equal(FiReportProblemRules.InternalError, p.ErrorCode);
        Assert.Empty(p.FieldErrors);
        Assert.NotEmpty(p.InternalDetails);
    }

    // ---- error codes ----

    [Fact]
    public void Error_codes_follow_module_prefix_convention()
    {
        Assert.Equal("FI.REPORT.TOKEN_INVALID", FiReportProblemRules.TokenInvalid);
        Assert.Equal("FI.REPORT.MANIFEST_EXPIRED", FiReportProblemRules.ManifestExpired);
        Assert.Equal("FI.REPORT.TOO_LARGE", FiReportProblemRules.TooLarge);
        Assert.Equal("COMMON.REQUEST.VALIDATION_FAILED", FiReportProblemRules.ValidationFailed);
        Assert.Equal("COMMON.SERVER.INTERNAL_ERROR", FiReportProblemRules.InternalError);
    }

    // ---- cursor token validation ----

    [Fact]
    public void Valid_token_has_no_problem()
    {
        Assert.Null(FiReportProblemRules.Map(Token()));
    }

    [Fact]
    public void Real_valid_token_round_trip_has_no_problem()
    {
        var token = FiReportCursorTokenRules.Issue(Ar(), new FiReportCursorPosition(900, 4321, 2), Key).Token;
        Assert.Null(FiReportProblemRules.Map(
            FiReportCursorTokenRules.Validate(token, Ar(), Key, Now, Now.AddMinutes(5))));
    }

    [Theory]
    [InlineData("token", "token", FiReportCursorError.Malformed)]
    [InlineData("token", "signature", FiReportCursorError.Tampered)]
    [InlineData("token", "payload", FiReportCursorError.Malformed)]
    [InlineData("binding", "partnerId", FiReportCursorError.Mismatch)]
    [InlineData("binding", "pageSize", FiReportCursorError.Mismatch)]
    [InlineData("binding", "companyId", FiReportCursorError.Mismatch)]
    [InlineData("binding", "actorScopeDigest", FiReportCursorError.Mismatch)]
    [InlineData("binding", "kind", FiReportCursorError.Mismatch)]
    public void Bad_token_is_400_token_invalid_without_saying_why(string collection, string field,
        FiReportCursorError error)
    {
        var p = FiReportProblemRules.Map(Token(T(collection, field, error)));

        Assert.NotNull(p);
        Assert.Equal(400, p.Status);
        Assert.Equal(FiReportProblemRules.TokenInvalid, p.ErrorCode);
        var only = Assert.Single(p.FieldErrors);
        Assert.Equal("token", only.Key);
        Assert.Equal(["Invalid"], only.Value);
        Assert.Contains($"{collection}.{field}:{error}", p.InternalDetails);
    }

    [Fact]
    public void Several_mismatches_still_expose_one_token_error()
    {
        var p = FiReportProblemRules.Map(Token(T("binding", "partnerId", FiReportCursorError.Mismatch),
            T("binding", "actorScopeDigest", FiReportCursorError.Mismatch)));

        Assert.Equal(400, p!.Status);
        Assert.Equal(["Invalid"], Assert.Single(p.FieldErrors).Value);
        Assert.Equal(2, p.InternalDetails.Count);
    }

    [Fact]
    public void Missing_token_is_400_token_required()
    {
        var p = FiReportProblemRules.Map(Token(T("token", "token", FiReportCursorError.Required)));

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.TokenInvalid, p.ErrorCode);
        Assert.Equal(["Required"], p.FieldErrors["token"]);
    }

    [Fact]
    public void Real_tampered_token_is_400_token_invalid()
    {
        var token = FiReportCursorTokenRules.Issue(Ar(), new FiReportCursorPosition(900, 4321, 2), Key).Token!;
        var tampered = (token[0] == 'A' ? "B" : "A") + token[1..];

        var p = FiReportProblemRules.Map(FiReportCursorTokenRules.Validate(tampered, Ar(), Key, Now, Now.AddMinutes(5)));

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.TokenInvalid, p.ErrorCode);
    }

    [Fact]
    public void Real_other_actor_token_is_400_token_invalid()
    {
        var token = FiReportCursorTokenRules.Issue(Ar(), new FiReportCursorPosition(900, 4321, 2), Key).Token;

        var p = FiReportProblemRules.Map(FiReportCursorTokenRules.Validate(token, Ar(actor: new string('b', 64)), Key,
            Now, Now.AddMinutes(5)));

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.TokenInvalid, p.ErrorCode);
        Assert.DoesNotContain("actorScopeDigest", p.FieldErrors.Keys);
    }

    [Fact]
    public void Expired_manifest_is_409_without_field_errors()
    {
        var p = FiReportProblemRules.Map(Token(T("manifest", "expiresAt", FiReportCursorError.Expired)));

        Assert.Equal(409, p!.Status);
        Assert.Equal(FiReportProblemRules.ManifestExpired, p.ErrorCode);
        Assert.Empty(p.FieldErrors);
    }

    [Fact]
    public void Real_gone_manifest_is_409()
    {
        var token = FiReportCursorTokenRules.Issue(Ar(), new FiReportCursorPosition(900, 4321, 2), Key).Token;

        var p = FiReportProblemRules.Map(FiReportCursorTokenRules.Validate(token, Ar(), Key, Now, null));

        Assert.Equal(409, p!.Status);
        Assert.Equal(FiReportProblemRules.ManifestExpired, p.ErrorCode);
    }

    [Theory]
    [InlineData("partnerId", FiReportCursorError.OutOfRange, "OutOfRange")]
    [InlineData("asOf", FiReportCursorError.Required, "Required")]
    [InlineData("from", FiReportCursorError.NotAllowed, "NotAllowed")]
    [InlineData("through", FiReportCursorError.OutOfRange, "OutOfRange")]
    [InlineData("kindFilter", FiReportCursorError.NotAllowed, "NotAllowed")]
    [InlineData("currency", FiReportCursorError.InvalidFormat, "InvalidFormat")]
    [InlineData("pageSize", FiReportCursorError.OutOfRange, "OutOfRange")]
    public void Invalid_client_filter_is_400_validation_failed_per_field(string field, FiReportCursorError error,
        string code)
    {
        var p = FiReportProblemRules.Map(Token(T("binding", field, error)));

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.ValidationFailed, p.ErrorCode);
        Assert.Equal([code], p.FieldErrors[field]);
    }

    [Fact]
    public void Real_invalid_page_size_is_400_on_page_size()
    {
        var p = FiReportProblemRules.Map(FiReportCursorTokenRules.Validate("x", Ar(pageSize: 0), Key, Now,
            Now.AddMinutes(5)));

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.ValidationFailed, p.ErrorCode);
        Assert.Equal(["OutOfRange"], p.FieldErrors["pageSize"]);
    }

    [Fact]
    public void Several_errors_on_one_field_are_listed_once_each_in_order()
    {
        var p = FiReportProblemRules.Map(Token(T("binding", "through", FiReportCursorError.Required),
            T("binding", "currency", FiReportCursorError.InvalidFormat),
            T("binding", "through", FiReportCursorError.OutOfRange),
            T("binding", "through", FiReportCursorError.OutOfRange)));

        Assert.Equal(["Required", "OutOfRange"], p!.FieldErrors["through"]);
        Assert.Equal(["InvalidFormat"], p.FieldErrors["currency"]);
        Assert.Equal(4, p.InternalDetails.Count);
    }

    [Theory]
    [InlineData("key", "key", FiReportCursorError.KeyInvalid)]
    [InlineData("clock", "now", FiReportCursorError.Required)]
    [InlineData("binding", "companyId", FiReportCursorError.OutOfRange)]
    [InlineData("binding", "actorScopeDigest", FiReportCursorError.InvalidFormat)]
    [InlineData("binding", "actorScopeDigest", FiReportCursorError.Required)]
    [InlineData("binding", "kind", FiReportCursorError.OutOfRange)]
    [InlineData("binding", "binding", FiReportCursorError.Required)]
    [InlineData("binding", "pageSize", FiReportCursorError.Expired)]
    [InlineData("binding", "pageSize", FiReportCursorError.KeyInvalid)]
    [InlineData("token", "token", FiReportCursorError.Mismatch)]
    [InlineData("position", "pageNumber", FiReportCursorError.Mismatch)]
    public void Server_wiring_fault_is_500_without_fields(string collection, string field, FiReportCursorError error)
    {
        var p = FiReportProblemRules.Map(Token(T(collection, field, error)));

        Internal(p);
        Assert.Contains($"{collection}.{field}:{error}", p!.InternalDetails);
    }

    [Fact]
    public void Real_missing_key_is_500()
    {
        Internal(FiReportProblemRules.Map(FiReportCursorTokenRules.Validate("x", Ar(), null, Now, Now.AddMinutes(5))));
    }

    [Fact]
    public void Server_fault_wins_over_client_filter_error()
    {
        Internal(FiReportProblemRules.Map(Token(T("binding", "pageSize", FiReportCursorError.OutOfRange),
            T("clock", "now", FiReportCursorError.Required))));
    }

    [Fact]
    public void Client_filter_error_wins_over_token_error()
    {
        var p = FiReportProblemRules.Map(Token(T("binding", "pageSize", FiReportCursorError.OutOfRange),
            T("token", "token", FiReportCursorError.Malformed)));

        Assert.Equal(FiReportProblemRules.ValidationFailed, p!.ErrorCode);
        Assert.DoesNotContain("token", p.FieldErrors.Keys);
    }

    [Fact]
    public void Client_fields_cannot_be_changed_by_callers()
    {
        Assert.False(FiReportProblemRules.ClientFields is ISet<string> { IsReadOnly: false });
        Assert.Equal(7, FiReportProblemRules.ClientFields.Count);
    }

    [Fact]
    public void Missing_validation_result_is_500()
    {
        Internal(FiReportProblemRules.Map((FiReportCursorValidationResult?)null));
    }

    // ---- cursor token issue ----

    [Fact]
    public void Issued_token_has_no_problem()
    {
        Assert.Null(FiReportProblemRules.Map(new FiReportCursorIssueResult("t", [])));
    }

    [Fact]
    public void Issue_with_client_filter_error_is_400_validation_failed()
    {
        var p = FiReportProblemRules.Map(FiReportCursorTokenRules.Issue(Ar(partner: 0),
            new FiReportCursorPosition(900, 4321, 2), Key));

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.ValidationFailed, p.ErrorCode);
        Assert.Equal(["OutOfRange"], p.FieldErrors["partnerId"]);
    }

    [Theory]
    [InlineData("position", "pageNumber", FiReportCursorError.OutOfRange)]
    [InlineData("position", "position", FiReportCursorError.Required)]
    [InlineData("key", "key", FiReportCursorError.KeyInvalid)]
    public void Issue_server_fault_is_500(string collection, string field, FiReportCursorError error)
    {
        Internal(FiReportProblemRules.Map(new FiReportCursorIssueResult(null, [T(collection, field, error)])));
    }

    [Fact]
    public void Missing_issue_result_is_500()
    {
        Internal(FiReportProblemRules.Map((FiReportCursorIssueResult?)null));
    }

    // ---- manifest create／page／export ----

    [Fact]
    public void Manifest_ok_has_no_problem()
    {
        Assert.Null(FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)[]));
    }

    [Theory]
    [InlineData("asOf", FiReportManifestError.Required, "Required")]
    [InlineData("kindFilter", FiReportManifestError.NotAllowed, "NotAllowed")]
    [InlineData("currency", FiReportManifestError.InvalidFormat, "InvalidFormat")]
    [InlineData("pageSize", FiReportManifestError.OutOfRange, "OutOfRange")]
    public void Manifest_client_filter_error_is_400(string field, FiReportManifestError error, string code)
    {
        var p = FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)[M("binding", field, error)]);

        Assert.Equal(400, p!.Status);
        Assert.Equal(FiReportProblemRules.ValidationFailed, p.ErrorCode);
        Assert.Equal([code], p.FieldErrors[field]);
    }

    [Fact]
    public void Too_large_is_422_without_fields()
    {
        var p = FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)
            [M("report", "rows", FiReportManifestError.TooLarge)]);

        Assert.Equal(422, p!.Status);
        Assert.Equal(FiReportProblemRules.TooLarge, p.ErrorCode);
        Assert.Empty(p.FieldErrors);
        Assert.Equal(["report.rows:TooLarge"], p.InternalDetails);
    }

    [Fact]
    public void Client_filter_error_wins_over_too_large()
    {
        var p = FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)
            [M("report", "rows", FiReportManifestError.TooLarge), M("binding", "currency", FiReportManifestError.Required)]);

        Assert.Equal(400, p!.Status);
        Assert.Equal(["Required"], p.FieldErrors["currency"]);
    }

    [Fact]
    public void Inconsistent_is_500_and_hides_every_internal_field()
    {
        var p = FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)
            [M("reread", "contentDigest", FiReportManifestError.Inconsistent),
             M("manifest", "memberIds", FiReportManifestError.Inconsistent)]);

        Internal(p);
        Assert.Equal(["reread.contentDigest:Inconsistent", "manifest.memberIds:Inconsistent"], p!.InternalDetails);
    }

    [Fact]
    public void Inconsistent_wins_over_client_filter_error()
    {
        Internal(FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)
            [M("binding", "pageSize", FiReportManifestError.OutOfRange),
             M("reread", "totals", FiReportManifestError.Inconsistent)]));
    }

    [Fact]
    public void Inconsistent_wins_over_too_large()
    {
        Internal(FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)
            [M("report", "rows", FiReportManifestError.TooLarge),
             M("report", "totals", FiReportManifestError.Inconsistent)]));
    }

    [Theory]
    [InlineData("request", "manifest", FiReportManifestError.Required)]
    [InlineData("request", "reread", FiReportManifestError.Required)]
    [InlineData("request", "report", FiReportManifestError.Required)]
    [InlineData("request", "headers", FiReportManifestError.OutOfRange)]
    [InlineData("request", "ttl", FiReportManifestError.OutOfRange)]
    [InlineData("request", "pageNumber", FiReportManifestError.OutOfRange)]
    [InlineData("clock", "createdAt", FiReportManifestError.Required)]
    [InlineData("binding", "companyId", FiReportManifestError.OutOfRange)]
    [InlineData("binding", "kind", FiReportManifestError.NotAllowed)]
    [InlineData("binding", "actorScopeDigest", FiReportManifestError.InvalidFormat)]
    public void Manifest_server_wiring_fault_is_500(string collection, string field, FiReportManifestError error)
    {
        Internal(FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)[M(collection, field, error)]));
    }

    [Fact]
    public void Real_export_without_manifest_is_500_not_client_error()
    {
        var r = FiReportManifestRules.ExportOtherIncomeExpense(null, null, null);

        Assert.Equal(FiReportManifestStatus.Rejected, r.Status);
        Internal(FiReportProblemRules.Map(r.Errors));
    }

    [Fact]
    public void Missing_manifest_error_list_is_500()
    {
        Internal(FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>?)null));
    }

    [Fact]
    public void Null_error_entries_are_500()
    {
        Internal(FiReportProblemRules.Map((IReadOnlyList<FiReportManifestFieldError>)[null!]));
        Internal(FiReportProblemRules.Map(new FiReportCursorValidationResult(null, [null!])));
    }
}
