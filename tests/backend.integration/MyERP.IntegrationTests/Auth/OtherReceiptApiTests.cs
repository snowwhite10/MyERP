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
using MyERP.Modules.Inventory;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.IntegrationTests.Auth;

/// <summary>INV-REC-003、INV-REC-005：其他入庫 HTTP API 的權限、公司隔離、真實單號與 SYS 多級審批後過帳（myerp_test + 本機 Redis）。</summary>
public sealed class OtherReceiptApiTests(TestDatabase database) : IAsyncLifetime
{
    private const string Password = "Strong pass 2026!";
    private const string Path = "/api/v1/inv/other-receipts";
    private ApiFactory factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        factory = new ApiFactory(database);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task ReceiptsFollowPermissionsCompanyScopeAndSysApprovalBeforePosting()
    {
        var company = await CreateCompanyAsync();
        var otherCompany = await CreateCompanyAsync();
        var admin = await CreateRoleAsync(true);
        var viewerRole = await CreateRoleAsync(false, InventoryPermissions.OtherReceiptView);
        var approverRole = await CreateRoleAsync(false, SystemPermissions.ApprovalProcess, SystemPermissions.ApprovalView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (viewerId, viewerToken) = await CreateSignedInUserAsync();
        var (approverId, approverToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, admin);
        await AssignAsync(adminId, otherCompany, admin);
        await AssignAsync(viewerId, company, viewerRole);
        await AssignAsync(approverId, company, approverRole);
        var master = await SetupMasterAsync(company);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows", adminToken, company, new
        {
            documentType = OtherReceiptService.DocumentType, name = "Other receipt", allowSelfApproval = false,
            steps = new object[] { new { name = "Warehouse manager", approverType = "User", userId = approverId } },
        })).StatusCode);
        var body = new
        {
            businessDate = "2026-10-13", remark = "Sample stock",
            lines = new object[] { new { lineNo = 1, itemId = master.Item, warehouseId = master.Warehouse, locationId = master.Location,
                quantity = "5.500000", unitCost = "2.000000" } },
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, Path, null, company)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Post, Path, viewerToken, company, body);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("COMMON.PERMISSION.DENIED", await ErrorCodeAsync(denied));
        var invalid = await SendAsync(client, HttpMethod.Post, Path, adminToken, company, new { businessDate = "2026-10-13", lines = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await SendAsync(client, HttpMethod.Post, Path, adminToken, company, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = await ReadAsync(created);
        var id = receipt.GetProperty("id").GetInt64();
        Assert.Equal(("OR2026100001", "Draft", 11m), (receipt.GetProperty("documentNo").GetString(), receipt.GetProperty("status").GetString(),
            receipt.GetProperty("totalAmount").GetDecimal()));

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Path}?status=Draft&from=2026-10-01&to=2026-10-31&sort=-documentNo", viewerToken, company));
        Assert.Equal([id], list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, $"{Path}?sort=remark", viewerToken, company)).StatusCode);
        var foreign = await SendAsync(client, HttpMethod.Get, $"{Path}/{id}", adminToken, otherCompany);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(OtherReceiptService.NotFound, await ErrorCodeAsync(foreign));
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, Path, adminToken, otherCompany))).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"{Path}/{id}/submit", viewerToken, company, new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Delete, $"{Path}/{id}?version=1", viewerToken, company)).StatusCode);

        var updated = await SendAsync(client, HttpMethod.Put, $"{Path}/{id}", adminToken, company, new
        {
            businessDate = "2026-10-14", remark = "Sample stock", version = 1,
            lines = new object[] { new { lineNo = 1, itemId = master.Item, warehouseId = master.Warehouse, locationId = master.Location, quantity = 6m, unitCost = 2.5m } },
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Post, $"{Path}/{id}/submit", adminToken, company, new { version = 1 })).StatusCode);
        var submitted = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{Path}/{id}/submit", adminToken, company, new { version = 2 }));
        Assert.Equal(("PendingApproval", "Pending"), (submitted.GetProperty("status").GetString(),
            submitted.GetProperty("approval").GetProperty("status").GetString()));
        Assert.Equal(0m, await OnHandAsync(company, master));

        var task = Assert.Single((await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/approvals/tasks?documentType={OtherReceiptService.DocumentType}", approverToken, company))).GetProperty("items").EnumerateArray());
        Assert.Equal(("OR2026100001", 15m), (task.GetProperty("documentNo").GetString(), task.GetProperty("amount").GetDecimal()));
        var approved = await SendAsync(client, HttpMethod.Post, $"/api/v1/sys/approvals/{task.GetProperty("instanceId").GetInt64()}/approve",
            approverToken, company, new { comment = "Counted", version = 1 });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{Path}/{id}", viewerToken, company));
        Assert.Equal(("Posted", "Approved", "2026-10-14"), (detail.GetProperty("status").GetString(),
            detail.GetProperty("approval").GetProperty("status").GetString(), detail.GetProperty("businessDate").GetString()));
        var line = Assert.Single(detail.GetProperty("lines").EnumerateArray());
        Assert.Equal((6m, 2.5m), (line.GetProperty("quantity").GetDecimal(), line.GetProperty("unitCost").GetDecimal()));
        Assert.Equal(6m, await OnHandAsync(company, master));
        var version = detail.GetProperty("version").GetInt32();
        var delete = await SendAsync(client, HttpMethod.Delete, $"{Path}/{id}?version={version}", adminToken, company);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, delete.StatusCode);
        Assert.Equal(OtherReceiptService.NotDraft, await ErrorCodeAsync(delete));
    }

    [Fact]
    public async Task MissingNumberRuleRejectsCreationWithoutSavingTheReceipt()
    {
        var company = await CreateCompanyAsync(numberRule: false);
        var admin = await CreateRoleAsync(true);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, admin);
        var master = await SetupMasterAsync(company);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, HttpMethod.Post, Path, adminToken, company, new
        {
            businessDate = "2026-10-13",
            lines = new object[] { new { lineNo = 1, itemId = master.Item, warehouseId = master.Warehouse, quantity = 1m, unitCost = 0m } },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var db = Context(company);
        Assert.False(await db.Set<OtherReceipt>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AnyAsync(Ct));
    }

    [Fact]
    public async Task StockQueryEndpointsSeparateBalanceAndMovementPermissions()
    {
        var company = await CreateCompanyAsync();
        var otherCompany = await CreateCompanyAsync();
        var adminRole = await CreateRoleAsync(true);
        var balanceRole = await CreateRoleAsync(false, InventoryPermissions.InventoryView);
        var movementRole = await CreateRoleAsync(false, InventoryPermissions.MovementView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (balanceId, balanceToken) = await CreateSignedInUserAsync();
        var (movementId, movementToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, adminRole);
        await AssignAsync(adminId, otherCompany, adminRole);
        await AssignAsync(balanceId, company, balanceRole);
        await AssignAsync(movementId, company, movementRole);
        var master = await SetupMasterAsync(company);
        await using (var db = Context(company))
        {
            var posting = new StockPostingService(db,
                new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(new DateTime(2026, 10, 13, 18, 0, 0)));
            await posting.PostAsync(new("QUERY_HTTP", 1, new DateOnly(2026, 10, 13),
                [new(1, MyERP.Modules.Inventory.Contracts.StockDirection.In, master.Item, master.Warehouse, master.Location, 4m)]), Ct);
        }
        using var client = factory.CreateClient();
        const string balances = "/api/v1/inv/balances";
        const string movements = "/api/v1/inv/movements";
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, balances, null, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, movements, balanceToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, balances, movementToken, company)).StatusCode);
        var balance = await ReadAsync(await SendAsync(client, HttpMethod.Get, balances, balanceToken, company));
        Assert.Equal(4m, Assert.Single(balance.GetProperty("items").EnumerateArray()).GetProperty("quantity").GetDecimal());
        var movement = await ReadAsync(await SendAsync(client, HttpMethod.Get, movements, movementToken, company));
        Assert.Equal("QUERY_HTTP", Assert.Single(movement.GetProperty("items").EnumerateArray()).GetProperty("sourceType").GetString());
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, balances, adminToken, otherCompany)))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, balances + "?pageSize=0", balanceToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, movements + "?from=2026-10-14&to=2026-10-13", movementToken, company)).StatusCode);
    }

    [Fact]
    public async Task OpeningStockEndpointsPreviewPostOnceAndRespectPermissions()
    {
        const string opening = "/api/v1/inv/opening-stocks";
        var company = await CreateCompanyAsync(numberRule: false);
        var otherCompany = await CreateCompanyAsync(numberRule: false);
        var adminRole = await CreateRoleAsync(true);
        var viewRole = await CreateRoleAsync(false, InventoryPermissions.OpeningStockView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (viewerId, viewerToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, adminRole);
        await AssignAsync(adminId, otherCompany, adminRole);
        await AssignAsync(viewerId, company, viewRole);
        var master = await SetupMasterAsync(company);
        using var client = factory.CreateClient();
        var body = new
        {
            requestKey = Guid.NewGuid().ToString(), openingDate = "2026-10-01", remark = "Go-live",
            lines = new object[] { new { lineNo = 1, itemId = master.Item, warehouseId = master.Warehouse, locationId = master.Location,
                quantity = "7.250000", unitCost = "4.000000" } },
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, opening, null, company)).StatusCode);
        var denied = await SendAsync(client, HttpMethod.Post, opening, viewerToken, company, body);
        Assert.Equal((HttpStatusCode.Forbidden, "COMMON.PERMISSION.DENIED"), (denied.StatusCode, await ErrorCodeAsync(denied)));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, opening + "/preview", viewerToken, company, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, opening + "/preview", adminToken, company,
            new { requestKey = "k", openingDate = "2026-10-01", lines = Array.Empty<object>() })).StatusCode);

        var badLine = await ReadAsync(await SendAsync(client, HttpMethod.Post, opening + "/preview", adminToken, company, new
        {
            requestKey = "preview", openingDate = "2026-10-01",
            lines = new object[] { new { lineNo = 1, itemId = 999_999_999, warehouseId = master.Warehouse, quantity = 1, unitCost = 0 } },
        }));
        Assert.Equal(1, badLine.GetProperty("errorCount").GetInt32());
        var lineError = Assert.Single(Assert.Single(badLine.GetProperty("lines").EnumerateArray()).GetProperty("errors").EnumerateArray());
        Assert.Equal(("itemId", "MD.ITEM.NOT_FOUND"), (lineError.GetProperty("field").GetString(), lineError.GetProperty("code").GetString()));

        var preview = await ReadAsync(await SendAsync(client, HttpMethod.Post, opening + "/preview", adminToken, company, body));
        Assert.Equal((0, 29m), (preview.GetProperty("errorCount").GetInt32(), preview.GetProperty("totalAmount").GetDecimal()));
        Assert.Equal(0m, await OnHandAsync(company, master));

        var created = await SendAsync(client, HttpMethod.Post, opening, adminToken, company, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var batch = await ReadAsync(created);
        var id = batch.GetProperty("id").GetInt64();
        Assert.Equal((false, 29m), (batch.GetProperty("replayed").GetBoolean(), batch.GetProperty("totalAmount").GetDecimal()));
        Assert.Equal(7.25m, await OnHandAsync(company, master));

        var replay = await SendAsync(client, HttpMethod.Post, opening, adminToken, company, body);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await ReadAsync(replay);
        Assert.Equal((id, true), (replayed.GetProperty("id").GetInt64(), replayed.GetProperty("replayed").GetBoolean()));
        Assert.Equal(7.25m, await OnHandAsync(company, master));
        var conflict = await SendAsync(client, HttpMethod.Post, opening, adminToken, company, body with { remark = "Changed" });
        Assert.Equal((HttpStatusCode.Conflict, OpeningStockService.RequestConflict), (conflict.StatusCode, await ErrorCodeAsync(conflict)));
        var duplicate = await SendAsync(client, HttpMethod.Post, opening, adminToken, company, body with { requestKey = Guid.NewGuid().ToString() });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, OpeningStockService.HasErrors), (duplicate.StatusCode, await ErrorCodeAsync(duplicate)));

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{opening}?itemId={master.Item}&from=2026-10-01", viewerToken, company));
        Assert.Equal([id], list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()));
        var detail = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{opening}/{id}", viewerToken, company));
        Assert.Equal(7.25m, Assert.Single(detail.GetProperty("lines").EnumerateArray()).GetProperty("quantity").GetDecimal());
        var foreign = await SendAsync(client, HttpMethod.Get, $"{opening}/{id}", adminToken, otherCompany);
        Assert.Equal((HttpStatusCode.NotFound, OpeningStockService.NotFound), (foreign.StatusCode, await ErrorCodeAsync(foreign)));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, opening + "?itemId=0", viewerToken, company)).StatusCode);
    }

    /// <summary>INV-LOT-015：期初經 HTTP 帶批號、追溯 API 權限 INV.TRACKING.VIEW、公司隔離與 404。</summary>
    [Fact]
    public async Task TrackingEndpointsTraceLotsCapturedThroughHttpAndRespectPermissions()
    {
        const string tracking = "/api/v1/inv/tracking";
        var company = await CreateCompanyAsync(numberRule: false);
        var otherCompany = await CreateCompanyAsync(numberRule: false);
        var adminRole = await CreateRoleAsync(true);
        var traceRole = await CreateRoleAsync(false, InventoryPermissions.TrackingView);
        var balanceRole = await CreateRoleAsync(false, InventoryPermissions.InventoryView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (traceId, traceToken) = await CreateSignedInUserAsync();
        var (balanceId, balanceToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, adminRole);
        await AssignAsync(adminId, otherCompany, adminRole);
        await AssignAsync(traceId, company, traceRole);
        await AssignAsync(balanceId, company, balanceRole);
        var master = await SetupMasterAsync(company);
        long lotItem;
        await using (var db = Context(company))
        {
            var uom = await db.Set<MyERP.Modules.MasterData.Domain.Item>().Where(x => x.Id == master.Item).Select(x => x.BaseUomId).SingleAsync(Ct);
            lotItem = (await new ItemService(db).CreateAsync("LT" + company, "Lot item", null, uom, null, null,
                MyERP.Modules.MasterData.Contracts.ItemTrackingMode.Lot, Ct)).Id;
        }
        using var client = factory.CreateClient();

        var opening = await SendAsync(client, HttpMethod.Post, "/api/v1/inv/opening-stocks", adminToken, company, new
        {
            requestKey = Guid.NewGuid().ToString(), openingDate = "2026-10-01",
            lines = new object[] { new { lineNo = 1, itemId = lotItem, warehouseId = master.Warehouse, locationId = master.Location,
                quantity = "3", unitCost = "1", tracking = new object[]
                {
                    new { lotNo = "LOT-1", quantity = "2.5", manufactureDate = "2026-09-01", expiryDate = "2027-09-01" },
                    new { lotNo = "LOT-2", quantity = 0.5m },
                } } },
        });
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        var openingLine = Assert.Single((await ReadAsync(opening)).GetProperty("lines").EnumerateArray());
        Assert.Equal(["LOT-1", "LOT-2"], openingLine.GetProperty("tracking").EnumerateArray().Select(x => x.GetProperty("lotNo").GetString()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, tracking + "/lots", null, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, tracking + "/lots", balanceToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Get, tracking + "/serials", balanceToken, company)).StatusCode);
        var lots = (await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{tracking}/lots?itemId={lotItem}&inStockOnly=true", traceToken, company)))
            .GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([("LOT-1", 2.5m, "2027-09-01"), ("LOT-2", 0.5m, null)], lots.Select(x => (x.GetProperty("lotNo").GetString(),
            x.GetProperty("onHand").GetDecimal(), x.GetProperty("expiryDate").GetString())));
        var trace = await ReadAsync(await SendAsync(client, HttpMethod.Get, $"{tracking}/lots/{lots[0].GetProperty("id").GetInt64()}", traceToken, company));
        Assert.Equal(2.5m, Assert.Single(trace.GetProperty("locations").EnumerateArray()).GetProperty("quantity").GetDecimal());
        Assert.Equal(OpeningStockService.SourceType, Assert.Single(trace.GetProperty("history").EnumerateArray()).GetProperty("sourceType").GetString());
        Assert.Empty((await ReadAsync(await SendAsync(client, HttpMethod.Get, tracking + "/serials", traceToken, company))).GetProperty("items").EnumerateArray());
        var foreign = await SendAsync(client, HttpMethod.Get, $"{tracking}/lots/{lots[0].GetProperty("id").GetInt64()}", adminToken, otherCompany);
        Assert.Equal((HttpStatusCode.NotFound, TrackingQueryService.LotNotFound), (foreign.StatusCode, await ErrorCodeAsync(foreign)));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, tracking + "/serials?itemId=0", traceToken, company)).StatusCode);

        var missing = await SendAsync(client, HttpMethod.Post, "/api/v1/inv/opening-stocks", adminToken, company, new
        {
            requestKey = Guid.NewGuid().ToString(), openingDate = "2026-10-01",
            lines = new object[] { new { lineNo = 1, itemId = lotItem, warehouseId = master.Warehouse, quantity = 1, unitCost = 1 } },
        });
        Assert.Equal((HttpStatusCode.UnprocessableEntity, OpeningStockService.HasErrors), (missing.StatusCode, await ErrorCodeAsync(missing)));
    }

    [Fact]
    public async Task TransferSysApprovalRollsBackOnInsufficientStockAndCanRetry()
    {
        const string transfers = "/api/v1/inv/transfers";
        var company = await CreateCompanyAsync(numberRule: false);
        var adminRole = await CreateRoleAsync(true);
        var approverRole = await CreateRoleAsync(false, SystemPermissions.ApprovalProcess, SystemPermissions.ApprovalView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (approverId, approverToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, adminRole);
        await AssignAsync(approverId, company, approverRole);
        var master = await SetupMasterAsync(company);
        await using (var db = Context(company))
        {
            db.Add(new NumberRule { CompanyId = company, DocumentType = TransferService.DocumentType,
                Name = "Transfer", Prefix = "TR", DateFormat = NumberDateFormat.YearMonth,
                SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly });
            await db.SaveChangesAsync(Ct);
        }
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, HttpMethod.Post, "/api/v1/sys/approval-flows",
            adminToken, company, new
            {
                documentType = TransferService.DocumentType, name = "Transfer approval", allowSelfApproval = false,
                steps = new object[] { new { name = "Approver", approverType = "User", userId = approverId } },
            })).StatusCode);
        var created = await SendAsync(client, HttpMethod.Post, transfers, adminToken, company, new
        {
            businessDate = "2026-10-13",
            lines = new object[] { new { lineNo = 1, itemId = master.Item,
                sourceWarehouseId = master.Warehouse, sourceLocationId = master.Location,
                destinationWarehouseId = master.Warehouse, destinationLocationId = (long?)null, quantity = 1m } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync(created)).GetProperty("id").GetInt64();
        var submitted = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{transfers}/{id}/submit",
            adminToken, company, new { version = 1 }));
        Assert.Equal("PendingApproval", submitted.GetProperty("status").GetString());
        var task = Assert.Single((await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"/api/v1/sys/approvals/tasks?documentType={TransferService.DocumentType}", approverToken, company)))
            .GetProperty("items").EnumerateArray());
        var approvalId = task.GetProperty("instanceId").GetInt64();
        Assert.Equal(0m, task.GetProperty("amount").GetDecimal());
        var approvalPath = $"/api/v1/sys/approvals/{approvalId}/approve";
        var failed = await SendAsync(client, HttpMethod.Post, approvalPath, approverToken, company,
            new { comment = "Approved", version = 1 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        Assert.Equal("INV.STOCK.INSUFFICIENT", await ErrorCodeAsync(failed));
        Assert.Equal("PendingApproval", (await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"{transfers}/{id}", adminToken, company))).GetProperty("status").GetString());
        await using (var db = Context(company))
            Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == TransferService.SourceType, Ct));

        await using (var db = Context(company))
        {
            var posting = new StockPostingService(db,
                new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(new DateTime(2026, 10, 13, 18, 0, 0)));
            await posting.PostAsync(new("TRANSFER_APPROVAL_SEED", 1, new DateOnly(2026, 10, 13),
                [new(1, MyERP.Modules.Inventory.Contracts.StockDirection.In, master.Item, master.Warehouse, master.Location, 2m)]), Ct);
        }
        var approved = await SendAsync(client, HttpMethod.Post, approvalPath, approverToken, company,
            new { comment = "Approved", version = 1 });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("Posted", (await ReadAsync(await SendAsync(client, HttpMethod.Get,
            $"{transfers}/{id}", adminToken, company))).GetProperty("status").GetString());
        await using (var db = Context(company))
            Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == TransferService.SourceType, Ct));
    }

    [Fact]
    public async Task TransferApiEnforcesPermissionsAndCompanyScope()
    {
        const string transfers = "/api/v1/inv/transfers";
        var company = await CreateCompanyAsync(numberRule: false);
        var otherCompany = await CreateCompanyAsync(numberRule: false);
        var adminRole = await CreateRoleAsync(true);
        var viewRole = await CreateRoleAsync(false, InventoryPermissions.TransferView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (viewId, viewToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, adminRole);
        await AssignAsync(adminId, otherCompany, adminRole);
        await AssignAsync(viewId, company, viewRole);
        var master = await SetupMasterAsync(company);
        await using (var db = Context(company))
        {
            db.Add(new NumberRule { CompanyId = company, DocumentType = TransferService.DocumentType,
                Name = "Transfer", Prefix = "TR", DateFormat = NumberDateFormat.YearMonth,
                SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly });
            await db.SaveChangesAsync(Ct);
        }
        using var client = factory.CreateClient();
        var body = new { businessDate = "2026-10-13", lines = new object[]
        {
            new { lineNo = 1, itemId = master.Item, sourceWarehouseId = master.Warehouse,
                sourceLocationId = master.Location, destinationWarehouseId = master.Warehouse,
                destinationLocationId = (long?)null, quantity = "1.250000" },
        } };
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, transfers, null, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, transfers, viewToken, company, body)).StatusCode);
        var created = await SendAsync(client, HttpMethod.Post, transfers, adminToken, company, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadAsync(created)).GetProperty("id").GetInt64();
        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, transfers + "?status=Draft&sort=-documentNo", viewToken, company));
        Assert.Contains(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetInt64() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"{transfers}/{id}", adminToken, otherCompany)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"{transfers}/{id}/submit", viewToken, company, new { version = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, transfers + "?sort=remark", viewToken, company)).StatusCode);
        await using (var db = Context(company))
        {
            var posting = new StockPostingService(db,
                new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(new DateTime(2026, 10, 13, 18, 0, 0)));
            await posting.PostAsync(new("TRANSFER_HTTP_SEED", 1, new DateOnly(2026, 10, 13),
                [new(1, MyERP.Modules.Inventory.Contracts.StockDirection.In, master.Item, master.Warehouse, master.Location, 2m)]), Ct);
        }
        var submitted = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{transfers}/{id}/submit",
            adminToken, company, new { version = 1 }));
        Assert.Equal("Posted", submitted.GetProperty("status").GetString());
        await using (var db = Context(company))
            Assert.Equal(2, await db.Set<StockMovement>().CountAsync(x => x.SourceType == TransferService.SourceType, Ct));
    }

    /// <summary>INV-STK-009（010a1 範圍）：盤點 API 權限、公司隔離、建立／保存／重新快照／刪除與錯誤碼。</summary>
    [Fact]
    public async Task StocktakeApiEnforcesPermissionsCompanyScopeAndDraftLifecycle()
    {
        const string stocktakes = "/api/v1/inv/stocktakes";
        var company = await CreateCompanyAsync(numberRule: false);
        var otherCompany = await CreateCompanyAsync(numberRule: false);
        var adminRole = await CreateRoleAsync(true);
        var viewRole = await CreateRoleAsync(false, InventoryPermissions.StocktakeView);
        var (adminId, adminToken) = await CreateSignedInUserAsync();
        var (viewId, viewToken) = await CreateSignedInUserAsync();
        await AssignAsync(adminId, company, adminRole);
        await AssignAsync(adminId, otherCompany, adminRole);
        await AssignAsync(viewId, company, viewRole);
        var master = await SetupMasterAsync(company);
        await using (var db = Context(company))
        {
            var posting = new StockPostingService(db,
                new InventoryReferenceService(db, new InventoryReferenceGuard(db)), new FixedClock(new DateTime(2026, 10, 13, 18, 0, 0)));
            await posting.PostAsync(new("STOCKTAKE_HTTP_SEED", 1, new DateOnly(2026, 10, 13),
                [new(1, MyERP.Modules.Inventory.Contracts.StockDirection.In, master.Item, master.Warehouse, master.Location, 2m)]), Ct);
        }
        using var client = factory.CreateClient();
        var body = new { businessDate = "2026-10-14", remark = "Cycle count", scope = new { warehouseIds = new[] { master.Warehouse } } };

        var noRule = await SendAsync(client, HttpMethod.Post, stocktakes, adminToken, company, body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noRule.StatusCode);
        await using (var db = Context(company))
        {
            db.Add(new NumberRule { CompanyId = company, DocumentType = StocktakeService.DocumentType,
                Name = "Stocktake", Prefix = "ST", DateFormat = NumberDateFormat.YearMonth,
                SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly });
            await db.SaveChangesAsync(Ct);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, stocktakes, null, company)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, stocktakes, viewToken, company, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Post, stocktakes, adminToken, company,
            new { businessDate = "2026-10-14", scope = new { warehouseIds = Array.Empty<long>() } })).StatusCode);
        var created = await SendAsync(client, HttpMethod.Post, stocktakes, adminToken, company, body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await ReadAsync(created);
        var id = json.GetProperty("id").GetInt64();
        Assert.StartsWith("ST", json.GetProperty("documentNo").GetString(), StringComparison.Ordinal);
        var line = Assert.Single(json.GetProperty("lines").EnumerateArray().ToList());
        Assert.Equal(2m, line.GetProperty("bookQuantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null, line.GetProperty("countedQuantity").ValueKind);
        Assert.Equal("INV.STOCKTAKE.SCOPE_IN_USE", await ErrorCodeAsync(
            await SendAsync(client, HttpMethod.Post, stocktakes, adminToken, company, body)));

        var list = await ReadAsync(await SendAsync(client, HttpMethod.Get, stocktakes + "?status=Draft&sort=-documentNo", viewToken, company));
        var summary = Assert.Single(list.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal((1, 1, 0), (summary.GetProperty("warehouseCount").GetInt32(), summary.GetProperty("lineCount").GetInt32(),
            summary.GetProperty("countedLineCount").GetInt32()));
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, HttpMethod.Get, stocktakes + "?sort=remark", viewToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"{stocktakes}/{id}", adminToken, otherCompany)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, $"{stocktakes}/{id}", viewToken, company)).StatusCode);
        var countBody = new { businessDate = "2026-10-14", version = 1,
            lines = new object[] { new { lineId = line.GetProperty("id").GetInt64(), countedQuantity = "1.500000" } } };
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Put, $"{stocktakes}/{id}", viewToken, company, countBody)).StatusCode);
        var saved = await ReadAsync(await SendAsync(client, HttpMethod.Put, $"{stocktakes}/{id}", adminToken, company, countBody));
        Assert.Equal(1.5m, saved.GetProperty("lines")[0].GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(client, HttpMethod.Put, $"{stocktakes}/{id}", adminToken, company, countBody)).StatusCode);
        var version = saved.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Post, $"{stocktakes}/{id}/refresh-snapshot",
            viewToken, company, new { version })).StatusCode);
        var refreshed = await ReadAsync(await SendAsync(client, HttpMethod.Post, $"{stocktakes}/{id}/refresh-snapshot",
            adminToken, company, new { version }));
        Assert.Equal(1.5m, refreshed.GetProperty("lines")[0].GetProperty("countedQuantity").GetDecimal());
        Assert.False(refreshed.GetProperty("lines")[0].GetProperty("recountRequired").GetBoolean());
        version = refreshed.GetProperty("version").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(client, HttpMethod.Delete, $"{stocktakes}/{id}?version={version}",
            viewToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"{stocktakes}/{id}?version={version}",
            adminToken, company)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, $"{stocktakes}/{id}", viewToken, company)).StatusCode);
        await using (var db = Context(company))
            Assert.Equal(1, await db.Set<StockMovement>().CountAsync(Ct));
    }

    private sealed record Master(long Item, long Warehouse, long Location);

    private async Task<Master> SetupMasterAsync(long companyId)
    {
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var item = await new ItemService(db).CreateAsync("I" + companyId, "Item", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var location = await new StorageLocationService(db).CreateAsync(warehouse.Id, "A" + companyId, "A", Ct);
        return new Master(item.Id, warehouse.Id, location.Id);
    }

    private async Task<decimal> OnHandAsync(long companyId, Master master)
    {
        await using var db = Context(companyId);
        return await new StockBalanceQuery(db).GetOnHandAsync(master.Item, master.Warehouse, master.Location, Ct);
    }

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13, 18, 0, 0)), new FixedUser(7), company);
        return new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new SystemOrganizationDbModel(), new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(),
                new InventoryReferenceDbModel(), new StockMovementDbModel(), new StockTrackingDbModel(), new OtherReceiptDbModel()], company);
    }

    private async Task<long> CreateCompanyAsync(bool numberRule = true)
    {
        long id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            id = (await scope.ServiceProvider.GetRequiredService<OrganizationService>()
                .CreateCompanyAsync("R" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), "Company", Ct)).Id;
        }

        if (numberRule)
        {
            await using var db = Context(id);
            db.Add(new NumberRule
            {
                CompanyId = id, DocumentType = OtherReceiptService.DocumentType, Name = "Other receipt", Prefix = "OR",
                DateFormat = NumberDateFormat.YearMonth, SequenceLength = 4, ResetPeriod = NumberResetPeriod.Monthly,
            });
            await db.SaveChangesAsync(Ct);
        }

        return id;
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
        var username = "inv" + Guid.NewGuid().ToString("N")[..20];
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
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (companyId is { } id) request.Headers.Add("X-Company-Id", id.ToString(CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body);
        return client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        return body.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
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
