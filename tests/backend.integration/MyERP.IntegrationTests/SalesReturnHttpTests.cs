using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// SD-RETURN-HTTP-001～006 (WU-SD-008a6, ADR-192; 006 WU-SD-008b1, ADR-193): sales return routes over the real API, SYS authorization and data
/// scope, the real SD Draft builder, SYS approval workflow and SQL. Unbilled-only returns keep FI out of the posting;
/// INV is a recording double of its public contract (real SALES_RETURN inbound: INV-SRET-001～005) and MD
/// destinations pass through a guard double. Billed portions and FI credit are proven by SD-RETURN-POST/APPROVE.
/// </summary>
public sealed class SalesReturnHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/returns";
    private const long Warehouse = 51;
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static readonly DateTime Now = new(2026, 11, 6, 9, 30, 0);
    private static readonly string[] WriterPermissions =
        ["SAL.RETURN.VIEW", "SAL.RETURN.CREATE", "SAL.RETURN.EDIT", "SAL.RETURN.DELETE", "SAL.RETURN.SUBMIT"];
    private readonly RecordingInventory inventory = new();
    private ApiFactory factory = null!;
    private long seededDeliveryLine;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database, inventory);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task RoutesRequireAuthenticationAndAppearInOpenApi()
    {
        // SD-RETURN-HTTP-001: anonymous 401 on every route; OpenAPI documents each status and the list filters.
        using var client = factory.CreateClient();
        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Get, Route), (HttpMethod.Get, Route + "/1"), (HttpMethod.Post, Route),
                     (HttpMethod.Put, Route + "/1"), (HttpMethod.Delete, Route + "/1?version=1"),
                     (HttpMethod.Post, Route + "/1/submit")
                 })
        {
            using var request = new HttpRequestMessage(method, path);
            if (method != HttpMethod.Get && method != HttpMethod.Delete) request.Content = JsonContent.Create(new { });
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        AssertStatuses(paths.GetProperty(Route).GetProperty("post"), "200", "201", "400", "401", "403", "404", "409",
            "422");
        var list = paths.GetProperty(Route).GetProperty("get");
        AssertStatuses(list, "200", "400", "401", "403");
        var parameters = list.GetProperty("parameters").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "keyword", "from", "to", "orderId", "customerId", "status", "sort", "page",
                     "pageSize" })
            Assert.Contains(name, parameters);
        var item = paths.GetProperty(Route + "/{id}");
        AssertStatuses(item.GetProperty("get"), "200", "401", "403", "404");
        AssertStatuses(item.GetProperty("put"), "200", "400", "401", "403", "404", "409", "422");
        AssertStatuses(item.GetProperty("delete"), "204", "401", "403", "404", "409", "422");
        AssertStatuses(paths.GetProperty(Route + "/{id}/submit").GetProperty("post"), "200", "401", "403", "404",
            "409", "422", "500");
    }

    [Fact]
    public async Task WriterCreatesReplaysEditsAndSubmitsWhileViewerReadsTheSameDetail()
    {
        // SD-RETURN-HTTP-002: 201 + Location; identical replay 200 without a second return; changed payload 409; the
        // viewer sees the same detail and list row; PUT replaces lines (version 2); a stale version is 409 and a new
        // request key 422; zero-step SUBMIT returns Posted with INV movements; Posted cannot be edited or deleted.
        var s = await SeedAsync("RA");
        var writer = await UserAsync(s.CompanyId, DataScope.Company, WriterPermissions);
        var viewer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.RETURN.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        var read = await LoginAsync(client, viewer);

        using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
            Body("R-1", 1m, "  First  "));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"{Route}/{id}", created.Headers.Location!.OriginalString);
        Assert.Equal(("RA001", "Draft", 1, "First"), (body.GetProperty("documentNo").GetString(),
            body.GetProperty("status").GetString(), body.GetProperty("version").GetInt32(),
            body.GetProperty("remark").GetString()));
        Assert.Equal((s.OrderNo, "C21", "USD", "TWD"), (body.GetProperty("orderDocumentNo").GetString(),
            body.GetProperty("customerCode").GetString(), body.GetProperty("currencyCode").GetString(),
            body.GetProperty("baseCurrencyCode").GetString()));
        // One of three unbilled units: a third of the order line's base 3,100 + 155, rounded to whole TWD.
        Assert.Equal((1033m, 52m, 1085m), (body.GetProperty("baseNet").GetDecimal(),
            body.GetProperty("baseTax").GetDecimal(), body.GetProperty("baseGross").GetDecimal()));
        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal((s.DeliveryLineId, s.DeliveryNo, 1, "I31", 1m, 1m, 0m), (
            line.GetProperty("deliveryLineId").GetInt64(), line.GetProperty("deliveryDocumentNo").GetString(),
            line.GetProperty("orderLineNo").GetInt32(), line.GetProperty("itemCode").GetString(),
            line.GetProperty("quantity").GetDecimal(), line.GetProperty("unbilledQuantity").GetDecimal(),
            line.GetProperty("billedQuantity").GetDecimal()));

        using (var replay = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                   Body("R-1", 1m, "  First  ")))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Null(replay.Headers.Location);
            Assert.Equal(body.GetRawText(), (await replay.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetRawText());
        }
        using (var changed = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route, Body("R-1", 2m)))
        {
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            Assert.Equal("SAL.RETURN.REQUEST_CONFLICT", await ErrorCodeAsync(changed));
        }
        Assert.Equal(1, await ReturnCountAsync(s));

        using (var detail = await SendAsync(client, read, s.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(body.GetRawText(), (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetRawText());
        }
        var page = await GetOkAsync(client, read, s.CompanyId, Route + "?status=Draft&orderId=" + s.OrderId);
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal((id, 1, "Draft"), (row.GetProperty("id").GetInt64(), row.GetProperty("lineCount").GetInt32(),
            row.GetProperty("status").GetString()));
        using (var noWrite = await SendAsync(client, read, s.CompanyId, HttpMethod.Post, Route, Body("R-2", 1m)))
            Assert.Equal(HttpStatusCode.Forbidden, noWrite.StatusCode);

        using (var edited = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   Replace(1, Body("R-1", 2m))))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var saved = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal((2, "Draft", 2m), (saved.GetProperty("version").GetInt32(),
                saved.GetProperty("status").GetString(),
                saved.GetProperty("lines")[0].GetProperty("quantity").GetDecimal()));
            Assert.Equal(JsonValueKind.Null, saved.GetProperty("remark").ValueKind);
        }
        using (var stale = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   Replace(1, Body("R-1", 1m))))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var rekeyed = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   Replace(2, Body("R-9", 1m))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rekeyed.StatusCode);
            Assert.Equal("SAL.RETURN.REQUEST_KEY_IMMUTABLE", await ErrorCodeAsync(rekeyed));
        }

        using (var stalePost = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, $"{Route}/{id}/submit",
                   new { version = 1 }))
            Assert.Equal(HttpStatusCode.Conflict, stalePost.StatusCode);
        Assert.Empty(inventory.Calls);
        using (var submitted = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, $"{Route}/{id}/submit",
                   new { version = 2 }))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            var posted = await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(("Posted", JsonValueKind.Number, JsonValueKind.Null), (posted.GetProperty("status").GetString(),
                posted.GetProperty("approvalInstanceId").ValueKind, posted.GetProperty("fiCreditNoteId").ValueKind));
            var call = Assert.Single(inventory.Calls);
            Assert.Equal(id, call.SourceId);
            Assert.Equal(call.Ids[0], posted.GetProperty("lines")[0].GetProperty("invMovementId").GetInt64());
            var version = posted.GetProperty("version").GetInt32();
            using var editPosted = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                Replace(version, Body("R-1", 1m)));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, editPosted.StatusCode);
            Assert.Equal("SAL.RETURN.INVALID_STATUS", await ErrorCodeAsync(editPosted));
            using var deletePosted = await SendAsync(client, write, s.CompanyId, HttpMethod.Delete,
                $"{Route}/{id}?version={version}");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, deletePosted.StatusCode);
            using var again = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, $"{Route}/{id}/submit",
                new { version });
            Assert.Equal("SAL.RETURN.INVALID_STATUS", await ErrorCodeAsync(again));
        }
        Assert.Single(inventory.Calls);
    }

    [Fact]
    public async Task RefusalsReturnNamedProblemDetailsAndLeaveTheDraftUnchanged()
    {
        // SD-RETURN-HTTP-003: 400 field errors; 404 source; 422 line errors with lineNo and remaining; INV replay 409
        // INV_POSTING_CONFLICT rolls the SYS instance back (still Draft v1); DELETE 204 then 404 for every route.
        var s = await SeedAsync("RB");
        var writer = await UserAsync(s.CompanyId, DataScope.Company, WriterPermissions);
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);

        using (var invalid = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                   new { requestKey = "bad key!", businessDate = "2026-11-07", lines = Array.Empty<object>() }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("requestKey", out _));
            Assert.True(errors.TryGetProperty("lines", out _));
        }
        using (var noDraft = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/1",
                   new { version = 1 }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noDraft.StatusCode);
            var errors = (await noDraft.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("requestKey", out _));
        }
        using (var missing = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                   Body("R-X", 1m, deliveryLineId: s.DeliveryLineId + 1_000_000)))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", await ErrorCodeAsync(missing));
        }
        using (var exceeded = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route, Body("R-X", 3.5m)))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, exceeded.StatusCode);
            var problem = await exceeded.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.StartsWith("SAL.RETURN.", problem.GetProperty("errorCode").GetString());
            Assert.Equal(1, problem.GetProperty("lineNo").GetInt32());
        }
        using (var early = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                   Body("R-X", 1m, date: "2026-11-05")))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, early.StatusCode);
            Assert.Equal("SAL.RETURN.DATE_BEFORE_DELIVERY", await ErrorCodeAsync(early));
        }
        Assert.Equal(0, await ReturnCountAsync(s));

        using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route, Body("R-2", 1m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        inventory.Replay = true;
        try
        {
            using var conflict = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, $"{Route}/{id}/submit",
                new { version = 1 });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("SAL.RETURN.INV_POSTING_CONFLICT", await ErrorCodeAsync(conflict));
        }
        finally
        {
            inventory.Replay = false;
        }
        var draft = await GetOkAsync(client, write, s.CompanyId, $"{Route}/{id}");
        Assert.Equal(("Draft", 1, JsonValueKind.Null), (draft.GetProperty("status").GetString(),
            draft.GetProperty("version").GetInt32(), draft.GetProperty("approvalInstanceId").ValueKind));

        using (var staleDelete = await SendAsync(client, write, s.CompanyId, HttpMethod.Delete,
                   $"{Route}/{id}?version=2"))
            Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);
        using (var deleted = await SendAsync(client, write, s.CompanyId, HttpMethod.Delete, $"{Route}/{id}?version=1"))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, $"{Route}/{id}", null), (HttpMethod.Put, $"{Route}/{id}", Replace(1, Body("R-2", 1m))),
                     (HttpMethod.Delete, $"{Route}/{id}?version=1", null),
                     (HttpMethod.Post, $"{Route}/{id}/submit", new { version = 1 })
                 })
        {
            using var gone = await SendAsync(client, write, s.CompanyId, method, path, body);
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
            Assert.Equal("SAL.RETURN.NOT_FOUND", await ErrorCodeAsync(gone));
        }
    }

    [Fact]
    public async Task ActionPermissionsAndSelfScopeFollowTheSourceOrderAndCompaniesAreIsolated()
    {
        // SD-RETURN-HTTP-004: each route needs its own action; Self scope hides another owner's order (404 on every
        // route, not counted); another company's return is 404.
        var s = await SeedAsync("RC");
        var writer = await UserAsync(s.CompanyId, DataScope.Company, WriterPermissions);
        var self = await UserAsync(s.CompanyId, DataScope.Self, WriterPermissions);
        var createOnly = await UserAsync(s.CompanyId, DataScope.Company, "SAL.RETURN.CREATE");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route, Body("R-1", 1m));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        var create = await LoginAsync(client, createOnly);
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, Route, null), (HttpMethod.Get, $"{Route}/{id}", null),
                     (HttpMethod.Put, $"{Route}/{id}", Replace(1, Body("R-1", 1m))),
                     (HttpMethod.Delete, $"{Route}/{id}?version=1", null),
                     (HttpMethod.Post, $"{Route}/{id}/submit", new { version = 1 })
                 })
        {
            using var denied = await SendAsync(client, create, s.CompanyId, method, path, body);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var hidden = await LoginAsync(client, self);
        using (var hiddenCreate = await SendAsync(client, hidden, s.CompanyId, HttpMethod.Post, Route, Body("R-S", 1m)))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenCreate.StatusCode);
            Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", await ErrorCodeAsync(hiddenCreate));
        }
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, $"{Route}/{id}", null),
                     (HttpMethod.Put, $"{Route}/{id}", Replace(1, Body("R-1", 1m))),
                     (HttpMethod.Delete, $"{Route}/{id}?version=1", null),
                     (HttpMethod.Post, $"{Route}/{id}/submit", new { version = 1 })
                 })
        {
            using var notFound = await SendAsync(client, hidden, s.CompanyId, method, path, body);
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
            Assert.Equal("SAL.RETURN.NOT_FOUND", await ErrorCodeAsync(notFound));
        }
        Assert.Equal(0, (await GetOkAsync(client, hidden, s.CompanyId, Route)).GetProperty("totalCount").GetInt32());

        var other = await SeedAsync("RD");
        var foreignWriter = await UserAsync(other.CompanyId, DataScope.Company, WriterPermissions);
        var foreign = await LoginAsync(client, foreignWriter);
        using (var foreignGet = await SendAsync(client, foreign, other.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);
        Assert.Equal(0, (await GetOkAsync(client, foreign, other.CompanyId, Route)).GetProperty("totalCount").GetInt32());
        Assert.Equal("Draft", (await GetOkAsync(client, write, s.CompanyId, $"{Route}/{id}")).GetProperty("status")
            .GetString());
    }

    [Fact]
    public async Task RejectedReturnIsRevisedByPutAndResubmittedInANewRound()
    {
        // SD-RETURN-HTTP-005: one-step SYS flow → Pending without INV; SYS reject → Rejected; DELETE Rejected 422;
        // PUT revises it to Draft (version +2); SUBMIT opens a new SYS round and the approver's approval posts it.
        var s = await SeedAsync("RE");
        var writer = await UserAsync(s.CompanyId, DataScope.Company, WriterPermissions);
        var (approverName, approverId) = await UserWithIdAsync(s.CompanyId, DataScope.Company,
            "SYS.APPROVAL.VIEW", "SYS.APPROVAL.PROCESS");
        var (adminName, _) = await UserWithIdAsync(s.CompanyId, DataScope.Company, "SYS.APPROVAL_FLOW.CREATE");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, writer);
        var approve = await LoginAsync(client, approverName);
        var admin = await LoginAsync(client, adminName);
        using (var flow = await SendAsync(client, admin, s.CompanyId, HttpMethod.Post, "/api/v1/sys/approval-flows",
                   new
                   {
                       documentType = "SAL.RETURN", name = "Return", allowSelfApproval = false,
                       steps = new object[] { new { name = "Manager", approverType = "User", userId = approverId } }
                   }))
            Assert.Equal(HttpStatusCode.Created, flow.StatusCode);

        using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route, Body("R-1", 1m));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        long firstRound;
        using (var submitted = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, $"{Route}/{id}/submit",
                   new { version = 1 }))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            var pending = await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(("Pending", 2), (pending.GetProperty("status").GetString(),
                pending.GetProperty("version").GetInt32()));
            firstRound = pending.GetProperty("approvalInstanceId").GetInt64();
        }
        Assert.Empty(inventory.Calls);
        using (var pendingEdit = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   Replace(2, Body("R-1", 2m))))
            Assert.Equal("SAL.RETURN.INVALID_STATUS", await ErrorCodeAsync(pendingEdit));

        using (var rejected = await SendAsync(client, approve, s.CompanyId, HttpMethod.Post,
                   $"/api/v1/sys/approvals/{firstRound}/reject", new { comment = "Wrong quantity", version = 1 }))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var rejectedDetail = await GetOkAsync(client, write, s.CompanyId, $"{Route}/{id}");
        Assert.Equal(("Rejected", 3), (rejectedDetail.GetProperty("status").GetString(),
            rejectedDetail.GetProperty("version").GetInt32()));
        using (var deleteRejected = await SendAsync(client, write, s.CompanyId, HttpMethod.Delete,
                   $"{Route}/{id}?version=3"))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, deleteRejected.StatusCode);
            Assert.Equal("SAL.RETURN.INVALID_STATUS", await ErrorCodeAsync(deleteRejected));
        }

        using (var revised = await SendAsync(client, write, s.CompanyId, HttpMethod.Put, $"{Route}/{id}",
                   Replace(3, Body("R-1", 2m))))
        {
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
            var draft = await revised.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(("Draft", 5, 2m), (draft.GetProperty("status").GetString(),
                draft.GetProperty("version").GetInt32(),
                draft.GetProperty("lines")[0].GetProperty("quantity").GetDecimal()));
        }
        long secondRound;
        using (var resubmitted = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, $"{Route}/{id}/submit",
                   new { version = 5 }))
        {
            Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
            secondRound = (await resubmitted.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("approvalInstanceId").GetInt64();
        }
        Assert.NotEqual(firstRound, secondRound);
        using (var approved = await SendAsync(client, approve, s.CompanyId, HttpMethod.Post,
                   $"/api/v1/sys/approvals/{secondRound}/approve", new { comment = "OK", version = 1 }))
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var posted = await GetOkAsync(client, write, s.CompanyId, $"{Route}/{id}");
        Assert.Equal("Posted", posted.GetProperty("status").GetString());
        Assert.Equal(2m, Assert.Single(inventory.Calls).Lines[0].Quantity);
    }

    [Fact]
    public async Task SourceOrderRoutesOfferReturnableLinesUnderTheCreateScope()
    {
        // SD-RETURN-HTTP-006 (WU-SD-008b1, ADR-193): source picker routes need SAL.RETURN.CREATE and its scope; the
        // detail carries the returnable, unbilled and default destination fields the form needs.
        using var client = factory.CreateClient();
        foreach (var path in new[] { Route + "/source-orders", Route + "/source-orders/1" })
        {
            using var anonymous = await client.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        using (var openApi = await client.GetAsync("/openapi/v1.json", Ct))
        {
            using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
            var paths = document.RootElement.GetProperty("paths");
            AssertStatuses(paths.GetProperty(Route + "/source-orders").GetProperty("get"), "200", "400", "401", "403");
            AssertStatuses(paths.GetProperty(Route + "/source-orders/{orderId}").GetProperty("get"), "200", "401",
                "403", "404", "422");
        }

        var s = await SeedAsync("RS");
        var create = await LoginAsync(client, await UserAsync(s.CompanyId, DataScope.Company, "SAL.RETURN.CREATE"));
        var list = await GetOkAsync(client, create, s.CompanyId, Route + "/source-orders?keyword=" + s.OrderNo);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var row = list.GetProperty("items")[0];
        Assert.Equal((s.OrderId, 1), (row.GetProperty("id").GetInt64(), row.GetProperty("openLineCount").GetInt32()));

        var detail = await GetOkAsync(client, create, s.CompanyId, $"{Route}/source-orders/{s.OrderId}");
        Assert.Equal(("C21", "USD", "TWD"), (detail.GetProperty("customerCode").GetString(),
            detail.GetProperty("currencyCode").GetString(), detail.GetProperty("baseCurrencyCode").GetString()));
        var line = Assert.Single(detail.GetProperty("lines").EnumerateArray());
        Assert.Equal((s.DeliveryLineId, s.DeliveryNo, Warehouse, false, 3m, 3m, 3m, 0),
            (line.GetProperty("deliveryLineId").GetInt64(), line.GetProperty("deliveryDocumentNo").GetString(),
                line.GetProperty("warehouseId").GetInt64(), line.GetProperty("trackingUnsupported").GetBoolean(),
                line.GetProperty("postedQuantity").GetDecimal(), line.GetProperty("returnableQuantity").GetDecimal(),
                line.GetProperty("unbilledRemainingQuantity").GetDecimal(),
                line.GetProperty("invoiceLines").GetArrayLength()));

        var viewer = await LoginAsync(client, await UserAsync(s.CompanyId, DataScope.Company, "SAL.RETURN.VIEW"));
        using (var denied = await SendAsync(client, viewer, s.CompanyId, HttpMethod.Get, Route + "/source-orders"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var self = await LoginAsync(client, await UserAsync(s.CompanyId, DataScope.Self, "SAL.RETURN.CREATE"));
        Assert.Equal(0, (await GetOkAsync(client, self, s.CompanyId, Route + "/source-orders"))
            .GetProperty("totalCount").GetInt32());
        using var hidden = await SendAsync(client, self, s.CompanyId, HttpMethod.Get,
            $"{Route}/source-orders/{s.OrderId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("SAL.RETURN.SOURCE_NOT_FOUND", await ErrorCodeAsync(hidden));
    }

    private static void AssertStatuses(JsonElement operation, params string[] statuses)
    {
        var responses = operation.GetProperty("responses");
        foreach (var status in statuses)
            Assert.True(responses.TryGetProperty(status, out _), status);
    }

    /// <summary>One unbilled-only line of the delivery line seeded last by this test.</summary>
    private object Body(string key, decimal unbilled, string? remark = null, long? deliveryLineId = null,
        string date = "2026-11-07") => new
    {
        requestKey = key, businessDate = date, remark,
        lines = new[]
        {
            new
            {
                lineNo = 1, deliveryLineId = deliveryLineId ?? seededDeliveryLine, warehouseId = Warehouse,
                locationId = (long?)null, unbilledQuantity = unbilled, billed = Array.Empty<object>()
            }
        }
    };

    private static object Replace(int version, object draft) => new { version, draft };

    private static async Task<JsonElement> GetOkAsync(HttpClient client, string token, long companyId, string path)
    {
        using var response = await SendAsync(client, token, companyId, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<int> ReturnCountAsync(Seeded s)
    {
        await using var db = s.Raw();
        return await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.sal_return WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
    }

    private sealed record Seeded(long CompanyId, long OrderId, string OrderNo, long DeliveryLineId, string DeliveryNo,
        Func<MyErpDbContext> Raw);

    /// <summary>
    /// A company with the SAL.RETURN number rule and an Approved USD order (owner 987654, never a test user, rate 31;
    /// line A: 3 units, net 100.00 + tax 5.00, base 3,100 + 155) fully delivered by one Posted delivery whose line
    /// carries INV outbound movement 8,100,001. Nothing is invoiced.
    /// </summary>
    private async Task<Seeded> SeedAsync(string prefix)
    {
        long companyId;
        await using (var scope = factory.Services.CreateAsyncScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Return HTTP", Ct)).Id;

        await using var db = Raw(companyId);
        db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.RETURN", Name = "Sales return",
            Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
            SequenceLength = 3 });
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = 987_654,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = 21,
            CustomerCode = "C21", CustomerName = "Return customer", BusinessDate = Day, CurrencyCode = "USD",
            BaseCurrencyCode = "TWD", ExchangeRate = 31m, OriginalNet = 100m, OriginalTax = 5m, OriginalGross = 105m,
            BaseNet = 3100m, BaseTax = 155m, BaseGross = 3255m };
        order.Lines.Add(new SalesOrderLine { CompanyId = companyId, LineNo = 1, ItemId = 31, ItemCode = "I31",
            ItemName = "Item", Quantity = 3m, UnitPrice = 33.333333m, TaxRateId = 41, TaxRatePercent = 5m,
            OriginalNet = 100m, OriginalTax = 5m, OriginalGross = 105m, BaseNet = 3100m, BaseTax = 155m,
            BaseGross = 3255m });
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE dbo.sal_order SET status = 'Approved', approved_at = '2026-11-06' WHERE id = {0}", [order.Id], Ct);
        var delivery = new SalesDelivery { CompanyId = companyId,
            DocumentNo = "DN" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), OrderId = order.Id,
            BusinessDate = Day, Status = SalesDeliveryStatus.Posted, PostedAt = Now };
        delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 1, OrderLineId = order.Lines[0].Id,
            ItemId = 31, WarehouseId = Warehouse, Quantity = 3m, InvMovementId = 8_100_001 });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        seededDeliveryLine = delivery.Lines[0].Id;
        return new Seeded(companyId, order.Id, order.DocumentNo, delivery.Lines[0].Id, delivery.DocumentNo,
            () => Raw(companyId));
    }

    private MyErpDbContext Raw(long companyId)
    {
        var company = new FixedCompany(companyId);
        var services = new ServiceCollection();
        new SalesModule().AddServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(new FixedClock(Now), new FixedUser(7),
                    company))).Options,
            [.. provider.GetServices<IModuleDbModel>(), new SystemOrganizationDbModel()], company);
    }

    private async Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions) =>
        (await UserWithIdAsync(companyId, scope, permissions)).Username;

    private async Task<(string Username, long Id)> UserWithIdAsync(long companyId, DataScope scope,
        params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("R" + Guid.NewGuid().ToString("N")[..12],
            "Return role", null, scope, false, permissions), Ct);
        var username = "ret" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.ServiceProvider.GetRequiredService<SystemUserService>()
            .CreateAsync(username, Password, Ct);
        await roles.AssignAsync(user.Id, companyId, new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        return (username, user.Id);
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

    /// <summary>Records SD's INV calls and answers with fresh movement IDs; can answer as an INV replay.</summary>
    private sealed class RecordingInventory : IExternalInventoryPostingService
    {
        private static long next = 9_500_000;
        public volatile bool Replay;
        public List<(long SourceId, SalesReturnInboundLine[] Lines, long[] Ids)> Calls { get; } = [];

        public Task<InventoryPostingResult> PostPurchaseInboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<PurchaseInboundLine> lines, CancellationToken token = default) =>
            throw new NotSupportedException();

        public Task<InventoryPostingResult> PostSalesOutboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<SalesOutboundLine> lines, CancellationToken token = default) =>
            throw new NotSupportedException();

        public Task<SalesReturnInboundResult> PostSalesReturnInboundAsync(long sourceId, DateOnly businessDate,
            IReadOnlyList<SalesReturnInboundLine> lines, CancellationToken token = default)
        {
            var ids = lines.Select(_ => Interlocked.Increment(ref next)).ToArray();
            if (!Replay)
                lock (Calls)
                    Calls.Add((sourceId, [.. lines], ids));
            return Task.FromResult(new SalesReturnInboundResult(Replay,
                lines.Select((x, i) => new SalesReturnInboundMovement(x.LineNo, ids[i], x.OriginalMovementId, 3m))
                    .ToArray()));
        }
    }

    private sealed class PassGuard : IInventoryReferenceGuard
    {
        public Task<T> RunAsync<T>(long itemId, long warehouseId, long? locationId, Func<Task<T>> action,
            CancellationToken token = default) => action();

        public Task<T> RunWithCodesAsync<T>(long itemId, long warehouseId, long? locationId,
            Func<InventoryReferenceCodes, Task<T>> action, CancellationToken token = default) =>
            action(new InventoryReferenceCodes("I" + itemId, "W" + warehouseId, null, ItemTrackingMode.None));

        public Task RequireWarehousesAsync(IReadOnlyCollection<long> warehouseIds, CancellationToken token = default) =>
            Task.CompletedTask;
    }

    private sealed class ApiFactory(TestDatabase database, RecordingInventory inventory)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            builder.ConfigureTestServices(services =>
            {
                foreach (var real in services.Where(x => x.ServiceType == typeof(IExternalInventoryPostingService)
                             || x.ServiceType == typeof(IInventoryReferenceGuard)).ToList())
                    services.Remove(real);
                services.AddSingleton<IExternalInventoryPostingService>(inventory);
                services.AddSingleton<IInventoryReferenceGuard>(new PassGuard());
            });
        }
    }
}
