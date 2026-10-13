using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyERP.IntegrationTests.Auth;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Api;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

/// <summary>MD-ITEM-DEL-001～005：物料刪除（REQ_F4118.3.4、ADR-050）。</summary>
public sealed class ItemDeleteTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel(), new ItemDbModel()], company);
    }

    private static long NewCompany() => Random.Shared.NextInt64(56000000, 57000000);

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    private sealed class FakeUsage : IItemUsage
    {
        public bool InUse { get; set; }
        public List<string> Calls { get; } = [];

        public Task<bool> IsInUseAsync(string itemCode, CancellationToken token = default)
        {
            Calls.Add(itemCode);
            return Task.FromResult(InUse);
        }
    }

    [Fact]
    public async Task DeleteSoftDeletesItemWithOwnedDataAndKeepsCodeReserved()
    {
        // MD-ITEM-DEL-001
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var uoms = new UomService(db);
        var pcs = await uoms.CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var box = await uoms.CreateAsync(NewCode("BOX"), "Box", 0, token);
        var items = new ItemService(db);
        var item = await items.CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
        var conversion = await new ItemUomConversionService(db).CreateAsync(item.Id, box.Id, 12m, token);
        var images = new ItemImageService(db);
        await images.UploadAsync(item.Id, "a.png", new MemoryStream(CompanyImageTests.Png(100, 100)), null, false, token);
        await images.UploadAsync(item.Id, "b.png", new MemoryStream(CompanyImageTests.Png(120, 100)), null, false, token);
        var attributes = new ItemAttributeService(db);
        var color = await attributes.CreateAsync(NewCode("A"), "Colour", 0, token);
        await new ItemAttributeValueService(db).ReplaceAsync(item.Id, [new ItemAttributeValueInput(color.Id, "Red")], token);

        // 其他公司、不存在的 id 回 404；版本不符回 409，資料不變。
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() =>
            new ItemService(otherDb).DeleteAsync(item.Id, item.Version, token))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => items.DeleteAsync(long.MaxValue, 1, token));
        await Assert.ThrowsAsync<ConflictException>(() => items.DeleteAsync(item.Id, item.Version + 1, token));
        await Assert.ThrowsAsync<ConflictException>(() => items.DeleteAsync(item.Id, 0, token));

        await items.DeleteAsync(item.Id, item.Version, token);
        db.ChangeTracker.Clear();

        Assert.Equal(0, (await items.ListAsync(new PagedRequest { Keyword = item.Code }, token)).TotalCount);
        var stored = await db.Set<Item>().IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == item.Id, token);
        Assert.True(stored.IsDeleted);
        Assert.False(stored.IsActive);
        // 專屬換算軟刪除；圖片與屬性值隨物料刪除（ADR-050）。
        Assert.True((await db.Set<ItemUomConversion>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Id == conversion.Id, token)).IsDeleted);
        Assert.False(await db.Set<ItemImage>().IgnoreQueryFilters().AnyAsync(x => x.ItemId == item.Id, token));
        Assert.False(await db.Set<ItemAttributeValue>().IgnoreQueryFilters().AnyAsync(x => x.ItemId == item.Id, token));

        // 刪除後的物料不可再維護，子資料 API 一律 404；代碼不可重用（含匯入）。
        await Assert.ThrowsAsync<NotFoundException>(() => items.UpdateAsync(item.Id, "Again", null, pcs.Id, null, null, true, stored.Version, token));
        await Assert.ThrowsAsync<NotFoundException>(() => items.DeleteAsync(item.Id, stored.Version, token));
        await Assert.ThrowsAsync<NotFoundException>(() => new ItemUomConversionService(db).ListAsync(item.Id, token));
        await Assert.ThrowsAsync<NotFoundException>(() => images.UploadAsync(item.Id, "c.png", new MemoryStream(CompanyImageTests.Png(100, 100)), null, false, token));
        await Assert.ThrowsAsync<NotFoundException>(() => new ItemAttributeValueService(db).ReplaceAsync(item.Id, [], token));
        Assert.Equal("MD.ITEM.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(() =>
            items.CreateAsync(item.Code, "Reuse", null, pcs.Id, null, null, token))).ErrorCode);

        // 屬性不再被使用，可以刪除定義。
        await attributes.DeleteAsync(color.Id, color.Version, token);
    }

    [Fact]
    public async Task ItemInUseCannotBeDeletedAndKeepsOwnedData()
    {
        // MD-ITEM-DEL-002
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var uoms = new UomService(db);
        var pcs = await uoms.CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var box = await uoms.CreateAsync(NewCode("BOX"), "Box", 0, token);
        var usage = new FakeUsage { InUse = true };
        var items = new ItemService(db, [usage]);
        var item = await items.CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
        await new ItemUomConversionService(db).CreateAsync(item.Id, box.Id, 6m, token);

        Assert.Equal("MD.ITEM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(() =>
            items.DeleteAsync(item.Id, item.Version, token))).ErrorCode);
        Assert.Equal([item.Code], usage.Calls);
        db.ChangeTracker.Clear();
        Assert.Single(await new ItemUomConversionService(db).ListAsync(item.Id, token));
        Assert.Equal(1, (await items.ListAsync(new PagedRequest { Keyword = item.Code }, token)).TotalCount);

        usage.InUse = false;
        await items.DeleteAsync(item.Id, item.Version, token);
        Assert.Equal(0, (await items.ListAsync(new PagedRequest { Keyword = item.Code }, token)).TotalCount);
    }

    [Fact]
    public async Task InactiveItemWithoutOwnedDataCanBeDeleted()
    {
        // MD-ITEM-DEL-003
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var pcs = await new UomService(db).CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var items = new ItemService(db);
        var item = await items.CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
        item = await items.UpdateAsync(item.Id, "Item", null, pcs.Id, null, null, false, item.Version, token);
        await items.DeleteAsync(item.Id, item.Version, token);
        Assert.False(await db.Set<Item>().AnyAsync(x => x.Id == item.Id, token));
        await Assert.ThrowsAsync<NotFoundException>(() => items.DeleteAsync(item.Id, item.Version, token));
    }

    [Fact]
    public async Task DeleteHoldsItemLocksThatExcludeChildWritesUntilCommit()
    {
        // MD-ITEM-DEL-004：刪除在同一交易內持有物料、換算、圖片、屬性的排他鎖，避免新增子資料到剛刪除的物料。
        var token = TestContext.Current.CancellationToken;
        var companyId = NewCompany();
        await using var db = Context(companyId);
        await using var probe = Context(companyId);
        var pcs = await new UomService(db).CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var items = new ItemService(db);
        var item = await items.CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);

        await using (var transaction = await db.Database.BeginTransactionAsync(token))
        {
            await items.DeleteAsync(item.Id, item.Version, token);
            foreach (var resource in new[] { "md_item", "md_item_attribute", "md_item_image", "md_item_uom_conversion" })
                Assert.Equal(0, await GrantableAsync(probe, $"{resource}:{companyId}", "Shared", token));
            await transaction.RollbackAsync(token);
        }
        Assert.Equal(1, await GrantableAsync(probe, $"md_item:{companyId}", "Exclusive", token));
        db.ChangeTracker.Clear();
        Assert.True(await db.Set<Item>().AnyAsync(x => x.Id == item.Id, token));
    }

    [Fact]
    public async Task DeleteEndpointRequiresDeletePermissionAndReturnsNoContent()
    {
        // MD-ITEM-DEL-005
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(NewCompany());
        var pcs = await new UomService(db).CreateAsync(NewCode("PCS"), "Piece", 0, token);
        var service = new ItemService(db);
        var item = await service.CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
        var controller = new ItemsController(service) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };

        var permission = Assert.Single(typeof(ItemsController).GetMethod(nameof(ItemsController.Delete))!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>()).Code;
        Assert.Equal(MasterDataPermissions.ItemDelete, permission);
        Assert.Equal("MD.ITEM.DELETE", MasterDataPermissions.ItemDelete);
        Assert.Contains(new MasterDataPermissions().Permissions, x => x.Code == MasterDataPermissions.ItemDelete);

        Assert.IsType<NoContentResult>(await controller.Delete(item.Id, item.Version, token));
        Assert.Equal(0, (await service.ListAsync(new PagedRequest { Keyword = item.Code }, token)).TotalCount);
    }

    private static async Task<int> GrantableAsync(MyErpDbContext db, string resource, string mode, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        return await db.Database.SqlQuery<int>(
            $"SELECT APPLOCK_TEST('public', {resource}, {mode}, 'Transaction') AS Value").SingleAsync(token);
    }
}
