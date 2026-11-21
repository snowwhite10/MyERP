using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
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
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-SD-003b2: authorised order submit/cancel HTTP on the real API, SYS authorization, MD customer/currency,
/// FI credit tables and SQL. SD-ORDER-HTTP-001～004; WU-SD-003b3 races and resubmission SD-ORDER-HTTP-005～007.
/// </summary>
public sealed class SalesOrderCommandHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/orders";
    private static readonly DateOnly Day = new(2026, 10, 1);
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task SubmitAndCancelRequireAuthenticationAndAppearInOpenApi()
    {
        // SD-ORDER-HTTP-001: anonymous 401; both commands document 403/404/409/422.
        using var client = factory.CreateClient();
        foreach (var action in new[] { "submit", "cancel" })
        {
            using var command = await client.PostAsJsonAsync($"{Route}/1/{action}", new { version = 1 }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, command.StatusCode);
        }

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        foreach (var action in new[] { "submit", "cancel" })
        {
            var responses = paths.GetProperty($"{Route}/{{id}}/{action}").GetProperty("post").GetProperty("responses");
            foreach (var status in new[] { "200", "403", "404", "409", "422" })
                Assert.True(responses.TryGetProperty(status, out _), $"{action} documents {status}");
        }
    }

    [Fact]
    public async Task SubmitChecksPermissionVersionAndCreditThenReturnsTheAssessment()
    {
        // SD-ORDER-HTTP-002: SUBMIT alone drives submit; refusal changes nothing; zero-step approves at once.
        var setup = await SetupAsync("SB", limit: 1000m);
        var over = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 1000.0001m);
        var zeroStep = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 400m);
        var pending = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 600m);
        var submitter = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.SUBMIT");
        var viewer = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.VIEW", "SAL.ORDER.CANCEL");
        var selfSubmitter = await UserAsync(setup.CompanyId, DataScope.Self, "SAL.ORDER.SUBMIT");
        var foreign = await SetupAsync("SC", limit: 1000m);
        var foreignSubmitter = await UserAsync(foreign.CompanyId, DataScope.Company, "SAL.ORDER.SUBMIT");
        using var client = factory.CreateClient();
        var submit = await LoginAsync(client, submitter);
        var view = await LoginAsync(client, viewer);

        using (var denied = await SendAsync(client, view, setup.CompanyId, $"{Route}/{zeroStep}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));
        }
        using (var missing = await SendAsync(client, submit, setup.CompanyId, $"{Route}/{long.MaxValue}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("SAL.ORDER.NOT_FOUND", await ErrorCodeAsync(missing));
        }
        using (var hidden = await SendAsync(client, await LoginAsync(client, selfSubmitter), setup.CompanyId,
                   $"{Route}/{zeroStep}/submit", 1))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var otherCompany = await SendAsync(client, await LoginAsync(client, foreignSubmitter),
                   foreign.CompanyId, $"{Route}/{zeroStep}/submit", 1))
            Assert.Equal(HttpStatusCode.NotFound, otherCompany.StatusCode);
        using (var stale = await SendAsync(client, submit, setup.CompanyId, $"{Route}/{zeroStep}/submit", 2))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("COMMON.CONCURRENCY.CONFLICT", await ErrorCodeAsync(stale));
        }
        using (var exceeded = await SendAsync(client, submit, setup.CompanyId, $"{Route}/{over}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, exceeded.StatusCode);
            var problem = await exceeded.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("SAL.CREDIT.LIMIT_EXCEEDED", problem.GetProperty("errorCode").GetString());
            Assert.Equal(0.0001m, problem.GetProperty("excess").GetDecimal());
            Assert.Equal("TWD", problem.GetProperty("currency").GetString());
            Assert.True(problem.TryGetProperty("traceId", out _));
        }

        // No SYS flow: zero-step approval in the same transaction; the credit evaluation is returned.
        using (var approved = await SendAsync(client, submit, setup.CompanyId, $"{Route}/{zeroStep}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
            var body = await approved.Content.ReadFromJsonAsync<JsonElement>(Ct);
            var order = body.GetProperty("order");
            Assert.Equal("Approved", order.GetProperty("status").GetString());
            Assert.Equal(2, order.GetProperty("version").GetInt32());
            Assert.True(order.GetProperty("approvalInstanceId").GetInt64() > 0);
            Assert.Equal(2, order.GetProperty("lines").GetArrayLength());
            var credit = body.GetProperty("credit");
            Assert.Equal("WithinLimit", credit.GetProperty("status").GetString());
            Assert.Equal("TWD", credit.GetProperty("baseCurrency").GetString());
            Assert.Equal(1000m, credit.GetProperty("limit").GetDecimal());
            Assert.Equal(0m, credit.GetProperty("postedOpenAr").GetDecimal());
            Assert.Equal(0m, credit.GetProperty("otherCommitments").GetDecimal());
            Assert.Equal(400m, credit.GetProperty("candidate").GetDecimal());
            Assert.Equal(600m, credit.GetProperty("headroom").GetDecimal());
            Assert.Equal(3, credit.GetProperty("limitVersion").GetInt32());
            Assert.Equal(JsonValueKind.String, credit.GetProperty("evaluationDate").ValueKind);
        }
        using (var again = await SendAsync(client, submit, setup.CompanyId, $"{Route}/{zeroStep}/submit", 2))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode);
            Assert.Equal("SAL.ORDER.INVALID_STATUS", await ErrorCodeAsync(again));
        }

        // With a one-step flow the next order waits in SYS; the Approved 400 now counts as a commitment.
        await FlowAsync(setup.CompanyId, viewer);
        using (var waiting = await SendAsync(client, submit, setup.CompanyId, $"{Route}/{pending}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
            var body = await waiting.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("Pending", body.GetProperty("order").GetProperty("status").GetString());
            Assert.Equal(400m, body.GetProperty("credit").GetProperty("otherCommitments").GetDecimal());
            Assert.Equal(0m, body.GetProperty("credit").GetProperty("headroom").GetDecimal());
        }

        await using var verify = Context(setup.CompanyId);
        var refused = await verify.Set<SalesOrder>().SingleAsync(x => x.Id == over, Ct);
        Assert.Equal((SalesOrderStatus.Draft, 1, (long?)null),
            (refused.Status, refused.Version, refused.ApprovalInstanceId));
    }

    [Fact]
    public async Task CancelNeedsApprovedCurrentOrderWithoutPendingOrPostedDelivery()
    {
        // SD-ORDER-HTTP-003: CANCEL alone; Draft/Pending refused; Pending/Posted delivery blocks;
        // Draft/Rejected/deleted deliveries do not; history and number are retained.
        var setup = await SetupAsync("CA", limit: 1000m);
        var draft = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 10m);
        var pending = await SeedOrderAsync(setup, SalesOrderStatus.Pending, 10m);
        var withPending = await SeedOrderAsync(setup, SalesOrderStatus.Approved, 10m);
        var withPosted = await SeedOrderAsync(setup, SalesOrderStatus.Approved, 10m);
        var cancellable = await SeedOrderAsync(setup, SalesOrderStatus.Approved, 10m);
        await SeedDeliveryAsync(setup.CompanyId, withPending, SalesDeliveryStatus.Pending);
        await SeedDeliveryAsync(setup.CompanyId, withPosted, SalesDeliveryStatus.Posted);
        await SeedDeliveryAsync(setup.CompanyId, cancellable, SalesDeliveryStatus.Draft);
        await SeedDeliveryAsync(setup.CompanyId, cancellable, SalesDeliveryStatus.Rejected);
        await SeedDeliveryAsync(setup.CompanyId, cancellable, SalesDeliveryStatus.Draft, deleted: true);
        var canceller = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.CANCEL");
        var submitter = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.SUBMIT", "SAL.ORDER.VIEW");
        var selfCanceller = await UserAsync(setup.CompanyId, DataScope.Self, "SAL.ORDER.CANCEL");
        using var client = factory.CreateClient();
        var cancel = await LoginAsync(client, canceller);
        var other = await LoginAsync(client, submitter);

        using (var denied = await SendAsync(client, other, setup.CompanyId, $"{Route}/{cancellable}/cancel", 1))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var hidden = await SendAsync(client, await LoginAsync(client, selfCanceller), setup.CompanyId,
                   $"{Route}/{cancellable}/cancel", 1))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal("SAL.ORDER.NOT_FOUND", await ErrorCodeAsync(hidden));
        }
        foreach (var notApproved in new[] { draft, pending })
        {
            using var refused = await SendAsync(client, cancel, setup.CompanyId, $"{Route}/{notApproved}/cancel", 1);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal("SAL.ORDER.INVALID_STATUS", await ErrorCodeAsync(refused));
        }
        foreach (var delivered in new[] { withPending, withPosted })
        {
            using var refused = await SendAsync(client, cancel, setup.CompanyId, $"{Route}/{delivered}/cancel", 1);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
            Assert.Equal("SAL.ORDER.HAS_DELIVERY", await ErrorCodeAsync(refused));
        }
        using (var stale = await SendAsync(client, cancel, setup.CompanyId, $"{Route}/{cancellable}/cancel", 2))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        string documentNo;
        using (var cancelled = await SendAsync(client, cancel, setup.CompanyId, $"{Route}/{cancellable}/cancel", 1))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            var body = await cancelled.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("Cancelled", body.GetProperty("status").GetString());
            Assert.Equal(2, body.GetProperty("version").GetInt32());
            Assert.Equal(2, body.GetProperty("lines").GetArrayLength());
            documentNo = body.GetProperty("documentNo").GetString()!;
        }
        using (var again = await SendAsync(client, cancel, setup.CompanyId, $"{Route}/{cancellable}/cancel", 2))
            Assert.Equal("SAL.ORDER.INVALID_STATUS", await ErrorCodeAsync(again));

        await using var verify = Context(setup.CompanyId);
        var states = await verify.Set<SalesOrder>().Where(x => x.Id == draft || x.Id == pending ||
                x.Id == withPending || x.Id == withPosted || x.Id == cancellable)
            .OrderBy(x => x.Id).Select(x => new { x.Status, x.Version, x.DocumentNo }).ToListAsync(Ct);
        Assert.Equal([SalesOrderStatus.Draft, SalesOrderStatus.Pending, SalesOrderStatus.Approved,
            SalesOrderStatus.Approved, SalesOrderStatus.Cancelled], states.Select(x => x.Status).ToArray());
        Assert.Equal([1, 1, 1, 1, 2], states.Select(x => x.Version).ToArray());
        Assert.Equal(documentNo, states[4].DocumentNo);
    }

    [Fact]
    public async Task CancellingAnApprovedOrderReleasesItsCreditCommitment()
    {
        // SD-ORDER-HTTP-004: an Approved 700 blocks a 400 submit; after cancel the same submit passes.
        var setup = await SetupAsync("CR", limit: 1000m);
        var approved = await SeedOrderAsync(setup, SalesOrderStatus.Approved, 700m);
        var next = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 400m);
        var user = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.SUBMIT", "SAL.ORDER.CANCEL");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, user);

        using (var exceeded = await SendAsync(client, token, setup.CompanyId, $"{Route}/{next}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, exceeded.StatusCode);
            Assert.Equal(100m, (await exceeded.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("excess").GetDecimal());
        }
        using (var cancelled = await SendAsync(client, token, setup.CompanyId, $"{Route}/{approved}/cancel", 1))
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var submitted = await SendAsync(client, token, setup.CompanyId, $"{Route}/{next}/submit", 1);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var credit = (await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("credit");
        Assert.Equal((0m, 600m), (credit.GetProperty("otherCommitments").GetDecimal(),
            credit.GetProperty("headroom").GetDecimal()));
    }

    [Fact]
    public async Task ConcurrentSubmitsOfOneDraftLeaveOneWinnerAndOneSysRound()
    {
        // SD-ORDER-HTTP-005: both requests queue behind a held order lock; only one may start a SYS round.
        var setup = await SetupAsync("DS", limit: 1000m);
        var order = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 300m);
        await FlowAsync(setup.CompanyId, await UserAsync(setup.CompanyId, DataScope.Company, "SYS.APPROVAL.PROCESS"));
        var submitter = await UserAsync(setup.CompanyId, DataScope.Company, "SAL.ORDER.SUBMIT");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, submitter);

        HttpResponseMessage[] results;
        await using (var held = await RowLock.HoldOrderAsync(database.ConnectionString, order))
        {
            var first = SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/submit", 1);
            var second = SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/submit", 1);
            await Task.Delay(300, Ct);
            Assert.False(first.IsCompleted || second.IsCompleted, "both submits wait for the order lock");
            await held.ReleaseAsync();
            results = await Task.WhenAll(first, second);
        }
        using var winner = Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        using var loser = Assert.Single(results, x => x.StatusCode != HttpStatusCode.OK);
        await AssertLoserAsync(loser, (HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT"),
            (HttpStatusCode.UnprocessableEntity, "SAL.ORDER.INVALID_STATUS"));
        var saved = (await winner.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("order");
        Assert.Equal(("Pending", 2), (saved.GetProperty("status").GetString(), saved.GetProperty("version").GetInt32()));

        await using var verify = Context(setup.CompanyId);
        var persisted = await verify.Set<SalesOrder>().SingleAsync(x => x.Id == order, Ct);
        Assert.Equal((SalesOrderStatus.Pending, 2), (persisted.Status, persisted.Version));
        Assert.Equal([persisted.ApprovalInstanceId!.Value], await InstancesAsync(verify, order));
    }

    [Fact]
    public async Task RejectedOrderIsEditedAndResubmittedThenOneSysDecisionWinsTheRace()
    {
        // SD-ORDER-HTTP-006: reject → PUT → resubmit (new SYS round); concurrent approve/reject has one winner.
        var setup = await SetupAsync("RS", limit: 1000m);
        var refs = await ReferencesAsync(setup);
        var order = await SeedOrderAsync(setup, SalesOrderStatus.Draft, 300m);
        var approverName = await UserAsync(setup.CompanyId, DataScope.Company, "SYS.APPROVAL.PROCESS", "SYS.APPROVAL.VIEW");
        await FlowAsync(setup.CompanyId, approverName);
        var clerk = await UserAsync(setup.CompanyId, DataScope.Company,
            "SAL.ORDER.SUBMIT", "SAL.ORDER.EDIT", "SAL.ORDER.VIEW", "SAL.ORDER.CANCEL", "SAL.PRICING.OVERRIDE");
        using var client = factory.CreateClient();
        var approver = await LoginAsync(client, approverName);
        var token = await LoginAsync(client, clerk);

        long firstRound;
        using (var submitted = await SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/submit", 1))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
            firstRound = (await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("order")
                .GetProperty("approvalInstanceId").GetInt64();
        }
        using (var rejected = await SendJsonAsync(client, approver, setup.CompanyId, HttpMethod.Post,
                   $"/api/v1/sys/approvals/{firstRound}/reject",
                   new { comment = "Price too low", version = await InstanceVersionAsync(client, approver, setup, firstRound) }))
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var afterReject = await OrderAsync(client, token, setup, order);
        Assert.Equal("Rejected", afterReject.GetProperty("status").GetString());
        var version = afterReject.GetProperty("version").GetInt32();
        using (var notDraft = await SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/submit", version))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, notDraft.StatusCode);
            Assert.Equal("SAL.ORDER.INVALID_STATUS", await ErrorCodeAsync(notDraft));
        }

        using (var edited = await SendJsonAsync(client, token, setup.CompanyId, HttpMethod.Put, $"{Route}/{order}",
                   new { version, draft = new { customerId = setup.CustomerId, businessDate = "2026-10-01",
                       currencyCode = "TWD", remark = "Revised after rejection",
                       lines = new[] { new { lineNo = 1, itemId = refs.ItemId, quantity = 2m, unitPrice = 100m,
                           discountRate = 0m, taxRateId = refs.TaxRateId, overrideReason = "revised" } } } }))
        {
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var body = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("Draft", body.GetProperty("status").GetString());
            Assert.Equal("Revised after rejection", body.GetProperty("remark").GetString());
            Assert.Equal(1, body.GetProperty("lines").GetArrayLength());
            version = body.GetProperty("version").GetInt32();
        }

        long secondRound;
        using (var resubmitted = await SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/submit", version))
        {
            Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
            var body = await resubmitted.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("Pending", body.GetProperty("order").GetProperty("status").GetString());
            secondRound = body.GetProperty("order").GetProperty("approvalInstanceId").GetInt64();
            // The rejected 300 reserved nothing; the revised 2 × 100 + 5% tax is the only candidate.
            Assert.Equal((210m, 0m), (body.GetProperty("credit").GetProperty("candidate").GetDecimal(),
                body.GetProperty("credit").GetProperty("otherCommitments").GetDecimal()));
        }
        Assert.NotEqual(firstRound, secondRound);

        var instanceVersion = await InstanceVersionAsync(client, approver, setup, secondRound);
        var approve = SendJsonAsync(client, approver, setup.CompanyId, HttpMethod.Post,
            $"/api/v1/sys/approvals/{secondRound}/approve", new { comment = (string?)null, version = instanceVersion });
        var reject = SendJsonAsync(client, approver, setup.CompanyId, HttpMethod.Post,
            $"/api/v1/sys/approvals/{secondRound}/reject", new { comment = "Race", version = instanceVersion });
        var decisions = await Task.WhenAll(approve, reject);
        using var won = Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.OK);
        using var lost = Assert.Single(decisions, x => x.StatusCode != HttpStatusCode.OK);
        Assert.Contains(lost.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity });
        var approvedWon = ReferenceEquals(won, decisions[0]);

        var final = await OrderAsync(client, token, setup, order);
        Assert.Equal(approvedWon ? "Approved" : "Rejected", final.GetProperty("status").GetString());
        await using (var verify = Context(setup.CompanyId))
        {
            var persisted = await verify.Set<SalesOrder>().SingleAsync(x => x.Id == order, Ct);
            Assert.Equal(approvedWon, persisted.ApprovedAt is not null);
            Assert.Equal(secondRound, persisted.ApprovalInstanceId);
            Assert.Equal([firstRound, secondRound], await InstancesAsync(verify, order));
        }
        if (!approvedWon) return;
        using var cancelled = await SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/cancel",
            final.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task CancelAndDeliverySubmitRaceNeverLeaveAPendingDeliveryOnACancelledOrder()
    {
        // SD-ORDER-HTTP-007: both commands lock the order row first, so the second sees the first's result.
        var setup = await SetupAsync("CD", limit: 1000m);
        var refs = await ReferencesAsync(setup);
        var order = await SeedOrderAsync(setup, SalesOrderStatus.Approved, 10m, refs.ItemId);
        await FlowAsync(setup.CompanyId, await UserAsync(setup.CompanyId, DataScope.Company, "SAL.DELIVERY.VIEW"),
            "SAL.DELIVERY");
        var actor = await UserAsync(setup.CompanyId, DataScope.Company,
            "SAL.ORDER.CANCEL", "SAL.DELIVERY.CREATE", "SAL.DELIVERY.SUBMIT");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, actor);
        long orderLineId;
        await using (var db = Context(setup.CompanyId))
            orderLineId = await db.Set<SalesOrderLine>().Where(x => x.OrderId == order).OrderBy(x => x.LineNo)
                .Select(x => x.Id).FirstAsync(Ct);
        long delivery;
        using (var created = await SendJsonAsync(client, token, setup.CompanyId, HttpMethod.Post, "/api/v1/sal/deliveries",
                   new { orderId = order, businessDate = "2026-10-02",
                       lines = new[] { new { lineNo = 1, orderLineId, warehouseId = refs.WarehouseId, quantity = 0.5m } } }))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            delivery = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();
        }

        HttpResponseMessage cancelResult, submitResult;
        await using (var held = await RowLock.HoldOrderAsync(database.ConnectionString, order))
        {
            var cancel = SendAsync(client, token, setup.CompanyId, $"{Route}/{order}/cancel", 1);
            var submit = SendAsync(client, token, setup.CompanyId, $"/api/v1/sal/deliveries/{delivery}/submit", 1);
            await Task.Delay(300, Ct);
            Assert.False(cancel.IsCompleted || submit.IsCompleted, "both commands wait for the order lock");
            await held.ReleaseAsync();
            cancelResult = await cancel;
            submitResult = await submit;
        }
        using (cancelResult)
        using (submitResult)
        {
            var cancelWon = cancelResult.StatusCode == HttpStatusCode.OK;
            Assert.NotEqual(cancelWon, submitResult.StatusCode == HttpStatusCode.OK);
            if (cancelWon)
                await AssertLoserAsync(submitResult, (HttpStatusCode.UnprocessableEntity, "SAL.DELIVERY.ORDER_NOT_APPROVED"));
            else
                await AssertLoserAsync(cancelResult, (HttpStatusCode.UnprocessableEntity, "SAL.ORDER.HAS_DELIVERY"));

            await using var verify = Context(setup.CompanyId);
            var orderState = await verify.Set<SalesOrder>().Where(x => x.Id == order)
                .Select(x => new { x.Status, x.Version }).SingleAsync(Ct);
            var deliveryState = await verify.Set<SalesDelivery>().Where(x => x.Id == delivery)
                .Select(x => new { x.Status, x.Version, x.ApprovalInstanceId }).SingleAsync(Ct);
            if (cancelWon)
            {
                Assert.Equal((SalesOrderStatus.Cancelled, 2), (orderState.Status, orderState.Version));
                Assert.Equal((SalesDeliveryStatus.Draft, 1, (long?)null),
                    (deliveryState.Status, deliveryState.Version, deliveryState.ApprovalInstanceId));
            }
            else
            {
                Assert.Equal((SalesOrderStatus.Approved, 1), (orderState.Status, orderState.Version));
                Assert.Equal((SalesDeliveryStatus.Pending, 2), (deliveryState.Status, deliveryState.Version));
                Assert.NotNull(deliveryState.ApprovalInstanceId);
            }
        }
    }

    private static async Task AssertLoserAsync(HttpResponseMessage loser,
        params (HttpStatusCode Status, string Code)[] allowed)
    {
        var actual = (loser.StatusCode, await ErrorCodeAsync(loser));
        Assert.Contains(allowed, x => x.Status == actual.StatusCode && x.Code == actual.Item2);
    }

    private static async Task<long[]> InstancesAsync(MyErpDbContext db, long orderId) =>
        (await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.sys_approval_instance WHERE document_type = 'SAL.ORDER' AND document_id = {0}",
            orderId).ToListAsync(Ct)).Order().ToArray();

    private static async Task<int> InstanceVersionAsync(HttpClient client, string token, Setup setup, long instanceId)
    {
        using var response = await SendJsonAsync(client, token, setup.CompanyId, HttpMethod.Get,
            $"/api/v1/sys/approvals/{instanceId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("version").GetInt32();
    }

    private static async Task<JsonElement> OrderAsync(HttpClient client, string token, Setup setup, long orderId)
    {
        using var response = await SendJsonAsync(client, token, setup.CompanyId, HttpMethod.Get, $"{Route}/{orderId}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private sealed record References(long ItemId, long TaxRateId, long WarehouseId);

    /// <summary>Real MD item, output tax rate and warehouse plus a delivery number rule for HTTP edit/delivery.</summary>
    private async Task<References> ReferencesAsync(Setup setup)
    {
        await using var db = Context(setup.CompanyId);
        var taxCode = new TaxCode { CompanyId = setup.CompanyId, Code = "OUT", Name = "Output", TaxType = "Output" };
        db.AddRange(taxCode, new NumberRule { CompanyId = setup.CompanyId, DocumentType = "SAL.DELIVERY",
            Name = "Sales delivery", Prefix = "RD", DateFormat = NumberDateFormat.None,
            ResetPeriod = NumberResetPeriod.Never, SequenceLength = 3 });
        await db.SaveChangesAsync(Ct);
        var uom = await new UomService(db).CreateAsync("U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            "Race item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync(
            "W" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Warehouse", null, null, null, Ct);
        var taxRate = new TaxRate { CompanyId = setup.CompanyId, TaxCodeId = taxCode.Id,
            EffectiveFrom = new DateOnly(2020, 1, 1), RatePercent = 5m };
        db.Add(taxRate);
        await db.SaveChangesAsync(Ct);
        return new References(item.Id, taxRate.Id, warehouse.Id);
    }

    /// <summary>A second SQL connection holding the order row UPDLOCK until released.</summary>
    private sealed class RowLock : IAsyncDisposable
    {
        private readonly SqlConnection connection;
        private SqlTransaction? transaction;

        private RowLock(SqlConnection connection, SqlTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        public static async Task<RowLock> HoldOrderAsync(string connectionString, long orderId)
        {
            var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(Ct);
            var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Ct);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK) WHERE id = @id";
            command.Parameters.AddWithValue("@id", orderId);
            Assert.Equal(orderId, (long)(await command.ExecuteScalarAsync(Ct))!);
            return new RowLock(connection, transaction);
        }

        public async Task ReleaseAsync()
        {
            if (transaction is null) return;
            await transaction.CommitAsync(Ct);
            await transaction.DisposeAsync();
            transaction = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
            }
            await connection.DisposeAsync();
        }
    }

    private sealed record Setup(long CompanyId, long CustomerId);

    private async Task<Setup> SetupAsync(string prefix, decimal limit)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Order command HTTP", Ct);
        await using var db = Context(company.Id);
        var customer = new Customer { CompanyId = company.Id,
            Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Command customer",
            ReviewStatus = PartnerReviewStatus.Approved };
        db.AddRange(customer, new Currency { CompanyId = company.Id, Code = "TWD", Name = "Taiwan dollar",
            DecimalPlaces = 0, IsBase = true });
        await db.SaveChangesAsync(Ct);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT dbo.ar_credit_key (company_id, customer_id, created_at, created_by, version) " +
            "VALUES ({0}, {1}, '2026-11-01', 1, 1); " +
            "INSERT dbo.ar_credit_limit (company_id, customer_id, base_currency, effective_from, effective_to, " +
            "amount, status, created_at, created_by, version) VALUES ({0}, {1}, 'TWD', '2020-01-01', '2099-12-31', " +
            "{2}, 'Active', '2026-11-01', 1, 3)", [company.Id, customer.Id, limit], Ct);
        return new Setup(company.Id, customer.Id);
    }

    private async Task<long> SeedOrderAsync(Setup setup, SalesOrderStatus status, decimal baseGross,
        long? itemId = null)
    {
        await using var db = Context(setup.CompanyId);
        var order = new SalesOrder { CompanyId = setup.CompanyId, OwnerUserId = 7,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = setup.CustomerId,
            CustomerCode = "C", CustomerName = "Command customer", BusinessDate = Day, CurrencyCode = "TWD",
            BaseCurrencyCode = "TWD", ExchangeRate = 1m, OriginalNet = baseGross, OriginalGross = baseGross,
            BaseNet = baseGross, BaseGross = baseGross };
        order.Lines.Add(Line(setup.CompanyId, 1, baseGross - 1m, itemId));
        order.Lines.Add(Line(setup.CompanyId, 2, 1m, itemId));
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        // Lines are inserted under Draft (trigger 51004); the header then follows allowed transitions (51005).
        string[] path = status switch
        {
            SalesOrderStatus.Pending => ["Pending"],
            SalesOrderStatus.Approved => ["Approved"],
            _ => []
        };
        foreach (var next in path)
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE dbo.sal_order SET status = {1}, approved_at = CASE WHEN {1} = 'Approved' " +
                "THEN '2026-10-27' END WHERE id = {0}", [order.Id, next], Ct);
        return order.Id;
    }

    private static SalesOrderLine Line(long companyId, int lineNo, decimal gross, long? itemId) => new()
    {
        CompanyId = companyId, LineNo = lineNo, ItemId = itemId ?? 30 + lineNo, ItemCode = "I" + lineNo, ItemName = "Item",
        Quantity = 1m, UnitPrice = gross, TaxRateId = 41, OriginalNet = gross, OriginalGross = gross,
        BaseNet = gross, BaseGross = gross
    };

    private async Task SeedDeliveryAsync(long companyId, long orderId, SalesDeliveryStatus status, bool deleted = false)
    {
        await using var db = Context(companyId);
        var orderLineId = await db.Set<SalesOrderLine>().Where(x => x.OrderId == orderId)
            .OrderBy(x => x.LineNo).Select(x => x.Id).FirstAsync(Ct);
        var delivery = new SalesDelivery { CompanyId = companyId, OrderId = orderId, BusinessDate = Day,
            DocumentNo = "SD" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), Status = status,
            PostedAt = status == SalesDeliveryStatus.Posted ? new DateTime(2026, 10, 28) : null,
            IsDeleted = deleted, DeletedAt = deleted ? new DateTime(2026, 10, 28) : null };
        delivery.Lines.Add(new SalesDeliveryLine { CompanyId = companyId, LineNo = 1, OrderLineId = orderLineId,
            ItemId = 31, WarehouseId = 1, Quantity = 0.5m });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
    }

    private async Task FlowAsync(long companyId, string approverName, string documentType = "SAL.ORDER")
    {
        await using var db = Context(companyId);
        var approverId = await db.Set<MyERP.Modules.System.Domain.SystemUser>().Where(x => x.Username == approverName)
            .Select(x => x.Id).SingleAsync(Ct);
        await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput(documentType, documentType, false,
            [new ApprovalStepInput("Approver", ApproverType.User, null, approverId, null)]), Ct);
    }

    private async Task<string> UserAsync(long companyId, DataScope scope, params string[] permissions)
    {
        await using var services = factory.Services.CreateAsyncScope();
        var roles = services.ServiceProvider.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput("O" + Guid.NewGuid().ToString("N")[..12],
            "Order command role", null, scope, false, permissions), Ct);
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

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token, long companyId,
        string path, int version) => SendJsonAsync(client, token, companyId, HttpMethod.Post, path, new { version });

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, string token, long companyId,
        HttpMethod method, string path, object? body)
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
