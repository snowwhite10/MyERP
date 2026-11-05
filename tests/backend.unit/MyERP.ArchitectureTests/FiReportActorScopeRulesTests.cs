using System.Security.Cryptography;
using System.Text;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-006（actor scope 摘要 U 層，BR-FI-130／131、ADR-097、WU-BI-001a7）：<c>FiReportActorScopeRules.Digest</c>
/// 以固定正規化（角色與夥伴 ID 排序去重、invariant 數字、版本前綴＋網域字串）產生 ADR-093
/// <c>FiReportCursorBinding.ActorScopeDigest</c> 所需的 64 位小寫 hex SHA-256。相同範圍不同順序或重複摘要不變；
/// 新增／移除夥伴、全部↔部分、角色撤銷、不同 actor／公司／種類摘要必變；無效輸入拒絕且不產生摘要。
/// 不讀 SYS 權限資料，不涉及 SQL、API 或畫面。
/// </summary>
public sealed class FiReportActorScopeRulesTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 4, 30, 0, TimeSpan.FromHours(8));

    private static string D(long actor = 11, long company = 7, FiReportKind kind = FiReportKind.ArDetail,
        long[]? roles = null, FiReportPartnerScope? scope = null)
    {
        var r = FiReportActorScopeRules.Digest(actor, company, kind, roles ?? [3, 5],
            scope ?? FiReportPartnerScope.Partners([55, 66]));
        Assert.Empty(r.Errors);
        Assert.NotNull(r.Digest);
        return r.Digest;
    }

    private static FiReportActorScopeResult R(long actor = 11, long company = 7,
        FiReportKind kind = FiReportKind.ArDetail, IReadOnlyCollection<long>? roles = null,
        FiReportPartnerScope? scope = null, bool nullRoles = false, bool nullScope = false) =>
        FiReportActorScopeRules.Digest(actor, company, kind, nullRoles ? null : roles ?? [3, 5],
            nullScope ? null : scope ?? FiReportPartnerScope.Partners([55, 66]));

    private static void Rejected(FiReportActorScopeResult r, string field, FiReportActorScopeError error)
    {
        Assert.Null(r.Digest);
        Assert.False(r.IsValid);
        Assert.Contains(new FiReportActorScopeFieldError(field, error), r.Errors);
    }

    private static FiReportCursorBinding Ar(string digest) =>
        new(7, digest, FiReportKind.ArDetail, 55, new DateOnly(2026, 10, 31), null, null, null, "TWD", 50);

    // ---- normal ----

    [Fact]
    public void Digest_is_64_lowercase_hex_accepted_by_cursor_binding()
    {
        var digest = D();
        Assert.Equal(64, digest.Length);
        Assert.All(digest, c => Assert.True(c is >= '0' and <= '9' or >= 'a' and <= 'f'));
        var errors = FiReportCursorTokenRules.Issue(Ar(digest), new FiReportCursorPosition(900, 4321, 2), Key).Errors;
        Assert.Empty(errors);
    }

    [Fact]
    public void Digest_is_deterministic()
    {
        Assert.Equal(D(), D());
        var r = R();
        Assert.True(r.IsValid);
    }

    [Fact]
    public void Digest_is_sha256_of_domain_and_canonical_text()
    {
        var text = "v1|actor=11|company=7|kind=ArDetail|roles=3,5|partners=55,66";
        var expected = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(FiReportActorScopeRules.HashDomain + text)));
        Assert.Equal("MyERP.FI.ReportActorScope.v1\n", FiReportActorScopeRules.HashDomain);
        Assert.Equal(text, FiReportActorScopeRules.Canonical(11, 7, FiReportKind.ArDetail, [5, 3],
            FiReportPartnerScope.Partners([66, 55, 66])));
        Assert.Equal(expected, D());
    }

    [Fact]
    public void All_partners_canonical_text_uses_star()
    {
        Assert.Equal("v1|actor=11|company=7|kind=OtherIncomeExpense|roles=3|partners=*",
            FiReportActorScopeRules.Canonical(11, 7, FiReportKind.OtherIncomeExpense, [3], FiReportPartnerScope.All));
    }

    [Theory]
    [InlineData(FiReportKind.ArDetail, "ArDetail")]
    [InlineData(FiReportKind.ApDetail, "ApDetail")]
    [InlineData(FiReportKind.OtherIncomeExpense, "OtherIncomeExpense")]
    public void Canonical_text_pins_kind_name_not_number(FiReportKind kind, string name)
    {
        Assert.Equal($"v1|actor=11|company=7|kind={name}|roles=3|partners=*",
            FiReportActorScopeRules.Canonical(11, 7, kind, [3], FiReportPartnerScope.All));
    }

    [Fact]
    public void Same_scope_in_different_order_or_with_duplicates_has_same_digest()
    {
        var baseline = D(roles: [3, 5], scope: FiReportPartnerScope.Partners([55, 66]));
        Assert.Equal(baseline, D(roles: [5, 3], scope: FiReportPartnerScope.Partners([66, 55])));
        Assert.Equal(baseline, D(roles: [5, 3, 5, 3], scope: FiReportPartnerScope.Partners([66, 55, 55, 66])));
    }

    [Fact]
    public void All_scope_digest_is_stable()
    {
        Assert.Equal(D(scope: FiReportPartnerScope.All), D(scope: FiReportPartnerScope.All));
    }

    // ---- changes must change the digest ----

    [Fact]
    public void Adding_a_partner_changes_digest()
    {
        Assert.NotEqual(D(), D(scope: FiReportPartnerScope.Partners([55, 66, 77])));
    }

    [Fact]
    public void Removing_a_partner_changes_digest()
    {
        Assert.NotEqual(D(), D(scope: FiReportPartnerScope.Partners([55])));
    }

    [Fact]
    public void All_versus_partial_changes_digest()
    {
        Assert.NotEqual(D(), D(scope: FiReportPartnerScope.All));
    }

    [Fact]
    public void Revoking_a_role_changes_digest()
    {
        Assert.NotEqual(D(), D(roles: [3]));
        Assert.NotEqual(D(), D(roles: [3, 5, 9]));
    }

    [Theory]
    [InlineData(12, 7, FiReportKind.ArDetail)]
    [InlineData(11, 8, FiReportKind.ArDetail)]
    [InlineData(11, 7, FiReportKind.ApDetail)]
    [InlineData(11, 7, FiReportKind.OtherIncomeExpense)]
    public void Different_actor_company_or_kind_changes_digest(long actor, long company, FiReportKind kind)
    {
        Assert.NotEqual(D(), D(actor, company, kind));
    }

    [Fact]
    public void Number_boundaries_are_unambiguous()
    {
        // roles {1,23} vs {12,3} and partners {1,23} vs {12,3} must not collide.
        Assert.NotEqual(D(roles: [1, 23]), D(roles: [12, 3]));
        Assert.NotEqual(D(scope: FiReportPartnerScope.Partners([1, 23])),
            D(scope: FiReportPartnerScope.Partners([12, 3])));
        // Role IDs must not be mistaken for partner IDs.
        Assert.NotEqual(D(roles: [3], scope: FiReportPartnerScope.Partners([5, 55, 66])),
            D(roles: [3, 5], scope: FiReportPartnerScope.Partners([55, 66])));
    }

    [Fact]
    public void Extreme_ids_are_accepted()
    {
        Assert.Equal(64, D(long.MaxValue, long.MaxValue, roles: [1, long.MaxValue],
            scope: FiReportPartnerScope.Partners([1, long.MaxValue])).Length);
    }

    [Fact]
    public void Max_partner_and_role_counts_are_accepted()
    {
        var partners = Enumerable.Range(1, FiReportActorScopeRules.MaxPartners).Select(i => (long)i).ToArray();
        var roles = Enumerable.Range(1, FiReportActorScopeRules.MaxRoles).Select(i => (long)i).ToArray();
        Assert.Equal(64, D(roles: roles, scope: FiReportPartnerScope.Partners(partners)).Length);
    }

    [Fact]
    public void Count_limit_applies_after_deduplication()
    {
        var partners = Enumerable.Range(1, FiReportActorScopeRules.MaxPartners).Select(i => (long)i)
            .Concat([1, 2, 3]).ToArray();
        Assert.True(R(scope: FiReportPartnerScope.Partners(partners)).IsValid);
    }

    // ---- invalid input ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_actor_is_rejected(long actor)
    {
        Rejected(R(actor: actor), "actorUserId", FiReportActorScopeError.OutOfRange);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public void Non_positive_company_is_rejected(long company)
    {
        Rejected(R(company: company), "companyId", FiReportActorScopeError.OutOfRange);
    }

    [Fact]
    public void Undefined_kind_is_rejected()
    {
        Rejected(R(kind: (FiReportKind)99), "kind", FiReportActorScopeError.OutOfRange);
    }

    [Fact]
    public void Missing_or_empty_roles_are_rejected()
    {
        Rejected(R(nullRoles: true), "roleIds", FiReportActorScopeError.Required);
        Rejected(R(roles: []), "roleIds", FiReportActorScopeError.Required);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Non_positive_role_is_rejected(long role)
    {
        Rejected(R(roles: [3, role]), "roleIds", FiReportActorScopeError.OutOfRange);
    }

    [Fact]
    public void Too_many_roles_are_rejected()
    {
        var roles = Enumerable.Range(1, FiReportActorScopeRules.MaxRoles + 1).Select(i => (long)i).ToArray();
        Rejected(R(roles: roles), "roleIds", FiReportActorScopeError.OutOfRange);
    }

    [Fact]
    public void Missing_partner_scope_is_rejected()
    {
        Rejected(R(nullScope: true), "partnerScope", FiReportActorScopeError.Required);
    }

    [Fact]
    public void Empty_partial_scope_is_rejected()
    {
        Rejected(R(scope: FiReportPartnerScope.Partners([])), "partnerIds", FiReportActorScopeError.Required);
        Rejected(R(scope: new FiReportPartnerScope(false, null)), "partnerIds", FiReportActorScopeError.Required);
    }

    [Fact]
    public void All_scope_with_partner_list_is_rejected()
    {
        Rejected(R(scope: new FiReportPartnerScope(true, [55])), "partnerIds", FiReportActorScopeError.NotAllowed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-55)]
    public void Non_positive_partner_is_rejected(long partner)
    {
        Rejected(R(scope: FiReportPartnerScope.Partners([55, partner])), "partnerIds",
            FiReportActorScopeError.OutOfRange);
    }

    [Fact]
    public void Too_many_partners_are_rejected()
    {
        var partners = Enumerable.Range(1, FiReportActorScopeRules.MaxPartners + 1).Select(i => (long)i).ToArray();
        Rejected(R(scope: FiReportPartnerScope.Partners(partners)), "partnerIds", FiReportActorScopeError.OutOfRange);
    }

    [Fact]
    public void All_errors_are_reported_in_fixed_order()
    {
        var r = FiReportActorScopeRules.Digest(0, 0, (FiReportKind)99, [], FiReportPartnerScope.Partners([]));
        Assert.Null(r.Digest);
        Assert.Equal(
        [
            new FiReportActorScopeFieldError("actorUserId", FiReportActorScopeError.OutOfRange),
            new FiReportActorScopeFieldError("companyId", FiReportActorScopeError.OutOfRange),
            new FiReportActorScopeFieldError("kind", FiReportActorScopeError.OutOfRange),
            new FiReportActorScopeFieldError("roleIds", FiReportActorScopeError.Required),
            new FiReportActorScopeFieldError("partnerIds", FiReportActorScopeError.Required)
        ], r.Errors);
    }

    [Fact]
    public void Canonical_rejects_invalid_input()
    {
        Assert.Throws<ArgumentException>(() =>
            FiReportActorScopeRules.Canonical(0, 7, FiReportKind.ArDetail, [3], FiReportPartnerScope.All));
    }

    // ---- token chaining (BR-FI-131) ----

    [Fact]
    public void Scope_change_rejects_old_token_as_token_invalid()
    {
        var before = D();
        var token = FiReportCursorTokenRules.Issue(Ar(before), new FiReportCursorPosition(900, 4321, 2), Key).Token;
        Assert.NotNull(token);

        // Same scope recomputed in a different order still validates.
        var same = D(roles: [5, 3], scope: FiReportPartnerScope.Partners([66, 55]));
        var ok = FiReportCursorTokenRules.Validate(token, Ar(same), Key, Now, Now.AddMinutes(5));
        Assert.Equal(FiReportCursorValidationStatus.Valid, ok.Status);
        Assert.Null(FiReportProblemRules.Map(ok));

        foreach (var after in new[]
                 {
                     D(scope: FiReportPartnerScope.Partners([55])),
                     D(scope: FiReportPartnerScope.All),
                     D(roles: [3])
                 })
        {
            var result = FiReportCursorTokenRules.Validate(token, Ar(after), Key, Now, Now.AddMinutes(5));
            Assert.Equal(FiReportCursorValidationStatus.Rejected, result.Status);
            Assert.Contains(new FiReportCursorFieldError("binding", "actorScopeDigest", FiReportCursorError.Mismatch),
                result.Errors);
            var problem = FiReportProblemRules.Map(result);
            Assert.NotNull(problem);
            Assert.Equal(400, problem.Status);
            Assert.Equal(FiReportProblemRules.TokenInvalid, problem.ErrorCode);
        }
    }
}
