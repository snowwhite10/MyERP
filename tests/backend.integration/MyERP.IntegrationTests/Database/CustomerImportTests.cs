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

public sealed class CustomerImportTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(database.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel(), new CustomerCategoryDbModel(), new CustomerDbModel(), new CustomerContactDbModel()], company);
    }

    private static byte[] Csv(string rows) => Encoding.UTF8.GetBytes(
        "code,name,categoryCode,contactName,contactMobile,contactPhone,contactEmail,address,postalCode,recipient,addressPhone\r\n" + rows);

    [Fact]
    public async Task PreviewIsReadOnlyAndCommitIsAtomicIdempotentAndCompanyScoped()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(44000000, 45000000);
        await using var db = Context(companyId);
        await using var other = Context(companyId + 1);
        var category = await new CustomerCategoryService(db).CreateAsync("WHOLESALE", "Wholesale", token);
        var existing = await new CustomerService(db).CreateAsync("EXISTING", "Original", null, token);
        var file = Csv("EXISTING,Changed,,,,,,,,,\r\nNEW1,New One,WHOLESALE,Amy,123,,amy@example.com,Taipei,100,Amy,456\r\nNEW2,New Two,,,,,,,,,\r\n");
        var service = new CustomerImportService(db);

        var preview = await service.PreviewAsync(file, token);
        Assert.Equal(["Skip", "Create", "Create"], preview.Rows.Select(x => x.Status));
        Assert.Equal([2, 3, 4], preview.Rows.Select(x => x.RowNumber));
        Assert.Equal(0, await db.Set<Customer>().CountAsync(x => x.Code == "NEW1", token));

        var committed = await service.CommitAsync(file, token);
        Assert.Equal((2, 1), (committed.CreatedCount, committed.SkippedCount));
        var created = await db.Set<Customer>().SingleAsync(x => x.Code == "NEW1", token);
        Assert.Equal(category.Id, created.CategoryId);
        Assert.Equal("Original", (await db.Set<Customer>().SingleAsync(x => x.Id == existing.Id, token)).Name);
        Assert.True((await db.Set<CustomerContact>().SingleAsync(x => x.PartnerId == created.Id, token)).IsDefault);
        Assert.True((await db.Set<CustomerAddress>().SingleAsync(x => x.PartnerId == created.Id, token)).IsDefault);
        Assert.Equal((0, 3), ((await service.CommitAsync(file, token)).CreatedCount, (await service.PreviewAsync(file, token)).SkippedCount));
        Assert.Equal("Create", Assert.Single((await new CustomerImportService(other).PreviewAsync(Csv("NEW1,Other,,,,,,,,,\r\n"), token)).Rows).Status);
    }

    [Fact]
    public async Task InvalidRowsBlockEntireCommitAndReportFieldErrors()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(45000000, 46000000));
        var service = new CustomerImportService(db);
        var file = Csv("GOOD,Good,,,,,,,,,\r\nBAD,Bad,MISSING,Amy,,,,,, ,\r\nGOOD,Again,,,,,,,,,\r\n");
        var preview = await service.PreviewAsync(file, token);
        Assert.Equal(["Create", "Error", "Error"], preview.Rows.Select(x => x.Status));
        Assert.Contains(preview.Rows[1].Errors, x => x.Field == "categoryCode" && x.Code == "NOT_FOUND");
        Assert.Contains(preview.Rows[2].Errors, x => x.Field == "code" && x.Code == "DUPLICATE_IN_FILE");
        Assert.Equal("MD.CUSTOMER_IMPORT.HAS_ERRORS", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CommitAsync(file, token))).ErrorCode);
        Assert.Equal(0, await db.Set<Customer>().CountAsync(x => x.Code == "GOOD", token));
        var malformed = await service.PreviewAsync(Csv("BAD2,Bad,,Amy,not-phone,,wrong-email,,,,\r\n"), token);
        Assert.Contains(Assert.Single(malformed.Rows).Errors, x => x.Field == "contactEmail" && x.Code == "INVALID");
    }

    [Fact]
    public async Task ApiExposesProtectedTemplatesPreviewAndCommit()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(46000000, 47000000));
        var controller = new CustomerImportsController(new CustomerImportService(db));
        foreach (var name in new[] { nameof(CustomerImportsController.Template), nameof(CustomerImportsController.Preview),
                     nameof(CustomerImportsController.Commit) })
            Assert.Equal(MasterDataPermissions.CustomerImport,
                Assert.Single(typeof(CustomerImportsController).GetMethod(name)!
                    .GetCustomAttributes(typeof(RequirePermissionAttribute), false).Cast<RequirePermissionAttribute>()).Code);
        Assert.Contains(new MasterDataPermissions().Permissions, x => x.Code == MasterDataPermissions.CustomerImport);
        var csv = Assert.IsType<FileContentResult>(controller.Template("csv"));
        Assert.Equal(TabularTemplate.CsvContentType, csv.ContentType);
        Assert.Equal(CustomerImportService.Columns, TabularFileReader.Read(csv.FileContents, 1000).Headers);
        var xlsx = Assert.IsType<FileContentResult>(controller.Template("xlsx"));
        Assert.Equal(CustomerImportService.Columns, TabularFileReader.Read(xlsx.FileContents, 1000).Headers);
        Assert.Equal("format", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => Task.FromResult(controller.Template("xls")))).Errors).Key);
        var bytes = Csv("API1,API Customer,,,,,,,,,\r\n");
        IFormFile file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "customers.csv");
        Assert.Equal("Create", Assert.Single((await controller.Preview(file, token)).Rows).Status);
        Assert.Equal(1, (await controller.Commit(file, token)).CreatedCount);
        Assert.Equal(1, (await controller.Commit(file, token)).SkippedCount);
        Assert.Equal("COMMON.IMPORT.TOO_LARGE", (await Assert.ThrowsAsync<BusinessRuleException>(
            () => controller.Preview(new FormFile(new MemoryStream(new byte[1]), 0,
                TabularFileReader.MaxBytes + 1, "file", "large.csv"), token))).ErrorCode);
    }

    [Fact]
    public async Task ConcurrentImportsSerializeByCompanyAndCreateOneCustomer()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(47000000, 48000000);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId);
        var file = Csv("RACE1,Concurrent,,,,,,,,,\r\n");
        var first = new CustomerImportService(firstDb).CommitAsync(file, token);
        var second = new CustomerImportService(secondDb).CommitAsync(file, token);
        var outcomes = await Task.WhenAll(first, second);
        Assert.Equal(1, outcomes.Sum(x => x.CreatedCount));
        Assert.Equal(1, outcomes.Sum(x => x.SkippedCount));
        Assert.Equal(1, await firstDb.Set<Customer>().AsNoTracking().CountAsync(x => x.Code == "RACE1", token));
    }
}
