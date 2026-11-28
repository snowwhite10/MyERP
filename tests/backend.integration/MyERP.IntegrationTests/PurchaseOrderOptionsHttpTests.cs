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
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

/// <summary>
/// WU-PUR-006f2a（REQ_F4114.1.2～3，PUR-PO-OPT-001～006）：介面 4.2／4.3 表單選項只需 PUR.ORDER.CREATE 或 EDIT，
/// 不需 MD.VIEW、ORDER.VIEW；請購來源只列目前公司已核准且有餘量的行，並回原量／已轉量／餘量。
/// </summary>
public sealed class PurchaseOrderOptionsHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Route = "/api/v1/pur/purchase-orders";
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] OptionPaths =
        ["supplier-options", "item-options", "requisition-options", "create-context?businessDate=2028-02-29"];
    private static readonly string[] InvalidPaths = ["requisition-options?itemId=0", "requisition-options?pageSize=0",
        "create-context?businessDate=0001-01-01", "create-context?businessDate=2028-02-30", "create-context"];

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task FormOptionsNeedCreateOrEditOnlyAndListApprovedRemainingSources()
    {
        var company = await NewCompanyAsync("PO options");
        var other = await NewCompanyAsync("PO options other");
        using var http = factory.CreateClient();
        var (creatorId, creatorToken) = await CreateUserAsync(http);
        var (editorId, editorToken) = await CreateUserAsync(http);
        var (viewerId, viewerToken) = await CreateUserAsync(http);
        await GrantAsync(creatorId, company, DataScope.Self, PurchasingPermissions.OrderCreate);
        await GrantAsync(editorId, company, DataScope.Self, PurchasingPermissions.OrderEdit);
        await GrantAsync(viewerId, company, DataScope.Company, PurchasingPermissions.OrderView,
            PurchasingPermissions.RfqCreate, PurchasingPermissions.RequisitionCreate);

        long itemId, otherItemId, supplierId, openLineId, openRequisitionId, inputRateId;
        string openDocumentNo;
        await using (var db = Context(company))
        {
            var uom = new UnitOfMeasure { CompanyId = company, Code = "EA", Name = "Each", DecimalPlaces = 6 };
            db.Add(uom);
            await db.SaveChangesAsync(Ct);
            var item = new Item { CompanyId = company, Code = "I" + Guid.NewGuid().ToString("N")[..8],
                Name = "PO option part", BaseUomId = uom.Id };
            var otherItem = new Item { CompanyId = company, Code = "J" + Guid.NewGuid().ToString("N")[..8],
                Name = "PO option other part", BaseUomId = uom.Id };
            var supplier = new Supplier { CompanyId = company, Code = "S" + Guid.NewGuid().ToString("N")[..8],
                Name = "PO approved supplier", ReviewStatus = PartnerReviewStatus.Approved };
            var pending = new Supplier { CompanyId = company, Code = "S" + Guid.NewGuid().ToString("N")[..8],
                Name = "PO pending supplier", ReviewStatus = PartnerReviewStatus.Pending };
            var currency = new Currency { CompanyId = company, Code = "TWD", Name = "Taiwan dollar" };
            var inactiveCurrency = new Currency { CompanyId = company, Code = "USD", Name = "US dollar", IsActive = false };
            var inputCode = new TaxCode { CompanyId = company, Code = "IN", Name = "Input tax", TaxType = "Input" };
            var outputCode = new TaxCode { CompanyId = company, Code = "OUT", Name = "Output tax", TaxType = "Output" };
            db.AddRange(item, otherItem, supplier, pending, currency, inactiveCurrency, inputCode, outputCode);
            await db.SaveChangesAsync(Ct);
            var inputRate = new TaxRate { CompanyId = company, TaxCodeId = inputCode.Id,
                EffectiveFrom = new DateOnly(2028, 2, 29), EffectiveTo = new DateOnly(2028, 2, 29), RatePercent = 5m };
            db.AddRange(inputRate, new TaxRate { CompanyId = company, TaxCodeId = outputCode.Id,
                EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 10m });
            Requisition NewRequisition(string prefix, RequisitionStatus status, long lineItem, decimal quantity,
                decimal ordered, bool deleted = false)
            {
                var requisition = new Requisition { CompanyId = company,
                    DocumentNo = prefix + Guid.NewGuid().ToString("N")[..8], BusinessDate = new DateOnly(2028, 2, 29),
                    RequesterId = 7, Status = status, IsDeleted = deleted,
                    DeletedAt = deleted ? new DateTime(2028, 2, 29) : null };
                requisition.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = lineItem,
                    RequiredDate = new DateOnly(2028, 3, 2), Quantity = quantity, OrderedQuantity = ordered });
                return requisition;
            }
            var open = NewRequisition("RQA-", RequisitionStatus.Approved, item.Id, 5.123456m, 2m);
            var otherItemOpen = NewRequisition("RQB-", RequisitionStatus.Approved, otherItem.Id, 3m, 0m);
            db.AddRange(open, otherItemOpen,
                NewRequisition("RQZ-", RequisitionStatus.Approved, item.Id, 4m, 4m),
                NewRequisition("RQD-", RequisitionStatus.Draft, item.Id, 5m, 0m),
                NewRequisition("RQP-", RequisitionStatus.PendingApproval, item.Id, 5m, 0m),
                NewRequisition("RQC-", RequisitionStatus.Closed, item.Id, 5m, 1m),
                NewRequisition("RQX-", RequisitionStatus.Draft, item.Id, 5m, 0m, deleted: true));
            await db.SaveChangesAsync(Ct);
            (itemId, otherItemId, supplierId, openLineId, openRequisitionId, openDocumentNo, inputRateId) =
                (item.Id, otherItem.Id, supplier.Id, open.Lines[0].Id, open.Id, open.DocumentNo, inputRate.Id);
        }
        await using (var db = Context(other))
        {
            var requisition = new Requisition { CompanyId = other, DocumentNo = "RQO-" + Guid.NewGuid().ToString("N")[..8],
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = 7, Status = RequisitionStatus.Approved };
            requisition.Lines.Add(new RequisitionLine { CompanyId = other, LineNo = 1, ItemId = itemId,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 9m });
            db.Add(requisition);
            await db.SaveChangesAsync(Ct);
        }

        var options = OptionPaths
            .Select(x => $"{Route}/{x}").ToArray();
        foreach (var path in options)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path, Ct)).StatusCode);
            await AssertProblemAsync(await SendAsync(http, path, viewerToken, company),
                HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED");
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, path, creatorToken, other)).StatusCode);
        }

        foreach (var token in new[] { creatorToken, editorToken })
        {
            var suppliers = await ReadAsync(http, $"{Route}/supplier-options?pageSize=20", token, company);
            Assert.Equal([supplierId], suppliers.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
            var items = await ReadAsync(http, $"{Route}/item-options?pageSize=20&sort=code", token, company);
            Assert.Equal(new[] { itemId, otherItemId }.Order(),
                items.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).Order());

            var context = await ReadAsync(http, $"{Route}/create-context?businessDate=2028-02-29", token, company);
            Assert.Equal(["TWD"], context.GetProperty("currencies").EnumerateArray().Select(x => x.GetProperty("code").GetString()));
            var rate = Assert.Single(context.GetProperty("taxRates").EnumerateArray());
            Assert.Equal(inputRateId, rate.GetProperty("id").GetInt64());
            Assert.Equal(5m, rate.GetProperty("ratePercent").GetDecimal());
            var nextDay = await ReadAsync(http, $"{Route}/create-context?businessDate=2028-03-01", token, company);
            Assert.Empty(nextDay.GetProperty("taxRates").EnumerateArray());

            var sources = await ReadAsync(http, $"{Route}/requisition-options?itemId={itemId}&pageSize=20", token, company);
            Assert.Equal(1, sources.GetProperty("totalCount").GetInt64());
            var only = sources.GetProperty("items")[0];
            Assert.Equal(openLineId, only.GetProperty("id").GetInt64());
            Assert.Equal(openRequisitionId, only.GetProperty("requisitionId").GetInt64());
            Assert.Equal(openDocumentNo, only.GetProperty("documentNo").GetString());
            Assert.Equal(1, only.GetProperty("lineNo").GetInt32());
            Assert.Equal(itemId, only.GetProperty("itemId").GetInt64());
            Assert.Equal("2028-03-02", only.GetProperty("requiredDate").GetString());
            Assert.Equal(5.123456m, only.GetProperty("quantity").GetDecimal());
            Assert.Equal(2m, only.GetProperty("orderedQuantity").GetDecimal());
            Assert.Equal(3.123456m, only.GetProperty("remainingQuantity").GetDecimal());

            var all = await ReadAsync(http, $"{Route}/requisition-options?pageSize=20", token, company);
            Assert.Equal(2, all.GetProperty("totalCount").GetInt64());
            var keyword = await ReadAsync(http, $"{Route}/requisition-options?keyword=RQB-", token, company);
            Assert.Equal(otherItemId, Assert.Single(keyword.GetProperty("items").EnumerateArray()).GetProperty("itemId").GetInt64());
            var none = await ReadAsync(http, $"{Route}/requisition-options?itemId={long.MaxValue}", token, company);
            Assert.Equal(0, none.GetProperty("totalCount").GetInt64());
        }

        foreach (var invalid in InvalidPaths)
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(http, $"{Route}/{invalid}", creatorToken, company)).StatusCode);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient http, string path, string token, long company)
    {
        var response = await SendAsync(http, path, token, company);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(errorCode, problem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
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
            "PO option role", null, scope, false, permissions), Ct)).Id;
        await roles.AssignAsync(userId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
    }

    private async Task<(long Id, string Token)> CreateUserAsync(HttpClient client)
    {
        const string password = "Strong pass 2026!";
        var username = "poo" + Guid.NewGuid().ToString("N")[..20];
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
        var stamper = new AuditStamper(new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel(), new CurrencyDbModel(), new TaxDbModel(), new ItemDbModel(), new UomDbModel(),
                new SupplierDbModel(), new PaymentSetupDbModel()], company);
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
