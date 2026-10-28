using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests;

public sealed class SalesQuotationHttpTests(TestDatabase database) : IAsyncLifetime
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
    public async Task AuthorizedCreateAndEditPersistRealMdSnapshotsAndRejectStaleVersion()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var company = await services.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "QC" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Quote HTTP create", Ct);
        var role = await services.GetRequiredService<RoleService>().CreateAsync(new RoleInput(
            "Q" + Guid.NewGuid().ToString("N")[..12], "Quote writer", null,
            DataScope.Company, false, ["SAL.QUOTATION.CREATE", "SAL.QUOTATION.EDIT",
                "SAL.QUOTATION.VIEW", "SAL.QUOTATION.SUBMIT"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>()
            .CreateAsync(username, "Strong pass 2026!", Ct);
        await services.GetRequiredService<RoleService>().AssignAsync(user.Id, company.Id,
            new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        var approverRole = await services.GetRequiredService<RoleService>().CreateAsync(new RoleInput(
            "Q" + Guid.NewGuid().ToString("N")[..12], "Quote approver", null,
            DataScope.Company, false, [SystemPermissions.ApprovalProcess,
                SystemPermissions.ApprovalView]), Ct);
        var approverName = "sal" + Guid.NewGuid().ToString("N")[..20];
        var approver = await services.GetRequiredService<SystemUserService>()
            .CreateAsync(approverName, "Strong pass 2026!", Ct);
        await services.GetRequiredService<RoleService>().AssignAsync(approver.Id, company.Id,
            new UserCompanyAssignment(null, null, true, true, [approverRole.Id]), Ct);

        long customerId;
        long itemId;
        long taxRateId;
        var prefix = "QC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await using (var db = MdContext(company.Id))
        {
            var customer = new Customer { CompanyId = company.Id,
                Code = "C" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "HTTP customer", ReviewStatus = PartnerReviewStatus.Approved };
            var uom = new UnitOfMeasure { CompanyId = company.Id,
                Code = "U" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), Name = "Unit" };
            var currency = new Currency { CompanyId = company.Id, Code = "TWD", Name = "Taiwan dollar",
                DecimalPlaces = 0, IsBase = true };
            var taxCode = new TaxCode { CompanyId = company.Id, Code = "OUT", Name = "Output", TaxType = "Output" };
            db.AddRange(customer, uom, currency, taxCode,
                new NumberRule { CompanyId = company.Id, DocumentType = "SAL.QUOTATION",
                    Name = "Quote", Prefix = prefix, DateFormat = NumberDateFormat.None,
                    ResetPeriod = NumberResetPeriod.Never, SequenceLength = 3 });
            await db.SaveChangesAsync(Ct);
            var item = new Item { CompanyId = company.Id,
                Code = "I" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                Name = "HTTP item", BaseUomId = uom.Id };
            var taxRate = new TaxRate { CompanyId = company.Id, TaxCodeId = taxCode.Id,
                EffectiveFrom = new DateOnly(2028, 2, 29), RatePercent = 5m };
            db.AddRange(item, taxRate);
            await db.SaveChangesAsync(Ct);
            customerId = customer.Id;
            itemId = item.Id;
            taxRateId = taxRate.Id;
        }
        await using (var db = QuoteContext(company.Id))
            await new ApprovalFlowService(db).CreateAsync(new ApprovalFlowInput(
                "SAL.QUOTATION", "Quotation", false,
                [new ApprovalStepInput("Approver", ApproverType.User, null, approver.Id, null)]), Ct);

        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = "Strong pass 2026!", captchaId = challenge.Id,
                captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
            request.Content = JsonContent.Create(body);
            return await client.SendAsync(request, Ct);
        }
        object Draft(decimal price) => new
        {
            customerId, businessDate = "2028-02-29", expiryDate = "2028-03-31", remark = "HTTP",
            lines = new[] { new { lineNo = 1, itemId, quantity = 2m, unitPrice = price,
                discountRate = 0m, currencyCode = "TWD", taxRateId } }
        };
        using var invalid = await SendAsync(HttpMethod.Post, "/api/v1/sal/quotations",
            new { customerId, businessDate = "2028-02-29", expiryDate = "2028-03-31",
                lines = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True((await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("errors").TryGetProperty("lines", out _));
        using var created = await SendAsync(HttpMethod.Post, "/api/v1/sal/quotations", Draft(10m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = createdBody.GetProperty("id").GetInt64();
        Assert.Equal(prefix + "001", createdBody.GetProperty("documentNo").GetString());
        Assert.Equal("HTTP customer", createdBody.GetProperty("customerName").GetString());
        Assert.Equal("HTTP item", createdBody.GetProperty("lines")[0].GetProperty("itemName").GetString());
        Assert.Equal(21m, createdBody.GetProperty("originalGross").GetDecimal());
        using var edited = await SendAsync(HttpMethod.Put, $"/api/v1/sal/quotations/{id}",
            new { version = 1, draft = Draft(20m) });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedBody = await edited.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, editedBody.GetProperty("version").GetInt32());
        Assert.Equal(42m, editedBody.GetProperty("originalGross").GetDecimal());
        using var stale = await SendAsync(HttpMethod.Put, $"/api/v1/sal/quotations/{id}",
            new { version = 1, draft = Draft(30m) });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/sal/quotations/{id}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        detailRequest.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
        using var detail = await client.SendAsync(detailRequest, Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(42m, (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("originalGross").GetDecimal());
        await using (var db = MdContext(company.Id))
            await db.Set<Customer>().Where(x => x.Id == customerId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.ReviewStatus, PartnerReviewStatus.Rejected), Ct);
        using var deniedSubmit = await SendAsync(HttpMethod.Post,
            $"/api/v1/sal/quotations/{id}/submit", new { version = 2 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deniedSubmit.StatusCode);
        Assert.Equal("MD.CUSTOMER.NOT_APPROVED",
            (await deniedSubmit.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("errorCode").GetString());
        await using var verify = QuoteContext(company.Id);
        var saved = await verify.Set<SalesQuotation>().Include(x => x.Lines).SingleAsync(x => x.Id == id, Ct);
        Assert.Equal(2, saved.Version);
        Assert.Equal(SalesQuotationStatus.Draft, saved.Status);
        Assert.Null(saved.ApprovalInstanceId);
        Assert.Equal(42m, saved.OriginalGross);
        Assert.Equal(20m, Assert.Single(saved.Lines).UnitPrice);
        verify.ChangeTracker.Clear();
        await using (var db = MdContext(company.Id))
            await db.Set<Customer>().Where(x => x.Id == customerId).ExecuteUpdateAsync(
                x => x.SetProperty(p => p.ReviewStatus, PartnerReviewStatus.Approved), Ct);
        using var submitted = await SendAsync(HttpMethod.Post,
            $"/api/v1/sal/quotations/{id}/submit", new { version = 2 });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var submittedBody = await submitted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((int)SalesQuotationStatus.Pending, submittedBody.GetProperty("status").GetInt32());
        verify.ChangeTracker.Clear();
        var instanceId = (await verify.Set<SalesQuotation>()
            .SingleAsync(x => x.Id == id, Ct)).ApprovalInstanceId!.Value;
        Assert.True(instanceId > 0);
        using var repeat = await SendAsync(HttpMethod.Post,
            $"/api/v1/sal/quotations/{id}/submit", new { version = 2 });
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        var approverChallenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var approverLogin = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username = approverName, password = "Strong pass 2026!",
                captchaId = approverChallenge.Id, captchaCode = approverChallenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, approverLogin.StatusCode);
        var approverToken = (await approverLogin.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;
        using var decisionRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/sys/approvals/{instanceId}/approve");
        decisionRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", approverToken);
        decisionRequest.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
        decisionRequest.Content = JsonContent.Create(new { version = 1, comment = "Approved" });
        using var decision = await client.SendAsync(decisionRequest, Ct);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Equal("Approved", (await decision.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("status").GetString());
        verify.ChangeTracker.Clear();
        var approved = await verify.Set<SalesQuotation>().SingleAsync(x => x.Id == id, Ct);
        Assert.Equal(SalesQuotationStatus.Approved, approved.Status);
        Assert.Equal(instanceId, approved.ApprovalInstanceId);
        Assert.NotNull(approved.ApprovedAt);
        using var historyRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/api/v1/sys/approvals?documentType=SAL.QUOTATION&documentId={id}");
        historyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", approverToken);
        historyRequest.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
        using var history = await client.SendAsync(historyRequest, Ct);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyBody = await history.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1, historyBody.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task EveryQuotationRouteRequiresAuthenticationAndIsInOpenApi()
    {
        using var client = factory.CreateClient();
        var requests = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, "/api/v1/sal/quotations"),
            (HttpMethod.Get, "/api/v1/sal/quotations/1"),
            (HttpMethod.Post, "/api/v1/sal/quotations"),
            (HttpMethod.Put, "/api/v1/sal/quotations/1"),
            (HttpMethod.Delete, "/api/v1/sal/quotations/1?version=1"),
            (HttpMethod.Post, "/api/v1/sal/quotations/1/submit")
        };
        foreach (var (method, path) in requests)
        {
            using var request = new HttpRequestMessage(method, path);
            if (method == HttpMethod.Post || method == HttpMethod.Put)
                request.Content = JsonContent.Create(new { version = 1 });
            using var response = await client.SendAsync(request, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var swagger = await client.GetAsync("/openapi/v1.json", Ct);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        var paths = (await swagger.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/sal/quotations", out var root));
        Assert.True(root.TryGetProperty("get", out _));
        Assert.True(root.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/sal/quotations/{id}", out var item));
        Assert.True(item.TryGetProperty("get", out _));
        Assert.True(item.TryGetProperty("put", out _));
        Assert.True(item.TryGetProperty("delete", out _));
        Assert.True(paths.TryGetProperty("/api/v1/sal/quotations/{id}/submit", out var submit));
        Assert.True(submit.TryGetProperty("post", out _));
    }

    [Fact]
    public async Task DeleteRequiresCurrentVersionAndRetainsPermanentNumber()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var company = await services.GetRequiredService<OrganizationService>().CreateCompanyAsync(
            "QD" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Quote deletion", Ct);
        var role = await services.GetRequiredService<RoleService>().CreateAsync(new RoleInput(
            "Q" + Guid.NewGuid().ToString("N")[..12], "Quote deletion", null,
            DataScope.Company, false, ["SAL.QUOTATION.DELETE", "SAL.QUOTATION.VIEW"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>()
            .CreateAsync(username, "Strong pass 2026!", Ct);
        await services.GetRequiredService<RoleService>().AssignAsync(user.Id, company.Id,
            new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        var number = "HT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        long id;
        await using (var db = QuoteContext(company.Id))
        {
            var quote = new SalesQuotation
            {
                CompanyId = company.Id, DocumentNo = number,
                CustomerId = 21, CustomerCode = "C21", CustomerName = "Customer",
                BusinessDate = new DateOnly(2028, 2, 29), ExpiryDate = new DateOnly(2028, 3, 31),
                CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
                OwnerUserId = user.Id, Version = 1, OriginalNet = 10m, OriginalGross = 10m,
                BaseNet = 10m, BaseGross = 10m
            };
            db.Add(quote);
            await db.SaveChangesAsync(Ct);
            id = quote.Id;
        }
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = "Strong pass 2026!", captchaId = challenge.Id,
                captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;
        async Task<HttpResponseMessage> DeleteAsync(int version)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"/api/v1/sal/quotations/{id}?version={version}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", company.Id.ToString(CultureInfo.InvariantCulture));
            return await client.SendAsync(request, Ct);
        }
        using (var stale = await DeleteAsync(2))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("COMMON.CONCURRENCY.CONFLICT",
                (await stale.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString());
        }
        using (var deleted = await DeleteAsync(1))
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var repeated = await DeleteAsync(1))
            Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        await using var verify = QuoteContext(company.Id);
        var saved = await verify.Set<SalesQuotation>().IgnoreQueryFilters()
            .SingleAsync(x => x.Id == id, Ct);
        Assert.True(saved.IsDeleted);
        Assert.Equal(number, saved.DocumentNo);
        Assert.Equal(user.Id, saved.OwnerUserId);
    }

    [Fact]
    public async Task ViewRoleCanReadOnlyItsCompanyAndCannotMutateQuotation()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var organizations = services.GetRequiredService<OrganizationService>();
        var home = await organizations.CreateCompanyAsync(
            "QH" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Quote home", Ct);
        var foreign = await organizations.CreateCompanyAsync(
            "QF" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Quote foreign", Ct);
        var roles = services.GetRequiredService<RoleService>();
        var role = await roles.CreateAsync(new RoleInput(
            "Q" + Guid.NewGuid().ToString("N")[..12], "Quote viewer", null,
            DataScope.Company, false, ["SAL.QUOTATION.VIEW"]), Ct);
        var username = "sal" + Guid.NewGuid().ToString("N")[..20];
        var user = await services.GetRequiredService<SystemUserService>()
            .CreateAsync(username, "Strong pass 2026!", Ct);
        await roles.AssignAsync(user.Id, home.Id,
            new UserCompanyAssignment(null, null, true, true, [role.Id]), Ct);
        await roles.AssignAsync(user.Id, foreign.Id,
            new UserCompanyAssignment(null, null, false, true, [role.Id]), Ct);
        long quotationId;
        await using (var db = QuoteContext(home.Id))
        {
            var quote = new SalesQuotation
            {
                CompanyId = home.Id, DocumentNo = "HT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CustomerId = 21, CustomerCode = "C21", CustomerName = "Customer",
                BusinessDate = new DateOnly(2028, 2, 29), ExpiryDate = new DateOnly(2028, 3, 31),
                CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m,
                OwnerUserId = user.Id, OriginalNet = 10m, OriginalGross = 10m,
                BaseNet = 10m, BaseGross = 10m
            };
            db.Add(quote);
            await db.SaveChangesAsync(Ct);
            quotationId = quote.Id;
        }
        var challenge = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
            new { username, password = "Strong pass 2026!", captchaId = challenge.Id,
                captchaCode = challenge.Code }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("accessToken").GetString()!;
        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, long companyId, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request, Ct);
        }
        using var detail = await SendAsync(HttpMethod.Get, $"/api/v1/sal/quotations/{quotationId}", home.Id);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(quotationId, (await detail.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("id").GetInt64());
        using var foreignDetail = await SendAsync(HttpMethod.Get,
            $"/api/v1/sal/quotations/{quotationId}", foreign.Id);
        Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
        using var foreignList = await SendAsync(HttpMethod.Get, "/api/v1/sal/quotations", foreign.Id);
        Assert.Equal(HttpStatusCode.OK, foreignList.StatusCode);
        Assert.Equal(0, (await foreignList.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("totalCount").GetInt64());
        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, "/api/v1/sal/quotations", new { customerId = 21 }),
            (HttpMethod.Put, $"/api/v1/sal/quotations/{quotationId}", new { version = 1 }),
            (HttpMethod.Delete, $"/api/v1/sal/quotations/{quotationId}?version=1", null),
            (HttpMethod.Post, $"/api/v1/sal/quotations/{quotationId}/submit", new { version = 1 })
        })
        {
            using var denied = await SendAsync(method, path, home.Id, body);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("COMMON.PERMISSION.DENIED",
                (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct))
                    .GetProperty("errorCode").GetString());
        }
    }

    private MyErpDbContext QuoteContext(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new SalesQuotationDbModel(), new SystemOrganizationDbModel()], new FixedCompany(companyId));

    private MyErpDbContext MdContext(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new SalesQuotationDbModel(), new SystemOrganizationDbModel(), new UomDbModel(),
            new ItemDbModel(), new CustomerDbModel(), new PaymentSetupDbModel(),
            new CurrencyDbModel(), new TaxDbModel()], new FixedCompany(companyId));

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
