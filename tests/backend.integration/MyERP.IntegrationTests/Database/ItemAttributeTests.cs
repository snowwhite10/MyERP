using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Api;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>MD-ATTR-001～004：物料屬性定義與物料屬性值（ADR-045 第 11–13 點）。</summary>
public sealed class ItemAttributeTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel(), new ItemDbModel()], company);
    }

    private static string NewCode(string prefix) => (prefix + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant();

    private static async Task<Item> NewItemAsync(MyErpDbContext db, CancellationToken token)
    {
        var pcs = await new UomService(db).CreateAsync(NewCode("PCS"), "Piece", 0, token);
        return await new ItemService(db).CreateAsync(NewCode("I"), "Item", null, pcs.Id, null, null, token);
    }

    private static ItemAttributeValueInput Value(long attributeId, string? value) => new(attributeId, value);

    private static string ErrorKey(RequestValidationException ex) => Assert.Single(ex.Errors).Key;

    [Fact]
    public async Task AttributeDefinitionsSortBySortOrderCountItemsAndCannotBeDeletedWhenUsed()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(58000000, 59000000);
        await using var db = Context(companyId);
        var service = new ItemAttributeService(db);

        var color = await service.CreateAsync(" color ", " Colour ", 20, token);
        Assert.Equal(("COLOR", "Colour", 20, true, 7L), (color.Code, color.Name, color.SortOrder, color.IsActive, color.CreatedBy));
        var size = await service.CreateAsync("SIZE", "Size", 10, token);
        var material = await service.CreateAsync("MATERIAL", "Material", 10, token);
        Assert.Equal("MD.ITEM_ATTRIBUTE.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("color", "Again", 0, token))).ErrorCode);
        foreach (var bad in new[] { -1, ItemAttributeDefinition.MaxSortOrder + 1 })
            Assert.Equal("sortOrder", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(
                () => service.CreateAsync("BAD", "Bad", bad, token))));

        // 預設按排序號，同號按代碼；也可按名稱或倒序排序號。
        Assert.Equal(["MATERIAL", "SIZE", "COLOR"], (await service.ListAsync(new(), token)).Items.Select(x => x.Code));
        Assert.Equal(["COLOR", "MATERIAL", "SIZE"], (await service.ListAsync(new() { Sort = "-sortOrder" }, token)).Items.Select(x => x.Code));
        Assert.Equal(["COLOR", "MATERIAL", "SIZE"], (await service.ListAsync(new() { Sort = "name" }, token)).Items.Select(x => x.Code));
        Assert.Equal(["SIZE"], (await service.ListAsync(new() { Keyword = "siz" }, token)).Items.Select(x => x.Code));

        material = await service.UpdateAsync(material.Id, "Fabric", 30, false, material.Version, token);
        Assert.Equal(("Fabric", 30, false), (material.Name, material.SortOrder, material.IsActive));
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(material.Id, "Stale", 0, true, material.Version - 1, token));
        Assert.Equal("sortOrder", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(
            () => service.UpdateAsync(material.Id, "Fabric", 10000, true, material.Version, token))));

        // 使用的物料數；被使用的屬性不可刪除，只可停用。
        var values = new ItemAttributeValueService(db);
        var shirt = await NewItemAsync(db, token);
        var pants = await NewItemAsync(db, token);
        await values.ReplaceAsync(shirt.Id, [Value(color.Id, "Red"), Value(size.Id, "L")], token);
        await values.ReplaceAsync(pants.Id, [Value(color.Id, "Blue")], token);
        Assert.Equal(new Dictionary<long, int> { [color.Id] = 2, [size.Id] = 1 },
            await service.ItemCountsAsync([color.Id, size.Id, material.Id], token));
        Assert.Empty(await service.ItemCountsAsync([], token));
        Assert.Equal("MD.ITEM_ATTRIBUTE.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.DeleteAsync(color.Id, color.Version, token))).ErrorCode);
        color = await service.UpdateAsync(color.Id, color.Name, color.SortOrder, false, color.Version, token);
        Assert.False(color.IsActive);

        // 移除使用後可刪除；代碼永久保留。
        await values.ReplaceAsync(shirt.Id, [Value(color.Id, "Red")], token);
        await service.DeleteAsync(size.Id, size.Version, token);
        Assert.Equal(["COLOR", "MATERIAL"], (await service.ListAsync(new() { Sort = "code" }, token)).Items.Select(x => x.Code));
        Assert.Equal("MD.ITEM_ATTRIBUTE.DUPLICATE_CODE", (await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateAsync("SIZE", "Size again", 0, token))).ErrorCode);
        Assert.Equal("MD.ITEM_ATTRIBUTE.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => service.DeleteAsync(size.Id, size.Version + 1, token))).ErrorCode);

        // 公司隔離
        await using var otherDb = Context(companyId + 1);
        var other = new ItemAttributeService(otherDb);
        Assert.Empty((await other.ListAsync(new(), token)).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => other.UpdateAsync(color.Id, "X", 0, true, color.Version, token));
        Assert.Empty(await other.ItemCountsAsync([color.Id], token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemAttributeService(noCompany).ListAsync(new(), token));
    }

    [Fact]
    public async Task ReplaceSetsUpdatesAndRemovesValuesInAttributeOrder()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(59000000, 60000000);
        await using var db = Context(companyId);
        var attributes = new ItemAttributeService(db);
        var color = await attributes.CreateAsync("COLOR", "Colour", 20, token);
        var size = await attributes.CreateAsync("SIZE", "Size", 10, token);
        var origin = await attributes.CreateAsync("ORIGIN", "Origin", 10, token);
        var item = await NewItemAsync(db, token);
        var service = new ItemAttributeValueService(db);
        Assert.Empty(await service.ListAsync(item.Id, token));

        // 值去除前後空白；列表按屬性排序號、代碼。
        var listed = await service.ReplaceAsync(item.Id, [Value(color.Id, " Red "), Value(size.Id, "L"), Value(origin.Id, "TW")], token);
        Assert.Equal([(origin.Id, "ORIGIN", "Origin", 10, true, "TW"), (size.Id, "SIZE", "Size", 10, true, "L"), (color.Id, "COLOR", "Colour", 20, true, "Red")],
            listed.Select(x => (x.AttributeId, x.AttributeCode, x.AttributeName, x.SortOrder, x.IsAttributeActive, x.Value)));
        Assert.Equal(listed, await service.ListAsync(item.Id, token));

        // 整批取代：修改、刪除、保留不變。
        var rows = await db.Set<ItemAttributeValue>().AsNoTracking().Where(x => x.ItemId == item.Id).ToListAsync(token);
        listed = await service.ReplaceAsync(item.Id, [Value(color.Id, "Blue"), Value(origin.Id, "TW")], token);
        Assert.Equal([("ORIGIN", "TW"), ("COLOR", "Blue")], listed.Select(x => (x.AttributeCode, x.Value)));
        var after = await db.Set<ItemAttributeValue>().AsNoTracking().Where(x => x.ItemId == item.Id).ToListAsync(token);
        Assert.Equal(rows.Single(x => x.AttributeId == origin.Id).Version, after.Single(x => x.AttributeId == origin.Id).Version);
        Assert.True(after.Single(x => x.AttributeId == color.Id).Version > rows.Single(x => x.AttributeId == color.Id).Version);

        // 原有值的屬性停用後仍可保留並修改值，但不可新設定停用屬性。
        color = await attributes.UpdateAsync(color.Id, color.Name, color.SortOrder, false, color.Version, token);
        listed = await service.ReplaceAsync(item.Id, [Value(color.Id, "Green"), Value(origin.Id, "TW")], token);
        Assert.Contains(listed, x => x is { AttributeCode: "COLOR", Value: "Green", IsAttributeActive: false });
        await service.ReplaceAsync(item.Id, [Value(origin.Id, "TW")], token);
        Assert.Equal("attributes[1].attributeId", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ReplaceAsync(item.Id, [Value(origin.Id, "TW"), Value(color.Id, "Green")], token))));

        // 空集合清除全部；停用物料仍可維護。
        await new ItemService(db).UpdateAsync(item.Id, item.Name, null, item.BaseUomId, null, null, false, item.Version, token);
        Assert.Empty(await service.ReplaceAsync(item.Id, [], token));
        Assert.Empty(await db.Set<ItemAttributeValue>().Where(x => x.ItemId == item.Id).ToListAsync(token));
    }

    [Fact]
    public async Task ReplaceValidatesRequestReferencesLimitAndScope()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(60000000, 61000000);
        await using var db = Context(companyId);
        var attributes = new ItemAttributeService(db);
        var color = await attributes.CreateAsync("COLOR", "Colour", 0, token);
        var size = await attributes.CreateAsync("SIZE", "Size", 0, token);
        var item = await NewItemAsync(db, token);
        var service = new ItemAttributeValueService(db);

        Assert.Equal("attributes", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(() => service.ReplaceAsync(item.Id, null, token))));
        foreach (var (values, key) in new (ItemAttributeValueInput[], string)[]
                 {
                     ([Value(color.Id, "Red"), Value(color.Id, "Blue")], "attributes[1].attributeId"),
                     ([Value(color.Id, "Red"), Value(0, "X")], "attributes[1].attributeId"),
                     ([Value(long.MaxValue, "X")], "attributes[0].attributeId"),
                     ([Value(color.Id, "  ")], "attributes[0].value"),
                     ([Value(size.Id, "S"), Value(color.Id, null)], "attributes[1].value"),
                     ([Value(color.Id, new string('x', ItemAttributeValue.MaxValueLength + 1))], "attributes[0].value"),
                 })
            Assert.Equal(key, ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(() => service.ReplaceAsync(item.Id, values, token))));
        Assert.Empty(await service.ListAsync(item.Id, token));

        // 剛好 200 字與 50 個屬性可接受；51 個 400。
        var many = new List<ItemAttributeDefinition>();
        for (var i = 0; i < ItemAttributeValueService.MaxAttributesPerItem + 1; i++)
            many.Add(await attributes.CreateAsync($"A{i:D2}", $"Attribute {i}", i, token));
        var full = many.Take(ItemAttributeValueService.MaxAttributesPerItem).Select(x => Value(x.Id, new string('v', ItemAttributeValue.MaxValueLength))).ToList();
        Assert.Equal(ItemAttributeValueService.MaxAttributesPerItem, (await service.ReplaceAsync(item.Id, full, token)).Count);
        Assert.Equal("attributes", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ReplaceAsync(item.Id, [.. full, Value(many[^1].Id, "x")], token))));
        Assert.Equal(ItemAttributeValueService.MaxAttributesPerItem, (await service.ListAsync(item.Id, token)).Count);

        // 刪除的屬性與別公司屬性不可設定；別公司看不到物料；未選公司 403。
        var deleted = await attributes.CreateAsync("GONE", "Gone", 0, token);
        await attributes.DeleteAsync(deleted.Id, deleted.Version, token);
        Assert.Equal("attributes[0].attributeId", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ReplaceAsync(item.Id, [Value(deleted.Id, "x")], token))));
        await using var foreign = Context(companyId + 1);
        var foreignAttribute = await new ItemAttributeService(foreign).CreateAsync("COLOR", "Colour", 0, token);
        Assert.Equal("attributes[0].attributeId", ErrorKey(await Assert.ThrowsAsync<RequestValidationException>(
            () => service.ReplaceAsync(item.Id, [Value(foreignAttribute.Id, "x")], token))));
        var foreignService = new ItemAttributeValueService(foreign);
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => foreignService.ListAsync(item.Id, token))).ErrorCode);
        Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => foreignService.ReplaceAsync(item.Id, [Value(foreignAttribute.Id, "x")], token))).ErrorCode);
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemAttributeValueService(noCompany).ListAsync(item.Id, token));
    }

    [Fact]
    public async Task ApiIsPermissionProtectedAndConcurrentReplaceKeepsOneValuePerAttribute()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(61000000, 62000000);
        await using var db = Context(companyId);
        string Permission(Type controller, string method) => Assert.Single(controller.GetMethod(method)!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>()).Code;
        Assert.Equal(MasterDataPermissions.ItemAttributeView, Permission(typeof(ItemAttributesController), nameof(ItemAttributesController.List)));
        Assert.Equal(MasterDataPermissions.ItemAttributeCreate, Permission(typeof(ItemAttributesController), nameof(ItemAttributesController.Create)));
        Assert.Equal(MasterDataPermissions.ItemAttributeEdit, Permission(typeof(ItemAttributesController), nameof(ItemAttributesController.Update)));
        Assert.Equal(MasterDataPermissions.ItemAttributeDelete, Permission(typeof(ItemAttributesController), nameof(ItemAttributesController.Delete)));
        Assert.Equal(MasterDataPermissions.ItemView, Permission(typeof(ItemAttributeValuesController), nameof(ItemAttributeValuesController.List)));
        Assert.Equal(MasterDataPermissions.ItemEdit, Permission(typeof(ItemAttributeValuesController), nameof(ItemAttributeValuesController.Replace)));
        var catalog = new MasterDataPermissions().Permissions.Select(x => x.Code).ToList();
        foreach (var code in new[] { "MD.ITEM_ATTRIBUTE.VIEW", "MD.ITEM_ATTRIBUTE.CREATE", "MD.ITEM_ATTRIBUTE.EDIT", "MD.ITEM_ATTRIBUTE.DELETE" })
            Assert.Contains(code, catalog);

        var definitions = new ItemAttributesController(new ItemAttributeService(db));
        var created = Assert.IsType<ItemAttributeResponse>(Assert.IsType<ObjectResult>(
            (await definitions.Create(new CreateItemAttributeRequest("COLOR", "Colour", 5), token)).Result).Value);
        Assert.Equal(("COLOR", 5, 0), (created.Code, created.SortOrder, created.ItemCount));
        var item = await NewItemAsync(db, token);
        var values = new ItemAttributeValuesController(new ItemAttributeValueService(db));
        var replaced = await values.Replace(item.Id, new ReplaceItemAttributesRequest([Value(created.Id, "Red")]), token);
        Assert.Equal("Red", Assert.Single(replaced).Value);
        Assert.Equal("Red", Assert.Single(await values.List(item.Id, token)).Value);
        var listedDefinition = Assert.Single((await definitions.List(new(), token)).Items);
        Assert.Equal(1, listedDefinition.ItemCount);
        var updated = await definitions.Update(created.Id, new UpdateItemAttributeRequest("Color", 6, true, created.Version), token);
        Assert.Equal(("Color", 6, 1), (updated.Name, updated.SortOrder, updated.ItemCount));
        await Assert.ThrowsAsync<ConflictException>(() => definitions.Delete(created.Id, updated.Version, token));

        // 兩個連線同時整批取代：以公司鎖序列化，每個屬性只有一個值。
        var size = await new ItemAttributeService(db).CreateAsync("SIZE", "Size", 0, token);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId);
        await Task.WhenAll(
            new ItemAttributeValueService(firstDb).ReplaceAsync(item.Id, [Value(created.Id, "Blue"), Value(size.Id, "M")], token),
            new ItemAttributeValueService(secondDb).ReplaceAsync(item.Id, [Value(size.Id, "L")], token));
        var final = await new ItemAttributeValueService(db).ListAsync(item.Id, token);
        Assert.Single(final, x => x.AttributeId == size.Id);
        Assert.True(final.Count is 1 or 2);

        var indexes = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('md_item_attribute_value')").ToListAsync(token);
        Assert.Contains("ux_md_item_attribute_value_company_id_item_id_attribute_id", indexes);
        Assert.Contains("ix_md_item_attribute_value_company_id_attribute_id", indexes);
        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_item_attribute_value')").ToListAsync(token);
        Assert.Equal(["fk_md_item_attribute_value_md_item_attribute_company_id_attribute_id", "fk_md_item_attribute_value_md_item_company_id_item_id"],
            foreignKeys.Order(StringComparer.Ordinal));
        var checks = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('md_item_attribute'), OBJECT_ID('md_item_attribute_value'))")
            .ToListAsync(token);
        Assert.Contains("ck_md_item_attribute_sort_order", checks);
        Assert.Contains("ck_md_item_attribute_value_value", checks);
    }
}
