using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Web;

namespace MyERP.Shared.Tests.Web;

public sealed class ErpProblemDetailsFactoryTests
{
    [Theory]
    [InlineData(typeof(BusinessRuleException), 422, "business-rule")]
    [InlineData(typeof(NotFoundException), 404, "not-found")]
    [InlineData(typeof(ConflictException), 409, "conflict")]
    [InlineData(typeof(ForbiddenException), 403, "forbidden")]
    [InlineData(typeof(UnauthenticatedException), 401, "unauthorized")]
    public void FromException_MapsErpExceptionKinds(Type exceptionType, int status, string typeSlug)
    {
        Exception ex = exceptionType.Name switch
        {
            nameof(BusinessRuleException) => new BusinessRuleException("SAL.ORDER.CREDIT_LIMIT_EXCEEDED", "信用額度不足"),
            nameof(NotFoundException) => new NotFoundException("SAL.ORDER.NOT_FOUND", "Order", 1),
            nameof(ConflictException) => new ConflictException(),
            nameof(UnauthenticatedException) => new UnauthenticatedException("SYS.AUTH.INVALID_CREDENTIALS", "denied"),
            _ => new ForbiddenException("SYS.PERMISSION.DENIED", "denied"),
        };

        var problem = ErpProblemDetailsFactory.FromException(ex, "en");

        problem.Status.Should().Be(status);
        problem.Type.Should().Be("https://myerp.local/errors/" + typeSlug);
        problem.Extensions.Should().ContainKey("errorCode");
        problem.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FromException_BusinessRule_UsesMessageAsDetailAndErrorCode()
    {
        var problem = ErpProblemDetailsFactory.FromException(new BusinessRuleException("SAL.ORDER.CREDIT_LIMIT_EXCEEDED", "信用額度不足"), "zh-Hant");

        problem.Detail.Should().Be("信用額度不足");
        problem.Extensions["errorCode"].Should().Be("SAL.ORDER.CREDIT_LIMIT_EXCEEDED");
    }

    [Fact]
    public void FromException_BusinessRule_CopiesExtensionsWithoutOverridingErrorCode()
    {
        var keys = new[] { new { itemId = 1L } };
        var ex = new BusinessRuleException("INV.STOCKTAKE.SNAPSHOT_STALE", "stale")
        {
            Extensions = new Dictionary<string, object?> { ["changedKeys"] = keys, ["errorCode"] = "HIJACK" },
        };

        var problem = ErpProblemDetailsFactory.FromException(ex, "en");

        problem.Extensions["changedKeys"].Should().BeSameAs(keys);
        problem.Extensions["errorCode"].Should().Be("INV.STOCKTAKE.SNAPSHOT_STALE");
    }

    [Fact]
    public void FromException_Validation_Returns400WithErrors()
    {
        var problem = ErpProblemDetailsFactory.FromException(new RequestValidationException("pageSize", "1-200"), "en");

        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Type.Should().EndWith("/validation");
        problem.Extensions["errors"].Should().BeAssignableTo<IReadOnlyDictionary<string, string[]>>()
            .Which.Should().ContainKey("pageSize");
    }

    [Fact]
    public void FromException_EfConcurrency_Returns409ConcurrencyConflict()
    {
        var problem = ErpProblemDetailsFactory.FromException(new DbUpdateConcurrencyException("db detail"), "en");

        problem.Status.Should().Be(409);
        problem.Extensions["errorCode"].Should().Be(CommonErrorCodes.ConcurrencyConflict);
        problem.Detail.Should().NotContain("db detail");
    }

    [Fact]
    public void FromException_Unexpected_Returns500WithoutLeakingDetails()
    {
        var problem = ErpProblemDetailsFactory.FromException(new InvalidOperationException("secret connection string"), "en");

        problem.Status.Should().Be(500);
        problem.Type.Should().EndWith("/internal");
        problem.Detail.Should().BeNull();
        problem.Extensions["errorCode"].Should().Be(CommonErrorCodes.InternalError);
        problem.Title.Should().NotContain("secret");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    public void FromException_BadHttpRequest_KeepsStatus(int status)
    {
        var problem = ErpProblemDetailsFactory.FromException(new BadHttpRequestException("framework detail", status), "en");

        problem.Status.Should().Be(status);
        problem.Detail.Should().BeNull();
    }

    [Fact]
    public void FromStatus_UnknownClientError_FallsBackToGenericTitle()
    {
        var problem = ErpProblemDetailsFactory.FromStatus(418, "en");

        problem.Title.Should().Be("Invalid input");
        problem.Extensions.Should().NotContainKey("errorCode");
    }

    [Fact]
    public void FromStatus_NotFound_HasCommonErrorCode()
    {
        ErpProblemDetailsFactory.FromStatus(404, "en").Extensions["errorCode"].Should().Be(CommonErrorCodes.NotFound);
    }

    [Fact]
    public void FromStatus_TooManyRequests_HasRateLimitedErrorCode()
    {
        var problem = ErpProblemDetailsFactory.FromStatus(429, "zh-Hans");

        problem.Extensions["errorCode"].Should().Be(CommonErrorCodes.RateLimited);
        problem.Title.Should().Be("请求过于频繁，请稍后再试");
    }

    [Theory]
    [InlineData("zh-Hant", "業務規則不允許此操作")]
    [InlineData("zh-TW", "業務規則不允許此操作")]
    [InlineData("zh-Hans", "业务规则不允许此操作")]
    [InlineData("zh-CN", "业务规则不允许此操作")]
    [InlineData("en", "Business rule violated")]
    [InlineData("en-US", "Business rule violated")]
    [InlineData("fr", "業務規則不允許此操作")]
    [InlineData(null, "業務規則不允許此操作")]
    public void Title_IsLocalizedByLanguage(string? language, string expected)
    {
        ErpProblemDetailsFactory.FromException(new BusinessRuleException("SAL.ORDER.X_Y", "d"), language).Title.Should().Be(expected);
    }

    [Theory]
    [InlineData(400, "validation")]
    [InlineData(401, "unauthorized")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not-found")]
    [InlineData(405, "method-not-allowed")]
    [InlineData(409, "conflict")]
    [InlineData(422, "business-rule")]
    [InlineData(429, "rate-limited")]
    [InlineData(500, "internal")]
    [InlineData(503, "internal")]
    public void TypeForStatus_FollowsApiConventions(int status, string slug)
    {
        ErpProblemDetailsFactory.TypeForStatus(status).Should().Be("https://myerp.local/errors/" + slug);
    }

    [Theory]
    [InlineData("zh-TW,zh;q=0.9,en;q=0.8", "zh-Hant")]
    [InlineData("en-US,en;q=0.9", "en")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh", "zh-Hant")]
    [InlineData("de-DE", "zh-Hant")]
    [InlineData("en;q=0.5,zh-CN;q=0.9", "zh-Hans")]
    [InlineData("fr-FR,en-GB;q=0.8", "en")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData(";;;", "zh-Hant")]
    [InlineData("", "zh-Hant")]
    public void ResolveLanguage_PicksSupportedLanguage(string header, string expected)
    {
        ErpProblemDetailsFactory.ResolveLanguage(header).Should().Be(expected);
    }
}
