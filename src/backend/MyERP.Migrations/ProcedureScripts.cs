namespace MyERP.Migrations;

/// <summary>
/// 讀取嵌入本組件的 SP 原始碼（<c>database/procedures/{前綴}/usp_*.sql</c>，資源名稱 <c>procedures/{前綴}/{檔名}</c>）。
/// migration 只引用路徑，SP 內容以 repo 中的 .sql 檔為唯一來源。
/// </summary>
public static class ProcedureScripts
{
    private const string ResourcePrefix = "procedures/";
    private const string Extension = ".sql";

    /// <summary>讀取 SP 原始碼；<paramref name="path"/> 為 <c>{前綴}/usp_xxx</c>（不含副檔名）。找不到時拋出。</summary>
    public static string Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var resourceName = ResourcePrefix + path.Replace('\\', '/') + Extension;
        var assembly = typeof(ProcedureScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Stored procedure script '{resourceName}' is not embedded in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>全部已嵌入的 SP 路徑（<c>{前綴}/usp_xxx</c>），供測試檢查每個 SP 都已部署。</summary>
    public static IReadOnlyList<string> All() =>
        [.. typeof(ProcedureScripts).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(Extension, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^Extension.Length])
            .Order(StringComparer.Ordinal)];
}
