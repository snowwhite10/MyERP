using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MyERP.Shared.Errors;

namespace MyERP.Shared.Infrastructure.Web;

/// <summary>全域例外處理：一律轉為 ProblemDetails；Controller 不寫 try/catch。</summary>
public sealed partial class ErpExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ErpExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var language = ErpProblemDetailsFactory.ResolveLanguage(httpContext.Request.Headers.AcceptLanguage);
        var problem = ErpProblemDetailsFactory.FromException(exception, language);

        if (exception is ErpException erp)
        {
            LogBusinessError(logger, erp.ErrorCode, erp.Kind, httpContext.Request.Path);
        }
        else
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        }).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Business error {ErrorCode} ({Kind}) at {Path}")]
    private static partial void LogBusinessError(ILogger logger, string errorCode, ErpErrorKind kind, PathString path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
