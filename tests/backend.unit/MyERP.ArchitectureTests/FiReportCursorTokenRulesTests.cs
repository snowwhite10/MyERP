using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-006（U 層）：FI 報表續頁 cutoff token。token 綁定公司、actor scope 摘要、報表種類、篩選、page size、
/// cutoff 與 manifest ID，以注入金鑰 HMAC-SHA256 簽章；竄改／格式錯／篩選、page size 或 actor 不符為 400，
/// manifest 過期（呼叫端以 IClock 傳入 now 與 expiresAt）為 409；token 不攜帶權限（BR-FI-130／131、ADR-093）。
/// 不涉及 SQL manifest 表、權限檢查、API 或畫面。
/// </summary>
public sealed class FiReportCursorTokenRulesTests
{
    // 固定測試位元組，不是真金鑰。
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();
    private static readonly string Actor = new('a', 64);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 3, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset ExpiresAt = Now.AddMinutes(30);

    private static FiReportCursorBinding Ar(long? partner = 55, string? currency = "TWD", int pageSize = 50) =>
        new(7, Actor, FiReportKind.ArDetail, partner, new DateOnly(2026, 10, 31), null, null, null, currency,
            pageSize);

    private static FiReportCursorBinding Other(OtherIncomeExpenseKind? kind = OtherIncomeExpenseKind.OtherPayment) =>
        new(7, Actor, FiReportKind.OtherIncomeExpense, null, null, new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31), kind, "TWD", 100);

    private static readonly FiReportCursorPosition Pos = new(900, 4321, 2);

    private static string Issue(FiReportCursorBinding? binding = null, FiReportCursorPosition? position = null,
        byte[]? key = null)
    {
        var r = FiReportCursorTokenRules.Issue(binding ?? Ar(), position ?? Pos, key ?? Key);
        Assert.True(r.Status == FiReportCursorIssueStatus.Issued, string.Join("; ", r.Errors));
        return r.Token!;
    }

    private static FiReportCursorValidationResult Validate(string? token, FiReportCursorBinding? binding = null,
        byte[]? key = null, DateTimeOffset? now = null, DateTimeOffset? expiresAt = null, bool manifestGone = false) =>
        FiReportCursorTokenRules.Validate(token, binding ?? Ar(), key ?? Key, now ?? Now,
            manifestGone ? null : expiresAt ?? ExpiresAt);

    private static void Rejected(FiReportCursorValidationResult r, string collection, string field,
        FiReportCursorError error)
    {
        Assert.Equal(FiReportCursorValidationStatus.Rejected, r.Status);
        Assert.Null(r.Position);
        Assert.Contains(new FiReportCursorFieldError(collection, field, error), r.Errors);
    }

    private static void IssueRejected(FiReportCursorBinding? binding, FiReportCursorPosition? position,
        string collection, string field, FiReportCursorError error)
    {
        var r = FiReportCursorTokenRules.Issue(binding, position, Key);
        Assert.Equal(FiReportCursorIssueStatus.Rejected, r.Status);
        Assert.Null(r.Token);
        Assert.Contains(new FiReportCursorFieldError(collection, field, error), r.Errors);
    }

    private static string Forge(string payload, byte[]? key = null)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var mac = HMACSHA256.HashData(key ?? Key,
            Encoding.UTF8.GetBytes(FiReportCursorTokenRules.MacDomain).Concat(bytes).ToArray());
        return Base64Url.EncodeToString(bytes) + "." + Base64Url.EncodeToString(mac);
    }

    private static string PayloadOf(string token) =>
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token.Split('.')[0]));

    // ---- Normal ----

    public static TheoryData<FiReportCursorBinding> Bindings() => new()
    {
        Ar(),
        Ar(partner: null) with { Kind = FiReportKind.ApDetail },
        Other(),
        Other(kind: null)
    };

    [Theory]
    [MemberData(nameof(Bindings))]
    public void Issued_token_round_trips_cutoff_manifest_and_page_for_every_report_kind(FiReportCursorBinding binding)
    {
        var r = Validate(Issue(binding), binding);

        Assert.Equal(FiReportCursorValidationStatus.Valid, r.Status);
        Assert.Empty(r.Errors);
        Assert.Equal(Pos, r.Position);
    }

    [Fact]
    public void Token_is_url_safe_deterministic_and_key_dependent()
    {
        var token = Issue();

        Assert.Matches("^[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+$", token);
        Assert.Equal(token, Issue());
        Assert.NotEqual(token, Issue(key: OtherKey));
        Assert.True(token.Length <= FiReportCursorTokenRules.MaxTokenLength);
    }

    [Fact]
    public void Token_payload_carries_binding_and_position_only_never_authority_or_key()
    {
        var payload = PayloadOf(Issue());

        Assert.Equal("v1|7|" + Actor + "|ArDetail|55|2026-10-31||||TWD|50|900|4321|2", payload);
        Assert.DoesNotContain("perm", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extreme_but_valid_values_round_trip()
    {
        var binding = Ar(pageSize: FiReportCursorTokenRules.MaxPageSize) with
        {
            CompanyId = long.MaxValue, PartnerId = long.MaxValue
        };
        var pos = new FiReportCursorPosition(long.MaxValue, long.MaxValue, FiReportCursorTokenRules.MaxPageNumber);
        var token = Issue(binding, pos);

        Assert.True(token.Length <= FiReportCursorTokenRules.MaxTokenLength);
        Assert.Equal(pos, Validate(token, binding).Position);
        Assert.Equal(FiReportCursorValidationStatus.Valid, Validate(Issue(Ar(pageSize: 1)), Ar(pageSize: 1)).Status);
    }

    // ---- Tamper／format (400) ----

    [Fact]
    public void Changing_any_payload_or_mac_character_is_tampered()
    {
        var token = Issue();
        var dot = token.IndexOf('.', StringComparison.Ordinal);

        foreach (var i in new[] { 0, dot - 1, dot + 1, token.Length - 1 })
        {
            var c = token[i] == 'A' ? 'B' : 'A';
            var tampered = token[..i] + c + token[(i + 1)..];
            var r = Validate(tampered);
            Assert.Equal(FiReportCursorValidationStatus.Rejected, r.Status);
            Assert.Null(r.Position);
            Assert.True(r.Errors.Any(e => e.Collection == "token"), tampered);
        }
    }

    [Fact]
    public void Payload_from_another_token_with_this_mac_or_another_key_is_tampered()
    {
        var a = Issue();
        var b = Issue(position: Pos with { PageNumber = 3 });
        var swapped = b.Split('.')[0] + "." + a.Split('.')[1];

        Rejected(Validate(swapped), "token", "signature", FiReportCursorError.Tampered);
        Rejected(Validate(a, key: OtherKey), "token", "signature", FiReportCursorError.Tampered);
        Rejected(Validate(Forge(PayloadOf(a), OtherKey)), "token", "signature", FiReportCursorError.Tampered);
    }

    [Theory]
    [InlineData(null, FiReportCursorError.Required)]
    [InlineData("", FiReportCursorError.Required)]
    [InlineData("   ", FiReportCursorError.Required)]
    [InlineData("abc", FiReportCursorError.Malformed)]
    [InlineData("a.b.c", FiReportCursorError.Malformed)]
    [InlineData(".abc", FiReportCursorError.Malformed)]
    [InlineData("abc.", FiReportCursorError.Malformed)]
    [InlineData("ab+c.def/", FiReportCursorError.Malformed)]
    [InlineData("abc=.def", FiReportCursorError.Malformed)]
    [InlineData(" abc.def", FiReportCursorError.Malformed)]
    public void Missing_or_unparseable_token_is_rejected(string? token, FiReportCursorError error) =>
        Rejected(Validate(token), "token", "token", error);

    [Fact]
    public void Over_length_token_is_malformed_without_verifying()
    {
        var token = new string('A', FiReportCursorTokenRules.MaxTokenLength) + ".A";
        Rejected(Validate(token), "token", "token", FiReportCursorError.Malformed);
    }

    [Fact]
    public void Truncated_mac_is_tampered()
    {
        var token = Issue();
        Rejected(Validate(token[..^4]), "token", "signature", FiReportCursorError.Tampered);
    }

    public static TheoryData<string> ForgedPayloads()
    {
        var ok = "v1|7|" + new string('a', 64) + "|ArDetail|55|2026-10-31||||TWD|50|900|4321|2";
        return new()
        {
            ok.Replace("v1|", "v2|", StringComparison.Ordinal),
            ok + "|9",
            ok[..ok.LastIndexOf('|')],
            ok.Replace("|7|", "|007|", StringComparison.Ordinal),
            ok.Replace("|7|", "|+7|", StringComparison.Ordinal),
            ok.Replace("|7|", "|-7|", StringComparison.Ordinal),
            ok.Replace("|ArDetail|", "|Payroll|", StringComparison.Ordinal),
            ok.Replace("|ArDetail|", "|0|", StringComparison.Ordinal),
            ok.Replace("|ArDetail|", "|ardetail|", StringComparison.Ordinal),
            ok.Replace("2026-10-31", "2026-02-30", StringComparison.Ordinal),
            ok.Replace("2026-10-31", "20261031", StringComparison.Ordinal),
            ok.Replace("|TWD|", "|twd|", StringComparison.Ordinal),
            ok.Replace("|50|", "|201|", StringComparison.Ordinal),
            ok.Replace("|4321|2", "|4321|1", StringComparison.Ordinal),
            ok.Replace("|55|", "|99999999999999999999|", StringComparison.Ordinal),
            ok.Replace("|ArDetail|55|2026-10-31|||", "|ArDetail|55|2026-10-31|2026-10-01||", StringComparison.Ordinal),
            ok.Replace(new string('a', 64), new string('A', 64), StringComparison.Ordinal),
            "v1"
        };
    }

    [Theory]
    [MemberData(nameof(ForgedPayloads))]
    public void Correctly_signed_but_non_canonical_or_invalid_payload_is_malformed(string payload) =>
        Rejected(Validate(Forge(payload)), "token", "payload", FiReportCursorError.Malformed);

    [Fact]
    public void Non_canonical_base64_with_valid_mac_is_malformed()
    {
        // 最後一個字元的低位元不為零：解碼後相同位元組，但不是規範編碼。
        var token = Issue();
        var parts = token.Split('.');
        var payload = parts[0];
        if (payload.Length % 4 == 0)
        {
            return; // 無剩餘位元，無非規範形式
        }

        var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var last = alphabet.IndexOf(payload[^1], StringComparison.Ordinal);
        var altered = payload[..^1] + alphabet[last | 1];
        if (altered == payload)
        {
            altered = payload[..^1] + alphabet[last ^ 1];
        }

        var r = Validate(altered + "." + parts[1]);
        Assert.Equal(FiReportCursorValidationStatus.Rejected, r.Status);
        Assert.Contains(r.Errors, e => e.Collection == "token");
    }

    // ---- Binding mismatch (400) ----

    public static TheoryData<string, FiReportCursorBinding> Mismatches() => new()
    {
        { "companyId", Ar() with { CompanyId = 8 } },
        { "actorScopeDigest", Ar() with { ActorScopeDigest = new string('b', 64) } },
        { "kind", Ar() with { Kind = FiReportKind.ApDetail } },
        { "partnerId", Ar(partner: 56) },
        { "partnerId", Ar(partner: null) },
        { "asOf", Ar() with { AsOf = new DateOnly(2026, 10, 30) } },
        { "currency", Ar(currency: "USD") },
        { "pageSize", Ar(pageSize: 51) }
    };

    [Theory]
    [MemberData(nameof(Mismatches))]
    public void Continuation_with_a_different_binding_is_rejected(string field, FiReportCursorBinding request) =>
        Rejected(Validate(Issue(), request), "binding", field, FiReportCursorError.Mismatch);

    [Fact]
    public void Other_report_range_and_kind_filter_mismatches_are_rejected()
    {
        var token = Issue(Other());

        Rejected(Validate(token, Other() with { From = new DateOnly(2026, 10, 2) }), "binding", "from",
            FiReportCursorError.Mismatch);
        Rejected(Validate(token, Other() with { Through = new DateOnly(2026, 11, 1) }), "binding", "through",
            FiReportCursorError.Mismatch);
        Rejected(Validate(token, Other(OtherIncomeExpenseKind.OtherCollection)), "binding", "kindFilter",
            FiReportCursorError.Mismatch);
        Rejected(Validate(token, Other(null)), "binding", "kindFilter", FiReportCursorError.Mismatch);
        Rejected(Validate(token, Ar()), "binding", "kind", FiReportCursorError.Mismatch);
    }

    [Fact]
    public void Every_mismatched_field_is_listed_in_binding_order()
    {
        var r = Validate(Issue(), Ar(partner: 1, currency: "USD", pageSize: 10) with { CompanyId = 9 });

        Assert.Equal(["companyId", "partnerId", "currency", "pageSize"], r.Errors.Select(e => e.Field));
        Assert.All(r.Errors, e => Assert.Equal(FiReportCursorError.Mismatch, e.Error));
    }

    [Fact]
    public void Changed_actor_scope_rejects_old_token_so_authority_is_rechecked()
    {
        // 角色或夥伴範圍改變後 actor scope 摘要不同：舊 token 不可續用，需重新取得 cutoff（BR-FI-131）。
        var r = Validate(Issue(), Ar() with { ActorScopeDigest = new string('c', 64) });
        Assert.Equal(new FiReportCursorFieldError("binding", "actorScopeDigest", FiReportCursorError.Mismatch),
            Assert.Single(r.Errors));
    }

    // ---- Expiry (409) ----

    [Fact]
    public void Manifest_expired_at_or_after_expires_at_or_gone_is_409()
    {
        var token = Issue();

        Assert.Equal(FiReportCursorValidationStatus.Valid, Validate(token, now: ExpiresAt.AddTicks(-1)).Status);
        foreach (var r in new[]
                 {
                     Validate(token, now: ExpiresAt), Validate(token, now: ExpiresAt.AddDays(1)),
                     Validate(token, manifestGone: true)
                 })
        {
            Assert.Equal(FiReportCursorValidationStatus.Expired, r.Status);
            Assert.Null(r.Position);
            Assert.Equal(new FiReportCursorFieldError("manifest", "expiresAt", FiReportCursorError.Expired),
                Assert.Single(r.Errors));
        }
    }

    [Fact]
    public void Expiry_compares_instants_across_offsets()
    {
        var token = Issue();
        var utcBefore = ExpiresAt.ToUniversalTime().AddSeconds(-1);
        var utcAt = ExpiresAt.ToOffset(TimeSpan.FromHours(-5));

        Assert.Equal(FiReportCursorValidationStatus.Valid, Validate(token, now: utcBefore).Status);
        Assert.Equal(FiReportCursorValidationStatus.Expired, Validate(token, now: utcAt).Status);
    }

    [Fact]
    public void Tampered_or_mismatched_expired_token_is_400_not_409()
    {
        var token = Issue();

        Assert.Equal(FiReportCursorValidationStatus.Rejected,
            Validate(token[..^1] + (token[^1] == 'A' ? 'B' : 'A'), now: ExpiresAt).Status);
        Assert.Equal(FiReportCursorValidationStatus.Rejected,
            Validate(token, Ar(pageSize: 10), now: ExpiresAt).Status);
    }

    [Fact]
    public void Default_now_is_required()
    {
        var r = FiReportCursorTokenRules.Validate(Issue(), Ar(), Key, default, ExpiresAt);
        Rejected(r, "clock", "now", FiReportCursorError.Required);
    }

    // ---- Binding／position validation ----

    public static TheoryData<FiReportCursorBinding?, string, FiReportCursorError> InvalidBindings() => new()
    {
        { null, "binding", FiReportCursorError.Required },
        { Ar() with { CompanyId = 0 }, "companyId", FiReportCursorError.OutOfRange },
        { Ar() with { ActorScopeDigest = null }, "actorScopeDigest", FiReportCursorError.Required },
        { Ar() with { ActorScopeDigest = new string('a', 63) }, "actorScopeDigest", FiReportCursorError.InvalidFormat },
        { Ar() with { ActorScopeDigest = new string('A', 64) }, "actorScopeDigest", FiReportCursorError.InvalidFormat },
        { Ar() with { ActorScopeDigest = new string('g', 64) }, "actorScopeDigest", FiReportCursorError.InvalidFormat },
        { Ar() with { Kind = (FiReportKind)99 }, "kind", FiReportCursorError.OutOfRange },
        { Ar(partner: 0), "partnerId", FiReportCursorError.OutOfRange },
        { Ar() with { AsOf = null }, "asOf", FiReportCursorError.Required },
        { Ar() with { AsOf = default(DateOnly) }, "asOf", FiReportCursorError.Required },
        { Ar() with { From = new DateOnly(2026, 10, 1) }, "from", FiReportCursorError.NotAllowed },
        { Ar() with { Through = new DateOnly(2026, 10, 1) }, "through", FiReportCursorError.NotAllowed },
        { Ar() with { KindFilter = OtherIncomeExpenseKind.OtherCollection }, "kindFilter", FiReportCursorError.NotAllowed },
        { Other() with { PartnerId = 5 }, "partnerId", FiReportCursorError.NotAllowed },
        { Other() with { AsOf = new DateOnly(2026, 10, 31) }, "asOf", FiReportCursorError.NotAllowed },
        { Other() with { From = null }, "from", FiReportCursorError.Required },
        { Other() with { Through = default(DateOnly) }, "through", FiReportCursorError.Required },
        { Other() with { Through = new DateOnly(2026, 9, 30) }, "through", FiReportCursorError.OutOfRange },
        { Other(OtherIncomeExpenseKind.Reversal), "kindFilter", FiReportCursorError.OutOfRange },
        { Other((OtherIncomeExpenseKind)99), "kindFilter", FiReportCursorError.OutOfRange },
        { Ar(currency: null), "currency", FiReportCursorError.Required },
        { Ar(currency: "twd"), "currency", FiReportCursorError.InvalidFormat },
        { Ar(currency: "TW"), "currency", FiReportCursorError.InvalidFormat },
        { Ar(currency: "TWDX"), "currency", FiReportCursorError.InvalidFormat },
        { Ar(pageSize: 0), "pageSize", FiReportCursorError.OutOfRange },
        { Ar(pageSize: FiReportCursorTokenRules.MaxPageSize + 1), "pageSize", FiReportCursorError.OutOfRange }
    };

    [Theory]
    [MemberData(nameof(InvalidBindings))]
    public void Invalid_binding_is_rejected_on_issue_and_on_validate(FiReportCursorBinding? binding, string field,
        FiReportCursorError error)
    {
        IssueRejected(binding, Pos, "binding", field, error);
        Rejected(FiReportCursorTokenRules.Validate(Issue(), binding, Key, Now, ExpiresAt), "binding", field, error);
    }

    [Fact]
    public void Same_day_other_range_is_valid()
    {
        var binding = Other() with { Through = new DateOnly(2026, 10, 1) };
        Assert.Equal(FiReportCursorValidationStatus.Valid, Validate(Issue(binding), binding).Status);
    }

    public static TheoryData<FiReportCursorPosition?, string> InvalidPositions() => new()
    {
        { null, "position" },
        { Pos with { CutoffSequence = 0 }, "cutoffSequence" },
        { Pos with { ManifestId = -1 }, "manifestId" },
        { Pos with { PageNumber = 1 }, "pageNumber" },
        { Pos with { PageNumber = FiReportCursorTokenRules.MaxPageNumber + 1 }, "pageNumber" }
    };

    [Theory]
    [MemberData(nameof(InvalidPositions))]
    public void Invalid_position_is_not_issued(FiReportCursorPosition? position, string field) =>
        IssueRejected(Ar(), position, "position", field,
            position is null ? FiReportCursorError.Required : FiReportCursorError.OutOfRange);

    // ---- Key misconfiguration (500) ----

    [Fact]
    public void Missing_or_short_key_is_misconfiguration_not_a_client_error()
    {
        foreach (var key in new[] { null, Array.Empty<byte>(), new byte[FiReportCursorTokenRules.MinKeyLength - 1] })
        {
            var issued = FiReportCursorTokenRules.Issue(Ar(), Pos, key);
            Assert.Equal(FiReportCursorIssueStatus.Misconfigured, issued.Status);
            Assert.Null(issued.Token);

            var validated = FiReportCursorTokenRules.Validate(Issue(), Ar(), key, Now, ExpiresAt);
            Assert.Equal(FiReportCursorValidationStatus.Misconfigured, validated.Status);
            Assert.Equal(new FiReportCursorFieldError("key", "key", FiReportCursorError.KeyInvalid),
                Assert.Single(validated.Errors));
        }

        Assert.Equal(FiReportCursorIssueStatus.Issued,
            FiReportCursorTokenRules.Issue(Ar(), Pos, new byte[FiReportCursorTokenRules.MinKeyLength]).Status);
    }

    [Fact]
    public void Caller_key_buffer_is_not_retained()
    {
        var key = Key.ToArray();
        var token = FiReportCursorTokenRules.Issue(Ar(), Pos, key).Token;
        key[0] ^= 0xFF;

        Rejected(FiReportCursorTokenRules.Validate(token, Ar(), key, Now, ExpiresAt), "token", "signature",
            FiReportCursorError.Tampered);
        Assert.Equal(FiReportCursorValidationStatus.Valid, Validate(token).Status);
    }
}
