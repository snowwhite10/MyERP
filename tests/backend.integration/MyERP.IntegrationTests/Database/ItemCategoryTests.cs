using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

public sealed class ItemCategoryTests(TestDatabase database)
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

    private static string Code(string suffix) => Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() + suffix;

    [Fact]
    public async Task ItemCategoryTreeIsCompanyScopedOrderedAndKeepsParents()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(13000000, 14000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var service = new ItemCategoryService(db);
        var other = new ItemCategoryService(otherDb);
        var root = await service.CreateAsync(Code("R").ToLowerInvariant(), " Food ", null, token);
        Assert.Null(root.ParentId);
        Assert.Equal("Food", root.Name);
        Assert.Equal(7, root.CreatedBy);
        var child = await service.CreateAsync(Code("C"), "Fruit", root.Id, token);
        Assert.Equal(root.Id, child.ParentId);
        var tree = await service.TreeAsync(token);
        Assert.Equal(2, tree.Count);
        Assert.Equal(tree.OrderBy(x => x.Code, StringComparer.Ordinal).Select(x => x.Id), tree.Select(x => x.Id));
        Assert.Empty(await other.TreeAsync(token));
        var foreign = await Assert.ThrowsAsync<BusinessRuleException>(() => other.CreateAsync(Code("X"), "Foreign", root.Id, token));
        Assert.Equal("MD.ITEM_CATEGORY.INVALID_PARENT", foreign.ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => other.UpdateAsync(child.Id, "X", true, null, child.Version, token));
        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(root.Code, "Again", null, token));
        Assert.Equal("MD.ITEM_CATEGORY.DUPLICATE_CODE", duplicate.ErrorCode);
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new ItemCategoryService(noCompany).TreeAsync(token));
    }

    [Fact]
    public async Task ItemCategoryMoveRejectsSelfDescendantsAndInactiveParents()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(14000000, 15000000));
        var service = new ItemCategoryService(db);
        var a = await service.CreateAsync(Code("A"), "A", null, token);
        var b = await service.CreateAsync(Code("B"), "B", a.Id, token);
        var c = await service.CreateAsync(Code("C"), "C", b.Id, token);
        var self = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(a.Id, "A", true, a.Id, a.Version, token));
        Assert.Equal("MD.ITEM_CATEGORY.CYCLE", self.ErrorCode);
        var cycle = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(a.Id, "A", true, c.Id, a.Version, token));
        Assert.Equal("MD.ITEM_CATEGORY.CYCLE", cycle.ErrorCode);
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateAsync(c.Id, "C", true, -1, c.Version, token));
        Assert.Equal("MD.ITEM_CATEGORY.INVALID_PARENT", missing.ErrorCode);

        var inactive = await service.CreateAsync(Code("I"), "Inactive", null, token);
        inactive = await service.UpdateAsync(inactive.Id, "Inactive", false, null, inactive.Version, token);
        var underInactive = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.CreateAsync(Code("D"), "D", inactive.Id, token));
        Assert.Equal("MD.ITEM_CATEGORY.INVALID_PARENT", underInactive.ErrorCode);

        // 把 C 移到根，再把 A 移到 C 底下：A→B 子樹跟著移動。
        c = await service.UpdateAsync(c.Id, "C moved", true, null, c.Version, token);
        Assert.Null(c.ParentId);
        a = await service.UpdateAsync(a.Id, "A", true, c.Id, a.Version, token);
        Assert.Equal(c.Id, a.ParentId);
        // 父分類停用後，子分類保留原父分類仍可修改名稱。
        c = await service.UpdateAsync(c.Id, "C", false, null, c.Version, token);
        a = await service.UpdateAsync(a.Id, "A renamed", true, c.Id, a.Version, token);
        Assert.Equal("A renamed", a.Name);
        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(b.Id, "Stale", true, a.Id, b.Version - 1, token));
    }

    [Fact]
    public async Task ItemCategoryDepthIsLimitedIncludingMovedSubtrees()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(15000000, 16000000));
        var service = new ItemCategoryService(db);
        long? parent = null;
        var chain = new List<ItemCategory>();
        for (var level = 1; level <= ItemCategory.MaxDepth; level++)
        {
            var node = await service.CreateAsync(Code("L" + level), "Level " + level, parent, token);
            chain.Add(node);
            parent = node.Id;
        }
        var tooDeep = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Code("L6"), "Level 6", parent, token));
        Assert.Equal("MD.ITEM_CATEGORY.TOO_DEEP", tooDeep.ErrorCode);
        var other = await service.CreateAsync(Code("O"), "Other root", null, token);
        var otherChild = await service.CreateAsync(Code("OC"), "Other child", other.Id, token);
        // 兩層子樹放在第 4 層底下會變成 6 層。
        var moveDeep = await Assert.ThrowsAsync<BusinessRuleException>(
            () => service.UpdateAsync(other.Id, other.Name, true, chain[3].Id, other.Version, token));
        Assert.Equal("MD.ITEM_CATEGORY.TOO_DEEP", moveDeep.ErrorCode);
        var moved = await service.UpdateAsync(other.Id, other.Name, true, chain[2].Id, other.Version, token);
        Assert.Equal(chain[2].Id, moved.ParentId);
        Assert.Equal(other.Id, (await db.Set<ItemCategory>().AsNoTracking().SingleAsync(x => x.Id == otherChild.Id, token)).ParentId);
    }

    [Fact]
    public async Task ItemCategoryWithChildrenCannotBeDeletedButLeavesCan()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(16000000, 17000000));
        var service = new ItemCategoryService(db);
        var root = await service.CreateAsync(Code("R"), "Root", null, token);
        var leaf = await service.CreateAsync(Code("L"), "Leaf", root.Id, token);
        var hasChildren = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(root.Id, root.Version, token));
        Assert.Equal("MD.ITEM_CATEGORY.HAS_CHILDREN", hasChildren.ErrorCode);
        await Assert.ThrowsAsync<ConflictException>(() => service.DeleteAsync(leaf.Id, leaf.Version + 1, token));
        await service.DeleteAsync(leaf.Id, leaf.Version, token);
        await service.DeleteAsync(root.Id, root.Version, token);
        Assert.DoesNotContain(await service.TreeAsync(token), x => x.Id == root.Id || x.Id == leaf.Id);
        var gone = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(Code("N"), "New", root.Id, token));
        Assert.Equal("MD.ITEM_CATEGORY.INVALID_PARENT", gone.ErrorCode);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(root.Id, root.Version, token));
        Assert.Equal("MD.ITEM_CATEGORY.NOT_FOUND", missing.ErrorCode);
    }

    [Fact]
    public async Task ItemCategoryParentForeignKeyRejectsOtherCompanyAtDatabaseLevel()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(17000000, 18000000);
        await using var db = Context(companyId);
        var root = await new ItemCategoryService(db).CreateAsync(Code("R"), "Root", null, token);
        await using var otherDb = Context(companyId + 1);
        otherDb.Add(new ItemCategory { CompanyId = companyId + 1, Code = Code("X"), Name = "Cross", ParentId = root.Id });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => otherDb.SaveChangesAsync(token));
        Assert.Equal(547, Assert.IsType<SqlException>(error.InnerException).Number);
        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('md_item_category')")
            .ToListAsync(token);
        Assert.Contains("ak_md_item_category_company_id_id", keys);
    }

    [Fact]
    public async Task ConcurrentOppositeMovesCannotCreateCycle()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(18000000, 19000000);
        await using var setup = Context(companyId);
        var setupService = new ItemCategoryService(setup);
        var x = await setupService.CreateAsync(Code("X"), "X", null, token);
        var y = await setupService.CreateAsync(Code("Y"), "Y", null, token);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var firstDb = Context(companyId);
            await using var secondDb = Context(companyId);
            var moveX = new ItemCategoryService(firstDb).UpdateAsync(x.Id, "X", true, y.Id, x.Version, token);
            var moveY = new ItemCategoryService(secondDb).UpdateAsync(y.Id, "Y", true, x.Id, y.Version, token);
            var results = await Task.WhenAll(Settle(moveX), Settle(moveY));
            Assert.Equal(1, results.Count(r => r is null));
            await using var check = Context(companyId);
            var nodes = await check.Set<ItemCategory>().AsNoTracking()
                .Where(n => n.Id == x.Id || n.Id == y.Id).ToListAsync(token);
            Assert.Contains(nodes, n => n.ParentId is null);
            // 還原為兩個根節點再試。
            var service = new ItemCategoryService(check);
            foreach (var node in nodes.Where(n => n.ParentId is not null))
                await service.UpdateAsync(node.Id, node.Name, true, null, node.Version, token);
            x = await check.Set<ItemCategory>().AsNoTracking().SingleAsync(n => n.Id == x.Id, token);
            y = await check.Set<ItemCategory>().AsNoTracking().SingleAsync(n => n.Id == y.Id, token);
        }
    }

    private static async Task<Exception?> Settle(Task task)
    {
        try { await task; return null; }
        catch (ErpException ex) { return ex; }
    }
}
