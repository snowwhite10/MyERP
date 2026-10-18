using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

public sealed class RequisitionAuthorizedHttpTests(TestDatabase database) : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task LoggedInViewerCanReadWhileMissingPermissionAndOtherCompanyCannot()
    {
        long company;
        long otherCompany;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
            company = (await organizations.CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Purchasing", Ct)).Id;
            otherCompany = (await organizations.CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Other", Ct)).Id;
        }
        long documentId;
        await using (var db = Context(company))
        {
            var document = new Requisition { CompanyId = company, DocumentNo = "PUR-HTTP-" + Guid.NewGuid().ToString("N")[..8],
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = 7 };
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            documentId = document.Id;
        }
        long otherDocumentId;
        await using (var db = Context(otherCompany))
        {
            var document = new Requisition { CompanyId = otherCompany, DocumentNo = "PUR-OTHER-" + Guid.NewGuid().ToString("N")[..8],
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = 7 };
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            otherDocumentId = document.Id;
        }
        using var http = factory.CreateClient();
        var (viewerId, viewerToken) = await CreateUserAsync(http);
        var (unrelatedId, unrelatedToken) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var viewerRole = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12], "Viewer", null,
                DataScope.Company, false, [PurchasingPermissions.RequisitionView]), Ct)).Id;
            var unrelatedRole = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12], "Unrelated", null,
                DataScope.Company, false, [PurchasingPermissions.RequisitionCreate]), Ct)).Id;
            await roles.AssignAsync(viewerId, company, new UserCompanyAssignment(null, null, false, true, [viewerRole]), Ct);
            await roles.AssignAsync(unrelatedId, company, new UserCompanyAssignment(null, null, false, true, [unrelatedRole]), Ct);
        }
        const string path = "/api/v1/pur/requisitions";
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, path, unrelatedToken, company)).StatusCode);
        var list = await SendAsync(http, path, viewerToken, company);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, (await list.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(http, $"{path}/{documentId}", viewerToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(http, $"{path}/{otherDocumentId}", viewerToken, company)).StatusCode);
    }

    private async Task<(long Id, string Token)> CreateUserAsync(HttpClient client)
    {
        const string password = "Strong pass 2026!";
        var username = "pur" + Guid.NewGuid().ToString("N")[..20];
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 19)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel()], company);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token, long companyId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return client.SendAsync(request, Ct);
    }

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
