using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyERP.IntegrationTests;

public sealed class RequisitionHttpTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("/api/v1/pur/requisitions")]
    [InlineData("/api/v1/pur/requisitions/1")]
    public async Task ReadEndpointsRequireAuthentication(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MutationEndpointsRequireAuthentication()
    {
        using var client = factory.CreateClient();
        using var update = await client.PutAsJsonAsync("/api/v1/pur/requisitions/1?version=1",
            new { businessDate = "2028-02-29", lines = new[] { new { lineNo = 1, itemId = 1,
                requiredDate = "2028-03-01", quantity = 1m } } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, update.StatusCode);
        using var delete = await client.DeleteAsync("/api/v1/pur/requisitions/1?version=1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }
}
