using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Auditing;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>SYS-APR-010～011：審批流程、代理與審批動作 API 的權限、公司隔離、稽核與結果分派（myerp_test + 本機 Redis）。</summary>
public sealed class ApprovalApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private static readonly FixedClock Clock = new(new DateTime(2026, 10, 11, 9, 30, 0));
    private readonly RecordingHandler handler = new();
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database, handler);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task FlowAndDelegationEndpointsRequirePermissionsAndStayInsideTheCompany()
    {
        var companyA = await CreateCompanyAsync();
        var companyB = await CreateCompanyAsync();
        var viewer = await CreateRoleAsync(false, SystemPermissions.ApprovalFlowView, SystemPermissions.ApprovalDelegationView);
        var admin = await CreateRoleAsync(true);
        var (viewerId, viewerToken) = await CreateSignedInUserAsync();
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        await AssignAsync(viewerId, companyA, viewer);
        await AssignAsync(adminId, companyA, admin);
        await AssignAsync(adminId, companyB, admin);
        using var client = factory.CreateClient();
        var body = new
        {
            documentType = "sal.order", name = "Sales order", allowSelfApproval = false,
            steps = new object[]
            {
                new { name = "Supervisor", approverType = "Role", roleId = viewer },
                new { name = "Admin", approverType = "User", userId = adminId, minAmount = 50000.5m },
            },
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approval-flows", null, companyA)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows", viewerToken, companyA, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));

        var created = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows", adminToken, companyA, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var flow = await ReadAsync(created);
        var id = flow.GetProperty("id").GetInt64();
        Assert.Equal("SAL.ORDER", flow.GetProperty("documentType").GetString());
        var steps = flow.GetProperty("steps");
        Assert.Equal(("Role", 1), (steps[0].GetProperty("approverType").GetString(), steps[0].GetProperty("stepNo").GetInt32()));
        Assert.Equal(50000.5m, steps[1].GetProperty("minAmount").GetDecimal());
        var duplicate = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows", adminToken, companyA, body);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ApprovalFlowService.DuplicateType, await ErrorCodeAsync(duplicate));

        // 用戶步驟的審批人必須是該公司的用戶：viewer 不屬於 B 公司
        var foreignUser = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows", adminToken, companyB, new
        {
            documentType = "SAL.ORDER", name = "B order", allowSelfApproval = false,
            steps = new object[] { new { name = "Viewer", approverType = "User", userId = viewerId } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignUser.StatusCode);
        Assert.True((await ReadAsync(foreignUser)).GetProperty("errors").TryGetProperty("steps[0].userId", out _));

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approval-flows?keyword=SAL", viewerToken, companyA));
        Assert.Equal([id], list.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("id").GetInt64()));
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/approval-flows/{id}", viewerToken, companyA)).StatusCode);
        var foreign = await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/approval-flows/{id}", adminToken, companyB);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(ApprovalFlowService.NotFound, await ErrorCodeAsync(foreign));
        var updated = await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/approval-flows/{id}", adminToken, companyA, new
        {
            name = "Sales order approval", allowSelfApproval = true, isActive = true, version = 1,
            steps = new object[] { new { name = "Admin", approverType = "User", userId = adminId } },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(1, (await ReadAsync(updated)).GetProperty("steps").GetArrayLength());

        // 代理
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-delegations", viewerToken, companyA,
            new { delegatorUserId = adminId, delegateUserId = viewerId, startDate = "2026-10-11", endDate = "2026-10-20" })).StatusCode);
        var delegation = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-delegations", adminToken, companyA,
            new { delegatorUserId = adminId, delegateUserId = viewerId, startDate = "2026-10-11", endDate = "2026-10-20", remark = "Leave" });
        Assert.Equal(HttpStatusCode.Created, delegation.StatusCode);
        var delegationId = (await ReadAsync(delegation)).GetProperty("id").GetInt64();
        var overlap = await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-delegations", adminToken, companyA,
            new { delegatorUserId = adminId, delegateUserId = viewerId, startDate = "2026-10-20", endDate = "2026-10-25" });
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal(ApprovalDelegationService.Overlap, await ErrorCodeAsync(overlap));
        var delegations = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/approval-delegations?userId={viewerId}&activeOn=2026-10-15", viewerToken, companyA));
        Assert.Equal("2026-10-20", delegations.GetProperty("items")[0].GetProperty("endDate").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/approval-delegations/{delegationId}", adminToken, companyB,
            new { startDate = "2026-10-11", endDate = "2026-10-12", isActive = false, version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Put, $"/api/v1/sys/approval-delegations/{delegationId}", adminToken, companyA,
            new { startDate = "2026-10-11", endDate = "2026-10-12", isActive = false, version = 1 })).StatusCode);
    }

    [Fact]
    public async Task ApproversProcessTasksOverHttpWithAuditRecordsAndModuleCallbacks()
    {
        var company = await CreateCompanyAsync();
        var approverRole = await CreateRoleAsync(false, SystemPermissions.ApprovalProcess, SystemPermissions.ApprovalView);
        var processOnly = await CreateRoleAsync(false, SystemPermissions.ApprovalProcess);
        var noAccess = await CreateRoleAsync(false, SystemPermissions.ApprovalFlowView);
        var admin = await CreateRoleAsync(true);
        var (submitterId, _) = await CreateSignedInUserAsync();
        var (approverId, approverToken) = await CreateSignedInUserAsync();
        var (managerId, managerToken) = await CreateSignedInUserAsync();
        var (clerkId, clerkToken) = await CreateSignedInUserAsync();
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        await AssignAsync(submitterId, company);
        await AssignAsync(approverId, company, approverRole);
        await AssignAsync(managerId, company, processOnly);
        await AssignAsync(clerkId, company, noAccess);
        await AssignAsync(adminId, company, admin);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows", adminToken, company, new
        {
            documentType = "TST.API", name = "API flow", allowSelfApproval = false,
            steps = new object[]
            {
                new { name = "Approver", approverType = "Role", roleId = approverRole },
                new { name = "Manager", approverType = "User", userId = managerId, minAmount = 1000 },
            },
        })).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            // 各模組在進程內經 Shared 介面送審
            Assert.IsType<ApprovalWorkflowService>(scope.ServiceProvider.GetRequiredService<IApprovalWorkflowService>());
        }

        await using (var db = Context(company))
        {
            var submitted = await new ApprovalWorkflowService(db, Clock, new FixedUser(submitterId), new AuditTrail(), [])
                .SubmitAsync(new ApprovalSubmission("TST.API", 42, "API-42", 5000m, "Laptop purchase"), Ct);
            Assert.Equal(2, submitted.StepCount);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approvals/tasks", null, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approvals/tasks", clerkToken, company)).StatusCode);
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approvals/tasks", managerToken, company))).GetProperty("items").EnumerateArray());
        var tasks = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approvals/tasks?documentType=tst.api", approverToken, company));
        var task = Assert.Single(tasks.GetProperty("items").EnumerateArray());
        var id = task.GetProperty("instanceId").GetInt64();
        Assert.Equal(("API-42", "Laptop purchase", 5000m, 1, 2), (task.GetProperty("documentNo").GetString(), task.GetProperty("summary").GetString(),
            task.GetProperty("amount").GetDecimal(), task.GetProperty("stepNo").GetInt32(), task.GetProperty("stepCount").GetInt32()));

        var stale = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{id}/approve", approverToken, company, new { version = 5 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var notYours = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{id}/approve", managerToken, company, new { version = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, notYours.StatusCode);
        Assert.Equal(ApprovalWorkflowService.NotApprover, await ErrorCodeAsync(notYours));
        var first = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{id}/approve", approverToken, company, new { comment = "Fine", version = 1 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var afterFirst = await ReadAsync(first);
        Assert.Equal(("Pending", 2), (afterFirst.GetProperty("status").GetString(), afterFirst.GetProperty("currentStepNo").GetInt32()));

        var noComment = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{id}/reject", managerToken, company, new { version = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, noComment.StatusCode);
        var rejected = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{id}/reject", managerToken, company, new { comment = "Over budget", version = 2 });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("Rejected", (await ReadAsync(rejected)).GetProperty("status").GetString());
        var result = Assert.Single(handler.Results);
        Assert.Equal((id, ApprovalStatus.Rejected, managerId, "Over budget"), (result.InstanceId, result.Status, result.ActedBy, result.Comment));
        var done = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{id}/approve", managerToken, company, new { version = 3 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, done.StatusCode);
        Assert.Equal(ApprovalWorkflowService.NotPending, await ErrorCodeAsync(done));

        // 審批歷史與明細
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/approvals/{id}", managerToken, company)).StatusCode);
        var history = await ReadAsync(await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approvals?documentType=TST.API&documentId=42&status=Rejected", approverToken, company));
        Assert.Equal([id], history.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()));
        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"/api/v1/sys/approvals/{id}", approverToken, company));
        Assert.Equal(["Approved", "Rejected"], detail.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("status").GetString()));
        Assert.Equal("Over budget", detail.GetProperty("steps")[1].GetProperty("comment").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, "/api/v1/sys/approvals?status=Unknown", approverToken, company)).StatusCode);

        // 審批動作寫稽核記錄（APPROVE、REJECT），與實例修改同一交易（送審在本測試以未掛稽核 interceptor 的 DbContext 執行）
        var audit = await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/audit-logs?tableName=sys_approval_instance&recordId={id}&sort=occurredAt", adminToken, company));
        Assert.Equal(["APPROVE", "REJECT"],audit.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("action").GetString()));
    }

    private static string NewCode() => "P" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(Clock, new FixedUser(null), company);
        return new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new SystemOrganizationDbModel()], company);
    }

    private async Task<long> CreateCompanyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var company = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(NewCode(), "Company", Ct);
        return company.Id;
    }

    private async Task<long> CreateRoleAsync(bool fullAccess, params string[] permissions)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var role = await scope.ServiceProvider.GetRequiredService<RoleService>()
            .CreateAsync(new RoleInput("T" + Guid.NewGuid().ToString("N")[..12], "Test role", null, DataScope.Company, fullAccess, permissions), Ct);
        return role.Id;
    }

    private async Task AssignAsync(long userId, long companyId, params long[] roleIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RoleService>()
            .AssignAsync(userId, companyId, new UserCompanyAssignment(null, null, false, true, roleIds), Ct);
    }

    private async Task<(long Id, string AccessToken)> CreateSignedInUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var username = "apr" + Guid.NewGuid().ToString("N")[..20];
        var user = await scope.ServiceProvider.GetRequiredService<SystemUserService>().CreateAsync(username, Password, Ct);
        var challenge = await scope.ServiceProvider.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = Password, captchaId = challenge.Id, captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user.Id, (await ReadAsync(login)).GetProperty("accessToken").GetString()!);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, long? companyId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (companyId is { } id)
        {
            request.Headers.Add("X-Company-Id", id.ToString(CultureInfo.InvariantCulture));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    /// <summary>模擬單據模組：登記為 TST.API 的審批結果處理器。</summary>
    private sealed class RecordingHandler : IApprovalResultHandler
    {
        public List<ApprovalCompleted> Results { get; } = [];

        public string DocumentType => "TST.API";

        public Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class ApiFactory(TestDatabase database, RecordingHandler handler) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            builder.ConfigureTestServices(services => services.AddSingleton<IApprovalResultHandler>(handler));
        }
    }
}
