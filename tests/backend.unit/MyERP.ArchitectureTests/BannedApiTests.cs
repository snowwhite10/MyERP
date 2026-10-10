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
        var types = SolutionAssemblies.All.SelectMany(TypeInspector.GetLoadableTypes)
            .Where(t => !IsAllowedClockSource(t));

        ArchitectureRules.FindForbiddenCalls(types, ArchitectureRules.ForbiddenClockMethods)
            .Should().BeEmpty("inject IClock instead of DateTime.Now / UtcNow / Today, DateTimeOffset.Now / UtcNow or TimeProvider");
    }

    [Fact]
    public void ClockSourceAllowlist_OnlyContainsExistingTypes()
    {
        // 白名單型別改名或刪除時，必須同步更新白名單，避免留下失效的例外
        var allTypeNames = SolutionAssemblies.All.SelectMany(TypeInspector.GetLoadableTypes).Select(t => t.FullName).ToHashSet();

        ArchitectureRules.ClockSourceAllowlist.Should().OnlyContain(name => allTypeNames.Contains(name));
    }

    // 白名單型別本身及其編譯器產生的巢狀型別（lambda 的 <>c）
    private static bool IsAllowedClockSource(Type type)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            if (ArchitectureRules.ClockSourceAllowlist.Contains(t.FullName!))
            {
                return true;
            }
        }

        return false;
    }
}
