using MyERP.ArchitectureTests.Infrastructure;

namespace MyERP.ArchitectureTests;

/// <summary>禁止使用的 API（MASTER_PROMPT 4、6.3 節、CLAUDE.md 硬性限制）。</summary>
public sealed class BannedApiTests
{
    [Fact]
    public void NoAssembly_ReferencesSystemDataSqlClient()
    {
        ArchitectureRules.FindForbiddenAssemblyReferences(SolutionAssemblies.All, (_, reference) => reference == "System.Data.SqlClient")
            .Should().BeEmpty("use Microsoft.Data.SqlClient");
    }

    [Fact]
    public void NoCode_UsesSystemClockDirectly()
    {
        var types = SolutionAssemblies.All.SelectMany(TypeInspector.GetLoadableTypes);

        ArchitectureRules.FindForbiddenCalls(types, ArchitectureRules.ForbiddenClockMethods)
            .Should().BeEmpty("inject IClock instead of DateTime.Now / UtcNow / Today or DateTimeOffset.Now / UtcNow");
    }
}
