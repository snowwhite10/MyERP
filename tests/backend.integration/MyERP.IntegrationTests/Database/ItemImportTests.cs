using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData;
using MyERP.Modules.MasterData.Api;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Import;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

public sealed class ItemImportTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new ItemDbModel(), new UomDbModel(), new ItemBrandDbModel(), new ItemCategoryDbModel()], company);
    }

    private static byte[] Csv(string rows) => Encoding.UTF8.GetBytes(
        "code,name,specification,baseUomCode,brandCode,categoryCode\r\n" + rows);

    [Fact]
    public async Task PreviewReportsRowsAndCommitIsAtomicIdempotentAndCompanyScoped()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(52000000, 53000000);
        await using var db = Context(companyId);
        await using var other = Context(companyId + 1);
        var uom = await new UomService(db).CreateAsync("PCS", "Pieces", 0, token);
        var brand = await new ItemBrandService(db).CreateAsync("ACME", "Acme", token);
        var category = await new ItemCategoryService(db).CreateAsync("RAW", "Raw", token);
        var existing = await new ItemService(db).CreateAsync("OLD", "Original", null, uom.Id, null, null, token);
        var service = new ItemImportService(db);
        var file = Csv("old,Changed,,PCS,,\r\nNEW1,New One, Spec ,pcs,acme,raw\r\nNEW2,New Two,,PCS,,\r\n");
        var preview = await service.PreviewAsync(file, token);
        Assert.Equal(["Skip", "Create", "Create"], preview.Rows.Select(x => x.Status));
        Assert.Equal([2, 3, 4], preview.Rows.Select(x => x.RowNumber));
        Assert.False(await db.Set<Item>().AnyAsync(x => x.Code == "NEW1", token));
        var result = await service.CommitAsync(file, token);
        Assert.Equal((2, 1), (result.CreatedCount, result.SkippedCount));
        var created = await db.Set<Item>().SingleAsync(x => x.Code == "NEW1", token);
        Assert.Equal(("Spec", uom.Id, brand.Id, category.Id),
            (created.Specification, created.BaseUomId, created.BrandId, created.CategoryId));
        Assert.Equal("Original", (await db.Set<Item>().SingleAsync(x => x.Id == existing.Id, token)).Name);
        Assert.Equal((0, 3), ((await service.CommitAsync(file, token)).CreatedCount,
            (await service.PreviewAsync(file, token)).SkippedCount));
        Assert.Equal("Error", Assert.Single((await new ItemImportService(other).PreviewAsync(
            Csv("NEW1,Other,,PCS,,\r\n"), token)).Rows).Status);
    }

    [Fact]
    public async Task InvalidReferencesAndDuplicateRowsBlockEntireCommit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(53000000, 54000000));
        var uom = await new UomService(db).CreateAsync("PCS", "Pieces", 0, token);
        var service = new ItemImportService(db);
        var file = Csv("GOOD,Good,,PCS,,\r\nBAD,Bad,,MISSING,,\r\nGOOD,Again,,PCS,,\r\nNO_NAME,,,PCS,,\r\nNO_BRAND,Name,,PCS,MISSING,\r\n");
        var preview = await service.PreviewAsync(file, token);
        Assert.Equal(["Create", "Error", "Error", "Error", "Error"], preview.Rows.Select(x => x.Status));
        Assert.Equal([2, 3, 4, 5, 6], preview.Rows.Select(x => x.RowNumber));
        Assert.Contains(preview.Rows[1].Errors, x => x.Field == "baseUomCode" && x.Code == "NOT_FOUND");
        Assert.Contains(preview.Rows[2].Errors, x => x.Field == "code" && x.Code == "DUPLICATE_IN_FILE");
        Assert.Contains(preview.Rows[3].Errors, x => x.Field == "name" && x.Code == "REQUIRED");
        Assert.Contains(preview.Rows[4].Errors, x => x.Field == "brandCode" && x.Code == "NOT_FOUND");
        Assert.Equal("MD.ITEM_IMPORT.HAS_ERRORS", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CommitAsync(file, token))).ErrorCode);
        Assert.False(await db.Set<Item>().AnyAsync(x => x.Code == "GOOD", token));
        await new UomService(db).UpdateAsync(uom.Id, "Pieces", false, 0, uom.Version, token);
        Assert.Contains(Assert.Single((await service.PreviewAsync(Csv("LATE,Late,,PCS,,\r\n"), token)).Rows).Errors,
            x => x.Field == "baseUomCode" && x.Code == "NOT_FOUND");
    }

    [Fact]
    public async Task ApiExposesProtectedTemplatesPreviewAndCommit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(54000000, 55000000));
        await new UomService(db).CreateAsync("PCS", "Pieces", 0, token);
        var controller = new ItemImportsController(new ItemImportService(db));
        foreach (var name in new[] { nameof(ItemImportsController.Template), nameof(ItemImportsController.Preview),
                     nameof(ItemImportsController.Commit) })
            Assert.Equal(MasterDataPermissions.ItemImport,
                Assert.Single(typeof(ItemImportsController).GetMethod(name)!
                    .GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>()).Code);
        foreach (var format in new[] { "csv", "xlsx" })
        {
            var template = Assert.IsType<FileContentResult>(controller.Template(format));
            Assert.Equal(ItemImportService.Columns, TabularFileReader.Read(template.FileContents, 1000).Headers);
        }
        var bytes = Csv("API1,API Item,,PCS,,\r\n");
        IFormFile file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "items.csv");
        Assert.Equal("Create", Assert.Single((await controller.Preview(file, token)).Rows).Status);
        Assert.Equal(1, (await controller.Commit(file, token)).CreatedCount);
        Assert.Equal(1, (await controller.Commit(file, token)).SkippedCount);
    }

    [Fact]
    public async Task CommitRevalidatesChangedReferencesAndSerializesConcurrentImports()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(55000000, 56000000);
        await using var first = Context(companyId);
        await using var second = Context(companyId);
        var uom = await new UomService(first).CreateAsync("PCS", "Pieces", 0, token);
        var file = Csv("RACE1,Concurrent,,PCS,,\r\n");
        Assert.Equal("Create", Assert.Single((await new ItemImportService(first).PreviewAsync(file, token)).Rows).Status);
        var outcomes = await Task.WhenAll(new ItemImportService(first).CommitAsync(file, token),
            new ItemImportService(second).CommitAsync(file, token));
        Assert.Equal((1, 1), (outcomes.Sum(x => x.CreatedCount), outcomes.Sum(x => x.SkippedCount)));
        Assert.Equal(1, await first.Set<Item>().AsNoTracking().CountAsync(x => x.Code == "RACE1", token));

        var later = Csv("LATER,Later,,PCS,,\r\n");
        Assert.Equal("Create", Assert.Single((await new ItemImportService(first).PreviewAsync(later, token)).Rows).Status);
        await new UomService(first).UpdateAsync(uom.Id, "Pieces", false, 0, uom.Version, token);
        Assert.Equal("MD.ITEM_IMPORT.HAS_ERRORS", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => new ItemImportService(second).CommitAsync(later, token))).ErrorCode);
        Assert.False(await first.Set<Item>().AnyAsync(x => x.Code == "LATER", token));
    }

    [Fact]
    public async Task PreviewReportsActualRowNumberAndFileErrors()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(56000000, 57000000));
        var service = new ItemImportService(db);
        var rows = (await service.PreviewAsync(Csv("\r\nBAD,Name,,MISSING,,\r\n"), token)).Rows;
        Assert.Equal(3, Assert.Single(rows).RowNumber);
        var missing = Encoding.UTF8.GetBytes("code,name\r\nBAD,Name\r\n");
        Assert.Equal(ImportErrorCodes.MissingColumn, (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.PreviewAsync(missing, token))).ErrorCode);
        var longSpec = new string('x', 501);
        Assert.Contains(Assert.Single((await service.PreviewAsync(Csv($"BAD,Name,{longSpec},MISSING,,\r\n"), token)).Rows).Errors,
            x => x.Field == "specification" && x.Code == "INVALID");
    }
}
