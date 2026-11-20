using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.IntegrationTests.Database;
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

/// <summary>SD-ORDER-EXPORT-HTTP-001–006: CSV export uses EXPORT permission, its data scope and request language.</summary>
public sealed class SalesOrderExportHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task ExportReturnsLocalisedScopedCsvAndRequiresExportPermission()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var organizations = services.GetRequiredService<OrganizationService>();
        var home = await organizations.CreateCompanyAsync(
            "EH" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Export home", Ct);
        var foreign = await organizations.CreateCompanyAsync(
            "EF" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Export foreign", Ct);
        var roles = services.GetRequiredService<RoleService>();
        var users = services.GetRequiredService<SystemUserService>();
        using var client = factory.CreateClient();

        async Task<string> TokenAsync(DataScope dataScope, long? branchId, params string[] permissions)
        {
            var role = await roles.CreateAsync(new RoleInput("X" + Guid.NewGuid().ToString("N")[..12],
                "Order export", null, dataScope, false, permissions), Ct);
            var username = "sal" + Guid.NewGuid().ToString("N")[..20];
            var user = await users.CreateAsync(username, Password, Ct);
            await roles.AssignAsync(user.Id, home.Id,
                new UserCompanyAssignment(branchId, null, true, true, [role.Id]), Ct);
            await roles.AssignAsync(user.Id, foreign.Id,
                new UserCompanyAssignment(null, null, false, true, [role.Id]), Ct);
            var captcha = await services.GetRequiredService<CaptchaService>().CreateAsync(Ct);
            using var login = await client.PostAsJsonAsync("/api/v1/sys/auth/login",
                new { username, password = Password, captchaId = captcha.Id, captchaCode = captcha.Code }, Ct);
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            return (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString()!;
        }

        long orderBranchId, otherBranchId;
        var documentNo = "EX" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        await using (var db = OrderContext(home.Id))
        {
            var orderBranch = new SystemBranch { CompanyId = home.Id,
                Code = "OB" + Guid.NewGuid().ToString("N")[..12], Name = "Order branch" };
            var otherBranch = new SystemBranch { CompanyId = home.Id,
                Code = "XB" + Guid.NewGuid().ToString("N")[..12], Name = "Other branch" };
            db.AddRange(orderBranch, otherBranch);
            await db.SaveChangesAsync(Ct);
            orderBranchId = orderBranch.Id;
            otherBranchId = otherBranch.Id;
            var order = new SalesOrder
            {
                CompanyId = home.Id, DocumentNo = documentNo, CustomerId = 21, CustomerCode = "C21",
                CustomerName = "=Acme, Ltd", BusinessDate = new DateOnly(2028, 2, 29), CurrencyCode = "USD",
                BaseCurrencyCode = "TWD", ExchangeRate = 31.5m, OwnerUserId = 1, BranchId = orderBranchId,
                Remark = "first\nsecond",
                OriginalNet = 20m, OriginalTax = 1m, OriginalGross = 21m,
                BaseNet = 630m, BaseTax = 31.5m, BaseGross = 661.5m
            };
            foreach (var lineNo in new[] { 2, 1 })
                order.Lines.Add(new SalesOrderLine
                {
                    CompanyId = home.Id, LineNo = lineNo, ItemId = 9, ItemCode = "I" + lineNo,
                    ItemName = "Item " + lineNo, Quantity = 2m, UnitPrice = 5m, DiscountRate = 0.125m,
                    TaxRateId = 1, TaxRatePercent = 5m, OriginalNet = 10m, OriginalTax = 0.5m,
                    OriginalGross = 10.5m, BaseNet = 315m, BaseTax = 15.75m, BaseGross = 330.75m
                });
            db.Add(order);
            await db.SaveChangesAsync(Ct);
            // Lines may only be written while Draft (database trigger); approve afterwards.
            order.Status = SalesOrderStatus.Approved;
            await db.SaveChangesAsync(Ct);
            // A soft-deleted Draft in the same company and branch must never be exported.
            var deleted = new SalesOrder
            {
                CompanyId = home.Id, DocumentNo = "DL" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CustomerId = 21, CustomerCode = "C21", CustomerName = "Deleted", BusinessDate = new DateOnly(2028, 2, 29),
                CurrencyCode = "TWD", BaseCurrencyCode = "TWD", ExchangeRate = 1m, OwnerUserId = 1,
                BranchId = orderBranchId, OriginalNet = 1m, OriginalGross = 1m, BaseNet = 1m, BaseGross = 1m
            };
            deleted.Lines.Add(new SalesOrderLine
            {
                CompanyId = home.Id, LineNo = 1, ItemId = 9, ItemCode = "ID", ItemName = "Deleted item",
                Quantity = 1m, UnitPrice = 1m, TaxRateId = 1, OriginalNet = 1m, OriginalGross = 1m,
                BaseNet = 1m, BaseGross = 1m
            });
            db.Add(deleted);
            await db.SaveChangesAsync(Ct);
            deleted.IsDeleted = true;
            deleted.DeletedAt = new DateTime(2028, 3, 1);
            await db.SaveChangesAsync(Ct);
        }

        async Task<HttpResponseMessage> ExportAsync(string token, long companyId, string query, string language)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sal/orders/export" + query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Company-Id", companyId.ToString(CultureInfo.InvariantCulture));
            request.Headers.AcceptLanguage.ParseAdd(language);
            return await client.SendAsync(request, Ct);
        }

        // VIEW alone does not grant export.
        var viewer = await TokenAsync(DataScope.Company, null, "SAL.ORDER.VIEW");
        using (var denied = await ExportAsync(viewer, home.Id, "", "en"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("COMMON.PERMISSION.DENIED", (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct))
                .GetProperty("errorCode").GetString());
        }

        // EXPORT alone is enough; English output with ordered lines and safe text cells.
        var exporter = await TokenAsync(DataScope.Company, null, "SAL.ORDER.EXPORT");
        using (var english = await ExportAsync(exporter, home.Id, "", "en"))
        {
            Assert.Equal(HttpStatusCode.OK, english.StatusCode);
            Assert.Equal("text/csv", english.Content.Headers.ContentType?.MediaType);
            Assert.Equal("2", english.Headers.GetValues("X-Export-Row-Count").Single());
            Assert.Contains("sales-orders.csv", english.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
            var bytes = await english.Content.ReadAsByteArrayAsync(Ct);
            Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
            var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.StartsWith("Order no.,Order date,Status,", lines[0], StringComparison.Ordinal);
            Assert.StartsWith($"{documentNo},2028-02-29,Approved,C21,\"'=Acme, Ltd\",USD,31.5", lines[1], StringComparison.Ordinal);
            Assert.Contains(",1,I1,Item 1,", lines[1], StringComparison.Ordinal);
            Assert.EndsWith(",\"first\nsecond\"", lines[1], StringComparison.Ordinal);
            Assert.Contains(",2,I2,Item 2,", lines[2], StringComparison.Ordinal);
            Assert.Equal(3, lines.Length);
        }

        // Simplified Chinese headers and status text after switching language.
        using (var hans = await ExportAsync(exporter, home.Id, "?status=Approved", "zh-Hans"))
        {
            Assert.Equal(HttpStatusCode.OK, hans.StatusCode);
            var text = await hans.Content.ReadAsStringAsync(Ct);
            Assert.Contains("单号,", text, StringComparison.Ordinal);
            Assert.Contains(",已批准,", text, StringComparison.Ordinal);
        }

        // Filters reuse list validation: a filter with no matches gives header only and zero count.
        using (var none = await ExportAsync(exporter, home.Id, "?status=Cancelled", "zh-Hant"))
        {
            Assert.Equal(HttpStatusCode.OK, none.StatusCode);
            Assert.Equal("0", none.Headers.GetValues("X-Export-Row-Count").Single());
            var text = (await none.Content.ReadAsStringAsync(Ct)).TrimStart('﻿');
            Assert.Single(text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
            Assert.StartsWith("單號,", text, StringComparison.Ordinal);
        }
        using (var numeric = await ExportAsync(exporter, home.Id, "?status=0", "en"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
        }
        using (var reversed = await ExportAsync(exporter, home.Id, "?from=2028-03-01&to=2028-02-01", "en"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        }

        // Another company never sees the home company's lines.
        using (var other = await ExportAsync(exporter, foreign.Id, "", "en"))
        {
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
            Assert.Equal("0", other.Headers.GetValues("X-Export-Row-Count").Single());
            Assert.DoesNotContain(documentNo, await other.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }

        // The EXPORT data scope decides rows: another branch sees none, the order branch sees both lines.
        var branchExporter = await TokenAsync(DataScope.Branch, otherBranchId, "SAL.ORDER.EXPORT");
        using (var hidden = await ExportAsync(branchExporter, home.Id, "", "en"))
        {
            Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
            Assert.Equal("0", hidden.Headers.GetValues("X-Export-Row-Count").Single());
        }
        var sameBranch = await TokenAsync(DataScope.Branch, orderBranchId, "SAL.ORDER.EXPORT");
        using (var visible = await ExportAsync(sameBranch, home.Id, "", "en"))
        {
            Assert.Equal("2", visible.Headers.GetValues("X-Export-Row-Count").Single());
        }
    }

    private MyErpDbContext OrderContext(long companyId) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString).Options,
        [new SalesOrderDbModel(), new SystemOrganizationDbModel()], new FixedCompany(companyId));

    private sealed class ApiFactory(TestDatabase database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
        }
    }
}
