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
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Shared.Approvals;
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
    public async Task RfqReadEndpointsEnforceAuthenticationPermissionAndCompany()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "RFQ read", Ct)).Id;
        using var http = factory.CreateClient();
        var (viewerId, token) = await CreateUserAsync(http);
        long id;
        var fixedCompany = new FixedCompany(company);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)), new FixedUser(viewerId), fixedCompany);
        await using (var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel()], fixedCompany))
        {
            var rfq = new Rfq { CompanyId = company, DocumentNo = $"RFQ-HTTP-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
                OwnerId = viewerId, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
            rfq.Lines.Add(new RfqLine { CompanyId = company, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1.123456m });
            rfq.Invitations.Add(new RfqInvitation { CompanyId = company, SupplierId = 21 });
            db.Add(rfq);
            await db.SaveChangesAsync(Ct);
            id = rfq.Id;
        }
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.GetAsync($"/api/v1/pur/rfqs/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.GetAsync($"/api/v1/pur/rfqs/{id}/comparison", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{id}", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{id}/comparison", token, company)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "RFQ viewer", null, DataScope.Company, false,
                [PurchasingPermissions.RfqView, PurchasingPermissions.RfqCompare]), Ct)).Id;
            await roles.AssignAsync(viewerId, company,
                new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        var detail = await SendAsync(http, $"/api/v1/pur/rfqs/{id}", token, company);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var body = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1.123456m, body.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(21, body.GetProperty("invitations")[0].GetProperty("supplierId").GetInt64());
        var list = await SendAsync(http, "/api/v1/pur/rfqs?sort=-documentNo&pageSize=1", token, company);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(id, (await list.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(http, "/api/v1/pur/rfqs?sort=ownerId", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{long.MaxValue}", token, company)).StatusCode);
        var comparison = await SendAsync(http, $"/api/v1/pur/rfqs/{id}/comparison", token, company);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, comparison.StatusCode);
        Assert.Equal("MD.EXCHANGE_RATE.NO_BASE_CURRENCY",
            (await comparison.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task QuotationContextRequiresQuoteOnlyAndHidesOutOfScopeRfqs()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Quote context", Ct)).Id;
        using var http = factory.CreateClient();
        var (ownerId, token) = await CreateUserAsync(http);
        long ownId, otherOwnerId;
        var fixedCompany = new FixedCompany(company);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)),
            new FixedUser(ownerId), fixedCompany);
        await using (var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new CurrencyDbModel(), new TaxDbModel()], fixedCompany))
        {
            var currency = new Currency { CompanyId = company, Code = "TWD", Name = "Taiwan dollar" };
            var inactiveCurrency = new Currency { CompanyId = company, Code = "USD",
                Name = "US dollar", IsActive = false };
            var inputCode = new TaxCode { CompanyId = company, Code = "IN",
                Name = "Input tax", TaxType = "Input" };
            var outputCode = new TaxCode { CompanyId = company, Code = "OUT",
                Name = "Output tax", TaxType = "Output" };
            db.AddRange(currency, inactiveCurrency, inputCode, outputCode);
            await db.SaveChangesAsync(Ct);
            db.AddRange(
                new TaxRate { CompanyId = company, TaxCodeId = inputCode.Id,
                    EffectiveFrom = new DateOnly(2028, 2, 29), EffectiveTo = new DateOnly(2028, 2, 29),
                    RatePercent = 5m },
                new TaxRate { CompanyId = company, TaxCodeId = outputCode.Id,
                    EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 10m });
            var own = new Rfq { CompanyId = company, DocumentNo = $"Q-CTX-{company}-1",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
                OwnerId = ownerId, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
            own.Lines.Add(new RfqLine { CompanyId = company, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1.123456m });
            own.Invitations.Add(new RfqInvitation { CompanyId = company, SupplierId = 21 });
            var other = new Rfq { CompanyId = company, DocumentNo = $"Q-CTX-{company}-2",
                BusinessDate = own.BusinessDate, ReplyDeadline = own.ReplyDeadline,
                OwnerId = ownerId + 1 };
            other.Lines.Add(new RfqLine { CompanyId = company, LineNo = 1, ItemId = 12,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1m });
            db.AddRange(own, other);
            await db.SaveChangesAsync(Ct);
            (ownId, otherOwnerId) = (own.Id, other.Id);
        }
        var path = $"/api/v1/pur/rfqs/{ownId}/quotation-context?quoteDate=2028-02-29";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, path, token, company)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Quote only", null, DataScope.Self, false,
                [PurchasingPermissions.QuotationQuote]), Ct)).Id;
            await roles.AssignAsync(ownerId, company,
                new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        var response = await SendAsync(http, path, token, company);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(ownId, body.GetProperty("rfq").GetProperty("id").GetInt64());
        Assert.Equal(1.123456m, body.GetProperty("rfq").GetProperty("lines")[0]
            .GetProperty("quantity").GetDecimal());
        Assert.Equal(21, body.GetProperty("rfq").GetProperty("invitations")[0]
            .GetProperty("supplierId").GetInt64());
        Assert.Equal("TWD", body.GetProperty("currencies")[0].GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("currencies").GetArrayLength());
        Assert.Equal("IN", body.GetProperty("taxRates")[0].GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("taxRates").GetArrayLength());
        var nextDay = await SendAsync(http,
            $"/api/v1/pur/rfqs/{ownId}/quotation-context?quoteDate=2028-03-01", token, company);
        Assert.Equal(HttpStatusCode.OK, nextDay.StatusCode);
        Assert.Equal(0, (await nextDay.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("taxRates").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{otherOwnerId}/quotation-context", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{long.MaxValue}/quotation-context", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{ownId}/quotation-context?quoteDate=not-a-date",
                token, company)).StatusCode);
    }

    [Fact]
    public async Task QuotationReadEndpointsRequireOwnPermissionAndReturnSavedHistory()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Quotation read", Ct)).Id;
        using var http = factory.CreateClient();
        var (viewerId, token) = await CreateUserAsync(http);
        long rfqId, quotationId;
        var fixedCompany = new FixedCompany(company);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)),
            new FixedUser(viewerId), fixedCompany);
        await using (var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel()], fixedCompany))
        {
            var rfq = new Rfq { CompanyId = company, DocumentNo = $"Q-HTTP-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
                OwnerId = viewerId };
            rfq.Lines.Add(new RfqLine { CompanyId = company, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1m });
            rfq.Invitations.Add(new RfqInvitation { CompanyId = company, SupplierId = 21 });
            db.Add(rfq);
            await db.SaveChangesAsync(Ct);
            var quotation = new SupplierQuotation { CompanyId = company, RfqId = rfq.Id,
                SupplierId = 21, CurrentRevisionNo = 1, Status = SupplierQuotationStatus.Withdrawn };
            quotation.Revisions.Add(new QuotationRevision { CompanyId = company,
                RevisionNo = 1, QuoteDate = new DateOnly(2028, 2, 29),
                ValidUntil = new DateOnly(2028, 3, 1), Lines = {
                    new QuotationLine { CompanyId = company, RfqLineId = rfq.Lines.Single().Id,
                        UnitPrice = 1.123456m, CurrencyCode = "USD", TaxRateId = 31 } } });
            db.Add(quotation);
            await db.SaveChangesAsync(Ct);
            (rfqId, quotationId) = (rfq.Id, quotation.Id);
        }
        var listPath = $"/api/v1/pur/rfqs/{rfqId}/quotations";
        var detailPath = $"{listPath}/{quotationId}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(listPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(detailPath, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, listPath, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, detailPath, token, company)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Quotation viewer", null, DataScope.Self, false,
                [PurchasingPermissions.QuotationView]), Ct)).Id;
            await roles.AssignAsync(viewerId, company,
                new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        var list = await SendAsync(http, listPath, token, company);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listed = await list.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(quotationId, listed.GetProperty("items")[0].GetProperty("id").GetInt64());
        var detail = await SendAsync(http, detailPath, token, company);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var saved = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(1.123456m, saved.GetProperty("revisions")[0].GetProperty("lines")[0]
            .GetProperty("unitPrice").GetDecimal());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SendAsync(http, listPath + "?sort=unitPrice", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(http, $"{listPath}/{long.MaxValue}", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{long.MaxValue}/quotations", token, company)).StatusCode);
    }

    [Fact]
    public async Task RfqAwardEndpointEnforcesAuthenticationScopeAndOpenState()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Award auth", Ct)).Id;
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        long rfqId, lineId, outsiderId;
        var fixedCompany = new FixedCompany(company);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)),
            new FixedUser(userId), fixedCompany);
        await using (var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel()], fixedCompany))
        {
            var rfq = new Rfq { CompanyId = company, DocumentNo = $"Q-AUTH-{company}",
                BusinessDate = new DateOnly(2026, 10, 20), ReplyDeadline = new DateOnly(2026, 10, 22),
                OwnerId = userId, Status = RfqStatus.Closed, SentAt = new DateTime(2026, 10, 21) };
            rfq.Lines.Add(new RfqLine { CompanyId = company, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2026, 11, 1), Quantity = 1m });
            var outsider = new Rfq { CompanyId = company, DocumentNo = $"Q-AUTH-OUT-{company}",
                BusinessDate = new DateOnly(2026, 10, 20), ReplyDeadline = new DateOnly(2026, 10, 22),
                OwnerId = long.MaxValue, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
            db.AddRange(rfq, outsider);
            await db.SaveChangesAsync(Ct);
            (rfqId, lineId) = (rfq.Id, rfq.Lines.Single().Id);
            outsiderId = outsider.Id;
        }
        var path = $"/api/v1/pur/rfqs/{rfqId}/awards";
        var body = new { version = 1, rfqLineId = lineId, quotationId = 1, reason = "Evaluation" };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync(path, body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, path, body, token, company)).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Award user", null, DataScope.Self, false, [PurchasingPermissions.RfqAward]), Ct)).Id;
            await roles.AssignAsync(userId, company,
                new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await MutationAsync(http, HttpMethod.Post, $"/api/v1/pur/rfqs/{rfqId + 1_000_000}/awards",
                body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await MutationAsync(http, HttpMethod.Post, $"/api/v1/pur/rfqs/{outsiderId}/awards",
                body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await MutationAsync(http, HttpMethod.Post, path, body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await MutationAsync(http, HttpMethod.Post, path,
                new { version = 1, rfqLineId = lineId, quotationId = 1, reason = " " }, token, company)).StatusCode);
    }

    [Fact]
    public async Task QuotationWriteEndpointsSaveRealMasterDataAndPreserveRevisionHistory()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Quotation write", Ct)).Id;
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Quotation editor", null, DataScope.Self, false,
                [PurchasingPermissions.QuotationView, PurchasingPermissions.QuotationQuote,
                    PurchasingPermissions.RfqCompare]), Ct)).Id;
            await roles.AssignAsync(userId, company,
                new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        long rfqId, outsiderId, lineId, taxRateId;
        var fixedCompany = new FixedCompany(company);
        var stamp = new AuditStamper(new FixedClock(new DateTime(2026, 10, 21)),
            new FixedUser(userId), fixedCompany);
        await using (var db = new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamp)).Options,
            [new RfqDbModel(), new QuotationDbModel(), new CurrencyDbModel(), new TaxDbModel()], fixedCompany))
        {
            var currency = new Currency { CompanyId = company, Code = "TWD", Name = "Taiwan dollar", IsBase = true };
            var taxCode = new TaxCode { CompanyId = company, Code = "IN", Name = "Input tax", TaxType = "Input" };
            db.AddRange(currency, taxCode);
            await db.SaveChangesAsync(Ct);
            var rate = new TaxRate { CompanyId = company, TaxCodeId = taxCode.Id,
                EffectiveFrom = new DateOnly(2028, 1, 1), RatePercent = 5m };
            var rfq = new Rfq { CompanyId = company, DocumentNo = $"Q-WRITE-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
                OwnerId = userId, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
            rfq.Lines.Add(new RfqLine { CompanyId = company, LineNo = 1, ItemId = 11,
                RequiredDate = new DateOnly(2028, 3, 2), Quantity = 1m });
            rfq.Invitations.Add(new RfqInvitation { CompanyId = company, SupplierId = 21,
                SupplierCodeSnapshot = "SUP21", SupplierNameSnapshot = "Supplier 21",
                SentAt = new DateTime(2026, 10, 21) });
            var outsider = new Rfq { CompanyId = company, DocumentNo = $"Q-OTHER-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1),
                OwnerId = long.MaxValue, Status = RfqStatus.Sent, SentAt = new DateTime(2026, 10, 21) };
            db.AddRange(rate, rfq, outsider);
            await db.SaveChangesAsync(Ct);
            (rfqId, outsiderId, lineId, taxRateId) = (rfq.Id, outsider.Id, rfq.Lines.Single().Id, rate.Id);
        }
        var path = $"/api/v1/pur/rfqs/{rfqId}/quotations";
        var input = new { supplierId = 21L, quoteDate = "2028-02-29", validUntil = "2028-03-31",
            lines = new[] { new { rfqLineId = lineId, unitPrice = 1.123456m,
                currencyCode = "TWD", taxRateId } } };
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Post,
            $"/api/v1/pur/rfqs/{outsiderId}/quotations", input, token, company)).StatusCode);
        var created = await MutationAsync(http, HttpMethod.Post, path, input, token, company);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var quotationId = first.GetProperty("id").GetInt64();
        var version = first.GetProperty("version").GetInt32();
        Assert.Equal(1.123456m, first.GetProperty("revisions")[0].GetProperty("lines")[0]
            .GetProperty("unitPrice").GetDecimal());
        var comparison = await SendAsync(http, $"/api/v1/pur/rfqs/{rfqId}/comparison", token, company);
        Assert.Equal(HttpStatusCode.OK, comparison.StatusCode);
        var offer = (await comparison.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("lines")[0].GetProperty("offers")[0];
        Assert.Equal(quotationId, offer.GetProperty("quotationId").GetInt64());
        Assert.Equal("PUR.QUOTATION.NOT_YET_VALID", offer.GetProperty("unavailableReason").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(http, $"/api/v1/pur/rfqs/{outsiderId}/comparison", token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await MutationAsync(http, HttpMethod.Post, path, input, token, company)).StatusCode);
        var revisionPath = $"{path}/{quotationId}/revisions?version={version}";
        var revised = await MutationAsync(http, HttpMethod.Post, revisionPath, input, token, company);
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        var saved = await revised.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, saved.GetProperty("revisions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict,
            (await MutationAsync(http, HttpMethod.Post, revisionPath, input, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{quotationId}/revisions?version={saved.GetProperty("version").GetInt32()}",
            new { supplierId = 21L, quoteDate = "2028-02-29", validUntil = "2028-03-31",
                lines = new[] { new { rfqLineId = lineId, unitPrice = -1m, currencyCode = "TWD", taxRateId } } },
            token, company)).StatusCode);
        var currentVersion = saved.GetProperty("version").GetInt32();
        await using (var db = FullContext(company))
        {
            var currency = await db.Set<Currency>().SingleAsync(x => x.Code == "TWD", Ct);
            currency.IsDeleted = true;
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{quotationId}/revisions?version={currentVersion}", input, token, company)).StatusCode);
        await using (var db = FullContext(company))
        {
            var currency = await db.Set<Currency>().IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == company && x.Code == "TWD", Ct);
            currency.IsDeleted = false;
            var rate = await db.Set<TaxRate>().SingleAsync(x => x.Id == taxRateId, Ct);
            rate.IsDeleted = true;
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{quotationId}/revisions?version={currentVersion}", input, token, company)).StatusCode);
        await using (var db = FullContext(company))
        {
            var rate = await db.Set<TaxRate>().IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == company && x.Id == taxRateId, Ct);
            rate.IsDeleted = false;
            await db.SaveChangesAsync(Ct);
        }
        var withdrawPath = $"{path}/{quotationId}/withdraw";
        Assert.Equal(HttpStatusCode.Conflict, (await MutationAsync(http, HttpMethod.Post,
            withdrawPath + $"?version={version}", null, token, company)).StatusCode);
        var withdrawn = await MutationAsync(http, HttpMethod.Post,
            withdrawPath + $"?version={saved.GetProperty("version").GetInt32()}", null, token, company);
        Assert.Equal(HttpStatusCode.OK, withdrawn.StatusCode);
        var history = await withdrawn.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((int)SupplierQuotationStatus.Withdrawn, history.GetProperty("status").GetInt32());
        Assert.Equal(2, history.GetProperty("revisions").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await MutationAsync(http, HttpMethod.Post,
            withdrawPath + $"?version={history.GetProperty("version").GetInt32()}", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{quotationId}/revisions?version={history.GetProperty("version").GetInt32()}",
            input, token, company)).StatusCode);
    }

    [Fact]
    public async Task RfqDraftMutationEndpointsRequireTheirOwnPermissions()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "RFQ mutation", Ct)).Id;
        using var http = factory.CreateClient();
        var body = new { businessDate = "2028-02-29", replyDeadline = "2028-03-01",
            lines = new[] { new { lineNo = 1, itemId = 11, requiredDate = "2028-03-02", quantity = 1m } },
            supplierIds = new[] { 21L } };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/api/v1/pur/rfqs", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsync("/api/v1/pur/rfqs/1/send?version=1", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsync("/api/v1/pur/rfqs/1/close?version=1", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/api/v1/pur/rfqs/1/quotations", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsJsonAsync("/api/v1/pur/rfqs/1/quotations/1/revisions?version=1", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.PostAsync("/api/v1/pur/rfqs/1/quotations/1/withdraw?version=1", null, Ct)).StatusCode);
        var (userId, token) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "RFQ viewer", null, DataScope.Company, false, [PurchasingPermissions.RfqView]), Ct)).Id;
            await roles.AssignAsync(userId, company,
                new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, "/api/v1/pur/rfqs", body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Put, "/api/v1/pur/rfqs/1?version=1", body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Delete, "/api/v1/pur/rfqs/1?version=1", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, "/api/v1/pur/rfqs/1/send?version=1", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, "/api/v1/pur/rfqs/1/close?version=1", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, "/api/v1/pur/rfqs/1/quotations", body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, "/api/v1/pur/rfqs/1/quotations/1/revisions?version=1", body, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await MutationAsync(http, HttpMethod.Post, "/api/v1/pur/rfqs/1/quotations/1/withdraw?version=1", null, token, company)).StatusCode);
    }

    [Fact]
    public async Task RfqDraftAuthorizedHttpCreatesReplacesDeletesAndSerializesNumbers()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "RFQ HTTP draft", Ct)).Id;
        long itemId, supplierId, outsiderId;
        await using (var db = FullContext(company))
        {
            var uom = new UnitOfMeasure { CompanyId = company, Code = "EA", Name = "Each", DecimalPlaces = 6 };
            db.Add(uom);
            await db.SaveChangesAsync(Ct);
            var item = new Item { CompanyId = company, Code = "I" + Guid.NewGuid().ToString("N")[..8],
                Name = "Part", BaseUomId = uom.Id };
            var supplier = new Supplier { CompanyId = company, Code = "S" + Guid.NewGuid().ToString("N")[..8],
                Name = "Supplier", ReviewStatus = PartnerReviewStatus.Approved };
            db.AddRange(item, supplier);
            db.Add(new NumberRule { CompanyId = company, DocumentType = "PUR.RFQ", Name = "RFQ",
                Prefix = "RFQ-", DateFormat = NumberDateFormat.None, SequenceLength = 4,
                ResetPeriod = NumberResetPeriod.Never });
            var outsider = new Rfq { CompanyId = company, DocumentNo = $"OUT-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), ReplyDeadline = new DateOnly(2028, 3, 1), OwnerId = long.MaxValue };
            db.Add(outsider);
            await db.SaveChangesAsync(Ct);
            (itemId, supplierId, outsiderId) = (item.Id, supplier.Id, outsider.Id);
        }
        using var http = factory.CreateClient();
        var (userId, token) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "RFQ editor", null, DataScope.Self, false,
                [PurchasingPermissions.RfqCreate, PurchasingPermissions.RfqEdit,
                    PurchasingPermissions.RfqDelete, PurchasingPermissions.RfqView,
                    PurchasingPermissions.RfqSend, PurchasingPermissions.RfqClose]), Ct)).Id;
            await roles.AssignAsync(userId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        const string path = "/api/v1/pur/rfqs";
        var input = new { businessDate = "2028-02-29", replyDeadline = "2028-03-01", remark = "draft",
            lines = new[] { new { lineNo = 1, itemId, requiredDate = "2028-03-02", quantity = "1.123456" } },
            supplierIds = new[] { supplierId } };
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Put,
            $"{path}/{outsiderId}?version=1", input, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Delete,
            $"{path}/{outsiderId}?version=1", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{outsiderId}/send?version=1", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{outsiderId}/close?version=1", null, token, company)).StatusCode);
        var responses = await Task.WhenAll(PostAsync(http, path, input, token, company),
            PostAsync(http, path, input, token, company));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var created = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<JsonElement>(Ct)));
        Assert.Equal(["RFQ-0001", "RFQ-0002"], created.Select(x => x.GetProperty("documentNo").GetString())
            .Order(StringComparer.Ordinal));
        var id = created[0].GetProperty("id").GetInt64();
        Assert.Equal(userId, created[0].GetProperty("ownerId").GetInt64());
        var edited = await MutationAsync(http, HttpMethod.Put, $"{path}/{id}?version=1", input, token, company);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var version = (await edited.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("version").GetInt32();
        Assert.Equal(2, version);
        Assert.Equal(HttpStatusCode.Conflict, (await MutationAsync(http, HttpMethod.Put,
            $"{path}/{id}?version=1", input, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await MutationAsync(http, HttpMethod.Delete,
            $"{path}/{id}?version={version}", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(http, $"{path}/{id}", token, company)).StatusCode);
        var sentId = created[1].GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{sentId}/close?version=1", null, token, company)).StatusCode);
        var sent = await MutationAsync(http, HttpMethod.Post, $"{path}/{sentId}/send?version=1", null, token, company);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var sentBody = await sent.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((int)RfqStatus.Sent, sentBody.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(sentBody.GetProperty("invitations")[0]
            .GetProperty("supplierNameSnapshot").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{sentId}/send?version=1", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{sentId}/send?version={sentBody.GetProperty("version").GetInt32()}", null, token, company)).StatusCode);
        var closed = await MutationAsync(http, HttpMethod.Post,
            $"{path}/{sentId}/close?version={sentBody.GetProperty("version").GetInt32()}", null, token, company);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal((int)RfqStatus.Closed, (await closed.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("status").GetInt32());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await MutationAsync(http, HttpMethod.Post,
            $"{path}/{sentId}/close?version={sentBody.GetProperty("version").GetInt32() + 1}", null, token, company)).StatusCode);
        await using var check = FullContext(company);
        Assert.Equal(2, await check.Set<Rfq>().IgnoreQueryFilters().CountAsync(
            x => x.CompanyId == company && x.DocumentNo.StartsWith("RFQ-"), Ct));
        Assert.Equal(2, (await check.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
    }

    [Fact]
    public async Task DetailReturnsEverySavedApprovalRoundOnlyForAuthorizedCompany()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Requisition history", Ct)).Id;
        using var http = factory.CreateClient();
        var (viewerId, token) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Requisition viewer", null, DataScope.Company, false,
                [PurchasingPermissions.RequisitionView, SystemPermissions.ApprovalProcess]), Ct)).Id;
            await roles.AssignAsync(viewerId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        long id;
        await using (var db = FullContext(company))
        {
            var document = new Requisition { CompanyId = company, DocumentNo = $"PUR-HIST-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = viewerId,
                Status = RequisitionStatus.Approved };
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            id = document.Id;
            foreach (var (status, comment) in new[]
            {
                (ApprovalStatus.Rejected, "Revise quantity"),
                (ApprovalStatus.Approved, "Looks good")
            })
            {
                var approval = new ApprovalInstance { CompanyId = company, DocumentType = "PUR.REQUISITION",
                    DocumentId = id, DocumentNo = document.DocumentNo, Status = status,
                    SubmittedBy = viewerId, SubmittedAt = new DateTime(2026, 10, 20, 15, 0, 0),
                    CompletedAt = new DateTime(2026, 10, 20, 15, 1, 0) };
                approval.Steps.Add(new ApprovalInstanceStep { CompanyId = company, StepNo = 1,
                    Name = "Manager", ApproverType = ApproverType.User, UserId = viewerId,
                    Status = status == ApprovalStatus.Approved ? ApprovalStepStatus.Approved : ApprovalStepStatus.Rejected,
                    ActedBy = viewerId, Comment = comment });
                db.Add(approval);
                await db.SaveChangesAsync(Ct);
            }
        }
        var response = await SendAsync(http, $"/api/v1/pur/requisitions/{id}", token, company);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var history = body.GetProperty("approvalHistory");
        Assert.Equal(2, history.GetArrayLength());
        Assert.Equal("Looks good", history[0].GetProperty("steps")[0].GetProperty("comment").GetString());
        Assert.Equal("Revise quantity", history[1].GetProperty("steps")[0].GetProperty("comment").GetString());

        long pendingId;
        await using (var db = FullContext(company))
        {
            var document = await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct);
            document.Status = RequisitionStatus.PendingApproval;
            var approval = new ApprovalInstance { CompanyId = company, DocumentType = "PUR.REQUISITION",
                DocumentId = id, DocumentNo = document.DocumentNo, Status = ApprovalStatus.Pending,
                CurrentStepNo = 1, AllowSelfApproval = true, SubmittedBy = viewerId,
                SubmittedAt = new DateTime(2026, 10, 20, 15, 2, 0) };
            approval.Steps.Add(new ApprovalInstanceStep { CompanyId = company, StepNo = 1,
                Name = "Manager", ApproverType = ApproverType.User, UserId = viewerId,
                Status = ApprovalStepStatus.Pending });
            db.Add(approval);
            await db.SaveChangesAsync(Ct);
            pendingId = approval.Id;
        }
        var decided = await PostAsync(http, $"/api/v1/sys/approvals/{pendingId}/approve",
            new { version = 1, comment = "Final approval" }, token, company);
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        await using (var db = FullContext(company))
        {
            Assert.Equal(RequisitionStatus.Approved,
                (await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct)).Status);
            Assert.Equal(ApprovalStatus.Approved,
                (await db.Set<ApprovalInstance>().SingleAsync(x => x.Id == pendingId, Ct)).Status);
            var decisionAudits = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM dbo.sys_audit_log WHERE company_id = {0} AND table_name = 'sys_approval_instance' AND record_id = {1} AND [action] = 'APPROVE'",
                company, pendingId.ToString(CultureInfo.InvariantCulture)).SingleAsync(Ct);
            Assert.Equal(1, decisionAudits);
        }
    }

    [Fact]
    public async Task CreatorPostsRealDraftWithSystemNumberAndMasterDataValidation()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Purchasing Create", Ct)).Id;
        long itemId;
        await using (var db = FullContext(company))
        {
            var uom = new UnitOfMeasure { CompanyId = company, Code = "EA", Name = "Each", DecimalPlaces = 6 };
            db.Add(uom);
            await db.SaveChangesAsync(Ct);
            var item = new Item { CompanyId = company, Code = "I" + Guid.NewGuid().ToString("N")[..8],
                Name = "Part", BaseUomId = uom.Id };
            db.Add(item);
            db.Add(new NumberRule { CompanyId = company, DocumentType = "PUR.REQUISITION", Name = "Requisition",
                Prefix = "PR", DateFormat = NumberDateFormat.YearMonth, SequenceLength = 4,
                ResetPeriod = NumberResetPeriod.Monthly });
            await db.SaveChangesAsync(Ct);
            itemId = item.Id;
        }
        using var http = factory.CreateClient();
        var (creatorId, token) = await CreateUserAsync(http);
        var (_, noCreateToken) = await CreateUserAsync(http);
        var (editorId, editOnlyToken) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12], "Creator", null,
                DataScope.Company, false, [PurchasingPermissions.RequisitionCreate, PurchasingPermissions.RequisitionView,
                    PurchasingPermissions.RequisitionEdit, PurchasingPermissions.RequisitionDelete]), Ct)).Id;
            await roles.AssignAsync(creatorId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
            var editRole = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Requisition editor", null, DataScope.Company, false,
                [PurchasingPermissions.RequisitionEdit]), Ct)).Id;
            await roles.AssignAsync(editorId, company, new UserCompanyAssignment(null, null, false, true, [editRole]), Ct);
        }
        const string path = "/api/v1/pur/requisitions";
        var optionsPath = $"{path}/item-options?page=1&pageSize=20";
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, optionsPath, noCreateToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(http, optionsPath, editOnlyToken, company)).StatusCode);
        var options = await SendAsync(http, optionsPath, token, company);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Contains((await options.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray(),
            option => option.GetProperty("id").GetInt64() == itemId);
        var input = new { businessDate = "2028-02-29", remark = "draft", requesterId = 99999,
            branchId = 99999, departmentId = 99999,
            lines = new[] { new { lineNo = 1, itemId, requiredDate = "2028-03-01", quantity = "1.123456", remark = "part" } } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path, input, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(http, path, input, noCreateToken, company)).StatusCode);
        var created = await PostAsync(http, path, input, token, company);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(creatorId, body.GetProperty("requesterId").GetInt64());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("branchId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("departmentId").ValueKind);
        Assert.Equal("PR2028020001", body.GetProperty("documentNo").GetString());
        Assert.Equal(1.123456m, body.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        var concurrent = await Task.WhenAll(PostAsync(http, path, input, token, company),
            PostAsync(http, path, input, token, company));
        Assert.All(concurrent, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var concurrentNumbers = await Task.WhenAll(concurrent.Select(async response =>
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("documentNo").GetString()!));
        Assert.Equal(2, concurrentNumbers.Distinct().Count());
        Assert.DoesNotContain(body.GetProperty("documentNo").GetString(), concurrentNumbers);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(http, $"{path}/{id}", editOnlyToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(http, $"{path}/{id}/edit", editOnlyToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(http, $"{path}/{id}/saved-item-options", editOnlyToken, company)).StatusCode);
        var savedOptions = await SendAsync(http, $"{path}/{id}/saved-item-options", token, company);
        Assert.Equal(HttpStatusCode.OK, savedOptions.StatusCode);
        Assert.Contains((await savedOptions.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray(),
            option => option.GetProperty("id").GetInt64() == itemId);
        var updateInput = new { businessDate = "2028-03-01", remark = "revised",
            lines = new[] { new { lineNo = 2, itemId, requiredDate = "2028-03-02", quantity = "2.123456" } } };
        Assert.Equal(HttpStatusCode.Forbidden, (await MutationAsync(http, HttpMethod.Put, $"{path}/{id}?version=1",
            updateInput, noCreateToken, company)).StatusCode);
        var updated = await MutationAsync(http, HttpMethod.Put, $"{path}/{id}?version=1", updateInput, token, company);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(2, updatedBody.GetProperty("lines")[0].GetProperty("lineNo").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await MutationAsync(http, HttpMethod.Put,
            $"{path}/{id}?version=1", updateInput, token, company)).StatusCode);
        var invalid = new { businessDate = "2028-02-29", lines = new[] { new { lineNo = 1, itemId = itemId + 99999,
            requiredDate = "2028-03-01", quantity = 1m } } };
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(http, path, invalid, token, company)).StatusCode);
        await using (var db = FullContext(company))
        {
            var item = await db.Set<Item>().SingleAsync(x => x.Id == itemId, Ct);
            item.IsActive = false;
            await db.SaveChangesAsync(Ct);
        }
        var inactiveOptions = await SendAsync(http, optionsPath, token, company);
        Assert.Equal(HttpStatusCode.OK, inactiveOptions.StatusCode);
        Assert.DoesNotContain((await inactiveOptions.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray(),
            option => option.GetProperty("id").GetInt64() == itemId);
        var savedInactive = await SendAsync(http, $"{path}/{id}/saved-item-options", token, company);
        Assert.Contains((await savedInactive.Content.ReadFromJsonAsync<JsonElement>(Ct)).EnumerateArray(),
            option => option.GetProperty("id").GetInt64() == itemId && !option.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await PostAsync(http, path, input, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await MutationAsync(http, HttpMethod.Put,
            $"{path}/{id}?version={updatedBody.GetProperty("version").GetInt32()}", updateInput, token, company)).StatusCode);
        await using var check = FullContext(company);
        Assert.Equal(3, await check.Set<Requisition>().CountAsync(Ct));
        Assert.Equal(3, (await check.Set<NumberSequence>().SingleAsync(Ct)).CurrentValue);
        Assert.Equal(2, (await check.Set<Requisition>().Include(x => x.Lines)
            .SingleAsync(x => x.Id == id, Ct)).Lines.Single().LineNo);
        await using (var db = FullContext(company))
        {
            var item = await db.Set<Item>().SingleAsync(x => x.Id == itemId, Ct);
            item.IsActive = true;
            await db.SaveChangesAsync(Ct);
        }
        var retried = await PostAsync(http, path, input, token, company);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal("PR2028020004", (await retried.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("documentNo").GetString());
        await using (var db = FullContext(company))
        {
            var item = await db.Set<Item>().SingleAsync(x => x.Id == itemId, Ct);
            item.IsDeleted = true;
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await MutationAsync(http, HttpMethod.Put,
            $"{path}/{id}?version={updatedBody.GetProperty("version").GetInt32()}", updateInput, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await MutationAsync(http, HttpMethod.Delete,
            $"{path}/{id}?version={updatedBody.GetProperty("version").GetInt32()}", null, noCreateToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await MutationAsync(http, HttpMethod.Delete,
            $"{path}/{id}?version={updatedBody.GetProperty("version").GetInt32()}", null, token, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(http, $"{path}/{id}", token, company)).StatusCode);
    }

    [Fact]
    public async Task SubmitRevalidatesRealMasterDataAndPersistsOnlySuccessfulApproval()
    {
        long company;
        await using (var scope = factory.Services.CreateAsyncScope())
            company = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("P" + Guid.NewGuid().ToString("N")[..12], "Requisition submit", Ct)).Id;
        long itemId;
        await using (var db = FullContext(company))
        {
            var uom = new UnitOfMeasure { CompanyId = company, Code = "EA", Name = "Each", DecimalPlaces = 6 };
            db.Add(uom);
            await db.SaveChangesAsync(Ct);
            var item = new Item { CompanyId = company, Code = "I" + Guid.NewGuid().ToString("N")[..8],
                Name = "Part", BaseUomId = uom.Id };
            db.Add(item);
            await db.SaveChangesAsync(Ct);
            itemId = item.Id;
        }
        using var http = factory.CreateClient();
        var (submitterId, token) = await CreateUserAsync(http);
        var (_, noSubmitToken) = await CreateUserAsync(http);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleService>();
            var role = (await roles.CreateAsync(new RoleInput("P" + Guid.NewGuid().ToString("N")[..12],
                "Submitter", null, DataScope.Company, false,
                [PurchasingPermissions.RequisitionSubmit, PurchasingPermissions.RequisitionView]), Ct)).Id;
            await roles.AssignAsync(submitterId, company, new UserCompanyAssignment(null, null, false, true, [role]), Ct);
        }
        long id;
        await using (var db = FullContext(company))
        {
            var document = new Requisition { CompanyId = company, DocumentNo = $"PUR-SUB-HTTP-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = submitterId };
            document.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = itemId,
                RequiredDate = new DateOnly(2028, 3, 1), Quantity = 1.123456m });
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            id = document.Id;
        }
        var path = $"/api/v1/pur/requisitions/{id}/submit";
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync(path, new { version = 1 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(http, path, new { version = 1 }, noSubmitToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(http, path, new { version = 0 }, token, company)).StatusCode);
        await using (var db = FullContext(company))
        {
            var item = await db.Set<Item>().SingleAsync(x => x.Id == itemId, Ct);
            item.IsActive = false;
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await PostAsync(http, path, new { version = 1 }, token, company)).StatusCode);
        await using (var db = FullContext(company))
        {
            var item = await db.Set<Item>().SingleAsync(x => x.Id == itemId, Ct);
            item.IsActive = true;
            item.IsDeleted = true;
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostAsync(http, path, new { version = 1 }, token, company)).StatusCode);
        await using (var db = FullContext(company))
        {
            Assert.Equal(RequisitionStatus.Draft, (await db.Set<Requisition>().SingleAsync(x => x.Id == id, Ct)).Status);
            Assert.Empty(await db.Set<ApprovalInstance>().Where(x => x.DocumentType == "PUR.REQUISITION" && x.DocumentId == id).ToListAsync(Ct));
            var item = await db.Set<Item>().IgnoreQueryFilters().SingleAsync(x => x.Id == itemId, Ct);
            item.IsDeleted = false;
            await db.SaveChangesAsync(Ct);
        }
        var submitted = await PostAsync(http, path, new { version = 1 }, token, company);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        await using var check = FullContext(company);
        Assert.Equal(RequisitionStatus.Approved, (await check.Set<Requisition>().SingleAsync(x => x.Id == id, Ct)).Status);
        Assert.Single(await check.Set<ApprovalInstance>().Where(x => x.DocumentType == "PUR.REQUISITION" && x.DocumentId == id).ToListAsync(Ct));
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.inv_stock_movement WHERE company_id = {0}", company).SingleAsync(Ct));
        var auditCount = await check.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.sys_audit_log WHERE company_id = {0} AND table_name = 'pur_requisition' AND record_id = {1} AND [action] = 'UPDATE'",
            company, id.ToString(CultureInfo.InvariantCulture)).SingleAsync(Ct);
        Assert.True(auditCount > 0);

        long concurrentId;
        await using (var db = FullContext(company))
        {
            var document = new Requisition { CompanyId = company, DocumentNo = $"PUR-SUB-RACE-{company}",
                BusinessDate = new DateOnly(2028, 2, 29), RequesterId = submitterId };
            document.Lines.Add(new RequisitionLine { CompanyId = company, LineNo = 1, ItemId = itemId,
                RequiredDate = new DateOnly(2028, 3, 1), Quantity = 1m });
            db.Add(document);
            await db.SaveChangesAsync(Ct);
            concurrentId = document.Id;
        }
        var concurrentPath = $"/api/v1/pur/requisitions/{concurrentId}/submit";
        var responses = await Task.WhenAll(PostAsync(http, concurrentPath, new { version = 1 }, token, company),
            PostAsync(http, concurrentPath, new { version = 1 }, token, company));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity);
        await using var raceCheck = FullContext(company);
        Assert.Equal(RequisitionStatus.Approved,
            (await raceCheck.Set<Requisition>().SingleAsync(x => x.Id == concurrentId, Ct)).Status);
        Assert.Single(await raceCheck.Set<ApprovalInstance>()
            .Where(x => x.DocumentType == "PUR.REQUISITION" && x.DocumentId == concurrentId).ToListAsync(Ct));
    }

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

    private MyErpDbContext FullContext(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 19)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new RequisitionDbModel(), new RfqDbModel(), new QuotationDbModel(), new CurrencyDbModel(),
                new TaxDbModel(), new ItemDbModel(), new UomDbModel(), new SupplierDbModel(),
                new PaymentSetupDbModel(), new SystemOrganizationDbModel()], company);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string token, long companyId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string token, long companyId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
        return client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> MutationAsync(HttpClient client, HttpMethod method, string path,
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
