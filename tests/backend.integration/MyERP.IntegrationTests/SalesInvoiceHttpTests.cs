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
using Moq;
using MyERP.IntegrationTests.Database;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Application;
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
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests;

/// <summary>
/// SD-INVOICE-HTTP-001～004 (WU-SD-007a3b, ADR-179): invoice ISSUE and detail over the real API, SYS authorization,
/// the real FI/AR/GL posting service, the FI credit lock and SQL. Only the FI account mapping (fixed 1131/4101/2171)
/// and an optional FI fault/receipt tamper are test doubles.
/// </summary>
public sealed class SalesInvoiceHttpTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Route = "/api/v1/sal/invoices";
    private const long Customer = 21;
    private static readonly DateOnly Day = new(2026, 11, 6);
    private static readonly DateTime Now = new(2026, 11, 6, 9, 30, 0);
    private readonly Switches switches = new();
    private ApiFactory factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database, switches);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task RoutesRequireAuthenticationAndAppearInOpenApi()
    {
        // SD-INVOICE-HTTP-001: anonymous 401; ISSUE documents 201/200/409/422/500, the detail 404 and the list
        // (WU-SD-007a3b2) its filters and 400/403.
        using var client = factory.CreateClient();
        using (var post = await client.PostAsJsonAsync(Route, Body("A-1", (1, 1L, 1m)), Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        using (var get = await client.GetAsync(Route + "/1", Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        using (var anonymousList = await client.GetAsync(Route, Ct))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);

        using var openApi = await client.GetAsync("/openapi/v1.json", Ct);
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(Ct));
        var paths = document.RootElement.GetProperty("paths");
        var responses = paths.GetProperty(Route).GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "200", "201", "400", "403", "404", "409", "422", "500" })
            Assert.True(responses.TryGetProperty(status, out _), status);
        var list = paths.GetProperty(Route).GetProperty("get");
        foreach (var status in new[] { "200", "400", "401", "403" })
            Assert.True(list.GetProperty("responses").TryGetProperty(status, out _), "list " + status);
        var parameters = list.GetProperty("parameters").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "keyword", "from", "to", "orderId", "customerId", "sort", "page", "pageSize" })
            Assert.Contains(name, parameters);
        Assert.True(paths.GetProperty(Route + "/{id}").GetProperty("get").GetProperty("responses")
            .TryGetProperty("404", out _));
    }

    [Fact]
    public async Task IssuerCreatesAndReplaysWhileViewerReadsTheSameDetail()
    {
        // SD-INVOICE-HTTP-002: 201 + Location with FI IDs equal to SQL AR/GL; identical replay 200 posts nothing;
        // changed payload 409; ISSUE needs no VIEW and VIEW cannot ISSUE.
        var s = await SeedAsync("HA");
        var issuer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.ISSUE");
        var viewer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, issuer);
        var read = await LoginAsync(client, viewer);

        using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
            Body("K-1", (1, s.DeliveryLineA, 1m)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"{Route}/{id}", created.Headers.Location!.OriginalString);
        Assert.Equal("HA001", body.GetProperty("documentNo").GetString());
        Assert.Equal(nameof(SalesInvoiceStatus.Posted), body.GetProperty("status").GetString());
        Assert.Equal(s.OrderNo, body.GetProperty("orderDocumentNo").GetString());
        Assert.Equal(("C21", "Invoice customer"), (body.GetProperty("customerCode").GetString(),
            body.GetProperty("customerName").GetString()));
        Assert.Equal(("USD", "TWD"), (body.GetProperty("currencyCode").GetString(),
            body.GetProperty("baseCurrencyCode").GetString()));
        Assert.Equal((1033m, 52m, 1085m), (body.GetProperty("baseNet").GetDecimal(),
            body.GetProperty("baseTax").GetDecimal(), body.GetProperty("baseGross").GetDecimal()));
        var line = Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal((s.DeliveryLineA, s.DeliveryNo, 1, "I31"), (line.GetProperty("deliveryLineId").GetInt64(),
            line.GetProperty("deliveryDocumentNo").GetString(), line.GetProperty("orderLineNo").GetInt32(),
            line.GetProperty("itemCode").GetString()));
        Assert.Equal(1m, line.GetProperty("quantity").GetDecimal());

        await using (var verify = s.Raw())
        {
            var fi = await verify.Database.SqlQuery<FiRow>($"""
                SELECT s.id AS source_id, r.id AS receivable_id, s.gl_journal_id AS journal_id, r.open_base_amount
                FROM dbo.ar_sales_source s
                JOIN dbo.ar_posted_receivable r ON r.company_id = s.company_id AND r.source_id = s.source_id
                     AND r.source_type = 'Invoice'
                WHERE s.company_id = {s.CompanyId} AND s.source_type = 'Invoice' AND s.source_id = {id}
                """).SingleAsync(Ct);
            Assert.Equal((fi.SourceId, fi.ReceivableId, fi.JournalId, 1085m),
                (body.GetProperty("fiSalesSourceId").GetInt64(), body.GetProperty("fiReceivableId").GetInt64(),
                    body.GetProperty("fiJournalId").GetInt64(), fi.OpenBaseAmount));
            var journal = await verify.Database.SqlQuery<JournalRow>($"""
                SELECT account_code, debit, credit FROM dbo.gl_journal_line
                WHERE company_id = {s.CompanyId} AND journal_id = {fi.JournalId} ORDER BY account_code
                """).ToListAsync(Ct);
            Assert.Equal([new("1131", 1085m, 0m), new("2171", 0m, 52m), new("4101", 0m, 1033m)], journal);
        }
        var counts = await FiCountsAsync(s);

        using (var replay = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                   Body("K-1", (1, s.DeliveryLineA, 1m))))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Null(replay.Headers.Location);
            var again = await replay.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal((id, body.GetProperty("fiJournalId").GetInt64()),
                (again.GetProperty("id").GetInt64(), again.GetProperty("fiJournalId").GetInt64()));
        }
        using (var changed = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                   Body("K-1", (1, s.DeliveryLineA, 2m))))
        {
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            Assert.Equal("SAL.INVOICE.REQUEST_CONFLICT", await ErrorCodeAsync(changed));
        }
        Assert.Equal(counts, await FiCountsAsync(s));

        using (var noView = await SendAsync(client, write, s.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
            Assert.Equal(HttpStatusCode.Forbidden, noView.StatusCode);
        using (var noIssue = await SendAsync(client, read, s.CompanyId, HttpMethod.Post, Route,
                   Body("K-2", (1, s.DeliveryLineA, 1m))))
            Assert.Equal(HttpStatusCode.Forbidden, noIssue.StatusCode);
        using (var detail = await SendAsync(client, read, s.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var viewed = await detail.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(body.GetRawText(), viewed.GetRawText());
        }
        using (var missing = await SendAsync(client, read, s.CompanyId, HttpMethod.Get, $"{Route}/{id + 100000}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("SAL.INVOICE.NOT_FOUND", await ErrorCodeAsync(missing));
        }
    }

    [Fact]
    public async Task RefusalsAndFiFailuresReturnNamedProblemDetailsAndSaveNothing()
    {
        // SD-INVOICE-HTTP-003: 400 fields, 404/422 SD codes with lineNo, FI 422 code kept, FI fault and receipt
        // mismatch 500 with errorCode and no detail; nothing survives any of them and the same key then succeeds.
        var s = await SeedAsync("HB");
        var issuer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.ISSUE");
        using var client = factory.CreateClient();
        var token = await LoginAsync(client, issuer);

        using (var invalid = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
                   new { requestKey = "bad key!", businessDate = "2026-11-06", lines = Array.Empty<object>() }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.TryGetProperty("requestKey", out _));
            Assert.True(errors.TryGetProperty("lines", out _));
        }
        using (var unknown = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
                   Body("U-1", (1, s.DeliveryLineA + 100000, 1m))))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal("SAL.INVOICE.SOURCE_NOT_FOUND", await ErrorCodeAsync(unknown));
        }
        using (var over = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
                   Body("U-2", (3, s.DeliveryLineA, 1.5m))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, over.StatusCode);
            var problem = await over.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("SAL.INVOICE.BALANCE_EXCEEDED", problem.GetProperty("errorCode").GetString());
            Assert.Equal(3, problem.GetProperty("lineNo").GetInt32());
        }

        switches.MapMissing = true;
        using (var unmapped = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
                   Body("U-3", (1, s.DeliveryLineA, 1m))))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unmapped.StatusCode);
            Assert.Equal(SalesPostingErrors.MappingMissing, await ErrorCodeAsync(unmapped));
        }
        switches.MapMissing = false;

        switches.Fault = true;
        using (var fault = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
                   Body("U-3", (1, s.DeliveryLineA, 1m))))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, fault.StatusCode);
            var problem = await fault.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(SalesPostingErrors.WriteFailed, problem.GetProperty("errorCode").GetString());
            Assert.False(problem.TryGetProperty("detail", out var detail) && detail.ValueKind != JsonValueKind.Null);
            Assert.True(problem.TryGetProperty("traceId", out _));
        }
        switches.Fault = false;

        switches.TamperGross = true;
        using (var mismatch = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
                   Body("U-3", (1, s.DeliveryLineA, 1m))))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, mismatch.StatusCode);
            Assert.Equal("SAL.INVOICE.FI_RECEIPT_MISMATCH", await ErrorCodeAsync(mismatch));
        }
        switches.TamperGross = false;

        Assert.Equal((0, 0, 0), await FiCountsAsync(s));
        await using (var verify = s.Raw())
        {
            Assert.False(await verify.Set<SalesInvoice>().AnyAsync(Ct));
            Assert.False(await verify.Set<NumberSequence>().AnyAsync(Ct));
        }

        using var issued = await SendAsync(client, token, s.CompanyId, HttpMethod.Post, Route,
            Body("U-3", (1, s.DeliveryLineA, 1m)));
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.Equal("HB001", (await issued.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("documentNo").GetString());
    }

    [Fact]
    public async Task IssueAndViewScopesFollowTheSourceOrderAndCompaniesAreIsolated()
    {
        // SD-INVOICE-HTTP-004: Self scopes cannot issue or read invoices of another owner's order; another company
        // cannot discover the invoice.
        var s = await SeedAsync("HC");
        var selfIssuer = await UserAsync(s.CompanyId, DataScope.Self, "SAL.INVOICE.ISSUE");
        var issuer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.ISSUE");
        var selfViewer = await UserAsync(s.CompanyId, DataScope.Self, "SAL.INVOICE.VIEW");
        var other = await SeedAsync("HD");
        var foreignViewer = await UserAsync(other.CompanyId, DataScope.Company, "SAL.INVOICE.VIEW");
        using var client = factory.CreateClient();

        var self = await LoginAsync(client, selfIssuer);
        using (var hidden = await SendAsync(client, self, s.CompanyId, HttpMethod.Post, Route,
                   Body("S-1", (1, s.DeliveryLineA, 1m))))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal("SAL.INVOICE.SOURCE_NOT_FOUND", await ErrorCodeAsync(hidden));
        }
        Assert.Equal((0, 0, 0), await FiCountsAsync(s));

        var write = await LoginAsync(client, issuer);
        using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
            Body("S-1", (1, s.DeliveryLineA, 1m)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetInt64();

        var selfRead = await LoginAsync(client, selfViewer);
        using (var hiddenDetail = await SendAsync(client, selfRead, s.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
            Assert.Equal("SAL.INVOICE.NOT_FOUND", await ErrorCodeAsync(hiddenDetail));
        }
        var foreign = await LoginAsync(client, foreignViewer);
        using (var foreignDetail = await SendAsync(client, foreign, other.CompanyId, HttpMethod.Get, $"{Route}/{id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, foreignDetail.StatusCode);
            Assert.Equal("SAL.INVOICE.NOT_FOUND", await ErrorCodeAsync(foreignDetail));
        }
    }

    [Fact]
    public async Task ListFiltersSortsAndPagesPostedInvoicesStably()
    {
        // SD-INVOICE-HTTP-005 (WU-SD-007a3b2): default -businessDate with ID tie-break; documentNo both ways; keyword
        // on invoice or order number; from/to inclusive; orderId; customerId; pages do not overlap; rows equal the
        // detail; 400 for bad filters; ISSUE alone cannot list.
        var s = await SeedAsync("HE");
        var b = await AddOrderAsync(s.CompanyId, 22, "Second customer");
        var issuer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.ISSUE");
        var viewer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.VIEW");
        using var client = factory.CreateClient();
        var write = await LoginAsync(client, issuer);
        var read = await LoginAsync(client, viewer);

        var ids = new Dictionary<string, long>();
        foreach (var (key, date, line, qty) in new[]
                 {
                     ("L-1", "2026-11-06", s.DeliveryLineA, 0.25m), ("L-2", "2026-11-07", s.DeliveryLineA, 0.5m),
                     ("L-3", "2026-11-07", b.DeliveryLineId, 1m)
                 })
        {
            using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                DatedBody(key, date, (1, line, qty)));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
            ids[body.GetProperty("documentNo").GetString()!] = body.GetProperty("id").GetInt64();
        }
        Assert.Equal(["HE001", "HE002", "HE003"], ids.Keys.Order());

        using (var forbidden = await SendAsync(client, write, s.CompanyId, HttpMethod.Get, Route))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var all = await ListAsync(client, read, s.CompanyId, "");
        Assert.Equal(3, all.GetProperty("totalCount").GetInt64());
        Assert.Equal(["HE003", "HE002", "HE001"], Numbers(all));

        var first = all.GetProperty("items")[0];
        using (var detailResponse = await SendAsync(client, read, s.CompanyId, HttpMethod.Get,
                   $"{Route}/{ids["HE003"]}"))
        {
            var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
            foreach (var field in new[]
                     {
                         "id", "documentNo", "orderId", "orderDocumentNo", "customerId", "customerName",
                         "businessDate", "currencyCode", "baseCurrencyCode", "originalGross", "baseGross", "postedAt",
                         "fiJournalId", "version"
                     })
                Assert.Equal(detail.GetProperty(field).GetRawText(), first.GetProperty(field).GetRawText());
            Assert.Equal(detail.GetProperty("lines").GetArrayLength(), first.GetProperty("lineCount").GetInt32());
        }
        Assert.Equal((b.OrderNo, 22L, "Second customer"), (first.GetProperty("orderDocumentNo").GetString(),
            first.GetProperty("customerId").GetInt64(), first.GetProperty("customerName").GetString()));
        Assert.True(first.GetProperty("fiJournalId").GetInt64() > 0);

        Assert.Equal(["HE001", "HE002", "HE003"], Numbers(await ListAsync(client, read, s.CompanyId, "sort=documentNo")));
        Assert.Equal(["HE003", "HE002", "HE001"], Numbers(await ListAsync(client, read, s.CompanyId, "sort=-documentNo")));
        Assert.Equal(["HE001", "HE002", "HE003"], Numbers(await ListAsync(client, read, s.CompanyId, "sort=businessDate")));

        var page1 = await ListAsync(client, read, s.CompanyId, "pageSize=2&page=1");
        var page2 = await ListAsync(client, read, s.CompanyId, "pageSize=2&page=2");
        Assert.Equal((3L, 2L), (page1.GetProperty("totalCount").GetInt64(), page1.GetProperty("totalPages").GetInt64()));
        Assert.Equal(["HE003", "HE002", "HE001"], [.. Numbers(page1), .. Numbers(page2)]);

        Assert.Equal(["HE003"], Numbers(await ListAsync(client, read, s.CompanyId,
            "keyword=" + Uri.EscapeDataString(" " + b.OrderNo + " "))));
        Assert.Equal(["HE002"], Numbers(await ListAsync(client, read, s.CompanyId, "keyword=E002")));
        Assert.Equal(["HE003", "HE002"], Numbers(await ListAsync(client, read, s.CompanyId, "from=2026-11-07")));
        Assert.Equal(["HE001"], Numbers(await ListAsync(client, read, s.CompanyId, "to=2026-11-06")));
        Assert.Equal(["HE003", "HE002", "HE001"], Numbers(await ListAsync(client, read, s.CompanyId,
            "from=2026-11-06&to=2026-11-07")));
        var orderA = (await ListAsync(client, read, s.CompanyId, "keyword=HE001")).GetProperty("items")[0]
            .GetProperty("orderId").GetInt64();
        Assert.Equal(["HE002", "HE001"], Numbers(await ListAsync(client, read, s.CompanyId, $"orderId={orderA}")));
        Assert.Equal(["HE003"], Numbers(await ListAsync(client, read, s.CompanyId, "customerId=22")));
        Assert.Empty(Numbers(await ListAsync(client, read, s.CompanyId, "customerId=23")));

        foreach (var (query, field) in new[]
                 {
                     ("from=2026-11-08&to=2026-11-07", "to"), ("orderId=0", "orderId"), ("customerId=-1", "customerId"),
                     ("sort=status", "sort"), ("sort=documentNo,businessDate", "sort"), ("pageSize=201", "pageSize")
                 })
        {
            using var bad = await SendAsync(client, read, s.CompanyId, HttpMethod.Get, $"{Route}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors");
            Assert.True(errors.EnumerateObject().Any(x => x.Name.Equals(field, StringComparison.OrdinalIgnoreCase)),
                query + " → " + errors.GetRawText());
        }
    }

    [Fact]
    public async Task ListAppliesTheSelfScopeToTheSourceOrderAndIsolatesCompanies()
    {
        // SD-INVOICE-HTTP-006 (WU-SD-007a3b2): a Self viewer lists and counts only invoices of orders it owns; another
        // company's viewer sees only its own invoices.
        var s = await SeedAsync("HF");
        var selfViewer = await UserWithIdAsync(s.CompanyId, DataScope.Self, "SAL.INVOICE.VIEW");
        var own = await AddOrderAsync(s.CompanyId, ownerUserId: selfViewer.Id);
        var issuer = await UserAsync(s.CompanyId, DataScope.Company, "SAL.INVOICE.ISSUE");
        var other = await SeedAsync("HG");
        var otherIssuer = await UserAsync(other.CompanyId, DataScope.Company, "SAL.INVOICE.ISSUE");
        var foreignViewer = await UserAsync(other.CompanyId, DataScope.Company, "SAL.INVOICE.VIEW");
        using var client = factory.CreateClient();

        var write = await LoginAsync(client, issuer);
        foreach (var (key, line) in new[] { ("P-1", s.DeliveryLineA), ("P-2", own.DeliveryLineId) })
        {
            using var created = await SendAsync(client, write, s.CompanyId, HttpMethod.Post, Route,
                Body(key, (1, line, 1m)));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        var otherWrite = await LoginAsync(client, otherIssuer);
        using (var created = await SendAsync(client, otherWrite, other.CompanyId, HttpMethod.Post, Route,
                   Body("P-1", (1, other.DeliveryLineA, 1m))))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var self = await ListAsync(client, await LoginAsync(client, selfViewer.Username), s.CompanyId, "");
        Assert.Equal(1, self.GetProperty("totalCount").GetInt64());
        Assert.Equal(["HF002"], Numbers(self));
        Assert.Equal(own.OrderNo, self.GetProperty("items")[0].GetProperty("orderDocumentNo").GetString());

        var foreign = await ListAsync(client, await LoginAsync(client, foreignViewer), other.CompanyId, "keyword=H");
        Assert.Equal(1, foreign.GetProperty("totalCount").GetInt64());
        Assert.Equal(["HG001"], Numbers(foreign));
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<JsonElement> ListAsync(HttpClient client, string token, long companyId, string query)
    {
        using var response = await SendAsync(client, token, companyId, HttpMethod.Get,
            query.Length == 0 ? Route : $"{Route}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static List<string> Numbers(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("documentNo").GetString()!)];

    private sealed record FiRow(long SourceId, long ReceivableId, long JournalId, decimal OpenBaseAmount);

    private sealed record JournalRow(string AccountCode, decimal Debit, decimal Credit);

    private sealed record Seeded(long CompanyId, string OrderNo, long DeliveryLineA, string DeliveryNo,
        Func<MyErpDbContext> Raw);

    /// <summary>Test switches shared with the API host for one test.</summary>
    private sealed class Switches
    {
        public volatile bool MapMissing;
        public volatile bool Fault;
        public volatile bool TamperGross;
    }

    private sealed class SwitchableMap(Switches switches) : ISalesInvoiceAccountMapSource, IGlPostingMappingVersionSource
    {
        public string SourceModule => "SD";
        public string SourceType => "Invoice";

        public Task<SalesInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken) =>
            Task.FromResult<SalesInvoiceAccountMap?>(switches.MapMissing ? null : new(3, "1131", "4101", "2171"));

        public Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
            Task.FromResult(switches.MapMissing ? 0 : 3);
    }

    private sealed class SwitchablePosting(SalesInvoicePostingService real, Switches switches, MyErpDbContext db)
        : ISalesInvoicePostingService
    {
        public async Task<SalesInvoicePostingResult> PostInCurrentTransactionAsync(SalesInvoicePostingCommand? command,
            CancellationToken cancellationToken = default)
        {
            var result = await real.PostInCurrentTransactionAsync(command, cancellationToken);
            if (switches.Fault)
            {
                // FI's Fault contract: the caller's whole transaction has already been rolled back.
                await db.Database.RollbackTransactionAsync(CancellationToken.None);
                return new(SalesPostingOutcomeKind.Fault, false, 500, SalesPostingErrors.WriteFailed);
            }
            return switches.TamperGross && result.Receipt is { } receipt
                ? result with { Receipt = receipt with { GrossAmount = receipt.GrossAmount + 1m } }
                : result;
        }
    }

    private static object Body(string key, params (int LineNo, long DeliveryLineId, decimal Quantity)[] lines) =>
        DatedBody(key, "2026-11-06", lines);

    private static object DatedBody(string key, string businessDate,
        params (int LineNo, long DeliveryLineId, decimal Quantity)[] lines) => new
    {
        requestKey = key, businessDate,
        lines = lines.Select(x => new { lineNo = x.LineNo, deliveryLineId = x.DeliveryLineId, quantity = x.Quantity })
    };

    private static async Task<(int Sources, int Receivables, int Journals)> FiCountsAsync(Seeded s)
    {
        await using var db = s.Raw();
        var sources = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_sales_source WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var receivables = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.ar_posted_receivable WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        var journals = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM dbo.gl_journal WHERE company_id = {s.CompanyId}").SingleAsync(Ct);
        return (sources, receivables, journals);
    }

    /// <summary>
    /// A company with the SAL.INVOICE number rule, TWD base currency, accounts 1131/2171/4101, an open November 2026
    /// period and an Approved USD order (owner 987654, never a test user, rate 31) with a Posted delivery of 1 unit of line A
    /// (line A: 3 units, net 100.00 + tax 5.00, base 3,100 + 155).
    /// </summary>
    private async Task<Seeded> SeedAsync(string prefix)
    {
        long companyId;
        await using (var scope = factory.Services.CreateAsyncScope())
            companyId = (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateCompanyAsync(
                prefix + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Invoice HTTP", Ct)).Id;

        await using (var provider = FinanceSetupProvider(companyId))
        await using (var scope = provider.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<CurrencyService>().CreateAsync("TWD", "New Taiwan Dollar", "NT$", 2, true, Ct);
            var accounts = sp.GetRequiredService<AccountService>();
            var assets = await accounts.CreateAsync("1", "Assets", null, AccountType.Asset, null, Ct);
            await accounts.CreateAsync("1131", "Receivable", assets.Id, null, null, Ct);
            var liabilities = await accounts.CreateAsync("2", "Liabilities", null, AccountType.Liability, null, Ct);
            await accounts.CreateAsync("2171", "Output tax", liabilities.Id, null, null, Ct);
            var revenue = await accounts.CreateAsync("4", "Revenue", null, AccountType.Revenue, null, Ct);
            await accounts.CreateAsync("4101", "Sales", revenue.Id, null, null, Ct);
            await sp.GetRequiredService<GlPeriodWriteService>().CreateAsync(companyId,
                new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), true, Ct);
        }

        await using (var db = Raw(companyId))
        {
            db.Add(new NumberRule { CompanyId = companyId, DocumentType = "SAL.INVOICE", Name = "Sales invoice",
                Prefix = prefix, DateFormat = NumberDateFormat.None, ResetPeriod = NumberResetPeriod.Never,
                SequenceLength = 3 });
            await db.SaveChangesAsync(Ct);
        }
        var source = await AddOrderAsync(companyId);
        return new Seeded(companyId, source.OrderNo, source.DeliveryLineId, source.DeliveryNo, () => Raw(companyId));
    }

    private sealed record OrderSource(long OrderId, string OrderNo, long DeliveryLineId, string DeliveryNo);

    /// <summary>
    /// Adds an Approved USD order (rate 31, line A: 3 units, net 100.00 + tax 5.00, base 3,100 + 155) with a Posted
    /// delivery of 1 unit of line A to a company created by <see cref="SeedAsync"/>.
    /// </summary>
    private async Task<OrderSource> AddOrderAsync(long companyId, long customerId = Customer,
        string customerName = "Invoice customer", long ownerUserId = 987_654)
    {
        await using var db = Raw(companyId);
        var order = new SalesOrder { CompanyId = companyId, OwnerUserId = ownerUserId,
            DocumentNo = "SO" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), CustomerId = customerId,
            CustomerCode = "C" + customerId.ToString(CultureInfo.InvariantCulture), CustomerName = customerName,
            BusinessDate = Day, CurrencyCode = "USD",
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
            ItemId = 31, WarehouseId = 51, Quantity = 1m });
        db.Add(delivery);
        await db.SaveChangesAsync(Ct);
        return new OrderSource(order.Id, order.DocumentNo, delivery.Lines[0].Id, delivery.DocumentNo);
    }

    private ServiceProvider FinanceSetupProvider(long companyId)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        new MasterDataModule().AddServices(services, configuration);
        new FinanceModule().AddServices(services, configuration);
        var company = new FixedCompany(companyId);
        services.AddSingleton<ICurrentCompany>(company);
        services.AddSingleton<ICurrentUser>(new FixedUser(7));
        services.AddSingleton<IClock>(new FixedClock(Now));
        var auth = new Mock<ICurrentAuthorization>();
        auth.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(7, companyId, null, null, true, new Dictionary<string, DataScope>()));
        services.AddSingleton(auth.Object);
        services.AddScoped(sp => new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(sp.GetRequiredService<IClock>(),
                    sp.GetRequiredService<ICurrentUser>(), company))).Options,
            sp.GetServices<IModuleDbModel>(), company));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
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
        var role = await roles.CreateAsync(new RoleInput("V" + Guid.NewGuid().ToString("N")[..12],
            "Invoice role", null, scope, false, permissions), Ct);
        var username = "inv" + Guid.NewGuid().ToString("N")[..20];
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

    private sealed class ApiFactory(TestDatabase database, Switches switches) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            database.UseTestDatabase(builder);
            builder.UseSetting("App:Logging:FileEnabled", "false");
            builder.ConfigureTestServices(services =>
            {
                foreach (var real in services.Where(x => x.ImplementationType == typeof(SalesInvoiceAccountMapStore)
                             || x.ServiceType == typeof(ISalesInvoicePostingService)).ToList())
                    services.Remove(real);
                services.AddSingleton(switches);
                services.AddScoped<ISalesInvoiceAccountMapSource, SwitchableMap>();
                services.AddScoped<IGlPostingMappingVersionSource, SwitchableMap>();
                services.AddScoped(sp => ActivatorUtilities.CreateInstance<SalesInvoicePostingService>(sp));
                services.AddScoped<ISalesInvoicePostingService, SwitchablePosting>();
            });
        }
    }
}
