using MyERP.Shared.Errors;

namespace MyERP.Shared.Tests.Errors;

public sealed class ErpExceptionTests
{
    [Theory]
    [InlineData("SAL.ORDER.CREDIT_LIMIT_EXCEEDED")]
    [InlineData("INV.STOCK.INSUFFICIENT")]
    [InlineData("COMMON.CONCURRENCY.CONFLICT")]
    [InlineData("SAL.PRICE_LIST.ITEM.DUPLICATED")]
    public void ErrorCode_ValidFormat_IsAccepted(string code)
    {
        new BusinessRuleException(code, "detail").ErrorCode.Should().Be(code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sal.order.x")]
    [InlineData("SAL.ORDER")]
    [InlineData("SAL..X")]
    [InlineData("SAL.ORDER.X-Y")]
    [InlineData("SAL.ORDER.X ")]
    public void ErrorCode_InvalidFormat_Throws(string code)
    {
        var act = () => new BusinessRuleException(code, "detail");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BusinessRuleException_CarriesMessageAndKind()
    {
        var ex = new BusinessRuleException("SAL.ORDER.CREDIT_LIMIT_EXCEEDED", "信用額度不足");

        ex.Message.Should().Be("信用額度不足");
        ex.Kind.Should().Be(ErpErrorKind.BusinessRule);
    }

    [Fact]
    public void NotFoundException_DescribesResource()
    {
        var ex = new NotFoundException("SAL.ORDER.NOT_FOUND", "Order", 42);

        ex.Kind.Should().Be(ErpErrorKind.NotFound);
        ex.Message.Should().Contain("Order").And.Contain("42");
    }

    [Fact]
    public void ConflictException_DefaultsToConcurrencyCode()
    {
        var ex = new ConflictException();

        ex.Kind.Should().Be(ErpErrorKind.Conflict);
        ex.ErrorCode.Should().Be(CommonErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public void ForbiddenException_HasForbiddenKind()
    {
        new ForbiddenException("SYS.PERMISSION.DENIED", "no").Kind.Should().Be(ErpErrorKind.Forbidden);
    }

    [Fact]
    public void RequestValidationException_SingleField_BuildsErrorDictionary()
    {
        var ex = new RequestValidationException("pageSize", "must be between 1 and 200");

        ex.Kind.Should().Be(ErpErrorKind.Validation);
        ex.ErrorCode.Should().Be(CommonErrorCodes.ValidationFailed);
        ex.Errors.Should().ContainKey("pageSize").WhoseValue.Should().Equal("must be between 1 and 200");
    }

    [Fact]
    public void RequestValidationException_CopiesErrorsDefensively()
    {
        var source = new Dictionary<string, string[]> { ["a"] = ["x"] };
        var ex = new RequestValidationException(source);

        source["b"] = ["y"];

        ex.Errors.Should().ContainSingle();
    }

    [Fact]
    public void RequestValidationException_EmptyErrors_Throws()
    {
        var act = () => new RequestValidationException(new Dictionary<string, string[]>());

        act.Should().Throw<ArgumentException>();
    }
}
