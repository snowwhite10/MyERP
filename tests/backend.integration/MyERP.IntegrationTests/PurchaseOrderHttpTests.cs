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

/// <summary>
/// WU-PUR-006a4（REQ_F4114.1.2～3，PUR-PO-018～020）：採購單草稿 HTTP 受權、公司隔離與 ProblemDetails。
/// 以真實 JWT、角色與 SQL 驗證 401／403／404／409／422，且失敗請求不改保存資料。
/// </summary>
public sealed class PurchaseOrderHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Route = "/api/v1/pur/purchase-orders";
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    private static object Body(string remark) => new
    {
        supplierId = 101, businessDate = "2028-02-29", remark,
        lines = new[] { new { lineNo = 1, itemId = 11, requisitionLineId = (long?)null, quantity = 9m,
            unitPrice = 10m, currencyCode = "TWD", taxRateId = 51 } }
    };

    [Fact]
    public async Task PurchaseReceiptRoutesRequireIndependentPermissionsAndHideMissingDocuments()
    {
        const string receipts = "/api/v1/pur/purchase-receipts";
        var company = await NewCompanyAsync("receipt HTTP");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(receipts + "/999999", Ct)).StatusCode);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, receipts + "/999999", null, token, company),
            HttpStatusCode.Forbidden, "COMMON.COMPANY.ACCESS_DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderView);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, receipts + "/999999", null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.ReceiptView);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, receipts + "/999999", null, token, company),
            HttpStatusCode.NotFound, "PUR.RECEIPT.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, receipts + "/999999/edit", null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.ReceiptEdit);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, receipts + "/999999/edit", null, token, company),
            HttpStatusCode.NotFound, "PUR.RECEIPT.NOT_FOUND");
    }

    [Fact]
    public async Task ExportRequiresViewAndExportAndPrintReadOnlyNeedsView()
    {
        var company = await NewCompanyAsync("PO export");
        var other = await NewCompanyAsync("PO export other");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (orderId, _) = await SeedOrderAsync(company, userId, "Draft");
        var path = Route + "/export";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path, Ct)).StatusCode);
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderView);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, path, null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderExport);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, path, null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await GrantAsync(userId, company, DataScope.Company,
            PurchasingPermissions.OrderView, PurchasingPermissions.OrderExport);
        await GrantAsync(userId, other, DataScope.Company,
            PurchasingPermissions.OrderView, PurchasingPermissions.OrderExport);
        var exported = await SendAsync(http, HttpMethod.Get, path, null, token, company);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("1", Assert.Single(exported.Headers.GetValues("X-Export-Row-Count")));
        Assert.Equal("text/csv; charset=utf-8", exported.Content.Headers.ContentType!.ToString());
        Assert.StartsWith("\uFEFF", await exported.Content.ReadAsStringAsync(Ct));
        var empty = await SendAsync(http, HttpMethod.Get, path, null, token, other);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal("0", Assert.Single(empty.Headers.GetValues("X-Export-Row-Count")));
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, path + "?supplierId=0", null, token, company),
            HttpStatusCode.BadRequest, "COMMON.REQUEST.VALIDATION_FAILED");
        // Printing uses a fresh VIEW detail, including current saved version and lines.
        var detail = await SendAsync(http, HttpMethod.Get, $"{Route}/{orderId}", null, token, company);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(1, (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("lines").GetArrayLength());
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderExport);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, $"{Route}/{orderId}", null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
    }

    [Fact]
    public async Task ListRequiresViewAndReturnsOnlyCurrentCompanyOrders()
    {
        var company = await NewCompanyAsync("PO list");
        var other = await NewCompanyAsync("PO list other");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (orderId, version) = await SeedOrderAsync(company, userId, "Draft");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(Route, Ct)).StatusCode);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, Route, null, token, company),
            HttpStatusCode.Forbidden, "COMMON.COMPANY.ACCESS_DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderEdit);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, Route, null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderView);
        await GrantAsync(userId, other, DataScope.Company, PurchasingPermissions.OrderView);
        var list = await SendAsync(http, HttpMethod.Get, $"{Route}?status=Draft&pageSize=1", null, token, company);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, body.GetProperty("totalCount").GetInt64());
        Assert.Equal(orderId, body.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(version, body.GetProperty("items")[0].GetProperty("version").GetInt32());
        var empty = await SendAsync(http, HttpMethod.Get, Route, null, token, other);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal(0, (await empty.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, $"{Route}?supplierId=0", null, token, company),
            HttpStatusCode.BadRequest, "COMMON.REQUEST.VALIDATION_FAILED");
        await AssertUnchangedAsync(company, orderId, version);
    }

    [Fact]
    public async Task MissingPermissionsAre403AndEditDoesNotFollowViewOrCreate()
    {
        var company = await NewCompanyAsync("PO perms");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (orderId, version) = await SeedOrderAsync(company, userId, "Draft");

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(Route, Body("anon"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync($"{Route}/{orderId}/edit", Ct)).StatusCode);
        // Not assigned to the company at all: the company gate refuses before any permission check.
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Route, Body("x"), token, company),
            HttpStatusCode.Forbidden, "COMMON.COMPANY.ACCESS_DENIED");
        // Assigned, but only with an unrelated PUR permission.
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.RfqView);
        foreach (var response in new[]
                 {
                     await SendAsync(http, HttpMethod.Post, Route, Body("x"), token, company),
                     await SendAsync(http, HttpMethod.Put, $"{Route}/{orderId}?version={version}", Body("x"), token, company),
                     await SendAsync(http, HttpMethod.Get, $"{Route}/{orderId}/edit", null, token, company),
                     await SendAsync(http, HttpMethod.Get, $"{Route}/{orderId}", null, token, company)
                 })
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");

        await GrantAsync(userId, company, DataScope.Company,
            PurchasingPermissions.OrderView, PurchasingPermissions.OrderCreate);
        var detail = await SendAsync(http, HttpMethod.Get, $"{Route}/{orderId}", null, token, company);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(version, (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("version").GetInt32());
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, $"{Route}/{orderId}/edit", null, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Put, $"{Route}/{orderId}?version={version}",
            Body("x"), token, company), HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        await AssertUnchangedAsync(company, orderId, version);
    }

    [Fact]
    public async Task OtherCompanyIs404AndStaleOrNonDraftEditsReturnProblemDetailsWithoutChanges()
    {
        var company = await NewCompanyAsync("PO edit");
        var other = await NewCompanyAsync("PO other");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (draftId, draftVersion) = await SeedOrderAsync(company, userId, "Draft");
        var (pendingId, pendingVersion) = await SeedOrderAsync(company, userId, "PendingApproval");
        string[] all = [PurchasingPermissions.OrderView, PurchasingPermissions.OrderCreate, PurchasingPermissions.OrderEdit];
        await GrantAsync(userId, company, DataScope.Company, all);
        await GrantAsync(userId, other, DataScope.Company, all);

        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, $"{Route}/{draftId}", null, token, other),
            HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, $"{Route}/{draftId}/edit", null, token, other),
            HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Put, $"{Route}/{draftId}?version={draftVersion}",
            Body("other"), token, other), HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Put, $"{Route}/{draftId}?version={draftVersion + 1}",
            Body("stale"), token, company), HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Put, $"{Route}/{pendingId}?version={pendingVersion}",
            Body("pending"), token, company), HttpStatusCode.UnprocessableEntity, "PUR.ORDER.NOT_EDITABLE");

        var edit = await SendAsync(http, HttpMethod.Get, $"{Route}/{draftId}/edit", null, token, company);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        await AssertUnchangedAsync(company, draftId, draftVersion);
        await AssertUnchangedAsync(company, pendingId, pendingVersion);
    }

    /// <summary>WU-PUR-006c1（REQ_F4114.1.6，PUR-PO-008／012／014）：DELETE 受權、公司隔離、版本與狀態守衛。</summary>
    [Fact]
    public async Task DeleteRequiresOwnPermissionAndSoftDeletesOnlyCurrentDraft()
    {
        var company = await NewCompanyAsync("PO delete");
        var other = await NewCompanyAsync("PO delete other");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (draftId, draftVersion) = await SeedOrderAsync(company, userId, "Draft");
        var (approvedId, approvedVersion) = await SeedOrderAsync(company, userId, "Approved");

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.DeleteAsync($"{Route}/{draftId}?version={draftVersion}", Ct)).StatusCode);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Delete, $"{Route}/{draftId}?version={draftVersion}",
            null, token, company), HttpStatusCode.Forbidden, "COMMON.COMPANY.ACCESS_DENIED");
        // EDIT does not imply DELETE.
        string[] edit = [PurchasingPermissions.OrderView, PurchasingPermissions.OrderEdit];
        await GrantAsync(userId, company, DataScope.Company, edit);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Delete, $"{Route}/{draftId}?version={draftVersion}",
            null, token, company), HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");

        await GrantAsync(userId, company, DataScope.Company, [.. edit, PurchasingPermissions.OrderDelete]);
        await GrantAsync(userId, other, DataScope.Company, PurchasingPermissions.OrderDelete);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Delete, $"{Route}/{draftId}?version={draftVersion}",
            null, token, other), HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Delete, $"{Route}/{draftId}?version={draftVersion + 1}",
            null, token, company), HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Delete,
            $"{Route}/{approvedId}?version={approvedVersion}", null, token, company),
            HttpStatusCode.UnprocessableEntity, "PUR.ORDER.NOT_DELETABLE");
        await AssertUnchangedAsync(company, draftId, draftVersion);
        await AssertUnchangedAsync(company, approvedId, approvedVersion);

        var deleted = await SendAsync(http, HttpMethod.Delete, $"{Route}/{draftId}?version={draftVersion}",
            null, token, company);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Get, $"{Route}/{draftId}", null, token, company),
            HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Delete, $"{Route}/{draftId}?version={draftVersion}",
            null, token, company), HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await using var db = Context(company, 7);
        var saved = await db.Set<PurchaseOrderSource>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == draftId, Ct);
        Assert.True(saved.IsDeleted);
        Assert.NotNull(saved.DeletedAt);
        Assert.Equal(1, await db.Set<PurchaseOrderSourceLine>().CountAsync(x => x.PurchaseOrderId == draftId, Ct));
    }

    /// <summary>WU-PUR-006c2（PUR-PO-012／016）：CANCEL 需獨立權限，經真實 JWT 與 SQL 回 200 並保存原因。</summary>
    [Fact]
    public async Task CancelRequiresOwnPermissionAndCancelsOnlyCurrentApprovedOrder()
    {
        var company = await NewCompanyAsync("PO cancel");
        var other = await NewCompanyAsync("PO cancel other");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (approvedId, approvedVersion) = await SeedOrderAsync(company, userId, "Approved", consumed: true);
        var (draftId, draftVersion) = await SeedOrderAsync(company, userId, "Draft");
        string Path(long id) => $"{Route}/{id}/cancel";
        object CancelBody(int version, string? reason = "Supplier closed") => new { version, reason };

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync(Path(approvedId), CancelBody(approvedVersion), Ct)).StatusCode);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(approvedVersion),
            token, company), HttpStatusCode.Forbidden, "COMMON.COMPANY.ACCESS_DENIED");
        // DELETE, EDIT and SUBMIT do not imply CANCEL.
        string[] others = [PurchasingPermissions.OrderView, PurchasingPermissions.OrderEdit,
            PurchasingPermissions.OrderSubmit, PurchasingPermissions.OrderDelete];
        await GrantAsync(userId, company, DataScope.Company, others);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(approvedVersion),
            token, company), HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");

        await GrantAsync(userId, company, DataScope.Company, [.. others, PurchasingPermissions.OrderCancel]);
        await GrantAsync(userId, other, DataScope.Company, PurchasingPermissions.OrderCancel);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(approvedVersion),
            token, other), HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(approvedVersion, "  "),
            token, company), HttpStatusCode.BadRequest, "COMMON.REQUEST.VALIDATION_FAILED");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(approvedVersion + 1),
            token, company), HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(draftId), CancelBody(draftVersion),
            token, company), HttpStatusCode.UnprocessableEntity, "PUR.ORDER.NOT_CANCELLABLE");
        await AssertUnchangedAsync(company, approvedId, approvedVersion);
        await AssertUnchangedAsync(company, draftId, draftVersion);

        var cancelled = await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(approvedVersion), token, company);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var result = await cancelled.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Cancelled", result.GetProperty("status").GetString());
        Assert.Equal(approvedId, result.GetProperty("id").GetInt64());
        var newVersion = result.GetProperty("version").GetInt32();
        Assert.True(newVersion > approvedVersion);
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, Path(approvedId), CancelBody(newVersion),
            token, company), HttpStatusCode.UnprocessableEntity, "PUR.ORDER.NOT_CANCELLABLE");
        await using var db = Context(company, 7);
        var saved = await db.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == approvedId, Ct);
        Assert.Equal("Cancelled", saved.Status);
        Assert.False(saved.RequisitionConsumptionActive);
        Assert.Equal("Supplier closed", saved.CancelReason);
        Assert.Equal(newVersion, saved.Version);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(errorCode, problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    private async Task AssertUnchangedAsync(long company, long orderId, int version)
    {
        await using var db = Context(company, 7);
        var order = await db.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == orderId, Ct);
        Assert.Equal(version, order.Version);
        Assert.Equal("seed", order.Remark);
        Assert.Equal(1m, (await db.Set<PurchaseOrderSourceLine>().AsNoTracking()
            .SingleAsync(x => x.PurchaseOrderId == orderId, Ct)).Quantity);
    }

    private async Task<(long Id, int Version)> SeedOrderAsync(long company, long ownerId, string status,
        bool consumed = false)
    {
        await using var db = Context(company, ownerId);
        var order = new PurchaseOrderSource
        {
            CompanyId = company, SupplierId = 101, DocumentNo = $"PO-H-{Guid.NewGuid():N}"[..20],
            BusinessDate = new DateOnly(2028, 2, 29), Currency = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
            Status = status, OwnerId = ownerId, Remark = "seed", BaseNet = 10m, BaseTax = 0.5m, BaseGross = 10.5m,
            RequisitionConsumptionActive = consumed
        };
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        db.Add(new PurchaseOrderSourceLine
        {
            CompanyId = company, PurchaseOrderId = order.Id, LineNo = 1, MaterialId = 11, Quantity = 1m,
            UnitPrice = 10m, TaxRateId = 51, TaxRatePercent = 5m, ExchangeRate = 1m,
            OriginalNet = 10m, OriginalTax = 0.5m, OriginalGross = 10.5m,
            BaseNet = 10m, BaseTax = 0.5m, BaseGross = 10.5m
        });
        await db.SaveChangesAsync(Ct);
        return (order.Id, order.Version);
    }

    private async Task<long> NewCompanyAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
            .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], name, Ct)).Id;
    }

    private async Task GrantAsync(long userId, long company, DataScope scope, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
            "PO role", null, scope, false, permissions), Ct)).Id;
        await roles.AssignAsync(userId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
    }

    private async Task<(long Id, string Token)> CreateUserAsync(HttpClient client)
    {
        const string password = "Strong pass 2026!";
        var username = "po" + Guid.NewGuid().ToString("N")[..20];
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!);
    }

    private MyErpDbContext Context(long companyId, long userId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(userId), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new RfqAwardDbModel(),
                new PurchaseInvoiceSourceDbModel()], company);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        object? body, string token, long companyId)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
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
