using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyERP.IntegrationTests.Auth;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Api;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>MD-IMG-001～004：物料圖片（ADR-045）的內容檢查、主圖與排序、張數上限、公司隔離、API 與併發。</summary>
public sealed class ItemImageTests(TestDatabase database)
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

    private static Task<ItemImageView> UploadAsync(ItemImageService service, long itemId, byte[] bytes,
        string? fileName = "photo.png", int? sortOrder = null, bool isPrimary = false, CancellationToken token = default) =>
        service.UploadAsync(itemId, fileName, new MemoryStream(bytes), sortOrder, isPrimary, token);

    [Fact]
    public async Task UploadsKeepExactlyOnePrimaryAndOrderByPrimaryThenSortOrder()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(54000000, 55000000);
        await using var db = Context(companyId);
        var item = await NewItemAsync(db, token);
        var service = new ItemImageService(db);
        Assert.Empty(await service.ListAsync(item.Id, token));

        // 第一張自動成為主圖；排序號預設為目前最大值 + 1。
        var png = CompanyImageTests.Png(800, 600);
        var first = await UploadAsync(service, item.Id, png, @"C:\fakepath\front.png", token: token);
        Assert.Equal((item.Id, "image/png", "front.png", png.Length, 800, 600, 1, true),
            (first.ItemId, first.ContentType, first.FileName, first.SizeBytes, first.Width, first.Height, first.SortOrder, first.IsPrimary));
        var jpeg = CompanyImageTests.Jpeg(1024, 768);
        var second = await UploadAsync(service, item.Id, jpeg, "", token: token);
        Assert.Equal(("image/jpeg", "image.jpg", 2, false), (second.ContentType, second.FileName, second.SortOrder, second.IsPrimary));
        var third = await UploadAsync(service, item.Id, CompanyImageTests.Png(100, 100), "side.png", sortOrder: 0, isPrimary: true, token: token);
        Assert.Equal((0, true), (third.SortOrder, third.IsPrimary));
        var listed = await service.ListAsync(item.Id, token);
        Assert.Equal([third.Id, first.Id, second.Id], listed.Select(x => x.Id));
        Assert.Equal([true, false, false], listed.Select(x => x.IsPrimary));
        Assert.True(listed[1].Version > first.Version); // 取消主圖會遞增版本

        // 內容與版本
        var content = await service.GetContentAsync(item.Id, first.Id, token);
        Assert.Equal(png, content.Content);
        Assert.Equal(("image/png", "front.png"), (content.ContentType, content.FileName));
        Assert.Equal(listed[1].Version, await service.GetVersionAsync(item.Id, first.Id, token));
        Assert.Null(await service.GetVersionAsync(item.Id, long.MaxValue, token));

        // 修改排序與主圖；不可直接取消主圖；舊版本 409。
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(item.Id, first.Id, 5, false, first.Version, token));
        var moved = await service.UpdateAsync(item.Id, second.Id, 9999, true, second.Version, token);
        Assert.Equal((9999, true), (moved.SortOrder, moved.IsPrimary));
        var unsetPrimary = await Assert.ThrowsAsync<RequestValidationException>(
            () => service.UpdateAsync(item.Id, second.Id, 9999, false, moved.Version, token));
        Assert.Equal("isPrimary", Assert.Single(unsetPrimary.Errors).Key);
        Assert.Equal([second.Id, third.Id, first.Id], (await service.ListAsync(item.Id, token)).Select(x => x.Id));

        // 刪除主圖時由排序號最小的圖片接任；刪除非主圖不影響主圖。
        await service.DeleteAsync(item.Id, second.Id, moved.Version, token);
        listed = await service.ListAsync(item.Id, token);
        Assert.Equal([third.Id, first.Id], listed.Select(x => x.Id));
        Assert.True(listed[0].IsPrimary);
        await service.DeleteAsync(item.Id, first.Id, listed[1].Version, token);
        Assert.True(Assert.Single(await service.ListAsync(item.Id, token)).IsPrimary);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(item.Id, first.Id, 1, token));
        Assert.Equal("MD.ITEM_IMAGE.NOT_FOUND", missing.ErrorCode);
        var gone = await Assert.ThrowsAsync<NotFoundException>(() => service.GetContentAsync(item.Id, second.Id, token));
        Assert.Equal("MD.ITEM_IMAGE.NOT_FOUND", gone.ErrorCode);
    }

    [Fact]
    public async Task UploadsValidateContentSizeDimensionsCountAndScope()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(55000000, 56000000);
        await using var db = Context(companyId);
        var item = await NewItemAsync(db, token);
        var service = new ItemImageService(db);

        Assert.Equal("file", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => UploadAsync(service, item.Id, [], token: token))).Errors).Key);
        foreach (var bytes in new[] { "GIF89a\x01\x00\x01\x00"u8.ToArray(), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() })
            Assert.Equal(ItemImageService.UnsupportedTypeCode, (await Assert.ThrowsAsync<BusinessRuleException>(
                () => UploadAsync(service, item.Id, bytes, "fake.png", token: token))).ErrorCode);
        Assert.Equal(ItemImageService.TooLargeCode, (await Assert.ThrowsAsync<BusinessRuleException>(
            () => UploadAsync(service, item.Id, CompanyImageTests.Png(100, 100, ItemImageService.MaxBytes), token: token))).ErrorCode);
        foreach (var (width, height) in new[] { (15u, 100u), (100u, 4097u) })
            Assert.Equal(ItemImageService.InvalidDimensionsCode, (await Assert.ThrowsAsync<BusinessRuleException>(
                () => UploadAsync(service, item.Id, CompanyImageTests.Png(width, height), token: token))).ErrorCode);
        foreach (var sortOrder in new[] { -1, 10000 })
            Assert.Equal("sortOrder", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => UploadAsync(service, item.Id, CompanyImageTests.Png(100, 100), sortOrder: sortOrder, token: token))).Errors).Key);
        Assert.Empty(await service.ListAsync(item.Id, token));

        // 剛好 2 MB 與 16／4096 像素可接受；第 11 張回 LIMIT_REACHED。
        var largest = CompanyImageTests.Png(4096, 16, ItemImageService.MaxBytes - 45);
        Assert.Equal(ItemImageService.MaxBytes, largest.Length);
        Assert.Equal(ItemImageService.MaxBytes, (await UploadAsync(service, item.Id, largest, token: token)).SizeBytes);
        for (var i = 1; i < ItemImageService.MaxImagesPerItem; i++)
            await UploadAsync(service, item.Id, CompanyImageTests.Jpeg(16, 16), token: token);
        Assert.Equal(ItemImageService.LimitReachedCode, (await Assert.ThrowsAsync<BusinessRuleException>(
            () => UploadAsync(service, item.Id, CompanyImageTests.Jpeg(16, 16), token: token))).ErrorCode);
        var images = await service.ListAsync(item.Id, token);
        Assert.Equal(ItemImageService.MaxImagesPerItem, images.Count);
        Assert.Single(images, x => x.IsPrimary);
        foreach (var sortOrder in new[] { -1, 10000 })
            Assert.Equal("sortOrder", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
                () => service.UpdateAsync(item.Id, images[0].Id, sortOrder, true, images[0].Version, token))).Errors).Key);

        // 停用物料仍可維護圖片；圖片必須屬路徑上的物料；別公司看不到物料。
        await new ItemService(db).UpdateAsync(item.Id, item.Name, null, item.BaseUomId, null, null, false, item.Version, token);
        var other = await NewItemAsync(db, token);
        var uploaded = await UploadAsync(service, other.Id, CompanyImageTests.Png(64, 64), token: token);
        Assert.Equal("MD.ITEM_IMAGE.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => service.UpdateAsync(item.Id, uploaded.Id, 1, true, uploaded.Version, token))).ErrorCode);
        Assert.Equal("MD.ITEM_IMAGE.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetContentAsync(item.Id, uploaded.Id, token))).ErrorCode);
        await service.DeleteAsync(item.Id, images[^1].Id, images[^1].Version, token);
        await using var foreign = Context(companyId + 1);
        var foreignService = new ItemImageService(foreign);
        foreach (var call in new Func<Task>[]
                 {
                     () => foreignService.ListAsync(item.Id, token),
                     () => UploadAsync(foreignService, item.Id, CompanyImageTests.Png(64, 64), token: token),
                     () => foreignService.GetContentAsync(item.Id, images[0].Id, token),
                     () => foreignService.DeleteAsync(item.Id, images[0].Id, images[0].Version, token),
                 })
            Assert.Equal("MD.ITEM.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(call)).ErrorCode);
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemImageService(noCompany).ListAsync(item.Id, token));
    }

    [Fact]
    public async Task ApiIsPermissionProtectedAndServesContentWithEtag()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(56000000, 57000000));
        var item = await NewItemAsync(db, token);
        var controller = new ItemImagesController(new ItemImageService(db)) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        // Content 與 ControllerBase.Content 多載同名，只取本類別宣告的方法。
        string Permission(string method) => Assert.Single(typeof(ItemImagesController)
            .GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>()).Code;
        Assert.Equal(MasterDataPermissions.ItemView, Permission(nameof(ItemImagesController.List)));
        Assert.Equal(MasterDataPermissions.ItemView, Permission(nameof(ItemImagesController.Content)));
        foreach (var method in new[] { nameof(ItemImagesController.Upload), nameof(ItemImagesController.Update), nameof(ItemImagesController.Delete) })
            Assert.Equal(MasterDataPermissions.ItemEdit, Permission(method));

        Assert.Equal("file", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => controller.Upload(item.Id, null, null, false, token))).Errors).Key);
        Assert.Equal(ItemImageService.TooLargeCode, (await Assert.ThrowsAsync<BusinessRuleException>(
            () => controller.Upload(item.Id, new FormFile(new MemoryStream([1]), 0, ItemImageService.MaxBytes + 1, "file", "big.png"),
                null, false, token))).ErrorCode);
        var png = CompanyImageTests.Png(320, 240);
        var created = Assert.IsType<ItemImageView>(Assert.IsType<ObjectResult>((await controller.Upload(item.Id,
            new FormFile(new MemoryStream(png), 0, png.Length, "file", "api.png"), null, false, token)).Result).Value);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>((await controller.Upload(item.Id,
            new FormFile(new MemoryStream(png), 0, png.Length, "file", "api2.png"), null, false, token)).Result).StatusCode);
        Assert.Equal(2, (await controller.List(item.Id, token)).Count);

        var file = Assert.IsType<FileContentResult>(await controller.Content(item.Id, created.Id, token));
        Assert.Equal(png, file.FileContents);
        Assert.Equal(("image/png", $"\"{created.Version}\""), (file.ContentType, file.EntityTag!.Tag.ToString()));
        Assert.Contains("inline", controller.Response.Headers.ContentDisposition.ToString());
        controller.ControllerContext.HttpContext = new DefaultHttpContext();
        controller.Request.Headers.IfNoneMatch = $"\"{created.Version}\"";
        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeResult>(await controller.Content(item.Id, created.Id, token)).StatusCode);

        var updated = await controller.Update(item.Id, created.Id, new UpdateItemImageRequest(7, true, created.Version), token);
        Assert.Equal(7, updated.SortOrder);
        Assert.IsType<NoContentResult>(await controller.Delete(item.Id, created.Id, updated.Version, token));
        Assert.True(Assert.Single(await controller.List(item.Id, token)).IsPrimary);
    }

    [Fact]
    public async Task SortPrimaryAndDeleteDoNotLoadOrAlterImageContent()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(58000000, 59000000);
        await using var setup = Context(companyId);
        var item = await NewItemAsync(setup, token);
        var png = CompanyImageTests.Png(64, 64);
        var jpeg = CompanyImageTests.Jpeg(32, 32);
        var first = await UploadAsync(new ItemImageService(setup), item.Id, png, token: token);
        var second = await UploadAsync(new ItemImageService(setup), item.Id, jpeg, token: token);

        // 新的 DbContext：修改主圖與排序只附加中繼資料，不讀取內容（MD-B08），仍遞增版本。
        await using var db = Context(companyId);
        var service = new ItemImageService(db);
        var moved = await service.UpdateAsync(item.Id, second.Id, 7, true, second.Version, token);
        Assert.Equal((7, true, second.Version + 1), (moved.SortOrder, moved.IsPrimary, moved.Version));
        var tracked = db.ChangeTracker.Entries<ItemImage>().ToList();
        Assert.Equal(2, tracked.Count);
        Assert.All(tracked, x => Assert.Null(x.Entity.Content));
        Assert.Equal(jpeg, (await service.GetContentAsync(item.Id, second.Id, token)).Content);
        Assert.Equal(png, (await service.GetContentAsync(item.Id, first.Id, token)).Content);

        // 刪除主圖後由另一張接任；內容不受影響；過期版本 409。
        await service.DeleteAsync(item.Id, second.Id, moved.Version, token);
        var remaining = Assert.Single(await service.ListAsync(item.Id, token));
        Assert.Equal((first.Id, true), (remaining.Id, remaining.IsPrimary));
        Assert.Equal(png, (await service.GetContentAsync(item.Id, first.Id, token)).Content);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(item.Id, first.Id, 1, true, first.Version, token));
    }

    [Fact]
    public async Task ConcurrentFirstUploadsProduceOnePrimaryAndTableUsesCompanyScopedKeys()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(57000000, 58000000);
        await using var setup = Context(companyId);
        var item = await NewItemAsync(setup, token);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId);
        var results = await Task.WhenAll(
            UploadAsync(new ItemImageService(firstDb), item.Id, CompanyImageTests.Png(64, 64), token: token),
            UploadAsync(new ItemImageService(secondDb), item.Id, CompanyImageTests.Jpeg(64, 64), token: token));
        Assert.Single(results, x => x.IsPrimary);
        Assert.Equal([1, 2], results.Select(x => x.SortOrder).Order());

        var indexes = await setup.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('md_item_image')").ToListAsync(token);
        Assert.Contains("ux_md_item_image_company_id_item_id_primary", indexes);
        Assert.Contains("ix_md_item_image_company_id_item_id_sort_order", indexes);
        var foreignKeys = await setup.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('md_item_image')").ToListAsync(token);
        Assert.Equal(["fk_md_item_image_md_item_company_id_item_id"], foreignKeys);
        var checks = await setup.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('md_item_image')").ToListAsync(token);
        Assert.Contains("ck_md_item_image_content_type", checks);
        Assert.Contains("ck_md_item_image_sort_order", checks);
    }
}
