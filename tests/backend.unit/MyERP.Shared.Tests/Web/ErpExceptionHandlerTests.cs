using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Web;

namespace MyERP.Shared.Tests.Web;

public sealed class ErpExceptionHandlerTests
{
    private readonly Mock<IProblemDetailsService> _problemDetails = new();
    private ProblemDetailsContext? _written;

    public ErpExceptionHandlerTests()
    {
        _problemDetails.Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => _written = c)
            .ReturnsAsync(true);
    }

    private ErpExceptionHandler Handler() => new(_problemDetails.Object, NullLogger<ErpExceptionHandler>.Instance);

    [Fact]
    public async Task BusinessRule_WritesProblemWithStatusAndLanguage()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "en-US";

        var handled = await Handler().TryHandleAsync(context, new BusinessRuleException("INV.STOCK.INSUFFICIENT", "short"), TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(422);
        _written!.ProblemDetails.Title.Should().Be("Business rule violated");
        _written.ProblemDetails.Extensions["errorCode"].Should().Be("INV.STOCK.INSUFFICIENT");
    }

    [Fact]
    public async Task BadHttpRequest_KeepsClientErrorStatus()
    {
        var context = new DefaultHttpContext();

        await Handler().TryHandleAsync(context, new BadHttpRequestException("bad json", 400), TestContext.Current.CancellationToken);

        context.Response.StatusCode.Should().Be(400);
        _written!.ProblemDetails.Detail.Should().BeNull("framework messages are not shown to users");
    }

    [Fact]
    public async Task Unexpected_Writes500()
    {
        var context = new DefaultHttpContext();

        await Handler().TryHandleAsync(context, new InvalidOperationException("boom"), TestContext.Current.CancellationToken);

        context.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task ClientAborted_IsHandledWithoutWritingResponse()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token };

        var handled = await Handler().TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        handled.Should().BeTrue();
        _written.Should().BeNull();
    }

    [Fact]
    public async Task CancellationWithoutClientAbort_IsServerError()
    {
        // 例如內部逾時：用戶端仍在等待，必須回 500 而非靜默
        var context = new DefaultHttpContext();

        await Handler().TryHandleAsync(context, new OperationCanceledException(), TestContext.Current.CancellationToken);

        context.Response.StatusCode.Should().Be(500);
    }
}
