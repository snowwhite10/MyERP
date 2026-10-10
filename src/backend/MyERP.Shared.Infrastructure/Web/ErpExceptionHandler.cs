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

        // 用戶端中斷連線：不是系統錯誤，不寫 Error 日誌，也不需要回應
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger, httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        var language = ErpProblemDetailsFactory.ResolveLanguage(httpContext.Request.Headers.AcceptLanguage);
        var problem = ErpProblemDetailsFactory.FromException(exception, language);

        if (exception is ErpException erp)
        {
            LogBusinessError(logger, erp.ErrorCode, erp.Kind, httpContext.Request.Path);
        }
        else if (problem.Status < StatusCodes.Status500InternalServerError)
        {
            LogBadRequest(logger, problem.Status!.Value, httpContext.Request.Method, httpContext.Request.Path);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Bad request {Status} for {Method} {Path}")]
    private static partial void LogBadRequest(ILogger logger, int status, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request aborted by client: {Method} {Path}")]
    private static partial void LogRequestAborted(ILogger logger, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
