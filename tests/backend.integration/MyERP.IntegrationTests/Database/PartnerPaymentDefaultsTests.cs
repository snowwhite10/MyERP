using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>客戶／供應商預設付款條件與結算方式（ADR-047）。</summary>
public sealed class PartnerPaymentDefaultsTests(TestDatabase database)
{
    private MyErpDbContext Context(long? companyId)
    {
        var company = new FixedCompany(companyId);
        var stamper = new AuditStamper(new FixedClock(new DateTime(2026, 10, 13)), new FixedUser(7), company);
        return new MyErpDbContext(
            new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(database.ConnectionString)
                .AddInterceptors(new AuditSaveChangesInterceptor(stamper)).Options,
            [new PaymentSetupDbModel(), new CustomerCategoryDbModel(), new CustomerDbModel(),
                new SupplierCategoryDbModel(), new SupplierDbModel()], company);
    }

    private static PaymentTermRule Net(int days) => new(PaymentTermDueType.NetDays, days, null, null, null);

    private static string Code(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task CustomerDefaultsAreValidatedKeptAndExposedByLookup()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(45000000, 46000000);
        await using var db = Context(companyId);
        var terms = new PaymentTermService(db, []);
        var methods = new SettlementMethodService(db, []);
        var net30 = await terms.CreateAsync("N30", "Net 30", Net(30), token);
        var net60 = await terms.CreateAsync("N60", "Net 60", Net(60), token);
        var cash = await methods.CreateAsync("CASH", "Cash", token);
        var customers = new CustomerService(db);

        var customer = await customers.CreateAsync(Code("C"), "Defaults", null, new PartnerDefaults(net30.Id, cash.Id), token);
        Assert.Equal((net30.Id, cash.Id), (customer.DefaultPaymentTermId, customer.DefaultSettlementMethodId));
        var plain = await customers.CreateAsync(Code("C"), "Plain", null, token);
        Assert.Equal(((long?)null, (long?)null), (plain.DefaultPaymentTermId, plain.DefaultSettlementMethodId));

        // 已停用的值可保留，但不可新選取。
        net30 = await terms.UpdateAsync(net30.Id, net30.Name, Net(30), false, net30.Version, token);
        cash = await methods.UpdateAsync(cash.Id, cash.Name, false, cash.Version, token);
        customer = await customers.UpdateAsync(customer.Id, "Kept", true, null, new PartnerDefaults(net30.Id, cash.Id),
            customer.Version, token);
        Assert.Equal((net30.Id, cash.Id), (customer.DefaultPaymentTermId, customer.DefaultSettlementMethodId));
        Assert.Equal("defaultPaymentTermId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => customers.UpdateAsync(plain.Id, "Plain", true, null, new PartnerDefaults(net30.Id, null), plain.Version, token)))
            .Errors).Key);
        Assert.Equal("defaultSettlementMethodId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => customers.CreateAsync(Code("C"), "New", null, new PartnerDefaults(null, cash.Id), token))).Errors).Key);

        // 舊多載保留原值；明確傳入時完整取代。
        customer = await customers.UpdateAsync(customer.Id, "Renamed", true, null, customer.Version, token);
        Assert.Equal((net30.Id, cash.Id), (customer.DefaultPaymentTermId, customer.DefaultSettlementMethodId));
        customer = await customers.UpdateAsync(customer.Id, "Switched", true, null, new PartnerDefaults(net60.Id, null),
            customer.Version, token);
        Assert.Equal((net60.Id, (long?)null), (customer.DefaultPaymentTermId, customer.DefaultSettlementMethodId));

        customer.ReviewStatus = PartnerReviewStatus.Approved;
        await db.SaveChangesAsync(token);
        Assert.Equal(new CustomerIdentity(customer.Id, customer.Code, "Switched", "N60", null),
            await customers.RequireApprovedAsync(customer.Id, token));
    }

    [Fact]
    public async Task SupplierDefaultsMustBelongToTheCurrentCompany()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(46000000, 47000000);
        await using var db = Context(companyId);
        await using var other = Context(companyId + 1);
        var foreignTerm = await new PaymentTermService(other, []).CreateAsync("N30", "Net 30", Net(30), token);
        var foreignMethod = await new SettlementMethodService(other, []).CreateAsync("WIRE", "Wire", token);
        var term = await new PaymentTermService(db, []).CreateAsync("N45", "Net 45", Net(45), token);
        var method = await new SettlementMethodService(db, []).CreateAsync("WIRE", "Wire", token);
        var suppliers = new SupplierService(db);

