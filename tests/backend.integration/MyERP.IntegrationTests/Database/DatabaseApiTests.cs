using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Web;

namespace MyERP.IntegrationTests.Database;

/// <summary>HTTP 管線 + 真實資料庫：樂觀鎖衝突回 409、公司隔離、就緒檢查。</summary>
public sealed class DatabaseApiTests(DatabaseApiFactory factory) : IClassFixture<DatabaseApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient CreateClient(long companyId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(HttpCurrentCompany.HeaderName, companyId.ToString(CultureInfo.InvariantCulture));
        return client;
    }

    private static async Task<TestOrderDto> CreateOrderAsync(HttpClient client, string docNo)
    {
        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/tst/orders", UriKind.Relative), new TestOrderInput(docNo, 10m, 0), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TestOrderDto>(Ct))!;
    }

    private static Uri OrderUri(long id) => new($"/api/v1/tst/orders/{id}", UriKind.Relative);

    [Fact]
    public async Task Update_WithCurrentVersion_Returns200AndNextVersion()
    {
        using var client = CreateClient(TestCompanies.Next());
        var created = await CreateOrderAsync(client, "API-OK");

        using var response = await client.PutAsJsonAsync(OrderUri(created.Id), new TestOrderInput("API-OK-2", 20m, created.Version), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TestOrderDto>(Ct);
        updated.Should().Be(new TestOrderDto(created.Id, "API-OK-2", 20m, 2));
    }

    [Fact]
    public async Task Update_WithStaleVersion_Returns409ProblemDetails()
    {
        using var client = CreateClient(TestCompanies.Next());
        var created = await CreateOrderAsync(client, "API-409");
        (await client.PutAsJsonAsync(OrderUri(created.Id), new TestOrderInput("API-409-A", 10m, created.Version), Ct)).Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Put, OrderUri(created.Id))
        {
            Content = JsonContent.Create(new TestOrderInput("API-409-B", 10m, created.Version)),
        };
        request.Headers.Add("Accept-Language", "zh-TW");
        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        problem.GetProperty("errorCode").GetString().Should().Be(CommonErrorCodes.ConcurrencyConflict);
        problem.GetProperty("title").GetString().Should().Be("資料已被他人修改，請重新載入");

        using var current = await client.GetAsync(OrderUri(created.Id), Ct);
        (await current.Content.ReadFromJsonAsync<TestOrderDto>(Ct))!.DocNo.Should().Be("API-409-A");
    }

    [Fact]
    public async Task Get_FromAnotherCompany_Returns404()
    {
        using var owner = CreateClient(TestCompanies.Next());
        var created = await CreateOrderAsync(owner, "API-ISO");
        using var other = CreateClient(TestCompanies.Next());

        using var response = await other.GetAsync(OrderUri(created.Id), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WithoutCompanyHeader_Returns404()
    {
        using var owner = CreateClient(TestCompanies.Next());
        var created = await CreateOrderAsync(owner, "API-NOCO");
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync(OrderUri(created.Id), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ready_WithReachableDatabase_ReturnsHealthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Be("Healthy");
    }

    [Theory]
    [InlineData(@"Server=localhost\NO_SUCH_INSTANCE;Database=myerp_test;Integrated Security=true;Connect Timeout=1")]
    [InlineData("")]
    public async Task Ready_WithUnreachableOrMissingDatabase_Returns503WithoutDetails(string connectionString)
    {
        await using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:MyErp", connectionString));
        using var client = broken.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Be("Unhealthy");
    }

    [Fact]
    public async Task Live_DoesNotTouchDatabase()
    {
        await using var broken = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:MyErp", ""));
        using var client = broken.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
