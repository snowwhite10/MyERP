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
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Import;
using MyERP.Shared.Infrastructure.Security;

namespace MyERP.IntegrationTests.Database;

/// <summary>供應商匯入（ADR-044）：沿用客戶匯入的往來對象共用實作；供應商只有聯絡人、沒有地址欄（ADR-043）。</summary>
public sealed class SupplierImportTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel(), new SupplierCategoryDbModel(), new SupplierDbModel(), new SupplierContactDbModel()], company);
    }

    private static byte[] Csv(string rows) => Encoding.UTF8.GetBytes(
        "code,name,categoryCode,contactName,contactMobile,contactPhone,contactEmail\r\n" + rows);

    [Fact]
    public async Task PreviewIsReadOnlyAndCommitIsAtomicIdempotentAndCompanyScoped()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(48000000, 49000000);
        await using var db = Context(companyId);
        await using var other = Context(companyId + 1);
        var category = await new SupplierCategoryService(db).CreateAsync("RAW", "Raw material", token);
        var existing = await new SupplierService(db).CreateAsync("EXISTING", "Original", null, token);
        var file = Csv("existing,Changed,,,,,\r\nNEW1,New One,raw,Amy,123,,amy@example.com\r\nNEW2,New Two,,,,,\r\n");
        var service = new SupplierImportService(db);

        var preview = await service.PreviewAsync(file, token);
        Assert.Equal(["Skip", "Create", "Create"], preview.Rows.Select(x => x.Status));
        Assert.Equal([2, 3, 4], preview.Rows.Select(x => x.RowNumber));
        Assert.Equal(0, await db.Set<Supplier>().CountAsync(x => x.Code == "NEW1", token));

        var committed = await service.CommitAsync(file, token);
        Assert.Equal((2, 1), (committed.CreatedCount, committed.SkippedCount));
        var created = await db.Set<Supplier>().SingleAsync(x => x.Code == "NEW1", token);
        Assert.Equal(category.Id, created.CategoryId);
        Assert.Equal("Original", (await db.Set<Supplier>().SingleAsync(x => x.Id == existing.Id, token)).Name);
        var contact = await db.Set<SupplierContact>().SingleAsync(x => x.PartnerId == created.Id, token);
        Assert.Equal(("Amy", "123", "amy@example.com", true), (contact.Name, contact.Mobile, contact.Email, contact.IsDefault));
        var plain = await db.Set<Supplier>().SingleAsync(x => x.Code == "NEW2", token);
        Assert.False(await db.Set<SupplierContact>().AnyAsync(x => x.PartnerId == plain.Id, token));
        Assert.Equal((0, 3), ((await service.CommitAsync(file, token)).CreatedCount, (await service.PreviewAsync(file, token)).SkippedCount));
        Assert.Equal("Create", Assert.Single((await new SupplierImportService(other).PreviewAsync(Csv("NEW1,Other,,,,,\r\n"), token)).Rows).Status);
    }

    [Fact]
    public async Task InvalidRowsBlockEntireCommitAndReportFieldErrors()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(49000000, 50000000));
        var categories = new SupplierCategoryService(db);
        var old = await categories.CreateAsync("OLD", "Old", token);
        await categories.UpdateAsync(old.Id, "Old", false, old.Version, token);
        var service = new SupplierImportService(db);
        var file = Csv("GOOD,Good,,,,,\r\nBAD,Bad,MISSING,,,,\r\nGOOD,Again,,,,,\r\nLATE,Late,OLD,,,,\r\nNOCONTACT,No contact,,,0912,,\r\n");
        var preview = await service.PreviewAsync(file, token);
        Assert.Equal(["Create", "Error", "Error", "Error", "Error"], preview.Rows.Select(x => x.Status));
        Assert.Contains(preview.Rows[1].Errors, x => x.Field == "categoryCode" && x.Code == "NOT_FOUND");
        Assert.Contains(preview.Rows[2].Errors, x => x.Field == "code" && x.Code == "DUPLICATE_IN_FILE");
        Assert.Contains(preview.Rows[3].Errors, x => x.Field == "categoryCode" && x.Code == "NOT_FOUND");
        Assert.Contains(preview.Rows[4].Errors, x => x.Field == "contactName" && x.Code == "REQUIRED");
        Assert.Equal("MD.SUPPLIER_IMPORT.HAS_ERRORS", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CommitAsync(file, token))).ErrorCode);
        Assert.Equal(0, await db.Set<Supplier>().CountAsync(x => x.Code == "GOOD", token));
        var malformed = await service.PreviewAsync(Csv("BAD2,Bad,,Amy,,,wrong-email\r\n"), token);
        Assert.Contains(Assert.Single(malformed.Rows).Errors, x => x.Field == "contactEmail" && x.Code == "INVALID");
        var withAddress = await service.PreviewAsync(Encoding.UTF8.GetBytes("code,name,address\r\nADDR1,Ignored address,Taipei\r\n"), token);
        Assert.Equal("Create", Assert.Single(withAddress.Rows).Status);
    }

    [Fact]
    public async Task ApiExposesProtectedTemplatesPreviewAndCommit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(50000000, 51000000));
        var controller = new SupplierImportsController(new SupplierImportService(db));
        foreach (var name in new[] { nameof(SupplierImportsController.Template), nameof(SupplierImportsController.Preview),
                     nameof(SupplierImportsController.Commit) })
            Assert.Equal(MasterDataPermissions.SupplierImport,
                Assert.Single(typeof(SupplierImportsController).GetMethod(name)!
                    .GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>()).Code);
        Assert.Contains(new MasterDataPermissions().Permissions, x => x.Code == MasterDataPermissions.SupplierImport);
        Assert.DoesNotContain("address", SupplierImportService.Columns);
        var csv = Assert.IsType<FileContentResult>(controller.Template("csv"));
        Assert.Equal(("supplier-import.csv", TabularTemplate.CsvContentType), (csv.FileDownloadName, csv.ContentType));
        Assert.Equal(SupplierImportService.Columns, TabularFileReader.Read(csv.FileContents, 1000).Headers);
        var xlsx = Assert.IsType<FileContentResult>(controller.Template("xlsx"));
        Assert.Equal(SupplierImportService.Columns, TabularFileReader.Read(xlsx.FileContents, 1000).Headers);
        Assert.Equal("format", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => Task.FromResult(controller.Template("xls")))).Errors).Key);
        var bytes = Csv("API1,API Supplier,,,,,\r\n");
        IFormFile file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "suppliers.csv");
        Assert.Equal("Create", Assert.Single((await controller.Preview(file, token)).Rows).Status);
        Assert.Equal(1, (await controller.Commit(file, token)).CreatedCount);
        Assert.Equal(1, (await controller.Commit(file, token)).SkippedCount);
        Assert.Equal("COMMON.IMPORT.TOO_LARGE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => controller.Preview(new FormFile(new MemoryStream(new byte[1]), 0,
                TabularFileReader.MaxBytes + 1, "file", "large.csv"), token))).ErrorCode);
    }

    [Fact]
    public async Task ConcurrentImportsSerializeByCompanyAndCreateOneSupplier()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(51000000, 52000000);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId);
        var file = Csv("RACE1,Concurrent,,,,,\r\n");
        var outcomes = await Task.WhenAll(new SupplierImportService(firstDb).CommitAsync(file, token),
            new SupplierImportService(secondDb).CommitAsync(file, token));
        Assert.Equal((1, 1), (outcomes.Sum(x => x.CreatedCount), outcomes.Sum(x => x.SkippedCount)));
        Assert.Equal(1, await firstDb.Set<Supplier>().AsNoTracking().CountAsync(x => x.Code == "RACE1", token));
    }
}
