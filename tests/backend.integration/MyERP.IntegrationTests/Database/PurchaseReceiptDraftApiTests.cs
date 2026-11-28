using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Errors;

namespace MyERP.IntegrationTests.Database;

public sealed class PurchaseReceiptDraftApiTests(TestDatabase database)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAndEditSaveSnapshotWithoutReservingOrPosting()
    {
        var company = TestCompanies.Next();
        var source = await SeedAsync(company);
        await using var db = Context(company);
        var service = Service(db, company);
        var created = await service.CreateAsync(new PurchaseReceiptSaveInput(source.OrderId,
            new DateOnly(2028, 2, 29), [new(1, source.LineId, 2m, 91)]), Ct);
        Assert.Equal("Draft", created.Status);
        Assert.Equal("PR-001", created.DocumentNo);
        Assert.Equal(4m, created.Lines[0].UnitCost);
        Assert.Equal(0m, await db.Set<PurchaseOrderSourceLine>().Where(x => x.Id == source.LineId)
            .Select(x => x.PendingReceiptReserved).SingleAsync(Ct));

        var edited = await service.UpdateAsync(created.Id, created.Version,
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 3, 1),
                [new(1, source.LineId, 3m, 92)]), Ct);
        Assert.Equal(3m, edited.Lines[0].Quantity);
        Assert.Equal(92, edited.Lines[0].WarehouseId);
        Assert.Equal(created.DocumentNo, edited.DocumentNo);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(created.Id, created.Version,
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 3, 1),
                [new(1, source.LineId, 1m, 91)]), Ct));
        Assert.Equal(0m, await db.Set<PurchaseOrderSourceLine>().Where(x => x.Id == source.LineId)
            .Select(x => x.PostedReceived).SingleAsync(Ct));
    }

    [Fact]
    public async Task InvalidSecondLineRollsBackWholeDraft()
    {
        var company = TestCompanies.Next();
        var source = await SeedAsync(company);
        await using var db = Context(company);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, company).CreateAsync(
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 2, 29),
                [new(1, source.LineId, 1m, 91), new(2, source.LineId, 5m, 91)]), Ct));
        Assert.Equal("PUR.RECEIPT.QUANTITY_EXCEEDED", error.ErrorCode);
        Assert.Empty(await db.Set<PurchaseReceiptSource>().Where(x => x.CompanyId == company).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task EditRejectsChangedSourceOtherOwnerAndNonDraftWithoutChangingSavedRows()
    {
        var company = TestCompanies.Next();
        var source = await SeedAsync(company);
        await using var db = Context(company);
        var created = await Service(db, company).CreateAsync(new PurchaseReceiptSaveInput(source.OrderId,
            new DateOnly(2028, 2, 29), [new(1, source.LineId, 2m, 91)]), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, company, 8).GetAsync(created.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db, company, 8).UpdateAsync(created.Id,
            created.Version, new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 2, 29),
                [new(1, source.LineId, 1m, 91)]), Ct));
        var sourceError = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, company).UpdateAsync(
            created.Id, created.Version, new PurchaseReceiptSaveInput(source.OrderId + 1,
                new DateOnly(2028, 2, 29), [new(1, source.LineId, 1m, 91)]), Ct));
        Assert.Equal("PUR.RECEIPT.SOURCE_LOCKED", sourceError.ErrorCode);
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.pur_purchase_receipt SET status='PendingApproval' WHERE id={created.Id}", Ct);
        var statusError = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db, company).UpdateAsync(
            created.Id, created.Version, new PurchaseReceiptSaveInput(source.OrderId,
                new DateOnly(2028, 2, 29), [new(1, source.LineId, 1m, 92)]), Ct));
        Assert.Equal("PUR.RECEIPT.STATUS_NOT_EDITABLE", statusError.ErrorCode);
        await using var check = Context(company);
        var saved = await check.Set<PurchaseReceiptSourceLine>().SingleAsync(x => x.PurchaseReceiptId == created.Id, Ct);
        Assert.Equal(2m, saved.Quantity);
        Assert.Equal(91, saved.WarehouseId);
    }

    [Theory]
    [InlineData("0", 91)]
    [InlineData("1", 0)]
    [InlineData("1.0000001", 91)]
    public async Task InvalidLineShapeDoesNotIssueNumberOrSave(string quantity, long warehouse)
    {
        var company = TestCompanies.Next();
        var source = await SeedAsync(company);
        await using var db = Context(company);
        await Assert.ThrowsAnyAsync<Exception>(() => Service(db, company).CreateAsync(
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 2, 29),
                [new(1, source.LineId, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture), warehouse)]), Ct));
        Assert.Empty(await db.Set<PurchaseReceiptSource>().Where(x => x.CompanyId == company).ToArrayAsync(Ct));
    }

    [Fact]
    public async Task InvalidOrderSourcesAndMissingCostRejectWholeDraftWithoutSaving()
    {
        var company = TestCompanies.Next();
        var other = TestCompanies.Next();
        var source = await SeedAsync(company);
        var foreign = await SeedAsync(other);
        await using var db = Context(company);
        var service = Service(db, company);
        var crossCompany = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReceiptSaveInput(foreign.OrderId, new DateOnly(2028, 2, 29),
                [new(1, foreign.LineId, 1m, 91)]), Ct));
        Assert.Equal("PUR.RECEIPT.INVALID_SOURCE", crossCompany.ErrorCode);
        var foreignLine = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 2, 29),
                [new(1, foreign.LineId, 1m, 91)]), Ct));
        Assert.Equal("PUR.RECEIPT.INVALID_SOURCE", foreignLine.ErrorCode);

        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_order_line SET base_net=NULL WHERE id={source.LineId}", Ct);
        var cost = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 2, 29),
                [new(1, source.LineId, 1m, 91)]), Ct));
        Assert.Equal("PUR.RECEIPT.COST_UNAVAILABLE", cost.ErrorCode);

        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_order SET status='Draft' WHERE id={source.OrderId}", Ct);
        var status = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2028, 2, 29),
                [new(1, source.LineId, 1m, 91)]), Ct));
        Assert.Equal("PUR.RECEIPT.INVALID_SOURCE", status.ErrorCode);
        await using var count = database.CreateRawContext();
        Assert.Equal(0, await count.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM dbo.pur_purchase_receipt WHERE company_id IN ({0},{1})",
            company, other).SingleAsync(Ct));
    }

    [Fact]
    public async Task RejectedDraftEditsBackToDraftAndLegacyInvoiceOnlyRowIsHidden()
    {
        var company = TestCompanies.Next();
        var source = await SeedAsync(company);
        await using var db = Context(company);
        var created = await Service(db, company).CreateAsync(new PurchaseReceiptSaveInput(source.OrderId,
            new DateOnly(2028, 12, 31), [new(1, source.LineId, 5m, 91)]), Ct);
        Assert.Equal(5m, created.Lines[0].Quantity);
        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.pur_purchase_receipt SET status='Rejected' WHERE id={created.Id}", Ct);
        await using var fresh = Context(company);
        var edited = await Service(fresh, company).UpdateAsync(created.Id, created.Version,
            new PurchaseReceiptSaveInput(source.OrderId, new DateOnly(2029, 1, 1),
                [new(2, source.LineId, 1m, 92)]), Ct);
        Assert.Equal("Draft", edited.Status);
        Assert.True(edited.Version > created.Version);
        Assert.Equal(2, Assert.Single(edited.Lines).LineNumber);
        Assert.Equal(new DateOnly(2029, 1, 1), edited.BusinessDate);
        await using var reread = Context(company);
        var view = await Service(reread, company).GetForEditAsync(created.Id, Ct);
        Assert.Equal(2, Assert.Single(view.Lines).LineNumber);
        Assert.Equal("Draft", view.Status);
        Assert.Equal(7, await reread.Set<PurchaseReceiptSource>().Where(x => x.Id == created.Id)
            .Select(x => x.OwnerId).SingleAsync(Ct));

        await using (var raw = database.CreateRawContext())
            await raw.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT dbo.pur_purchase_receipt(company_id,purchase_order_id,supplier_id,currency,status,is_deleted,owner_id,created_at,created_by,version)
                VALUES ({company},{source.OrderId},71,'TWD','Posted',0,7,'2026-01-01',1,1)
                """, Ct);
        var legacy = await reread.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_receipt WHERE company_id={0} AND document_no IS NULL",
            company).SingleAsync(Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => Service(reread, company).GetAsync(legacy, Ct));
    }

    private async Task<(long OrderId, long LineId)> SeedAsync(long company)
    {
        await using var db = database.CreateRawContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order(company_id,supplier_id,currency,status,is_deleted,created_at,created_by,version)
            VALUES ({company},71,'TWD','Approved',0,'2026-01-01',1,1)
            """, Ct);
        var order = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order WHERE company_id={0}", company).SingleAsync(Ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT dbo.pur_purchase_order_line(company_id,purchase_order_id,line_no,material_id,quantity,unit_price,tax_rate_percent,base_net,created_at,created_by,version)
            VALUES ({company},{order},1,81,5,4,0,20,'2026-01-01',1,1)
            """, Ct);
        var line = await db.Database.SqlQueryRaw<long>(
            "SELECT id AS [Value] FROM dbo.pur_purchase_order_line WHERE company_id={0}", company).SingleAsync(Ct);
        return (order, line);
    }

    private MyErpDbContext Context(long company) => new(
        new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditStamper(
                new FixedClock(new DateTime(2028, 2, 29)), new FixedUser(7), new FixedCompany(company)))).Options,
        [new PurchaseInvoiceSourceDbModel()], new FixedCompany(company));

    private static PurchaseReceiptDraftService Service(MyErpDbContext db, long company, long userId = 7)
    {
        var authorization = new Mock<ICurrentAuthorization>();
        authorization.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            new UserCompanyAuthorization(userId, company, 10, 20, false,
                new Dictionary<string, DataScope>
                {
                    [PurchasingPermissions.ReceiptCreate] = DataScope.Self,
                    [PurchasingPermissions.ReceiptEdit] = DataScope.Self,
                    [PurchasingPermissions.ReceiptView] = DataScope.Self
                }));
        var numbers = new Mock<INumberGenerator>();
        numbers.Setup(x => x.NextAsync("PUR.RECEIPT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("PR-001");
        var references = new Mock<IInventoryReferenceGuard>();
        return new PurchaseReceiptDraftService(db, new RequisitionCompanyAccess(new FixedCompany(company)),
            authorization.Object, numbers.Object, references.Object, new PurchaseReceiptTracking(db, references.Object));
    }
}
