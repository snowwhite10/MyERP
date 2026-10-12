using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>客戶聯絡人與地址（ADR-043）：公司隔離、唯一預設、停用／刪除規則與欄位驗證。</summary>
public sealed class CustomerContactTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 12)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new CustomerCategoryDbModel(), new CustomerDbModel(), new CustomerContactDbModel()], company);
    }

    private static async Task<Customer> NewCustomerAsync(MyErpDbContext db, CancellationToken token) =>
        await new CustomerService(db).CreateAsync("C" + Guid.NewGuid().ToString("N")[..12], "Customer", null, token);

    [Fact]
    public async Task ContactsKeepOneDefaultPerCustomerAndAreCompanyScoped()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(26000000, 27000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var contacts = new CustomerContactService(db);
        var customer = await NewCustomerAsync(db, token);
        var another = await NewCustomerAsync(db, token);

        var amy = await contacts.CreateAsync(customer.Id, new ContactInput(" Amy ", " Buyer ", " 0912-345678 ", " 02-1234 ",
            " amy@example.com ", " amy_line ", " Mornings ", IsDefault: true), token);
        Assert.Equal((customer.Id, "Amy", "Buyer", "0912-345678", "02-1234", "amy@example.com", "amy_line", "Mornings", true, true, 7L),
            (amy.PartnerId, amy.Name, amy.Position, amy.Mobile, amy.Phone, amy.Email, amy.MessagingId, amy.Note, amy.IsDefault,
             amy.IsActive, amy.CreatedBy));
        var blank = await contacts.CreateAsync(customer.Id, new ContactInput("Bob", " ", "", null, "  ", null, null), token);
        Assert.Equal((null, null, null, null, null, null, false),
            (blank.Position, blank.Mobile, blank.Phone, blank.Email, blank.MessagingId, blank.Note, blank.IsDefault));
        // 另一客戶的預設不受影響。
        var otherDefault = await contacts.CreateAsync(another.Id, new ContactInput("Cat", IsDefault: true), token);

        // 新增的預設聯絡人取代原預設；列表預設排第一。
        var cid = await contacts.CreateAsync(customer.Id, new ContactInput("Cid", IsDefault: true), token);
        var cidVersion = cid.Version;
        var list = await contacts.ListAsync(customer.Id, token);
        Assert.Equal(["Cid", "Amy", "Bob"], list.Select(x => x.Name));
        Assert.Equal([true, false, false], list.Select(x => x.IsDefault));
        Assert.True(Assert.Single(await contacts.ListAsync(another.Id, token)).IsDefault);

        // 修改時設為預設同樣取代；被取消預設的資料版本已變，舊版本修改衝突。
        var amyVersion = list.Single(x => x.Name == "Amy").Version;
        var bob = await contacts.UpdateAsync(customer.Id, blank.Id, new ContactInput("Bob", Email: "bob@example.com", IsDefault: true),
            true, blank.Version, token);
        Assert.Equal(("bob@example.com", true), (bob.Email, bob.IsDefault));
        list = await contacts.ListAsync(customer.Id, token);
        Assert.Equal(["Bob", "Amy", "Cid"], list.Select(x => x.Name));
        Assert.Single(list, x => x.IsDefault);
        Assert.NotEqual(cidVersion, list.Single(x => x.Name == "Cid").Version);
        await Assert.ThrowsAsync<ConflictException>(() => contacts.UpdateAsync(customer.Id, cid.Id, new ContactInput("Cid"),
            true, cidVersion, token));
        Assert.Equal(amyVersion, list.Single(x => x.Name == "Amy").Version);

        // 停用的聯絡人不可為預設；停用預設聯絡人時必須同時取消預設。
        var inactiveDefault = await Assert.ThrowsAsync<RequestValidationException>(() => contacts.UpdateAsync(customer.Id, bob.Id,
            new ContactInput("Bob", IsDefault: true), false, bob.Version, token));
        Assert.Equal("isDefault", Assert.Single(inactiveDefault.Errors).Key);
        var retired = await contacts.UpdateAsync(customer.Id, bob.Id, new ContactInput("Bob"), false, bob.Version, token);
        Assert.Equal((false, false), (retired.IsActive, retired.IsDefault));
        Assert.DoesNotContain(await contacts.ListAsync(customer.Id, token), x => x.IsDefault);

        // 刪除預設聯絡人後無預設；已刪除的不再列出，也不可再修改。
        var amyCurrent = (await contacts.ListAsync(customer.Id, token)).Single(x => x.Name == "Amy");
        amyCurrent = await contacts.UpdateAsync(customer.Id, amy.Id, new ContactInput("Amy", IsDefault: true), true, amyCurrent.Version, token);
        await contacts.DeleteAsync(customer.Id, amy.Id, amyCurrent.Version, token);
        list = await contacts.ListAsync(customer.Id, token);
        Assert.Equal(["Bob", "Cid"], list.Select(x => x.Name));
        Assert.DoesNotContain(list, x => x.IsDefault);
        var deleted = await Assert.ThrowsAsync<NotFoundException>(() => contacts.DeleteAsync(customer.Id, amy.Id, amyCurrent.Version + 1, token));
        Assert.Equal("MD.CUSTOMER_CONTACT.NOT_FOUND", deleted.ErrorCode);

        // 聯絡人須屬於路徑上的客戶；別公司看不到客戶。
        var wrongCustomer = await Assert.ThrowsAsync<NotFoundException>(() => contacts.UpdateAsync(another.Id, cid.Id,
            new ContactInput("Moved"), true, list.Single(x => x.Name == "Cid").Version, token));
        Assert.Equal("MD.CUSTOMER_CONTACT.NOT_FOUND", wrongCustomer.ErrorCode);
        var foreign = new CustomerContactService(otherDb);
        Assert.Equal("MD.CUSTOMER.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => foreign.ListAsync(customer.Id, token))).ErrorCode);
        Assert.Equal("MD.CUSTOMER.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => foreign.CreateAsync(customer.Id, new ContactInput("Spy"), token))).ErrorCode);
        Assert.Equal("MD.CUSTOMER.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => foreign.DeleteAsync(customer.Id, otherDefault.Id, otherDefault.Version, token))).ErrorCode);
        await Assert.ThrowsAsync<NotFoundException>(() => contacts.ListAsync(-1, token));

        // 停用的客戶仍可維護聯絡人。
        await new CustomerService(db).UpdateAsync(customer.Id, customer.Name, false, null, customer.Version, token);
        Assert.True((await contacts.CreateAsync(customer.Id, new ContactInput("Dan", IsDefault: true), token)).IsDefault);

        await using var noCompany = Context(null);
        await Assert.ThrowsAsync<ForbiddenException>(() => new CustomerContactService(noCompany).ListAsync(customer.Id, token));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => new CustomerAddressService(noCompany).CreateAsync(customer.Id, new AddressInput("X"), token));
    }

    [Fact]
    public async Task ContactFieldsAreValidated()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(Random.Shared.NextInt64(27000000, 28000000));
        var contacts = new CustomerContactService(db);
        var customer = await NewCustomerAsync(db, token);

        async Task<string> Invalid(ContactInput input) => Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => contacts.CreateAsync(customer.Id, input, token))).Errors).Key;

        Assert.Equal("name", await Invalid(new ContactInput("  ")));
        Assert.Equal("name", await Invalid(new ContactInput(null)));
        Assert.Equal("name", await Invalid(new ContactInput(new string('N', 101))));
        Assert.Equal("position", await Invalid(new ContactInput("A", Position: new string('P', 101))));
        Assert.Equal("mobile", await Invalid(new ContactInput("A", Mobile: new string('9', 51))));
        Assert.Equal("phone", await Invalid(new ContactInput("A", Phone: new string('9', 51))));
        Assert.Equal("email", await Invalid(new ContactInput("A", Email: "not-an-email")));
        Assert.Equal("email", await Invalid(new ContactInput("A", Email: "Amy <amy@example.com>")));
        Assert.Equal("email", await Invalid(new ContactInput("A", Email: "a@example.com, b@example.com")));
        Assert.Equal("email", await Invalid(new ContactInput("A", Email: new string('e', 189) + "@example.com")));
        Assert.Equal("messagingId", await Invalid(new ContactInput("A", MessagingId: new string('M', 101))));
        Assert.Equal("note", await Invalid(new ContactInput("A", Note: new string('n', 501))));

        // 上限內可保存；姓名可重複（同名不同人）。
        var max = await contacts.CreateAsync(customer.Id, new ContactInput(new string('N', 100), new string('P', 100),
            new string('9', 50), new string('9', 50), new string('e', 188) + "@example.com", new string('M', 100), new string('n', 500)), token);
        Assert.Equal(200, max.Email!.Length);
        await contacts.CreateAsync(customer.Id, new ContactInput(new string('N', 100)), token);
        Assert.Equal(2, (await contacts.ListAsync(customer.Id, token)).Count);
    }

    [Fact]
    public async Task AddressesKeepOneDefaultAndValidateFields()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(28000000, 29000000);
        await using var db = Context(companyId);
        await using var otherDb = Context(companyId + 1);
        var addresses = new CustomerAddressService(db);
        var customer = await NewCustomerAsync(db, token);

        var office = await addresses.CreateAsync(customer.Id,
            new AddressInput(" Taipei No.1 ", " 100 ", " Amy ", " 02-1234 ", IsDefault: true), token);
        Assert.Equal(("Taipei No.1", "100", "Amy", "02-1234", true), (office.Address, office.PostalCode, office.Recipient, office.Phone, office.IsDefault));
        var plant = await addresses.CreateAsync(customer.Id, new AddressInput("Taoyuan Plant", " ", ""), token);
        var plantVersion = plant.Version;
        Assert.Equal((null, null, null, false), (plant.PostalCode, plant.Recipient, plant.Phone, plant.IsDefault));

        var updated = await addresses.UpdateAsync(customer.Id, plant.Id, new AddressInput("Taoyuan Plant 2", "330", IsDefault: true),
            true, plantVersion, token);
        Assert.Equal(("Taoyuan Plant 2", "330", true), (updated.Address, updated.PostalCode, updated.IsDefault));
        var list = await addresses.ListAsync(customer.Id, token);
        Assert.Equal([plant.Id, office.Id], list.Select(x => x.Id));
        Assert.False(list[1].IsDefault);
        // 原本就是預設時再次保存不影響其他資料。
        updated = await addresses.UpdateAsync(customer.Id, plant.Id, new AddressInput("Taoyuan Plant 2", "330", IsDefault: true),
            true, updated.Version, token);
        Assert.Equal(list[1].Version, (await addresses.ListAsync(customer.Id, token))[1].Version);

        await Assert.ThrowsAsync<ConflictException>(() => addresses.UpdateAsync(customer.Id, plant.Id, new AddressInput("Stale"),
            true, plantVersion, token));
        await Assert.ThrowsAsync<ConflictException>(() => addresses.DeleteAsync(customer.Id, plant.Id, 0, token));
        Assert.Equal("isDefault", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => addresses.UpdateAsync(customer.Id, plant.Id, new AddressInput("X", IsDefault: true), false, updated.Version, token))).Errors).Key);
        await addresses.DeleteAsync(customer.Id, plant.Id, updated.Version, token);
        Assert.Equal([office.Id], (await addresses.ListAsync(customer.Id, token)).Select(x => x.Id));
        Assert.Equal("MD.CUSTOMER_ADDRESS.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => addresses.UpdateAsync(customer.Id, -1, new AddressInput("X"), true, 1, token))).ErrorCode);
        Assert.Equal("MD.CUSTOMER.NOT_FOUND", (await Assert.ThrowsAsync<NotFoundException>(
            () => new CustomerAddressService(otherDb).ListAsync(customer.Id, token))).ErrorCode);

        async Task<string> Invalid(AddressInput input) => Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => addresses.CreateAsync(customer.Id, input, token))).Errors).Key;
        Assert.Equal("address", await Invalid(new AddressInput(" ")));
        Assert.Equal("address", await Invalid(new AddressInput(new string('A', 301))));
        Assert.Equal("postalCode", await Invalid(new AddressInput("A", new string('1', 21))));
        Assert.Equal("recipient", await Invalid(new AddressInput("A", Recipient: new string('R', 101))));
        Assert.Equal("phone", await Invalid(new AddressInput("A", Phone: new string('9', 51))));
        var max = await addresses.CreateAsync(customer.Id, new AddressInput(new string('A', 300), new string('1', 20),
            new string('R', 100), new string('9', 50)), token);
        Assert.Equal(300, max.Address.Length);
    }

    [Fact]
    public async Task ChildTablesUseCompanyScopedKeysAndDatabaseGuards()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(29000000, 30000000);
        await using var db = Context(companyId);
        foreach (var table in new[] { "md_customer_contact", "md_customer_address" })
        {
            var foreignKey = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID({0})", table).SingleAsync(token);
            Assert.Equal($"fk_{table}_md_customer_company_id_customer_id", foreignKey);
            var filtered = await db.Database.SqlQueryRaw<string>(
                "SELECT filter_definition AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID({0}) AND name = {1} AND is_unique = 1",
                table, $"ux_{table}_company_id_customer_id_default").SingleAsync(token);
            Assert.Equal("([is_default]=(1))", filtered);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID({0}) AND name = {1}",
                table, $"ck_{table}_default_active").SingleAsync(token));
        }

        // 資料庫層：同一客戶兩筆預設、停用的預設、跨公司客戶都會被拒絕。
        var customer = await NewCustomerAsync(db, token);
        db.Add(new CustomerContact { CompanyId = companyId, PartnerId = customer.Id, Name = "A", IsDefault = true });
        await db.SaveChangesAsync(token);
        await using (var direct = Context(companyId))
        {
            direct.Add(new CustomerContact { CompanyId = companyId, PartnerId = customer.Id, Name = "B", IsDefault = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
        }
        await using (var direct = Context(companyId))
        {
            direct.Add(new CustomerAddress { CompanyId = companyId, PartnerId = customer.Id, Address = "X", IsDefault = true, IsActive = false });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
        }
        await using (var direct = Context(companyId + 1))
        {
            direct.Add(new CustomerContact { CompanyId = companyId + 1, PartnerId = customer.Id, Name = "Cross" });
            await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync(token));
        }
    }
}
