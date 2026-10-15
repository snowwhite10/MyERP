using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Domain;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.IntegrationTests.Database;

/// <summary>INV-ASM-002/003/007–010：草稿保存、版本、公司隔離與引用。</summary>
public sealed class AssemblyDraftTests(TestDatabase database)
{
    private static readonly DateOnly Day = new(2026, 12, 31);
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MyErpDbContext Context(long companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(Now), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
                .UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemDbModel(), new WarehouseDbModel(), new InventoryReferenceDbModel(),
                new StockMovementDbModel(), new StockTrackingDbModel(), new AssemblyDbModel()], company);
    }

    [Fact]
    public async Task DraftRoundTripsAndVersionAndCompanyIsolation()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var finished = await new ItemService(db).CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null, Ct);
        var component = await new ItemService(db).CreateAsync("C" + companyId, "Component", null, uom.Id, null, null, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var service = new AssemblyService(db, new FakeNumbers(), new InventoryReferenceGuard(db), new FixedClock(Now));
        var input = new AssemblyInput(Day, " draft ",
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1.123456m),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 2m)]);
        var draft = await service.CreateAsync(input, Ct);
        Assert.Equal(InventoryDocumentStatus.Draft, draft.Status);
        Assert.Equal("draft", draft.Remark);
        Assert.Equal(2, (await service.GetAsync(draft.Id, Ct)).Lines.Count);
        Assert.Equal([draft.Id], (await service.ListAsync(new AssemblyQuery { From = Day, To = Day,
            FinishedItemId = finished.Id, Status = InventoryDocumentStatus.Draft }, Ct)).Items.Select(x => x.Id));
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "ASSEMBLY", Ct));
        await using var other = Context(TestCompanies.Next());
        var otherService = new AssemblyService(other, new FakeNumbers(), new InventoryReferenceGuard(other), new FixedClock(Now));
        await Assert.ThrowsAsync<NotFoundException>(() => otherService.GetAsync(draft.Id, Ct));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(draft.Id, input, draft.Version + 1, Ct));
        var updated = await service.UpdateAsync(draft.Id, input with { Remark = "updated" }, draft.Version, Ct);
        Assert.Equal("updated", updated.Remark);
        var thousand = await service.CreateAsync(new AssemblyInput(new DateOnly(2028, 2, 29), null,
            input.Finished, Enumerable.Range(2, 999)
                .Select(i => new AssemblyLineInput(i, component.Id, warehouse.Id, null, 1m)).ToArray()), Ct);
        Assert.Equal(1000, (await service.GetAsync(thousand.Id, Ct)).Lines.Count);
        var firstPage = await service.ListAsync(new AssemblyQuery { Page = 1, PageSize = 1 }, Ct);
        var secondPage = await service.ListAsync(new AssemblyQuery { Page = 2, PageSize = 1 }, Ct);
        Assert.Equal([thousand.Id], firstPage.Items.Select(x => x.Id));
        Assert.Equal([draft.Id], secondPage.Items.Select(x => x.Id));
        await service.DeleteAsync(draft.Id, updated.Version, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(draft.Id, Ct));
    }

    [Fact]
    public async Task InvalidCompositionIsRejectedBeforeNumbering()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var numbers = new FakeNumbers();
        var service = new AssemblyService(db, numbers, new InventoryReferenceGuard(db), new FixedClock(Now));
        var line = new AssemblyLineInput(1, 1, 1, null, 1m);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line, []), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, null, [line]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line, [line with { LineNo = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line, [line with { ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line with { Quantity = 0m }, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line with { Quantity = -1m }, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, new string('x', 1001), line, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line,
                Enumerable.Range(2, 1000).Select(i => line with { LineNo = i, ItemId = 2 }).ToArray()), Ct));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(
            new AssemblyInput(Day, null, line with { Quantity = 1.0000001m }, [line with { LineNo = 2, ItemId = 2 }]), Ct));
        Assert.Equal(0, numbers.Count);
    }

    [Fact]
    public async Task LotAndSerialSelectionsSurviveDraftReplacementAndInvalidReferencesRollBack()
    {
        var companyId = TestCompanies.Next();
        await using var db = Context(companyId);
        var uom = await new UomService(db).CreateAsync("U" + companyId, "Unit", 0, Ct);
        var items = new ItemService(db);
        var finished = await items.CreateAsync("F" + companyId, "Finished", null, uom.Id, null, null,
            ItemTrackingMode.Serial, Ct);
        var component = await items.CreateAsync("C" + companyId, "Component", null, uom.Id, null, null,
            ItemTrackingMode.Lot, Ct);
        var warehouse = await new WarehouseService(db).CreateAsync("W" + companyId, "Warehouse", null, null, null, Ct);
        var numbers = new FakeNumbers();
        var service = new AssemblyService(db, numbers, new InventoryReferenceGuard(db), new FixedClock(Now));
        var input = new AssemblyInput(Day, null,
            new AssemblyLineInput(1, finished.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Serial("NEW-SERIAL")]),
            [new AssemblyLineInput(2, component.Id, warehouse.Id, null, 2m,
                [InventoryTrackingLine.Lot("LOT-A", 2m)])]);
        var draft = await service.CreateAsync(input, Ct);
        var saved = await service.GetAsync(draft.Id, Ct);
        Assert.Equal(2, saved.Lines.Sum(x => x.Tracking.Count));
        var replaced = await service.UpdateAsync(draft.Id, input with
        {
            Components = [new AssemblyLineInput(3, component.Id, warehouse.Id, null, 1m,
                [InventoryTrackingLine.Lot("LOT-B", 1m)])]
        }, draft.Version, Ct);
        Assert.Equal(["LOT-B", "NEW-SERIAL"], (await service.GetAsync(replaced.Id, Ct)).Lines
            .SelectMany(x => x.Tracking).Select(x => x.LotNo ?? x.SerialNo).OrderBy(x => x).ToArray());
        var invalid = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync(input with
        {
            Components = [new AssemblyLineInput(2, component.Id, warehouse.Id + 999999, null, 2m)]
        }, Ct));
        Assert.Equal("MD.WAREHOUSE.NOT_FOUND", invalid.ErrorCode);
        Assert.Equal(1, await db.Set<Assembly>().CountAsync(Ct));
        Assert.False(await db.Set<StockMovement>().AnyAsync(x => x.SourceType == "ASSEMBLY", Ct));
    }

    private sealed class FakeNumbers : INumberGenerator
    {
        private int count;
        public int Count => count;
        public Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default) =>
            Task.FromResult($"AS-{Interlocked.Increment(ref count):0000}");
    }
}
