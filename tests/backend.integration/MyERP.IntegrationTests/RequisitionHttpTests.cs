using System.Net;
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
}
