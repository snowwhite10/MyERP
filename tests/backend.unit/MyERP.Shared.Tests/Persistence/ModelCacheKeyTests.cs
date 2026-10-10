using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Shared.Tests.Persistence;

/// <summary>
/// EF 預設以 DbContext 型別作模型快取鍵；同一程序內（例如整合測試的正式模型與測試模型）以不同模組組合建立
/// <see cref="MyErpDbContext"/> 時必須各自建模，否則後建立的實例會拿到錯誤的模型。
/// </summary>
public sealed class ModelCacheKeyTests
{
    private const string NoConnection = "Server=invalid.myerp.test;Database=myerp_unit;Integrated Security=true;Connect Timeout=1";

    private static MyErpDbContext Create(params IModuleDbModel[] modules) =>
        new(new DbContextOptionsBuilder<MyErpDbContext>().UseMyErpSqlServer(NoConnection).Options, modules, new FixedCurrentCompany(1));

    [Fact]
    public void DifferentModuleSets_BuildSeparateModels()
    {
        using var withTestModule = Create(new TestModuleDbModel());
        using var withoutModules = Create();

        withTestModule.Model.FindEntityType(typeof(TestOrder)).Should().NotBeNull();
        withoutModules.Model.FindEntityType(typeof(TestOrder)).Should().BeNull();
    }

    [Fact]
    public void SameModuleSet_ReusesCachedModel()
    {
        using var first = Create(new TestModuleDbModel());
        using var second = Create(new TestModuleDbModel());

        second.Model.Should().BeSameAs(first.Model);
    }

    [Fact]
    public void ModuleSetKey_ListsModuleTypesInRegistrationOrder()
    {
        using var db = Create(new TestModuleDbModel(), new EmptyModuleDbModel());

        db.ModuleSetKey.Should().Be($"{typeof(TestModuleDbModel).FullName};{typeof(EmptyModuleDbModel).FullName}");
    }

    [Fact]
    public void Factory_KeyIncludesDesignTimeFlag()
    {
        using var db = Create(new TestModuleDbModel());
        var factory = new ErpModelCacheKeyFactory();

        factory.Create(db, designTime: true).Should().NotBe(factory.Create(db, designTime: false));
        factory.Create(db, designTime: false).Should().Be(factory.Create(db, designTime: false));
    }

    [Fact]
    public void Factory_OtherContextType_KeyedByTypeOnly()
    {
        using var other = new DbContext(new DbContextOptionsBuilder().UseSqlServer(NoConnection).Options);
        var factory = new ErpModelCacheKeyFactory();

        factory.Create(other, designTime: false).Should().Be(factory.Create(other, designTime: false));
    }

    private sealed class EmptyModuleDbModel : IModuleDbModel
    {
        public void Configure(ModelBuilder modelBuilder)
        {
        }
    }
}
