using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>供應商聯絡人（ADR-043）：沿用客戶聯絡人共用實作，驗證公司隔離、唯一預設與資料庫防線。</summary>
public sealed class SupplierContactTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel(), new SupplierCategoryDbModel(), new SupplierDbModel(), new SupplierContactDbModel()], company);
    }

    private static async Task<Supplier> NewSupplierAsync(MyErpDbContext db, CancellationToken token) =>
        await new SupplierService(db).CreateAsync("S" + Guid.NewGuid().ToString("N")[..12], "Supplier", null, token);

    [Fact]
    public async Task ContactsKeepOneDefaultPerSupplierAndAreCompanyScoped()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(30000000, 31000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var contacts = new SupplierContactService(db);
        var supplier = await NewSupplierAsync(db, token);
        var another = await NewSupplierAsync(db, token);

        var amy = await contacts.CreateAsync(supplier.Id, new ContactInput(" Amy ", " Sales ", " 0912-345678 ", " 02-1234 ",
            " amy@example.com ", " amy_line ", " Quotes ", IsDefault: true), token);
        Assert.Equal((supplier.Id, "Amy", "Sales", "0912-345678", "02-1234", "amy@example.com", "amy_line", "Quotes", true, true, 7L),
            (amy.PartnerId, amy.Name, amy.Position, amy.Mobile, amy.Phone, amy.Email, amy.MessagingId, amy.Note, amy.IsDefault,
             amy.IsActive, amy.CreatedBy));
        var bob = await contacts.CreateAsync(supplier.Id, new ContactInput("Bob", " ", Email: "  "), token);
        Assert.Equal((null, null, false), (bob.Position, bob.Email, bob.IsDefault));
        await contacts.CreateAsync(another.Id, new ContactInput("Cat", IsDefault: true), token);

        // 新預設取代原預設；另一供應商的預設不受影響；列表預設排第一。
        var amyVersion = amy.Version;
        bob = await contacts.UpdateAsync(supplier.Id, bob.Id, new ContactInput("Bob", Email: "bob@example.com", IsDefault: true),
            true, bob.Version, token);
        var list = await contacts.ListAsync(supplier.Id, token);
        Assert.Equal(["Bob", "Amy"], list.Select(x => x.Name));
        Assert.Equal([true, false], list.Select(x => x.IsDefault));
        Assert.True(Assert.Single(await contacts.ListAsync(another.Id, token)).IsDefault);
        await Assert.ThrowsAsync<ConflictException>(() => contacts.UpdateAsync(supplier.Id, amy.Id, new ContactInput("Amy"),
            true, amyVersion, token));

        // 停用的聯絡人不可為預設；欄位驗證與客戶聯絡人相同。
        Assert.Equal("isDefault", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(() => contacts.UpdateAsync(
            supplier.Id, bob.Id, new ContactInput("Bob", IsDefault: true), false, bob.Version, token))).Errors).Key);
        Assert.Equal("email", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(() => contacts.CreateAsync(
            supplier.Id, new ContactInput("X", Email: "not-an-email"), token))).Errors).Key);
        Assert.Equal("name", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(() => contacts.CreateAsync(
            supplier.Id, new ContactInput(" "), token))).Errors).Key);

        // 刪除預設聯絡人後無預設；已刪除的不可再修改；聯絡人須屬於路徑上的供應商。
        await contacts.DeleteAsync(supplier.Id, bob.Id, bob.Version, token);
        Assert.Equal(["Amy"], (await contacts.ListAsync(supplier.Id, token)).Select(x => x.Name));
        Assert.Equal("MD.SUPPLIER_CONTACT.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => contacts.DeleteAsync(supplier.Id, bob.Id, bob.Version + 1, token))).ErrorCode);
        var amyCurrent = (await contacts.ListAsync(supplier.Id, token)).Single();
        Assert.Equal("MD.SUPPLIER_CONTACT.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(() => contacts.UpdateAsync(
            another.Id, amy.Id, new ContactInput("Moved"), true, amyCurrent.Version, token))).ErrorCode);

        // 別公司看不到供應商；停用的供應商仍可維護聯絡人；未選公司 403。
        var foreign = new SupplierContactService(otherDb);
        Assert.Equal("MD.SUPPLIER.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => foreign.ListAsync(supplier.Id, token))).ErrorCode);
        Assert.Equal("MD.SUPPLIER.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => foreign.CreateAsync(supplier.Id, new ContactInput("Spy"), token))).ErrorCode);
        await new SupplierService(db).UpdateAsync(supplier.Id, supplier.Name, false, null, supplier.Version, token);
        Assert.True((await contacts.CreateAsync(supplier.Id, new ContactInput("Dan", IsDefault: true), token)).IsDefault);
        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new SupplierContactService(noCompany).ListAsync(supplier.Id, token));
    }

    [Fact]
    public async Task ContactTableUsesCompanyScopedKeyAndDatabaseGuards()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(31000000, 32000000);
        await using var db = Context(companyId);
        const string table = "md_supplier_contact";
        Assert.Equal($"fk_{table}_md_supplier_company_id_supplier_id", await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID({0})", table).SingleAsync(token));
        Assert.Equal("([is_default]=(1))", await db.Database.SqlQueryRaw<string>(
            "SELECT filter_definition AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID({0}) AND name = {1} AND is_unique = 1",
            table, $"ux_{table}_company_id_supplier_id_default").SingleAsync(token));
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID({0}) AND name = {1}",
            table, $"ck_{table}_default_active").SingleAsync(token));

        // 資料庫層：同一供應商兩筆預設、停用的預設、跨公司供應商都會被拒絕。
        var supplier = await NewSupplierAsync(db, token);
        db.Add(new SupplierContact { CompanyId = companyId, PartnerId = supplier.Id, Name = "A", IsDefault = true });
        await db.SaveChangesAsync(token);
        await using (var direct = Context(companyId))
        {
            direct.Add(new SupplierContact { CompanyId = companyId, PartnerId = supplier.Id, Name = "B", IsDefault = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
        }
        await using (var direct = Context(companyId))
        {
            direct.Add(new SupplierContact { CompanyId = companyId, PartnerId = supplier.Id, Name = "C", IsDefault = true, IsActive = false });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
        }
        await using (var direct = Context(companyId + 1))
        {
            direct.Add(new SupplierContact { CompanyId = companyId + 1, PartnerId = supplier.Id, Name = "Cross" });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
        }
    }
}
