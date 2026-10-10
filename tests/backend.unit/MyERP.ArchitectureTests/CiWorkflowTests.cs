using System.Text.RegularExpressions;
using MyERP.ArchitectureTests.Infrastructure;

namespace MyERP.ArchitectureTests;

/// <summary>
/// GitHub Actions CI 的結構檢查（MASTER_PROMPT 第 11 節）：windows-latest、只跑單元測試、不用機密。
/// 以文字規則檢查 .github/workflows/ci.yml，不引入 YAML 套件；CI 失敗前在本機就能發現設定錯誤。
/// </summary>
public sealed partial class CiWorkflowTests
{
    private const string WorkflowRelativePath = ".github/workflows/ci.yml";

    private static readonly Lazy<IReadOnlyList<string>> LazyLines = new(LoadLines);

    private static IReadOnlyList<string> Lines => LazyLines.Value;

    [Fact]
    public void Workflow_Exists()
    {
        File.Exists(WorkflowPath).Should().BeTrue($"{WorkflowRelativePath} defines the CI pipeline");
    }

    [Fact]
    public void AllJobs_RunOnWindowsLatest()
    {
        var runsOn = Lines.Select(l => RunsOnRegex().Match(l)).Where(m => m.Success).Select(m => m.Groups["value"].Value.Trim()).ToList();

        runsOn.Should().NotBeEmpty();
        runsOn.Should().OnlyContain(v => v == "windows-latest", "MASTER_PROMPT 11: CI runs on windows-latest");
    }

    [Fact]
    public void Workflow_UsesNoSecretsOrConnectionStrings()
    {
        var text = string.Join('\n', Lines);

        text.Should().NotContain("secrets.", "CI must not use secrets (MASTER_PROMPT 11)");
        text.Should().NotContainEquivalentOf("ConnectionStrings", "CI must not use connection strings");
        text.Should().NotContainEquivalentOf("Password", "CI must not contain passwords");
        text.Should().NotContain("pull_request_target", "pull_request_target exposes write tokens to fork code");
    }

    [Fact]
    public void Workflow_GrantsReadOnlyContentsPermission()
    {
        Lines.Should().Contain(l => l.Trim() == "permissions:");
        Lines.Should().Contain(l => l.Trim() == "contents: read");
        Lines.Should().NotContain(l => l.Contains(": write", StringComparison.Ordinal), "CI only needs read access");
    }

    [Fact]
    public void Backend_BuildsSolutionInRelease_WithSdkFromGlobalJson()
    {
        Lines.Should().Contain(l => l.Contains("actions/setup-dotnet@", StringComparison.Ordinal));
        Lines.Should().Contain(l => l.Trim() == "global-json-file: global.json");
        Lines.Should().Contain(l => l.Contains("dotnet build src/backend/MyERP.sln", StringComparison.Ordinal)
            && l.Contains("-c Release", StringComparison.Ordinal));
    }

    [Fact]
    public void Backend_RunsEveryUnitTestProject()
    {
        var unitProjects = TestProjects("tests/backend.unit");
        var testCommands = DotnetTestCommands();

        unitProjects.Should().NotBeEmpty();
        foreach (var project in unitProjects)
        {
            testCommands.Should().Contain(
                c => c.Contains($"--project {project}", StringComparison.Ordinal) && c.Contains("--no-build", StringComparison.Ordinal),
                $"unit test project {project} must run in CI against the Release build");
        }
    }

    [Fact]
    public void Backend_DoesNotRunIntegrationTests()
    {
        // 整合測試需要本機 SQL Server 2022（myerp_test），只在 VM 跑（MASTER_PROMPT 10、11）
        var testCommands = DotnetTestCommands();

        testCommands.Should().NotBeEmpty();
        testCommands.Should().OnlyContain(c => c.Contains("--project ", StringComparison.Ordinal), "running the whole solution would include integration tests");
        foreach (var project in TestProjects("tests/backend.integration"))
        {
            Lines.Should().NotContain(l => l.Contains(project, StringComparison.Ordinal), $"{project} needs a local SQL Server");
        }
    }

    [Fact]
    public void Frontend_UsesNodeVersionFromNvmrc_AndBuildsAndTests()
    {
        File.Exists(Path.Combine(RepositoryRoot.Path, ".nvmrc")).Should().BeTrue();
        Lines.Should().Contain(l => l.Contains("actions/setup-node@", StringComparison.Ordinal));
        Lines.Should().Contain(l => l.Trim() == "node-version-file: .nvmrc");
        Lines.Should().Contain(l => l.Trim() == "cache-dependency-path: src/frontend/package-lock.json");
        Lines.Should().Contain(l => l.Trim() == "working-directory: src/frontend");
        Lines.Should().Contain(l => RunValue(l) == "npm ci");
        Lines.Should().Contain(l => RunValue(l) == "npx ng build");
        Lines.Should().Contain(l => RunValue(l) == "npx ng test --watch=false");
    }

    [Fact]
    public void RepositoryRoot_NotFound_Throws()
    {
        var act = () => RepositoryRoot.Find(Path.GetPathRoot(Path.GetTempPath())!);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    private static string WorkflowPath => Path.Combine(RepositoryRoot.Path, WorkflowRelativePath);

    private static List<string> LoadLines() =>
        File.Exists(WorkflowPath)
            ? File.ReadAllLines(WorkflowPath).Where(l => !l.TrimStart().StartsWith('#')).ToList()
            : [];

    private static List<string> DotnetTestCommands() =>
        Lines.Select(RunValue).Where(c => c is not null && c.StartsWith("dotnet test", StringComparison.Ordinal)).Select(c => c!).ToList();

    private static string? RunValue(string line)
    {
        var match = RunRegex().Match(line);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>repo 相對路徑（正斜線），例如 tests/backend.unit/MyERP.Shared.Tests/MyERP.Shared.Tests.csproj。</summary>
    private static List<string> TestProjects(string relativeDirectory) =>
        Directory.GetFiles(Path.Combine(RepositoryRoot.Path, relativeDirectory), "*.csproj", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(RepositoryRoot.Path, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    [GeneratedRegex(@"^\s*runs-on:\s*(?<value>.+)$")]
    private static partial Regex RunsOnRegex();

    [GeneratedRegex(@"^\s*(-\s+)?run:\s*(?<value>[^|>].*)$")]
    private static partial Regex RunRegex();
}
