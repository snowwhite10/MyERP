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
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;

using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-PUR-006b3（REQ_F4114.1.4／1.5，PUR-PO-010／011／012／015／019）：採購單 SUBMIT 與 SYS 決策的受權 HTTP 驗收。
/// 以真實 JWT、角色、MD 主檔、請購與 SYS 審批引擎驗證 401／403／404／409／422 ProblemDetails（含 shortages／lineNos／
/// traceId），MD 供應商／物料失效整單 422，及經 SYS 核准／駁回 HTTP 的保留消耗、釋回與決策失敗整筆回滾。
/// </summary>
public sealed class PurchaseOrderSubmitHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Route = "/api/v1/pur/purchase-orders";
    private const string Approvals = "/api/v1/sys/approvals";
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2028, 2, 29);

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    /// <summary>PUR-PO-SUB-HTTP-001：匿名、公司閘道、缺 SUBMIT、他公司、本人範圍外、舊版本均零變更。</summary>
    [Fact]
    public async Task SubmitGuardsReturnProblemDetailsWithoutConsuming()
    {
        var company = await NewCompanyAsync("PO submit guards");
        var other = await NewCompanyAsync("PO submit other");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (strangerId, _) = await CreateUserAsync(http);
        var md = await SeedMasterDataAsync(company, userId);
        var source = await SeedRequisitionAsync(company, userId, md.ItemId, 10m, RequisitionStatus.Approved);
        var order = await SeedOrderAsync(company, userId, md, (md.ItemId, source, 4m));
        var foreign = await SeedOrderAsync(company, strangerId, md, (md.ItemId, source, 1m));
        var path = $"{Route}/{order.Id}/submit";

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync(path, new { version = order.Version }, Ct)).StatusCode);
        await AssertProblemAsync(await SubmitAsync(http, order, token, company),
            HttpStatusCode.Forbidden, "COMMON.COMPANY.ACCESS_DENIED");
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderView,
            PurchasingPermissions.OrderCreate, PurchasingPermissions.OrderEdit);
        await AssertProblemAsync(await SubmitAsync(http, order, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");

        await GrantAsync(userId, company, DataScope.Self, PurchasingPermissions.OrderSubmit);
        await GrantAsync(userId, other, DataScope.Company, PurchasingPermissions.OrderSubmit);
        await AssertProblemAsync(await SubmitAsync(http, order, token, other),
            HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SubmitAsync(http, foreign, token, company),
            HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");
        await AssertProblemAsync(await SubmitAsync(http, order with { Version = order.Version + 1 }, token, company),
            HttpStatusCode.Conflict, "COMMON.CONCURRENCY.CONFLICT");
        await AssertProblemAsync(await SendAsync(http, HttpMethod.Post, $"{Route}/{long.MaxValue}/submit",
            new { version = 1 }, token, company), HttpStatusCode.NotFound, "PUR.ORDER.NOT_FOUND");

        await AssertDraftAsync(company, order, (source, 0m));
        await AssertDraftAsync(company, foreign, (source, 0m));
    }

    /// <summary>
    /// PUR-PO-SUB-HTTP-002：不足 422 帶 shortages、無效來源 422 帶 lineNos、MD 供應商未核准／物料停用 422，
    /// 皆整單零變更；修正後同一版本可成功送審（零步驟流程立即 Approved）。
    /// </summary>
    [Fact]
    public async Task UnprocessableSubmitsAreWholeOrderAndCarryExtensions()
    {
        var company = await NewCompanyAsync("PO submit 422");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderSubmit);
        var md = await SeedMasterDataAsync(company, userId);
        var tight = await SeedRequisitionAsync(company, userId, md.ItemId, 6m, RequisitionStatus.Approved);
        var draftRequisition = await SeedRequisitionAsync(company, userId, md.ItemId, 5m, RequisitionStatus.Draft);

        var short_ = await SeedOrderAsync(company, userId, md, (md.ItemId, tight, 3m), (md.ItemId, tight, 3.000001m));
        var problem = await AssertProblemAsync(await SubmitAsync(http, short_, token, company),
            HttpStatusCode.UnprocessableEntity, "PUR.ORDER.REQUISITION_INSUFFICIENT");
        var shortage = Assert.Single(problem.GetProperty("shortages").EnumerateArray());
        Assert.Equal(tight, shortage.GetProperty("requisitionLineId").GetInt64());
        Assert.Equal([1, 2], shortage.GetProperty("orderLineNos").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(6.000001m, shortage.GetProperty("requested").GetDecimal());
        Assert.Equal(6m, shortage.GetProperty("available").GetDecimal());
        await AssertDraftAsync(company, short_, (tight, 0m));

        var invalid = await SeedOrderAsync(company, userId, md, (md.ItemId, tight, 1m), (md.ItemId, draftRequisition, 1m));
        problem = await AssertProblemAsync(await SubmitAsync(http, invalid, token, company),
            HttpStatusCode.UnprocessableEntity, "PUR.ORDER.INVALID_REQUISITION_SOURCE");
        Assert.Equal([2], problem.GetProperty("lineNos").EnumerateArray().Select(x => x.GetInt32()));
        await AssertDraftAsync(company, invalid, (tight, 0m), (draftRequisition, 0m));

        var valid = await SeedOrderAsync(company, userId, md, (md.ItemId, tight, 2m), (md.ItemId, null, 1m));
        await using (var db = Context(company, userId))
            await db.Set<Supplier>().Where(x => x.Id == md.SupplierId).ExecuteUpdateAsync(
                x => x.SetProperty(y => y.ReviewStatus, PartnerReviewStatus.Pending), Ct);
        await AssertProblemAsync(await SubmitAsync(http, valid, token, company),
            HttpStatusCode.UnprocessableEntity, "MD.SUPPLIER.NOT_APPROVED");
        await AssertDraftAsync(company, valid, (tight, 0m));

        await using (var db = Context(company, userId))
        {
            await db.Set<Supplier>().Where(x => x.Id == md.SupplierId).ExecuteUpdateAsync(
                x => x.SetProperty(y => y.ReviewStatus, PartnerReviewStatus.Approved), Ct);
            await db.Set<Item>().Where(x => x.Id == md.ItemId).ExecuteUpdateAsync(
                x => x.SetProperty(y => y.IsActive, false), Ct);
        }
        await AssertProblemAsync(await SubmitAsync(http, valid, token, company),
            HttpStatusCode.UnprocessableEntity, "MD.ITEM.INACTIVE");
        await AssertDraftAsync(company, valid, (tight, 0m));

        await using (var db = Context(company, userId))
            await db.Set<Item>().Where(x => x.Id == md.ItemId).ExecuteUpdateAsync(
                x => x.SetProperty(y => y.IsActive, true), Ct);
        var ok = await SubmitAsync(http, valid, token, company);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Approved", body.GetProperty("status").GetString());
        Assert.Equal(0, body.GetProperty("approval").GetProperty("stepCount").GetInt32());
        await AssertOrderAsync(company, valid.Id, "Approved", true, (tight, 2m));
        // Already submitted: the same version is no longer a draft.
        await AssertProblemAsync(await SubmitAsync(http, valid with { Version = body.GetProperty("version").GetInt32() },
            token, company), HttpStatusCode.UnprocessableEntity, "PUR.ORDER.NOT_SUBMITTABLE");
        await AssertOrderAsync(company, valid.Id, "Approved", true, (tight, 2m));
    }

    /// <summary>
    /// PUR-PO-SUB-HTTP-003：一步流程經 SYS HTTP——核准保留消耗；駁回釋回一次；請購量漂移時駁回 409
    /// REQUISITION_RELEASE_INVALID、旗標被清時核准 409 APPROVAL_STATE_CHANGED，SYS 實例仍 Pending 且 PUR 不變。
    /// </summary>
    [Fact]
    public async Task SysDecisionsOverHttpKeepReleaseOrRollBackTogether()
    {
        var company = await NewCompanyAsync("PO decisions");
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        var (approverId, approverToken) = await CreateUserAsync(http);
        await GrantAsync(userId, company, DataScope.Company, PurchasingPermissions.OrderSubmit);
        await GrantAsync(approverId, company, DataScope.Company, SystemPermissions.ApprovalProcess);
        var md = await SeedMasterDataAsync(company, userId);
        await using (var db = Context(company, userId))
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput("PUR.ORDER", "Purchase order", false,
                [new ApprovalStepInput("Buyer manager", ApproverType.User, null, approverId, null)]), Ct);
        var source = await SeedRequisitionAsync(company, userId, md.ItemId, 20m, RequisitionStatus.Approved);
        var approved = await SeedOrderAsync(company, userId, md, (md.ItemId, source, 2m), (md.ItemId, source, 1m));
        var rejected = await SeedOrderAsync(company, userId, md, (md.ItemId, source, 4m));
        var drifted = await SeedOrderAsync(company, userId, md, (md.ItemId, source, 5m));
        var cleared = await SeedOrderAsync(company, userId, md, (md.ItemId, source, 6m));
        foreach (var order in new[] { approved, rejected, drifted, cleared })
        {
            var response = await SubmitAsync(http, order, token, company);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("PendingApproval",
                (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("status").GetString());
        }
        await AssertOrderAsync(company, approved.Id, "PendingApproval", true, (source, 18m));

        // The submitter cannot decide on their own order.
        var (approvedInstance, approvedVersion) = await InstanceAsync(company, approved.Id);
        await AssertProblemAsync(await DecideAsync(http, approvedInstance, "approve", null, approvedVersion, token, company),
            HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
        var ok = await DecideAsync(http, approvedInstance, "approve", "OK", approvedVersion, approverToken, company);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await AssertOrderAsync(company, approved.Id, "Approved", true, (source, 18m));

        var (rejectedInstance, rejectedVersion) = await InstanceAsync(company, rejected.Id);
        ok = await DecideAsync(http, rejectedInstance, "reject", "Wrong price", rejectedVersion, approverToken, company);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await AssertOrderAsync(company, rejected.Id, "Rejected", false, (source, 14m));
        // Repeating the decision on the completed instance cannot release a second time.
        await AssertProblemAsync(await DecideAsync(http, rejectedInstance, "reject", "Again",
                (await InstanceAsync(company, rejected.Id)).Version, approverToken, company),
            HttpStatusCode.UnprocessableEntity, "SYS.APPROVAL.NOT_PENDING");
        await AssertOrderAsync(company, rejected.Id, "Rejected", false, (source, 14m));

        // Data drift: ordered quantity below this order's consumption → the whole SYS rejection rolls back.
        await using (var db = Context(company, userId))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_requisition_line SET ordered_quantity = 3 WHERE id = {source}", Ct);
        var (driftedInstance, driftedVersion) = await InstanceAsync(company, drifted.Id);
        await AssertProblemAsync(await DecideAsync(http, driftedInstance, "reject", "No", driftedVersion,
            approverToken, company), HttpStatusCode.Conflict, "PUR.ORDER.REQUISITION_RELEASE_INVALID");
        await AssertOrderAsync(company, drifted.Id, "PendingApproval", true, (source, 3m));
        Assert.Equal((driftedInstance, driftedVersion, ApprovalStatus.Pending), await InstanceStateAsync(company, drifted.Id));

        await using (var db = Context(company, userId))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE pur_purchase_order SET requisition_consumption_active = 0 WHERE id = {cleared.Id}", Ct);
        var (clearedInstance, clearedVersion) = await InstanceAsync(company, cleared.Id);
        await AssertProblemAsync(await DecideAsync(http, clearedInstance, "approve", null, clearedVersion,
            approverToken, company), HttpStatusCode.Conflict, "PUR.ORDER.APPROVAL_STATE_CHANGED");
        await AssertOrderAsync(company, cleared.Id, "PendingApproval", false, (source, 3m));
        Assert.Equal((clearedInstance, clearedVersion, ApprovalStatus.Pending), await InstanceStateAsync(company, cleared.Id));
    }

    // ------------------------------------------------------------------ helpers

    private sealed record MasterData(long SupplierId, long ItemId);

    private sealed record SeededOrder(long Id, int Version);

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status,
        string errorCode)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.True(status == response.StatusCode, $"Expected {status}, got {response.StatusCode}: {problem}");
        Assert.Equal(errorCode, problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        return problem;
    }

    private async Task AssertDraftAsync(long company, SeededOrder order, params (long Id, decimal Ordered)[] sources)
    {
        await AssertOrderAsync(company, order.Id, "Draft", false, sources);
        await using var db = Context(company, 7);
        Assert.Equal(order.Version, (await db.Set<PurchaseOrderSource>().AsNoTracking()
            .SingleAsync(x => x.Id == order.Id, Ct)).Version);
        Assert.False(await db.Set<ApprovalInstance>().AnyAsync(
            x => x.DocumentType == "PUR.ORDER" && x.DocumentId == order.Id, Ct));
    }

    private async Task AssertOrderAsync(long company, long orderId, string status, bool active,
        params (long Id, decimal Ordered)[] sources)
    {
        await using var db = Context(company, 7);
        var saved = await db.Set<PurchaseOrderSource>().AsNoTracking().SingleAsync(x => x.Id == orderId, Ct);
        Assert.Equal(status, saved.Status);
        Assert.Equal(active, saved.RequisitionConsumptionActive);
        foreach (var (id, ordered) in sources)
            Assert.Equal(ordered, await db.Set<RequisitionLine>().AsNoTracking().Where(x => x.Id == id)
                .Select(x => x.OrderedQuantity).SingleAsync(Ct));
    }

    private async Task<(long Id, int Version)> InstanceAsync(long company, long orderId)
    {
        var (id, version, _) = await InstanceStateAsync(company, orderId);
        return (id, version);
    }

    private async Task<(long Id, int Version, ApprovalStatus Status)> InstanceStateAsync(long company, long orderId)
    {
        await using var db = Context(company, 7);
        var instance = await db.Set<ApprovalInstance>().AsNoTracking()
            .SingleAsync(x => x.DocumentType == "PUR.ORDER" && x.DocumentId == orderId, Ct);
        return (instance.Id, instance.Version, instance.Status);
    }

    private async Task<MasterData> SeedMasterDataAsync(long company, long userId)
    {
        await using var db = Context(company, userId);
        var uom = new UnitOfMeasure { CompanyId = company, Code = "EA", Name = "Each", DecimalPlaces = 6 };
        db.Add(uom);
        await db.SaveChangesAsync(Ct);
        var item = new Item { CompanyId = company, Code = $"I{company}", Name = "Item", BaseUomId = uom.Id };
        var supplier = new Supplier { CompanyId = company, Code = $"S{company}", Name = "Supplier",
            ReviewStatus = PartnerReviewStatus.Approved };
        db.AddRange(item, supplier);
        await db.SaveChangesAsync(Ct);
        return new MasterData(supplier.Id, item.Id);
    }

    private async Task<long> SeedRequisitionAsync(long company, long userId, long itemId, decimal quantity,
        RequisitionStatus status)
    {
        await using var db = Context(company, userId);
        var requisition = new Requisition { CompanyId = company, DocumentNo = $"RQ-{Guid.NewGuid():N}"[..20],
            BusinessDate = Day.AddDays(-1), RequesterId = userId, Status = status };
        requisition.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = itemId,
            RequiredDate = Day.AddDays(10), Quantity = quantity });
        db.Add(requisition);
        await db.SaveChangesAsync(Ct);
        return requisition.Lines[0].Id;
    }

    /// <summary>Saved Draft owned by <paramref name="ownerId"/>; each line costs 10 + 5% tax.</summary>
    private async Task<SeededOrder> SeedOrderAsync(long company, long ownerId, MasterData md,
        params (long Item, long? Source, decimal Quantity)[] lines)
    {
        await using var db = Context(company, ownerId);
        var nets = lines.Select(x => x.Quantity * 10m).ToArray();
        var taxes = nets.Select(x => Math.Round(x * 0.05m, 4)).ToArray();
        var order = new PurchaseOrderSource
        {
            CompanyId = company, SupplierId = md.SupplierId, DocumentNo = $"PO-S-{Guid.NewGuid():N}"[..20],
            BusinessDate = Day, Currency = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m, Status = "Draft",
            OwnerId = ownerId, BaseNet = nets.Sum(), BaseTax = taxes.Sum(), BaseGross = nets.Sum() + taxes.Sum()
        };
        db.Add(order);
        await db.SaveChangesAsync(Ct);
        db.AddRange(lines.Select((line, i) => new PurchaseOrderSourceLine
        {
            CompanyId = company, PurchaseOrderId = order.Id, LineNo = i + 1, MaterialId = line.Item,
            RequisitionLineId = line.Source, Quantity = line.Quantity, UnitPrice = 10m, TaxRateId = 51,
            TaxRatePercent = 5m, ExchangeRate = 1m, OriginalNet = nets[i], OriginalTax = taxes[i],
            OriginalGross = nets[i] + taxes[i], BaseNet = nets[i], BaseTax = taxes[i], BaseGross = nets[i] + taxes[i]
        }));
        await db.SaveChangesAsync(Ct);
        return new SeededOrder(order.Id, order.Version);
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
            "PO submit role", null, scope, false, permissions), Ct)).Id;
        await roles.AssignAsync(userId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
    }

    private async Task<(long Id, string Token)> CreateUserAsync(HttpClient client)
    {
        const string password = "Strong pass 2026!";
        var username = "ps" + Guid.NewGuid().ToString("N")[..20];
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
                new PurchaseInvoiceSourceDbModel(), new UomDbModel(), new ItemDbModel(), new SupplierDbModel(),
                new PaymentSetupDbModel(), new SystemOrganizationDbModel()], company);
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, SeededOrder order, string token,
        long companyId) =>
        SendAsync(client, HttpMethod.Post, $"{Route}/{order.Id}/submit", new { version = order.Version }, token,
            companyId);

    private static Task<HttpResponseMessage> DecideAsync(HttpClient client, long instanceId, string action,
        string? comment, int version, string token, long companyId) =>
        SendAsync(client, HttpMethod.Post, $"{Approvals}/{instanceId}/{action}", new { comment, version }, token,
            companyId);

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
