using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using MyERP.Modules.Finance.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>
/// FI-RPT-014（U 層）：僅供匯出的 token（WU-BI-001a12、ADR-102）。匯出與續頁用不同 MAC domain 區分，互相驗不過
/// （signature Tampered → 400）；匯出 token 位置固定第 1 頁，續頁 token 仍須第 2 頁起；預設用途為續頁，ADR-093 不變。
/// </summary>
public sealed class FiReportCursorTokenPurposeTests
{
    // 固定測試位元組，不是真金鑰。
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 3, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset ExpiresAt = Now.AddMinutes(30);

    private static readonly FiReportCursorBinding Binding = new(7, new string('a', 64), FiReportKind.ArDetail, 55,
        new DateOnly(2026, 10, 31), null, null, null, "TWD", 50);

    private static readonly FiReportCursorPosition First = new(900, 4321, 1);

    private static string Issue(FiReportCursorPosition position, FiReportCursorPurpose purpose)
    {
        var r = FiReportCursorTokenRules.Issue(Binding, position, Key, purpose);
        Assert.True(r.Status == FiReportCursorIssueStatus.Issued, string.Join("; ", r.Errors));
        return r.Token!;
    }

    private static FiReportCursorValidationResult Validate(string? token, FiReportCursorPurpose purpose) =>
        FiReportCursorTokenRules.Validate(token, Binding, Key, Now, ExpiresAt, purpose);

    [Fact]
    public void Export_token_for_page_one_round_trips_under_the_export_purpose()
    {
        var token = Issue(First, FiReportCursorPurpose.Export);

        var r = Validate(token, FiReportCursorPurpose.Export);

        Assert.Equal(FiReportCursorValidationStatus.Valid, r.Status);
        Assert.Equal(First, r.Position);
    }

    [Fact]
    public void Export_token_is_tampered_under_the_continuation_purpose_and_by_default()
    {
        var token = Issue(First, FiReportCursorPurpose.Export);

        var expected = new FiReportCursorFieldError("token", "signature", FiReportCursorError.Tampered);
        Assert.Equal([expected], Validate(token, FiReportCursorPurpose.Continuation).Errors);
        Assert.Equal([expected], FiReportCursorTokenRules.Validate(token, Binding, Key, Now, ExpiresAt).Errors);
    }

    [Fact]
    public void Continuation_token_is_tampered_under_the_export_purpose()
    {
        var token = Issue(First with { PageNumber = 2 }, FiReportCursorPurpose.Continuation);

        var r = Validate(token, FiReportCursorPurpose.Export);

        Assert.Equal(FiReportCursorValidationStatus.Rejected, r.Status);
        Assert.Equal([new FiReportCursorFieldError("token", "signature", FiReportCursorError.Tampered)], r.Errors);
    }

    [Fact]
    public void Default_purpose_is_continuation_and_issues_the_same_token()
    {
        var position = First with { PageNumber = 2 };

        Assert.Equal(FiReportCursorTokenRules.Issue(Binding, position, Key).Token,
            Issue(position, FiReportCursorPurpose.Continuation));
        Assert.NotEqual(Issue(position with { PageNumber = 1 }, FiReportCursorPurpose.Export),
            FiReportCursorTokenRules.Issue(Binding, position, Key).Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Export_token_must_point_at_page_one(int page)
    {
        var r = FiReportCursorTokenRules.Issue(Binding, First with { PageNumber = page }, Key,
            FiReportCursorPurpose.Export);

        Assert.Equal(FiReportCursorIssueStatus.Rejected, r.Status);
        Assert.Null(r.Token);
        Assert.Contains(new FiReportCursorFieldError("position", "pageNumber", FiReportCursorError.OutOfRange),
            r.Errors);
    }

    [Fact]
    public void Continuation_token_still_rejects_page_one()
    {
        var r = FiReportCursorTokenRules.Issue(Binding, First, Key, FiReportCursorPurpose.Continuation);

        Assert.Equal(FiReportCursorIssueStatus.Rejected, r.Status);
        Assert.Contains(new FiReportCursorFieldError("position", "pageNumber", FiReportCursorError.OutOfRange),
            r.Errors);
    }

    [Fact]
    public void Forged_export_payload_with_page_two_is_malformed_even_with_a_valid_export_mac()
    {
        var page2 = Issue(First with { PageNumber = 2 }, FiReportCursorPurpose.Continuation);
        var payload = Base64Url.DecodeFromChars(page2.Split('.')[0]);
        var mac = HMACSHA256.HashData(Key,
            Encoding.UTF8.GetBytes(FiReportCursorTokenRules.ExportMacDomain).Concat(payload).ToArray());
        var forged = page2.Split('.')[0] + "." + Base64Url.EncodeToString(mac);

        var r = Validate(forged, FiReportCursorPurpose.Export);

        Assert.Equal([new FiReportCursorFieldError("token", "payload", FiReportCursorError.Malformed)], r.Errors);
    }

    [Fact]
    public void Undefined_purpose_is_a_misconfiguration_500()
    {
        var issued = FiReportCursorTokenRules.Issue(Binding, First, Key, (FiReportCursorPurpose)9);
        var validated = Validate(Issue(First, FiReportCursorPurpose.Export), (FiReportCursorPurpose)9);

        var expected = new FiReportCursorFieldError("key", "purpose", FiReportCursorError.KeyInvalid);
        Assert.Equal(FiReportCursorIssueStatus.Misconfigured, issued.Status);
        Assert.Null(issued.Token);
        Assert.Equal([expected], issued.Errors);
        Assert.Equal(FiReportCursorValidationStatus.Misconfigured, validated.Status);
        Assert.Equal([expected], validated.Errors);
    }
}
