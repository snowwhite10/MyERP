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
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Approvals;
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
        foreach (var action in new[] { "submit", "withdraw" })
        {
            using var command = await client.PostAsJsonAsync($"{Route}/1/{action}", new { version = 1 }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, command.StatusCode);
        }

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
        foreach (var action in new[] { "submit", "withdraw" })
            Assert.True(paths.GetProperty(Route + "/{id}/" + action).GetProperty("post")
                .GetProperty("responses").TryGetProperty("422", out _));
        Assert.True(paths.GetProperty(Route + "/tracking-options").GetProperty("get")
            .GetProperty("responses").TryGetProperty("404", out _));
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
        Assert.Equal(nameof(SalesDeliveryStatus.Draft), body.GetProperty("status").GetString());
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
            Assert.Equal(nameof(SalesDeliveryStatus.Draft), row.GetProperty("status").GetString());
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

    [Fact]
    public async Task SubmitReservesBalanceAndWithdrawReleasesItWithVersionedProblemDetails()
    {
        // SD-DEL-HTTP-005 (WU-SD-004a2b13): SUBMIT alone drives submit/withdraw; SYS round stays Pending.
        var setup = await SetupAsync("DS");
        var approverName = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW");
        await using (var db = Context(setup.CompanyId))
        {
            var approverId = await db.Set<MyERP.Modules.System.Domain.SystemUser>().Where(x => x.Username == approverName)
                .Select(x => x.Id).SingleAsync(Ct);
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("SAL.DELIVERY", "Delivery", false,
                [new ApprovalStepInput("Approver", ApproverType.User, null, approverId, null)]), Ct);
        }
        var creator = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE");
        var submitter = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.SUBMIT");
        using var client = factory.CreateClient();
        var create = await LoginAsync(client, creator);
        var submit = await LoginAsync(client, submitter);
        var view = await LoginAsync(client, approverName);

        async Task<long> CreateAsync(decimal quantity)
        {
            using var created = await SendAsync(client, create, setup.CompanyId, HttpMethod.Post, Route,
                Draft(setup.OrderId, setup.LineA, setup.WarehouseId, quantity));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        var first = await CreateAsync(5m);
        var second = await CreateAsync(0.000001m);

        using (var noSubmit = await SendAsync(client, create, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{first}/submit", new { version = 1 }))
            Assert.Equal(HttpStatusCode.Forbidden, noSubmit.StatusCode);
        using (var stale = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{first}/submit", new { version = 2 }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var missing = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{long.MaxValue}/submit", new { version = 1 }))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using (var submitted = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{first}/submit", new { version = 1 }))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            var body = await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(nameof(SalesDeliveryStatus.Pending), body.GetProperty("status").GetString());
            Assert.Equal(2, body.GetProperty("version").GetInt32());
            Assert.Equal(setup.OrderNo, body.GetProperty("orderDocumentNo").GetString());
        }
        using (var again = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{first}/submit", new { version = 2 }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
            Assert.Equal("SAL.DELIVERY.INVALID_STATUS", await ErrorCodeAsync(again));
        }
        // The Pending 5 reserves all of line A: one micro-unit more is refused and stays Draft.
        using (var over = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{second}/submit", new { version = 1 }))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);
            Assert.Equal("SAL.DELIVERY.ORDER_BALANCE_EXCEEDED", await ErrorCodeAsync(over));
        }

        using (var withdrawn = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{first}/withdraw", new { version = 2 }))
        {
            Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
            var body = await withdrawn.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(nameof(SalesDeliveryStatus.Draft), body.GetProperty("status").GetString());
            Assert.Equal(3, body.GetProperty("version").GetInt32());
        }
        using (var notPending = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{first}/withdraw", new { version = 3 }))
            Assert.Equal("SAL.DELIVERY.INVALID_STATUS", await ErrorCodeAsync(notPending));
        using (var released = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                   $"{Route}/{second}/submit", new { version = 1 }))
            Assert.Equal(HttpStatusCode.OK, released.StatusCode);

        using var detail = await SendAsync(client, view, setup.CompanyId, HttpMethod.Get, $"{Route}/{second}");
        var pendingDetail = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(nameof(SalesDeliveryStatus.Pending), pendingDetail.GetProperty("status").GetString());
        // SD-DEL-HTTP-007 (WU-SD-004c1): VIEW detail lists SYS rounds newest first — the open round of the
        // resubmitted delivery only; the first delivery keeps its own cancelled round.
        var round = Assert.Single(pendingDetail.GetProperty("approvalHistory").EnumerateArray());
        Assert.Equal("Pending", round.GetProperty("status").GetString());
        Assert.Equal("Approver", Assert.Single(round.GetProperty("steps").EnumerateArray()).GetProperty("name").GetString());
        using var withdrawnDetail = await SendAsync(client, view, setup.CompanyId, HttpMethod.Get, $"{Route}/{first}");
        var cancelled = Assert.Single((await withdrawnDetail.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("approvalHistory").EnumerateArray());
        Assert.NotEqual("Pending", cancelled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SysApprovalEndpointPostsThroughInventoryOrKeepsPendingAndRacesWithdrawOnce()
    {
        // SD-DEL-HTTP-006 (WU-SD-004b2, ADR-146): SD-DEL-003/010/011/020 through the real SYS approval endpoint.
        var setup = await SetupAsync("DA");
        var approverName = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW",
            SystemPermissions.ApprovalProcess);
        long itemId;
        await using (var db = Context(setup.CompanyId))
        {
            var approverId = await db.Set<MyERP.Modules.System.Domain.SystemUser>().Where(x => x.Username == approverName)
                .Select(x => x.Id).SingleAsync(Ct);
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("SAL.DELIVERY", "Delivery", false,
                [new ApprovalStepInput("Approver", ApproverType.User, null, approverId, null)]), Ct);
            itemId = await db.Set<SalesOrderLine>().Where(x => x.Id == setup.LineA).Select(x => x.ItemId).SingleAsync(Ct);
            await SeedStockAsync(db, 1, itemId, setup.WarehouseId, 2m);
        }
        var creator = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE");
        var submitter = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.SUBMIT");
        using var client = factory.CreateClient();
        var create = await LoginAsync(client, creator);
        var submit = await LoginAsync(client, submitter);
        var approver = await LoginAsync(client, approverName);

        async Task<(long Id, long Instance)> SubmittedAsync(long orderLineId, decimal quantity)
        {
            using var created = await SendAsync(client, create, setup.CompanyId, HttpMethod.Post, Route,
                Draft(setup.OrderId, orderLineId, setup.WarehouseId, quantity));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
            using var submitted = await SendAsync(client, submit, setup.CompanyId, HttpMethod.Post,
                $"{Route}/{id}/submit", new { version = 1 });
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            return (id, (await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("approvalInstanceId").GetInt64());
        }
        Task<HttpResponseMessage> ApproveAsync(long instance) => SendAsync(client, approver, setup.CompanyId,
            HttpMethod.Post, $"/api/v1/sys/approvals/{instance}/approve", new { comment = "Ship", version = 1 });
        async Task<JsonElement> DetailAsync(long id)
        {
            using var detail = await SendAsync(client, approver, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            return await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
        }
        async Task<long[]> OutboundAsync(long id)
        {
            await using var db = Context(setup.CompanyId);
            return await db.Set<StockMovement>().AsNoTracking()
                .Where(x => x.SourceType == ExternalInventorySourceTypes.SalesOutbound && x.SourceId == id)
                .OrderBy(x => x.SourceLineNo).Select(x => x.Id).ToArrayAsync(Ct);
        }

        // Stock 2 < 3: INV's refusal reaches the approver; SYS and the delivery stay Pending with no movement.
        var (first, firstInstance) = await SubmittedAsync(setup.LineA, 3m);
        using (var refused = await ApproveAsync(firstInstance))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("INV.STOCK.INSUFFICIENT", problem.GetProperty("errorCode").GetString());
            // SD-DEL-HTTP-011 (ADR-154): the SYS approval endpoint passes INV's delivery line numbers through.
            Assert.Equal((1, 1), (problem.GetProperty("lineNo").GetInt32(), problem.GetProperty("lineNos").GetArrayLength()));
        }
        var pending = await DetailAsync(first);
        Assert.Equal((nameof(SalesDeliveryStatus.Pending), 2), (pending.GetProperty("status").GetString(),
            pending.GetProperty("version").GetInt32()));
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("postedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("lines")[0].GetProperty("invMovementId").ValueKind);
        Assert.Empty(await OutboundAsync(first));

        // After a receipt the same SYS round approves: Posted, and the detail links the INV movement.
        await using (var db = Context(setup.CompanyId))
            await SeedStockAsync(db, 2, itemId, setup.WarehouseId, 3m);
        using (var approved = await ApproveAsync(firstInstance))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var posted = await DetailAsync(first);
        var movements = await OutboundAsync(first);
        Assert.Single(movements);
        Assert.Equal(nameof(SalesDeliveryStatus.Posted), posted.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, posted.GetProperty("postedAt").ValueKind);
        Assert.Equal(movements[0], posted.GetProperty("lines")[0].GetProperty("invMovementId").GetInt64());
        var decided = Assert.Single(posted.GetProperty("approvalHistory").EnumerateArray());
        Assert.Equal(firstInstance, decided.GetProperty("instanceId").GetInt64());
        Assert.Equal("Approved", decided.GetProperty("status").GetString());
        Assert.Equal("Ship", Assert.Single(decided.GetProperty("steps").EnumerateArray()).GetProperty("comment").GetString());
        // A repeated decision cannot post again.
        using (var again = await ApproveAsync(firstInstance))
            Assert.Contains(again.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity });
        Assert.Equal(movements, await OutboundAsync(first));

        // Approve and withdraw of one Pending delivery race: exactly one wins and the loser changes nothing.
        var (second, secondInstance) = await SubmittedAsync(setup.LineB, 1m);
        var approveTask = ApproveAsync(secondInstance);
        var withdrawTask = SendAsync(client, submit, setup.CompanyId, HttpMethod.Post, $"{Route}/{second}/withdraw",
            new { version = 2 });
        using var approveResult = await approveTask;
        using var withdrawResult = await withdrawTask;
        var winners = new[] { approveResult.StatusCode, withdrawResult.StatusCode };
        Assert.Single(winners, HttpStatusCode.OK);
        Assert.All(winners.Where(x => x != HttpStatusCode.OK), x =>
            Assert.Contains(x, new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity }));
        var raced = await DetailAsync(second);
        var racedMovements = await OutboundAsync(second);
        if (approveResult.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(nameof(SalesDeliveryStatus.Posted), raced.GetProperty("status").GetString());
            Assert.Equal(racedMovements.Single(), raced.GetProperty("lines")[0].GetProperty("invMovementId").GetInt64());
        }
        else
        {
            Assert.Equal((nameof(SalesDeliveryStatus.Draft), 3), (raced.GetProperty("status").GetString(),
                raced.GetProperty("version").GetInt32()));
            Assert.Empty(racedMovements);
        }
    }

    [Fact]
    public async Task ReceiptsAndDetailNameEachLineItemTrackingMode()
    {
        // SD-DEL-HTTP-008 (WU-SD-004c2b2b1, ADR-151): every line carries the item's current MD tracking mode
        // (None/Lot/Serial) on the POST and PUT receipts and the VIEW detail, so the form knows which picker to show
        // without MD.ITEM.VIEW. An incomplete Draft selection stays allowed.
        var setup = await SetupAsync("DT");
        long lotItem, serialItem, orderId, lotLine, serialLine;
        await using (var db = Context(setup.CompanyId))
        {
            var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Unit", 0, Ct);
            var items = new ItemService(db);
            lotItem = (await items.CreateAsync("L" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Lot item", null,
                uom.Id, null, null, ItemTrackingMode.Lot, Ct)).Id;
            serialItem = (await items.CreateAsync("S" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Serial item",
                null, uom.Id, null, null, ItemTrackingMode.Serial, Ct)).Id;
            var order = new SalesOrder { CompanyId = setup.CompanyId, OwnerUserId = 7,
                DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = 21,
                CustomerCode = "C21", CustomerName = "Delivery customer", BusinessDate = Day, CurrencyCode = "TWD",
                BaseCurrencyCode = "TWD", ExchangeRate = 1m, OriginalNet = 50m, OriginalGross = 50m,
                BaseNet = 50m, BaseGross = 50m };
            order.Lines.Add(Line(setup.CompanyId, 1, lotItem, "LOT", 3m));
            order.Lines.Add(Line(setup.CompanyId, 2, serialItem, "SER", 2m));
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-10-27' WHERE id = {0}", [order.Id], Ct);
            (orderId, lotLine, serialLine) = (order.Id, order.Lines[0].Id, order.Lines[1].Id);
        }
        var writer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT");
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        var read = await LoginAsync(client, viewer);

        static string[] Modes(JsonElement body) => [.. body.GetProperty("lines").EnumerateArray()
            .OrderBy(x => x.GetProperty("lineNo").GetInt32()).Select(x => x.GetProperty("trackingMode").GetString()!)];

        using (var plain = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post, Route,
                   Draft(setup.OrderId, setup.LineA, setup.WarehouseId, 1m)))
        {
            Assert.Equal(HttpStatusCode.Created, plain.StatusCode);
            Assert.Equal(["None"], Modes(await plain.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }

        var tracked = new
        {
            orderId, businessDate = "2028-02-29",
            lines = new object[]
            {
                new { lineNo = 1, orderLineId = lotLine, warehouseId = setup.WarehouseId, quantity = 3m,
                    tracking = new[] { new { lotNo = "LOT-A", serialNo = (string?)null, quantity = 1m } } },
                new { lineNo = 2, orderLineId = serialLine, warehouseId = setup.WarehouseId, quantity = 2m },
            }
        };
        using var created = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post, Route, tracked);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(["Lot", "Serial"], Modes(receipt));
        var id = receipt.GetProperty("id").GetInt64();

        using (var edited = await SendAsync(client, write, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   new { version = 1, draft = tracked }))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.Equal(["Lot", "Serial"], Modes(await edited.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        using var detail = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(["Lot", "Serial"], Modes(await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)));
    }

    [Fact]
    public async Task TrackingOptionsListInStockLotsAndSerialsAtTheChosenWarehouseAndLocation()
    {
        // SD-DEL-HTTP-009 (WU-SD-004c2b2b2a, ADR-152): CREATE or EDIT (no INV.TRACKING.VIEW, no VIEW) lists the
        // lots/serials with positive stock at exactly the line's warehouse and location (null = no location), lots by
        // earliest expiry; the item comes from a visible order line, never from the caller.
        var setup = await SetupAsync("DO");
        var other = await SetupAsync("DX");
        long lotItem, serialItem, lotLine, serialLine, w2, location;
        await using (var db = Context(setup.CompanyId))
        {
            var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Unit", 0, Ct);
            var items = new ItemService(db);
            lotItem = (await items.CreateAsync("L" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Lot item", null,
                uom.Id, null, null, ItemTrackingMode.Lot, Ct)).Id;
            serialItem = (await items.CreateAsync("S" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Serial item",
                null, uom.Id, null, null, ItemTrackingMode.Serial, Ct)).Id;
            w2 = (await new WarehouseService(db).CreateAsync("W" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                "Second", null, null, null, Ct)).Id;
            location = (await new StorageLocationService(db).CreateAsync(setup.WarehouseId, "BIN1", "Bin 1", Ct)).Id;
            var order = new SalesOrder { CompanyId = setup.CompanyId, OwnerUserId = 7,
                DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = 21,
                CustomerCode = "C21", CustomerName = "Delivery customer", BusinessDate = Day, CurrencyCode = "TWD",
                BaseCurrencyCode = "TWD", ExchangeRate = 1m, OriginalNet = 50m, OriginalGross = 50m,
                BaseNet = 50m, BaseGross = 50m };
            order.Lines.Add(Line(setup.CompanyId, 1, lotItem, "LOT", 3m));
            order.Lines.Add(Line(setup.CompanyId, 2, serialItem, "SER", 2m));
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-10-27' WHERE id = {0}", [order.Id], Ct);
            (lotLine, serialLine) = (order.Lines[0].Id, order.Lines[1].Id);

            var lots = new[]
            {
                (No: "LOT-B", Wh: setup.WarehouseId, Loc: (long?)null, Qty: 5m, Expiry: (DateOnly?)new DateOnly(2028, 6, 1)),
                (No: "LOT-A", Wh: setup.WarehouseId, Loc: (long?)null, Qty: 2.5m, Expiry: (DateOnly?)new DateOnly(2028, 3, 1)),
                (No: "LOT-N", Wh: setup.WarehouseId, Loc: (long?)null, Qty: 1m, Expiry: (DateOnly?)null),
                (No: "LOT-C", Wh: setup.WarehouseId, Loc: (long?)location, Qty: 4m, Expiry: (DateOnly?)null),
                (No: "LOT-D", Wh: w2, Loc: (long?)null, Qty: 3m, Expiry: (DateOnly?)null),
                (No: "LOT-E", Wh: setup.WarehouseId, Loc: (long?)null, Qty: 2m, Expiry: (DateOnly?)null),
            };
            var source = 900L;
            foreach (var lot in lots)
                await PostTrackedAsync(db, ++source, StockDirection.In, lotItem, lot.Wh, lot.Loc, lot.Qty,
                    [InventoryTrackingLine.Lot(lot.No, lot.Qty, null, lot.Expiry)]);
            await PostTrackedAsync(db, ++source, StockDirection.Out, lotItem, setup.WarehouseId, null, 2m,
                [InventoryTrackingLine.Lot("LOT-E", 2m)]);
            foreach (var (serial, wh) in new[] { ("SN-2", setup.WarehouseId), ("SN-1", setup.WarehouseId),
                         ("SN-3", setup.WarehouseId), ("SN-4", w2) })
                await PostTrackedAsync(db, ++source, StockDirection.In, serialItem, wh, null, 1m,
                    [InventoryTrackingLine.Serial(serial)]);
            await PostTrackedAsync(db, ++source, StockDirection.Out, serialItem, setup.WarehouseId, null, 1m,
                [InventoryTrackingLine.Serial("SN-3")]);
        }
        var creator = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE");
        var editor = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.EDIT");
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW", "INV.TRACKING.VIEW");
        var selfCreator = await UserAsync(setup.CompanyId, DataScope.Self, "SAL.DELIVERY.CREATE");
        using var client = factory.CreateClient();
        var create = await LoginAsync(client, creator);
        var edit = await LoginAsync(client, editor);
        var view = await LoginAsync(client, viewer);
        var self = await LoginAsync(client, selfCreator);

        string Options(long orderLineId, long warehouseId, long? locationId = null, string? keyword = null) =>
            $"{Route}/tracking-options?orderLineId={orderLineId}&warehouseId={warehouseId}"
            + (locationId is { } l ? $"&locationId={l}" : "") + (keyword is null ? "" : $"&keyword={Uri.EscapeDataString(keyword)}");
        async Task<(string? Mode, string[] Numbers, decimal[] Quantities, bool Truncated)> ReadAsync(string token, string path)
        {
            using var response = await SendAsync(client, token, setup.CompanyId, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            var items = body.GetProperty("items").EnumerateArray().ToArray();
            return (body.GetProperty("trackingMode").GetString(), [.. items.Select(x => x.GetProperty("number").GetString()!)],
                [.. items.Select(x => x.GetProperty("quantity").GetDecimal())], body.GetProperty("truncated").GetBoolean());
        }

        using (var anonymous = await client.GetAsync(Options(lotLine, setup.WarehouseId), Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var lotOptions = await ReadAsync(create, Options(lotLine, setup.WarehouseId));
        Assert.Equal("Lot", lotOptions.Mode);
        Assert.Equal(["LOT-A", "LOT-B", "LOT-N"], lotOptions.Numbers);
        Assert.Equal([2.5m, 5m, 1m], lotOptions.Quantities);
        Assert.False(lotOptions.Truncated);
        using (var dated = await SendAsync(client, create, setup.CompanyId, HttpMethod.Get, Options(lotLine, setup.WarehouseId)))
        {
            var first = (await dated.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items")[0];
            Assert.Equal("2028-03-01", first.GetProperty("expiryDate").GetString());
        }
        Assert.Equal(["LOT-C"], (await ReadAsync(edit, Options(lotLine, setup.WarehouseId, location))).Numbers);
        Assert.Equal(["LOT-D"], (await ReadAsync(edit, Options(lotLine, w2))).Numbers);
        Assert.Equal(["LOT-B"], (await ReadAsync(create, Options(lotLine, setup.WarehouseId, keyword: " lot-b "))).Numbers);

        var serialOptions = await ReadAsync(edit, Options(serialLine, setup.WarehouseId));
        Assert.Equal("Serial", serialOptions.Mode);
        Assert.Equal(["SN-1", "SN-2"], serialOptions.Numbers);
        Assert.Equal([1m, 1m], serialOptions.Quantities);
        var plain = await ReadAsync(create, Options(setup.LineA, setup.WarehouseId));
        Assert.Equal(("None", 0), (plain.Mode, plain.Numbers.Length));

        using (var forbidden = await SendAsync(client, view, setup.CompanyId, HttpMethod.Get, Options(lotLine, setup.WarehouseId)))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var hidden = await SendAsync(client, self, setup.CompanyId, HttpMethod.Get, Options(lotLine, setup.WarehouseId)))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal("SAL.ORDER.NOT_FOUND", await ErrorCodeAsync(hidden));
        }
        using (var foreign = await SendAsync(client, create, setup.CompanyId, HttpMethod.Get, Options(other.LineA, other.WarehouseId)))
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        foreach (var bad in new[] { Options(lotLine, 0), Options(0, setup.WarehouseId), Options(lotLine, setup.WarehouseId, 0),
                     Options(lotLine, setup.WarehouseId, keyword: new string('x', 101)) })
        {
            using var invalid = await SendAsync(client, create, setup.CompanyId, HttpMethod.Get, bad);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task ReceiptsAndDetailNameEachLineWarehouseAndLocation()
    {
        // SD-DEL-HTTP-010 (WU-SD-004c2b2c, ADR-153): POST/PUT receipts and the VIEW detail carry the warehouse and
        // location code and name of every line without MD.WAREHOUSE.VIEW; a line without location has null location
        // labels; an inactive warehouse is still labelled; a soft-deleted reference has null labels (UI falls back to ID).
        var setup = await SetupAsync("DN");
        long location;
        string warehouseCode;
        await using (var db = Context(setup.CompanyId))
        {
            location = (await new StorageLocationService(db).CreateAsync(setup.WarehouseId, "BIN7", "Bin seven", Ct)).Id;
            warehouseCode = await db.Database.SqlQueryRaw<string>(
                "SELECT code AS Value FROM dbo.md_warehouse WHERE id = {0}", setup.WarehouseId).SingleAsync(Ct);
        }
        var writer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT");
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        var read = await LoginAsync(client, viewer);
        var draft = new
        {
            orderId = setup.OrderId, businessDate = "2028-02-29",
            lines = new object[]
            {
                new { lineNo = 1, orderLineId = setup.LineA, warehouseId = setup.WarehouseId, locationId = location, quantity = 1m },
                new { lineNo = 2, orderLineId = setup.LineB, warehouseId = setup.WarehouseId, quantity = 1m },
            }
        };
        static (string? WhCode, string? WhName, string? LocCode, string? LocName)[] Labels(JsonElement body) =>
            [.. body.GetProperty("lines").EnumerateArray().OrderBy(x => x.GetProperty("lineNo").GetInt32())
                .Select(x => (x.GetProperty("warehouseCode").GetString(), x.GetProperty("warehouseName").GetString(),
                    x.GetProperty("locationCode").GetString(), x.GetProperty("locationName").GetString()))];
        (string?, string?, string?, string?)[] expected =
            [(warehouseCode, "Warehouse", "BIN7", "Bin seven"), (warehouseCode, "Warehouse", null, null)];

        using var created = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post, Route, draft);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(expected, Labels(receipt));
        var id = receipt.GetProperty("id").GetInt64();
        using (var edited = await SendAsync(client, write, setup.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   new { version = 1, draft }))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.Equal(expected, Labels(await edited.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        using (var detail = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var body = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(expected, Labels(body));
            Assert.Equal([(true, true), (true, null)], Active(body));
        }

        await using (var db = Context(setup.CompanyId))
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.md_warehouse SET is_active = 0 WHERE id = {0}", [setup.WarehouseId], Ct);
            await db.Database.ExecuteSqlRawAsync("UPDATE dbo.md_storage_location SET is_deleted = 1 WHERE id = {0}", [location], Ct);
        }
        using (var detail = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal([(warehouseCode, "Warehouse", null, null), (warehouseCode, "Warehouse", null, null)],
                Labels(await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        }
        static (bool? Warehouse, bool? Location)[] Active(JsonElement body) =>
            [.. body.GetProperty("lines").EnumerateArray().OrderBy(x => x.GetProperty("lineNo").GetInt32())
                .Select(x => (Flag(x, "warehouseActive"), Flag(x, "locationActive")))];
        static bool? Flag(JsonElement line, string name) =>
            line.GetProperty(name).ValueKind == JsonValueKind.Null ? null : line.GetProperty(name).GetBoolean();
        using (var inactive = await SendAsync(client, read, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
            Assert.Equal([(false, null), (false, null)], Active(await inactive.Content.ReadFromJsonAsync<JsonElement>(Ct)));
    }

    [Fact]
    public async Task ZeroStepSubmitInventoryRefusalNamesTheDeliveryLines()
    {
        // SD-DEL-HTTP-011 (WU-SD-004c2b2d, ADR-154): with no applicable approval step the submission posts through
        // INV; an INV stock refusal carries the delivery lineNo (smallest) and lineNos (every line of the short
        // balance key) so the detail can mark them; the delivery stays Draft v1 with no SYS round or movement.
        var setup = await SetupAsync("DZ");
        long location;
        await using (var db = Context(setup.CompanyId))
        {
            location = (await new StorageLocationService(db).CreateAsync(setup.WarehouseId, "BIN9", "Bin nine", Ct)).Id;
            var itemId = await db.Set<SalesOrderLine>().Where(x => x.Id == setup.LineA).Select(x => x.ItemId).SingleAsync(Ct);
            await SeedStockAsync(db, 1, itemId, setup.WarehouseId, 2m);
        }
        var writer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.CREATE", "SAL.DELIVERY.EDIT",
            "SAL.DELIVERY.SUBMIT", "SAL.DELIVERY.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        async Task<long> CreateAsync(params object[] lines)
        {
            using var created = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post, Route,
                new { orderId = setup.OrderId, businessDate = "2028-02-29", lines });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }
        async Task<(string?, int, string)> RefusedAsync(long id)
        {
            using var refused = await SendAsync(client, write, setup.CompanyId, HttpMethod.Post,
                $"{Route}/{id}/submit", new { version = 1 });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            var body = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
            return (body.GetProperty("errorCode").GetString(), body.GetProperty("lineNo").GetInt32(),
                string.Join(",", body.GetProperty("lineNos").EnumerateArray().Select(x => x.GetInt32())));
        }

        // Line 1 (no location) has stock; only line 2 at BIN9 is short.
        var single = await CreateAsync(
            new { lineNo = 1, orderLineId = setup.LineA, warehouseId = setup.WarehouseId, quantity = 1m },
            new { lineNo = 2, orderLineId = setup.LineB, warehouseId = setup.WarehouseId, locationId = location, quantity = 1m });
        Assert.Equal(("INV.STOCK.INSUFFICIENT", 2, "2"), await RefusedAsync(single));
        // Two lines of one balance key are each covered but together short: both are named.
        var both = await CreateAsync(
            new { lineNo = 1, orderLineId = setup.LineA, warehouseId = setup.WarehouseId, quantity = 1.5m },
            new { lineNo = 2, orderLineId = setup.LineB, warehouseId = setup.WarehouseId, quantity = 1m });
        Assert.Equal(("INV.STOCK.INSUFFICIENT", 1, "1,2"), await RefusedAsync(both));

        foreach (var id in new[] { single, both })
        {
            using var detail = await SendAsync(client, write, setup.CompanyId, HttpMethod.Get, $"{Route}/{id}");
            var body = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal((nameof(SalesDeliveryStatus.Draft), 1, 0), (body.GetProperty("status").GetString(),
                body.GetProperty("version").GetInt32(), body.GetProperty("approvalHistory").GetArrayLength()));
        }
        await using (var db = Context(setup.CompanyId))
            Assert.False(await db.Set<StockMovement>().AnyAsync(x =>
                x.SourceType == ExternalInventorySourceTypes.SalesOutbound, Ct));
    }

    private static async Task PostTrackedAsync(MyErpDbContext db, long sourceId, StockDirection direction, long itemId,
        long warehouseId, long? locationId, decimal quantity, IReadOnlyList<InventoryTrackingLine> tracking) =>
        await new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
                new FixedClock(new DateTime(2026, 10, 27, 8, 0, 0)))
            .PostAsync(new InventoryPostingRequest("SEED_SD_TRACKING", sourceId, Day,
                [new InventoryPostingLine(1, direction, itemId, warehouseId, locationId, quantity,
                    direction == StockDirection.In ? 10m : null, tracking)]), Ct);

    private static async Task SeedStockAsync(MyErpDbContext db, long sourceId, long itemId, long warehouseId,
        decimal quantity) =>
        await new StockPostingService(db, new InventoryReferenceService(db, new InventoryReferenceGuard(db)),
                new FixedClock(new DateTime(2026, 10, 27, 8, 0, 0)))
            .PostAsync(new InventoryPostingRequest("SEED_SD_DELIVERY", sourceId, Day,
                [new InventoryPostingLine(1, StockDirection.In, itemId, warehouseId, null, quantity, 10m)]), Ct);

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
            new CurrencyDbModel(), new TaxDbModel(), new InventoryReferenceDbModel(), new StockMovementDbModel(),
            new StockTrackingDbModel()],
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
