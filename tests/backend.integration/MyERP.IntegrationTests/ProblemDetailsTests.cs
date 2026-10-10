using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests;

/// <summary>全域例外處理：例外 → ProblemDetails（api-conventions.md 第 4 節）。</summary>
public sealed class ProblemDetailsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private HttpClient CreateClient() =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IStartupFilter, ThrowingEndpointStartupFilter>()))
            .CreateClient();

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (language is not null)
        {
            request.Headers.Add("Accept-Language", language);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BusinessRuleException_Returns422WithErrorCodeAndTraceId()
    {
        using var client = CreateClient();

        using var response = await GetAsync(client, "/__test/throw/business", "en");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("SAL.ORDER.CREDIT_LIMIT_EXCEEDED");
        problem.GetProperty("detail").GetString().Should().Be("credit limit exceeded");
        problem.GetProperty("title").GetString().Should().Be("Business rule violated");
        problem.GetProperty("type").GetString().Should().Be("https://myerp.local/errors/business-rule");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        problem.GetProperty("instance").GetString().Should().Be("/__test/throw/business");
    }

    [Fact]
    public async Task ValidationException_Returns400WithFieldErrors()
    {
        using var client = CreateClient();

        using var response = await GetAsync(client, "/__test/throw/validation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errors").GetProperty("pageSize")[0].GetString().Should().Be("1-200");
        problem.GetProperty("title").GetString().Should().Be("輸入資料不正確");
    }

    [Fact]
    public async Task UnexpectedException_Returns500WithoutDetails()
    {
        using var client = CreateClient();

        using var response = await GetAsync(client, "/__test/throw/unexpected");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().NotContain("secret-connection-string");
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be(CommonErrorCodes.InternalError);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        using var client = CreateClient();

        using var response = await GetAsync(client, "/api/v1/does-not-exist", "zh-Hans");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("type").GetString().Should().Be("https://myerp.local/errors/not-found");
        problem.GetProperty("title").GetString().Should().Be("资源不存在");
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>在應用程式管線最後加入會拋例外的測試端點（位於全域例外處理之內）。</summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((HttpContext context, RequestDelegate nextDelegate) => context.Request.Path.Value switch
            {
                "/__test/throw/business" => throw new BusinessRuleException("SAL.ORDER.CREDIT_LIMIT_EXCEEDED", "credit limit exceeded"),
                "/__test/throw/validation" => throw new RequestValidationException("pageSize", "1-200"),
                "/__test/throw/unexpected" => throw new InvalidOperationException("secret-connection-string"),
                _ => nextDelegate(context),
            });
        };
    }
}
