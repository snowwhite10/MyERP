namespace MyERP.ArchitectureTests.Infrastructure;

/// <summary>由測試輸出目錄往上找含 global.json 與 MASTER_PROMPT.md 的 repo 根目錄。</summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<string> LazyPath = new(() => Find(AppContext.BaseDirectory));

    public static string Path => LazyPath.Value;

    public static string Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "global.json"))
                && File.Exists(System.IO.Path.Combine(dir.FullName, "MASTER_PROMPT.md")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"找不到 repo 根目錄（由 {startDirectory} 往上找 global.json + MASTER_PROMPT.md）");
    }
}
