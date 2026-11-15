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
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-SD-004a2b12: authorised delivery HTTP on the real API, SYS authorization, MD references and SQL.
/// SD-DEL-001/002/005/006/007/010/013/014/015/017 HTTP parts.
/// </summary>
public sealed class SalesDeliveryAuthorizedHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/deliveries";
    private static readonly DateOnly Day = new(2028, 2, 29);
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task RoutesRequireAuthenticationAndAppearInOpenApi()
    {
        // SD-DEL-HTTP-001: anonymous 401 on every route; the five operations are documented.
        using var client = factory.CreateClient();
        var draft = new { orderId = 1, businessDate = "2028-02-29",
            lines = new[] { new { lineNo = 1, orderLineId = 1, warehouseId = 1, quantity = 1m } } };
        using (var list = await client.GetAsync(Route, Ct)) Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        using (var detail = await client.GetAsync(Route + "/1", Ct)) Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        using (var create = await client.PostAsJsonAsync(Route, draft, Ct)) Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        using (var edit = await client.PutAsJsonAsync(Route + "/1", new { version = 1, draft }, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, edit.StatusCode);
        using (var delete = await client.DeleteAsync(Route + "/1?version=1", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty(Route);
        Assert.True(collection.TryGetProperty("get", out _));
        Assert.True(collection.TryGetProperty("post", out var post));
        Assert.True(post.GetProperty("responses").TryGetProperty("422", out _));
        var item = paths.GetProperty(Route + "/{id}");
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out var put));
        Assert.True(put.GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(item.TryGetProperty("delete", out _));
    }

    [Fact]
    public async Task WriterWithoutViewCreatesEditsAndDeletesWhileViewerReadsListAndDetail()
    {
        // SD-DEL-HTTP-002: CREATE/EDIT/DELETE work without VIEW; VIEW lists and reads; versions give 409.
        var setup = await SetupAsync("DH");
        var writer = await UserAsync(setup.CompanyId, DataScope.Company,
            "SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT", "SAL.DELIVERY.DELETE");
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        var read = await LoginAsync(client, viewer);

        var draft = Draft(setup.OrderId, setup.LineA, setup.WarehouseId, 2m);
        using var created = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post, Route, draft);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"{Route}/{id}", created.Headers.Location!.OriginalString);
        Assert.Equal("DH001", body.GetProperty("documentNo").GetString());
        Assert.Equal(setup.OrderNo, body.GetProperty("orderDocumentNo").GetString());
        Assert.Equal("Delivery customer", body.GetProperty("customerName").GetString());
        Assert.Equal((int)SalesDeliveryStatus.Draft, body.GetProperty("status").GetInt32());
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(setup.LineA, line.GetProperty("orderLineId").GetInt64());
        Assert.Equal(1, line.GetProperty("orderLineNo").GetInt32());
        Assert.Equal(setup.ItemCode, line.GetProperty("itemCode").GetString());
        Assert.Equal(2m, line.GetProperty("quantity").GetDecimal());

        // The writer has no VIEW: reads are 403, never a scope leak.
        using (var noView = await SendAsync(client, write, setup.CompanyId, HttpMethod.Get, Route))
            Assert.Equal(HttpStatusCode.Forbidden, noView.StatusCode);

        // The viewer sees the delivery with filters and a stable page.
        using (var list = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get,
                   $"{Route}?orderId={setup.OrderId}&status=Draft&keyword=DH0&sort=documentNo&page=1&pageSize=20"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var page = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(1, page.GetProperty("totalCount").GetInt64());
            var row = page.GetProperty("items")[0];
            Assert.Equal(id, row.GetProperty("id").GetInt64());
            Assert.Equal(setup.OrderNo, row.GetProperty("orderDocumentNo").GetString());
            Assert.Equal(1, row.GetProperty("lineCount").GetInt32());
            Assert.Equal(2m, row.GetProperty("totalQuantity").GetDecimal());
        }
        using (var none = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}?status=Posted"))
            Assert.Equal(0, (await none.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
        using (var badSort = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}?sort=quantity"))
            Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        using (var badRange = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}?from=2028-03-01&to=2028-02-29"))
            Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);

        // The viewer cannot write.
        foreach (var (method, path, request) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Post, Route, draft),
                     (HttpMethod.Put, $"{Route}/{id}", new { version = 1, draft }),
                     (HttpMethod.Delete, $"{Route}/{id}?version=1", null)
                 })
        {
            using var denied = await SendAsync(client, read, setup.CompanyId, method, path, request);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));
        }

        var replacement = new { version = 1, draft = new { orderId = setup.OrderId, businessDate = "2028-03-01",
            lines = new object[]
            {
                new { lineNo = 1, orderLineId = setup.LineA, warehouseId = setup.WarehouseId, quantity = 1.5m },
                new { lineNo = 2, orderLineId = setup.LineB, warehouseId = setup.WarehouseId, quantity = 0.000001m }
            } } };
        using (var edited = await SendAsync(client, write, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}", replacement))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var saved = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(2, saved.GetProperty("version").GetInt32());
            Assert.Equal("2028-03-01", saved.GetProperty("businessDate").GetString());
            Assert.Equal(2, saved.GetProperty("lines").GetArrayLength());
            Assert.Equal(setup.OrderNo, saved.GetProperty("orderDocumentNo").GetString());
        }
        using (var stale = await SendAsync(client, write, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}", replacement))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("COMMON.CONCURRENCY.CONFLICT", await ErrorCodeAsync(stale));
        }
        using (var detail = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var saved = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(2, saved.GetProperty("version").GetInt32());
            Assert.Equal([1, 2], saved.GetProperty("lines").EnumerateArray()
                .Select(x => x.GetProperty("orderLineNo").GetInt32()).ToArray());
            Assert.Equal(0.000001m, saved.GetProperty("lines")[1].GetProperty("quantity").GetDecimal());
        }

        using (var staleDelete = await SendAsync(client, write, setup.CompanyId, HttpMethod.Delete, $"{Route}/{id}?version=1"))
            Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using (var deleted = await SendAsync(client, write, setup.CompanyId, HttpMethod.Delete, $"{Route}/{id}?version=2"))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var again = await SendAsync(client, write, setup.CompanyId, HttpMethod.Delete, $"{Route}/{id}?version=3"))
        {
            Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
            Assert.Equal("SAL.DELIVERY.NOT_FOUND", await ErrorCodeAsync(again));
        }
        using (var gone = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        using (var emptyList = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, Route))
            Assert.Equal(0, (await emptyList.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());

        await using var verify = Context(setup.CompanyId);
        var persisted = await verify.Set<SalesDelivery>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .Include(x => x.Lines).SingleAsync(x => x.Id == id, Ct);
        Assert.True(persisted.IsDeleted);
        Assert.Equal("DH001", persisted.DocumentNo);
        Assert.Equal(new DateOnly(2028, 3, 1), persisted.BusinessDate);
        Assert.Equal(2, persisted.Lines.Count);
    }

    [Fact]
    public async Task RejectedInputsReturnNamedProblemDetailsAndSaveNothing()
    {
        // SD-DEL-HTTP-003: 400 fields, 404 hidden order, 422 state/line/balance with lineNo; no row, no number.
        var setup = await SetupAsync("DI");
        var writer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, writer);

        using (var invalid = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   new { orderId = setup.OrderId, businessDate = "2028-02-29", lines = Array.Empty<object>() }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.True(problem.GetProperty("errors").TryGetProperty("lines", out _));
        }
        using (var precision = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   Draft(setup.OrderId, setup.LineA, setup.WarehouseId, 0.0000001m)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, precision.StatusCode);
            var problem = await precision.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.True(problem.GetProperty("errors").TryGetProperty("lines[0].quantity", out _));
        }
        using (var missing = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   Draft(long.MaxValue, setup.LineA, setup.WarehouseId, 1m)))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("SAL.ORDER.NOT_FOUND", await ErrorCodeAsync(missing));
        }
        using (var draftOrder = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   Draft(setup.DraftOrderId, setup.DraftOrderLine, setup.WarehouseId, 1m)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, draftOrder.StatusCode);
            Assert.Equal("SAL.DELIVERY.ORDER_NOT_APPROVED", await ErrorCodeAsync(draftOrder));
        }
        using (var foreignLine = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   new { orderId = setup.OrderId, businessDate = "2028-02-29", lines = new[]
                   {
                       new { lineNo = 1, orderLineId = setup.LineA, warehouseId = setup.WarehouseId, quantity = 1m },
                       new { lineNo = 7, orderLineId = setup.DraftOrderLine, warehouseId = setup.WarehouseId, quantity = 1m }
                   } }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, foreignLine.StatusCode);
            var problem = await foreignLine.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("SAL.DELIVERY.ORDER_LINE_INVALID", problem.GetProperty("errorCode").GetString());
            Assert.Equal(7, problem.GetProperty("lineNo").GetInt32());
            Assert.True(problem.TryGetProperty("traceId", out _));
        }
        using (var over = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   Draft(setup.OrderId, setup.LineA, setup.WarehouseId, 5.000001m)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);
            Assert.Equal("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED", await ErrorCodeAsync(over));
        }
        using (var badWarehouse = await SendAsync(client, token, setup.CompanyId, HttpMethod.Post, Route,
                   Draft(setup.OrderId, setup.LineA, long.MaxValue, 1m)))
        {
            Assert.Equal(HttpStatusCode.NotFound, badWarehouse.StatusCode);
            Assert.Equal("MD.WAREHOUSE.NOT_FOUND", await ErrorCodeAsync(badWarehouse));
        }

        await using var verify = Context(setup.CompanyId);
        Assert.False(await verify.Set<SalesDelivery>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AnyAsync(Ct));
        Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
    }

    [Fact]
    public async Task ViewScopeFollowsSourceOrderAndOtherCompaniesCannotDiscoverDeliveries()
    {
        // SD-DEL-HTTP-004: Self VIEW scope hides deliveries of other owners' orders; foreign company is 404/empty.
        var setup = await SetupAsync("DJ");
        var writer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE");
        var selfViewer = await UserAsync(setup.CompanyId, DataScope.Self, "SAL.DELIVERY.VIEW");
        var other = await SetupAsync("DK");
        var foreignViewer = await UserAsync(other.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        using var created = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post, Route,
            Draft(setup.OrderId, setup.LineA, setup.WarehouseId, 1m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        // The order belongs to owner 7, not the Self viewer.
        var self = await LoginAsync(client, selfViewer);
        using (var hidden = await SendAsync(client, self, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal("SAL.DELIVERY.NOT_FOUND", await ErrorCodeAsync(hidden));
        }
        using (var hiddenList = await SendAsync(client, self, setup.CompanyId, HttpMethod.Get, Route))
            Assert.Equal(0, (await hiddenList.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());

        var foreign = await LoginAsync(client, foreignViewer);
        using (var foreignDetail = await SendAsync(client, foreign, other.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        using (var foreignList = await SendAsync(client, foreign, other.CompanyId, HttpMethod.Get, Route))
            Assert.Equal(0, (await foreignList.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("totalCount").GetInt64());
    }

    private static object Draft(long orderId, long orderLineId, long warehouseId, decimal quantity) => new
    {
        orderId, businessDate = "2028-02-29",
        lines = new[] { new { lineNo = 1, orderLineId, warehouseId, quantity } }
    };

    private sealed record Setup(long CompanyId, long OrderId, string OrderNo, long LineA, long LineB,
        long DraftOrderId, long DraftOrderLine, long WarehouseId, string ItemCode);

    private async Task<Setup> SetupAsync(string prefix)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Delivery HTTP", Ct);
        await using var db = Context(company.Id);
        db.Add(new NumberRule { CompanyId = company.Id, DocumentType = "SAL.DELIVERY", Name = "Sales delivery",
            Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Unit", 0, Ct);
        var itemCode = "I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var item = await new ItemService(db).CreateAsync(itemCode, "Delivery item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync(
            "W" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Warehouse", null, null, null, Ct);

        SalesOrder NewOrder()
        {
            var order = new SalesOrder { CompanyId = company.Id, OwnerUserId = 7,
                DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = 21,
                CustomerCode = "C21", CustomerName = "Delivery customer", BusinessDate = Day, CurrencyCode = "TWD",
                BaseCurrencyCode = "TWD", ExchangeRate = 1m, OriginalNet = 65m, OriginalGross = 65m,
                BaseNet = 65m, BaseGross = 65m };
            order.Lines.Add(Line(company.Id, 1, item.Id, itemCode, 5m));
            order.Lines.Add(Line(company.Id, 2, item.Id, itemCode, 1.5m));
            return order;
        }
        var approved = NewOrder();
        var draft = NewOrder();
        db.AddRange(approved, draft);
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-10-27' WHERE id = {0}", [approved.Id], Ct);
        return new Setup(company.Id, approved.Id, approved.DocumentNo, approved.Lines[0].Id, approved.Lines[1].Id,
            draft.Id, draft.Lines[0].Id, warehouse.Id, itemCode);
    }

    private static SalesOrderLine Line(long companyId, int lineNo, long itemId, string itemCode, decimal quantity) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = itemId, ItemCode = itemCode, ItemName = "Delivery item",
        Quantity = quantity, UnitPrice = 10m, TaxRateId = 41, OriginalNet = quantity * 10m,
        OriginalGross = quantity * 10m, BaseNet = quantity * 10m, BaseGross = quantity * 10m
    };

    private async Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("D" + Guid.NewGuid().ToString("N")[..12],
            "Delivery role", null, scope, false, permissions), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.ServiceProvider.GetRequiredService<SystemUserService>()
            .CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, companyId, new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        return username;
    }

    private async Task<string> LoginAsync(HttpClient client, string username)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, long companyId,
        HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, Ct);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString();

    private MyErpDbContext Context(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2026, 10, 27)), new FixedUser(7), new FixedCompany(companyId)))).Options,
        [new SalesDeliveryDbModel(), new SalesOrderDbModel(), new SystemOrganizationDbModel(), new UomDbModel(),
            new ItemDbModel(), new WarehouseDbModel(), new CustomerDbModel(), new PaymentSetupDbModel(),
            new CurrencyDbModel(), new TaxDbModel()],
        new FixedCompany(companyId));

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
