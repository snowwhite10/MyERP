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
}
