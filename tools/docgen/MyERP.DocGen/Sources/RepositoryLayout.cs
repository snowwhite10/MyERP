namespace MyERP.DocGen.Sources;

/// <summary>docgen 讀寫的 repo 路徑（MASTER_PROMPT 第 3 節）。</summary>
public sealed class RepositoryLayout
{
    public RepositoryLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string ModulesDirectory => Combine("docs/02-modules");

    public string DocSourcesDirectory => Combine("docs/05-doc-sources");

    public string OutputDirectory => Combine("docs/99-output");

    public string ReferenceNotes => Combine("docs/00-management/reference-notes.md");

    public string Architecture => Combine("docs/01-architecture/architecture.md");

    public string ReferenceDirectory => Combine("docs/reference");

    public string Combine(string relative) => Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>repo 相對路徑（正斜線），用於報告與錯誤訊息。</summary>
    public string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>往上尋找含 MASTER_PROMPT.md 的目錄。</summary>
    public static RepositoryLayout Discover(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MASTER_PROMPT.md")))
            {
                return new RepositoryLayout(directory.FullName);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Cannot find repository root (MASTER_PROMPT.md) from '{startDirectory}'.");
    }
}
