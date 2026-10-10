using MyERP.ArchitectureTests.Fixtures.Domain;
using MyERP.ArchitectureTests.Infrastructure;

namespace MyERP.ArchitectureTests;

/// <summary>驗證檢查邏輯本身：對刻意違規的範例必須報錯，對乾淨型別不得誤報。</summary>
public sealed class ArchitectureRulesSelfTests
{
    private static IEnumerable<Type> TypesOf<T>() =>
        typeof(T).Assembly.GetTypes().Where(t => t == typeof(T) || t.DeclaringType == typeof(T));

    [Fact]
    public void FindForbiddenCalls_DetectsDirectDateTimeNow()
    {
        ArchitectureRules.FindForbiddenCalls(TypesOf<DirectClockUser>(), ArchitectureRules.ForbiddenClockMethods)
            .Should().ContainSingle().Which.Should().Contain("DateTime.get_Now");
    }

    [Fact]
    public void FindForbiddenCalls_DetectsUsageInsideCompilerGeneratedLambda()
    {
        // lambda 編譯到外層型別的巢狀 <>c 類別，必須一併掃描；只掃外層型別本身則找不到
        ArchitectureRules.FindForbiddenCalls([typeof(LambdaClockUser)], ArchitectureRules.ForbiddenClockMethods)
            .Should().BeEmpty();
        ArchitectureRules.FindForbiddenCalls(TypesOf<LambdaClockUser>(), ArchitectureRules.ForbiddenClockMethods)
            .Should().Contain(v => v.Contains("DateTimeOffset.get_UtcNow"));
    }

    [Fact]
    public void FindForbiddenCalls_CleanType_ReportsNothing()
    {
        ArchitectureRules.FindForbiddenCalls([typeof(CleanType)], ArchitectureRules.ForbiddenClockMethods)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(HttpFieldHolder))]
    [InlineData(typeof(HttpBodyUser))]
    public void FindForbiddenTypeReferences_DetectsSignatureAndBodyReferences(Type fixture)
    {
        ArchitectureRules.FindForbiddenTypeReferences([fixture], (_, r) => ArchitectureRules.IsInNamespace(r, "System.Net.Http"))
            .Should().NotBeEmpty();
    }

    [Fact]
    public void FindForbiddenTypeReferences_CleanType_ReportsNothing()
    {
        ArchitectureRules.FindForbiddenTypeReferences([typeof(CleanType)], (_, r) => ArchitectureRules.IsInNamespace(r, "System.Net.Http"))
            .Should().BeEmpty();
    }

    [Fact]
    public void FindForbiddenAssemblyReferences_DetectsReference()
    {
        ArchitectureRules.FindForbiddenAssemblyReferences([typeof(ArchitectureRulesSelfTests).Assembly], (_, name) => name == "FluentAssertions")
            .Should().ContainSingle();
    }

    [Fact]
    public void HasNamespaceSegment_MatchesWholeSegmentOnly()
    {
        ArchitectureRules.HasNamespaceSegment(typeof(CleanType), "Domain").Should().BeTrue();
        ArchitectureRules.HasNamespaceSegment(typeof(CleanType), "Dom").Should().BeFalse();
    }

    [Theory]
    [InlineData("MyERP.Modules.Sales", true, false)]
    [InlineData("MyERP.Modules.Sales.Contracts", false, true)]
    [InlineData("MyERP.Shared", false, false)]
    public void SolutionAssemblies_ClassifiesModuleAssemblies(string name, bool implementation, bool contracts)
    {
        SolutionAssemblies.IsModuleImplementation(name).Should().Be(implementation);
        SolutionAssemblies.IsModuleContracts(name).Should().Be(contracts);
    }

    [Fact]
    public void ModuleRoot_StripsContractsSuffix()
    {
        SolutionAssemblies.ModuleRoot("MyERP.Modules.Sales.Contracts").Should().Be("MyERP.Modules.Sales");
        SolutionAssemblies.ModuleRoot("MyERP.Modules.Sales").Should().Be("MyERP.Modules.Sales");
    }
}
