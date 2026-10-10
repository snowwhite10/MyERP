using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MyERP.Shared.Infrastructure.Web;

/// <summary>ProblemDetails 註冊與中介層（api-conventions.md 第 4 節）。</summary>
public static class ErpProblemDetailsSetup
{
    public static IServiceCollection AddErpProblemDetails(this IServiceCollection services)
    {
        services.AddExceptionHandler<ErpExceptionHandler>();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Customize);
        return services;
    }

    /// <summary>例外處理 + 狀態碼頁（例如路由不存在的 404 也回 ProblemDetails）。須放在管線最前面。</summary>
    public static IApplicationBuilder UseErpProblemDetails(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    // 所有 ProblemDetails（含框架產生的）統一補上 type / title（三語）/ instance / traceId / errorCode
    private static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var http = context.HttpContext;
        var status = problem.Status ?? http.Response.StatusCode;

        if (context.Exception is null && problem.Extensions.ContainsKey("errorCode") is false)
        {
            var language = ErpProblemDetailsFactory.ResolveLanguage(http.Request.Headers.AcceptLanguage);
            var standard = ErpProblemDetailsFactory.FromStatus(status, language);
            problem.Type = standard.Type;
            problem.Title = standard.Title;
            foreach (var (key, value) in standard.Extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        problem.Instance ??= http.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
    }
}
