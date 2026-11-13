using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MyERP.Shared.Infrastructure.Web;

namespace MyERP.Shared.Tests.Web;

public sealed class HttpContextAccessorsTests
{
    private static HttpContextAccessor Accessor(HttpContext? context) => new() { HttpContext = context };

    private static DefaultHttpContext Authenticated(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")) };

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public void CurrentUser_ReadsUserIdFromClaim(string claimType)
    {
        var user = new HttpCurrentUser(Accessor(Authenticated(new Claim(claimType, "42"))));

        user.UserId.Should().Be(42);
        user.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void CurrentUser_NoHttpContext_IsAnonymous()
    {
        var user = new HttpCurrentUser(Accessor(null));

        user.UserId.Should().BeNull();
        user.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void CurrentUser_UnauthenticatedIdentity_IsAnonymousEvenWithClaim()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "42")])) };

        new HttpCurrentUser(Accessor(context)).UserId.Should().BeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public void CurrentUser_InvalidUserId_IsAnonymous(string value)
    {
        new HttpCurrentUser(Accessor(Authenticated(new Claim("sub", value)))).UserId.Should().BeNull();
    }

    [Fact]
    public void CurrentCompany_ReadsHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HttpCurrentCompany.HeaderName] = "12";

        new HttpCurrentCompany(Accessor(context)).CompanyId.Should().Be(12);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,2")]
    public void CurrentCompany_MissingOrInvalidHeader_IsNull(string? value)
    {
        var context = new DefaultHttpContext();
        if (value is not null)
        {
            context.Request.Headers[HttpCurrentCompany.HeaderName] = value;
        }

        new HttpCurrentCompany(Accessor(context)).CompanyId.Should().BeNull();
    }

    [Fact]
    public void CurrentCompany_NoHttpContext_IsNull()
    {
        new HttpCurrentCompany(Accessor(null)).CompanyId.Should().BeNull();
    }

    [Fact]
    public void CurrentCompany_NoHttpContext_UsesSeedCompanyContextUntilDisposed()
    {
        // ADR-105：種子程序沒有 HTTP 請求，以 SeedCompanyContext 指定公司；巢狀範圍結束後恢復外層值。
        var company = new HttpCurrentCompany(Accessor(null));
        using (Infrastructure.Seeding.SeedCompanyContext.Enter(5))
        {
            company.CompanyId.Should().Be(5);
            using (Infrastructure.Seeding.SeedCompanyContext.Enter(9))
                company.CompanyId.Should().Be(9);
            company.CompanyId.Should().Be(5);
        }
        company.CompanyId.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    public void CurrentCompany_DuringHttpRequest_IgnoresSeedCompanyContext(string? header)
    {
        // 請求中的程式碼不能藉種子上下文切換公司：一律只看標頭（沒有標頭即 null）。
        var context = new DefaultHttpContext();
        if (header is not null)
            context.Request.Headers[HttpCurrentCompany.HeaderName] = header;
        using (Infrastructure.Seeding.SeedCompanyContext.Enter(99))
        {
            new HttpCurrentCompany(Accessor(context)).CompanyId.Should().Be(header is null ? null : 12);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SeedCompanyContext_RejectsNonPositiveCompany(long companyId)
    {
        var act = () => Infrastructure.Seeding.SeedCompanyContext.Enter(companyId);
        act.Should().Throw<ArgumentOutOfRangeException>();
        Infrastructure.Seeding.SeedCompanyContext.CompanyId.Should().BeNull();
    }
}