        Assert.Equal("defaultPaymentTermId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => suppliers.CreateAsync(Code("S"), "X", null, new PartnerDefaults(foreignTerm.Id, null), token))).Errors).Key);
        Assert.Equal("defaultSettlementMethodId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => suppliers.CreateAsync(Code("S"), "X", null, new PartnerDefaults(null, foreignMethod.Id), token))).Errors).Key);
        Assert.Equal("defaultPaymentTermId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => suppliers.CreateAsync(Code("S"), "X", null, new PartnerDefaults(0, null), token))).Errors).Key);
        Assert.Equal("defaultSettlementMethodId", Assert.Single((await Assert.ThrowsAsync<RequestValidationException>(
            () => suppliers.CreateAsync(Code("S"), "X", null, new PartnerDefaults(null, -1), token))).Errors).Key);

        var supplier = await suppliers.CreateAsync(Code("S"), "Supplier", null, new PartnerDefaults(term.Id, method.Id), token);
        supplier.ReviewStatus = PartnerReviewStatus.Approved;
        await db.SaveChangesAsync(token);
        var identity = await suppliers.RequireApprovedAsync(supplier.Id, token);
        Assert.Equal(("N45", "WIRE"), (identity.DefaultPaymentTermCode, identity.DefaultSettlementMethodCode));

        // 資料庫複合外鍵阻止跨公司引用。
        other.Add(new Supplier { CompanyId = companyId + 1, Code = Code("S"), Name = "Invalid FK", DefaultPaymentTermId = term.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync(token));
    }

    [Fact]
    public async Task PaymentSetupUsedAsPartnerDefaultCannotBeDeleted()
    {
        var token = TestContext.Current.CancellationToken;
        var companyId = Random.Shared.NextInt64(47000000, 48000000);
        await using var db = Context(companyId);
        var terms = new PaymentTermService(db, []);
        var methods = new SettlementMethodService(db, []);
        var term = await terms.CreateAsync("N30", "Net 30", Net(30), token);
        var method = await methods.CreateAsync("CASH", "Cash", token);
        var customer = await new CustomerService(db).CreateAsync(Code("C"), "Customer", null, new PartnerDefaults(term.Id, null), token);
        var supplier = await new SupplierService(db).CreateAsync(Code("S"), "Supplier", null, new PartnerDefaults(null, method.Id), token);

        Assert.Equal("MD.PAYMENT_TERM.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => terms.DeleteAsync(term.Id, term.Version, token))).ErrorCode);
        Assert.Equal("MD.SETTLEMENT_METHOD.IN_USE", (await Assert.ThrowsAsync<ConflictException>(
            () => methods.DeleteAsync(method.Id, method.Version, token))).ErrorCode);

        // 往來對象清除預設值或被刪除後即可刪除。
        await new CustomerService(db).UpdateAsync(customer.Id, customer.Name, true, null, PartnerDefaults.None, customer.Version, token);
        supplier.ReviewStatus = PartnerReviewStatus.Approved;
        await db.SaveChangesAsync(token);
        await new SupplierService(db).DeleteAsync(supplier.Id, supplier.Version, token);
        await terms.DeleteAsync(term.Id, term.Version, token);
        await methods.DeleteAsync(method.Id, method.Version, token);
    }

    [Fact]
    public async Task PartnerTablesHaveCompositeDefaultForeignKeys()
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = Context(1);
        var keys = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS [Value] FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('md_customer'), OBJECT_ID('md_supplier'))")
            .ToListAsync(token);
        Assert.Contains("fk_md_customer_payment_term_company_id_default_payment_term_id", keys);
        Assert.Contains("fk_md_customer_settlement_method_company_id_default_settlement_method_id", keys);
        Assert.Contains("fk_md_supplier_payment_term_company_id_default_payment_term_id", keys);
        Assert.Contains("fk_md_supplier_settlement_method_company_id_default_settlement_method_id", keys);
    }
}
