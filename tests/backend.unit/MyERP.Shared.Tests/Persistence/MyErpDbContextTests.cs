using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Shared.Tests.Persistence;

public sealed class MyErpDbContextTests
{
    private static IModel Model()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(1));
        return db.Model;
    }

    private static IProperty Property<T>(string name) =>
        Model().FindEntityType(typeof(T))!.FindProperty(name)!;

    [Fact]
    public void Model_UsesSnakeCaseColumnNames()
    {
        var order = Model().FindEntityType(typeof(TestOrder))!;
        var table = StoreObjectIdentifier.Table("tst_order");

        order.GetTableName().Should().Be("tst_order");
        order.FindProperty(nameof(TestOrder.DocNo))!.GetColumnName(table).Should().Be("doc_no");
        order.FindProperty(nameof(TestOrder.CompanyId))!.GetColumnName(table).Should().Be("company_id");
        order.FindProperty(nameof(TestOrder.CreatedAt))!.GetColumnName(table).Should().Be("created_at");
    }

    [Fact]
    public void Model_DecimalDefaultsToMoneyPrecision_AndModuleCanOverride()
    {
        var amount = Property<TestOrder>(nameof(TestOrder.Amount));
        amount.GetPrecision().Should().Be(19);
        amount.GetScale().Should().Be(4);

        var unitPrice = Property<TestOrder>(nameof(TestOrder.UnitPrice));
        unitPrice.GetPrecision().Should().Be(19);
        unitPrice.GetScale().Should().Be(6);
    }

    [Theory]
    [InlineData(nameof(TestOrder.CreatedAt))]
    [InlineData(nameof(TestOrder.UpdatedAt))]
    [InlineData(nameof(TestOrder.ApprovedAt))]
    public void Model_DateTimeMapsToDateTime2Precision6(string property)
    {
        Property<TestOrder>(property).GetColumnType().Should().Be("datetime2(6)");
    }

    [Fact]
    public void Model_DateOnlyMapsToDate()
    {
        Property<TestOrder>(nameof(TestOrder.DocDate)).GetColumnType().Should().Be("date");
    }

    [Fact]
    public void Model_EnumStoredAsVarchar30String()
    {
        var status = Property<TestOrder>(nameof(TestOrder.Status));

        status.GetProviderClrType().Should().Be<string>();
        status.GetMaxLength().Should().Be(30);
        status.IsUnicode().Should().BeFalse();
        status.GetColumnType().Should().Be("varchar(30)");
    }

    [Fact]
    public void Model_VersionIsConcurrencyToken()
    {
        Property<TestOrder>(nameof(TestOrder.Version)).IsConcurrencyToken.Should().BeTrue();
        Property<TestCustomer>(nameof(TestCustomer.Version)).IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Query_CompanyOwnedEntity_FiltersByCurrentCompany()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(7));

        var sql = db.Set<TestOrder>().ToQueryString();

        sql.Should().Contain("[t].[company_id] = @");
        sql.Should().Contain("= CAST(7 AS bigint)");
    }

    [Fact]
    public void Query_FilterValueFollowsContextInstance()
    {
        using var a = TestDb.Create(new FixedCurrentCompany(7));
        using var b = TestDb.Create(new FixedCurrentCompany(8));

        a.Set<TestOrder>().ToQueryString().Should().Contain("CAST(7 AS bigint)");
        b.Set<TestOrder>().ToQueryString().Should().Contain("CAST(8 AS bigint)");
    }

    [Fact]
    public void Query_NoCurrentCompany_ReturnsNothingInsteadOfAllCompanies()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(null));

        var sql = db.Set<TestOrder>().ToQueryString();

        // 沒有公司上下文時必須「封閉失敗」：company_id 為 NOT NULL，EF 把與 NULL 的比較化簡為恆假
        sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..].Should().Be("WHERE 0 = 1");
    }

    [Fact]
    public void Query_SoftDeletableEntity_ExcludesDeletedRows()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(1));

        var sql = db.Set<TestCustomer>().ToQueryString();

        sql.Should().Contain("[t].[is_deleted] = CAST(0 AS bit)");
        sql.Should().Contain("[t].[company_id] = @");
    }

    [Fact]
    public void Query_IgnoreSoftDeleteFilter_KeepsCompanyIsolation()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(1));

        var sql = db.Set<TestCustomer>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).ToQueryString();
        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];

        where.Should().NotContain("is_deleted");
        where.Should().Contain("[t].[company_id] = @");
    }

    [Fact]
    public void Query_EntityWithoutSharedInterfaces_HasNoFilter()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(1));

        db.Set<TestSetting>().ToQueryString().Should().NotContain("WHERE");
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>().Options;

        var noModules = () => new MyErpDbContext(options, null!, new FixedCurrentCompany(1));
        var noCompany = () => new MyErpDbContext(options, [], null!);

        noModules.Should().Throw<ArgumentNullException>();
        noCompany.Should().Throw<ArgumentNullException>();
    }
}
