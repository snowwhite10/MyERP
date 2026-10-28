using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyERP.IntegrationTests;

public sealed class SalesOrderHttpTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ManualCreateAndDetailRoutesRequireAuthenticationAndAppearInOpenApi()
    {
        using var client = factory.CreateClient();
        using var list = await client.GetAsync("/api/v1/sal/orders", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using var detail = await client.GetAsync("/api/v1/sal/orders/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        using var create = await client.PostAsJsonAsync("/api/v1/sal/orders", new
        {
            customerId = 21, businessDate = "2028-02-29", currencyCode = "TWD",
            lines = new[] { new { lineNo = 1, itemId = 31, quantity = 1m,
                unitPrice = 10m, discountRate = 0m, taxRateId = 41 } }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        using var edit = await client.PutAsJsonAsync("/api/v1/sal/orders/1", new
        {
            version = 1, draft = new { customerId = 21, businessDate = "2028-02-29",
                currencyCode = "TWD", lines = new[] { new { lineNo = 1, itemId = 31,
                    quantity = 1m, unitPrice = 10m, discountRate = 0m, taxRateId = 41 } } }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);
        using var delete = await client.DeleteAsync("/api/v1/sal/orders/1?version=1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        using var openApi = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        openApi.EnsureSuccessStatusCode();
        var document = await openApi.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("/api/v1/sal/orders", document, StringComparison.Ordinal);
        Assert.Contains("/api/v1/sal/orders/{id}", document, StringComparison.Ordinal);
        Assert.Contains("\"put\"", document, StringComparison.Ordinal);
        Assert.Contains("\"delete\"", document, StringComparison.Ordinal);
    }
}
