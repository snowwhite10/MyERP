using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

public sealed class ItemBrandTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new ItemBrandDbModel()], company);
    }

    [Fact]
    public async Task ItemBrandLifecycleIsCompanyScopedAndDeletedCodesStayReserved()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(11000000, 12000000);
        await using var firstDb = Context(companyId);
        await using var secondDb = Context(companyId + 1);
        var first = new ItemBrandService(firstDb);
        var second = new ItemBrandService(secondDb);
        var code = ("B" + Guid.NewGuid().ToString("N")[..12]).ToUpperInvariant();
        var created = await first.CreateAsync(code.ToLowerInvariant(), " Acme ", token);
        Assert.Equal(code, created.Code);
        Assert.Equal("Acme", created.Name);
        Assert.True(created.IsActive);
        Assert.Equal(7, created.CreatedBy);
        Assert.Equal(0, (await second.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<NotFoundException>(() => second.UpdateAsync(created.Id, "Other", false, created.Version, token));
        await Assert.ThrowsAsync<NotFoundException>(() => second.DeleteAsync(created.Id, created.Version, token));
        Assert.NotEqual(created.Id, (await second.CreateAsync(code, "Separate company", token)).Id);
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Duplicate", token));
        Assert.Equal("MD.ITEM_BRAND.DUPLICATE_CODE", duplicate.ErrorCode);
        var staleVersion = created.Version;
        var updated = await first.UpdateAsync(created.Id, " Acme Pro ", false, created.Version, token);
        Assert.Equal("Acme Pro", updated.Name);
        Assert.False(updated.IsActive);
        await Assert.ThrowsAsync<ConflictException>(() => first.UpdateAsync(created.Id, "Stale", true, staleVersion, token));
        await first.DeleteAsync(created.Id, updated.Version, token);
        Assert.Equal(0, (await first.ListAsync(new PagedRequest { Keyword = code }, token)).TotalCount);
        await Assert.ThrowsAsync<ConflictException>(() => first.CreateAsync(code, "Reused", token));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => first.DeleteAsync(created.Id, updated.Version, token));
        Assert.Equal("MD.ITEM_BRAND.NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task ItemBrandListSortsPagesAndValidatesInput()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(12000000, 13000000);
        await using var db = Context(companyId);
        var service = new ItemBrandService(db);
        var prefix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await service.CreateAsync(prefix + "A", "Zeta", token);
        await service.CreateAsync(prefix + "B", "Alpha", token);
        var page = await service.ListAsync(new PagedRequest { Page = 2, PageSize = 1, Sort = "code", Keyword = prefix }, token);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(prefix + "B", Assert.Single(page.Items).Code);
        var byName = await service.ListAsync(new PagedRequest { Sort = "-name", Keyword = prefix }, token);
        Assert.Equal(["Zeta", "Alpha"], byName.Items.Select(x => x.Name));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync("bad code", "Name", token));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.CreateAsync(prefix + "C", new string('N', 201), token));
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemBrandService(noCompany).ListAsync(new PagedRequest(), token));
    }

    [Fact]
    public async Task ItemBrandHasCompanyScopedAlternateKeyForItemForeignKeys()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('md_item_brand')")
            .ToListAsync(token);
        Assert.Contains("pk_md_item_brand", keys);
        Assert.Contains("ak_md_item_brand_company_id_id", keys);
    }
}
