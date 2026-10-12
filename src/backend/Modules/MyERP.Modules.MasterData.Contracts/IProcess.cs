namespace MyERP.Modules.MasterData.Contracts;

/// <summary>工序（ADR-042）；<see cref="StandardRate"/> 是本位幣標準計件工價，單據須自行保存實際工價。</summary>
public sealed record ProcessInfo(string Code, string Name, string? CategoryCode, string? CategoryName, string? UomCode,
    decimal StandardRate, int SortOrder, bool IsActive);

/// <summary>其他模組（PP）讀取目前公司工序的唯一入口（不得直接讀 md_process，ADR-042）。</summary>
public interface IProcessLookup
{
    /// <summary>依代碼找工序，含停用；不存在回 null。</summary>
    Task<ProcessInfo?> FindAsync(string code, CancellationToken token = default);

    /// <summary>新單據選用前驗證：不存在 404 <c>MD.PROCESS.NOT_FOUND</c>，停用 422 <c>MD.PROCESS.INACTIVE</c>。</summary>
    Task<ProcessInfo> RequireActiveAsync(string code, CancellationToken token = default);

    /// <summary>
    /// 啟用工序清單，可只取某類別；按類別排序號、類別代碼、工序排序號、工序代碼排序，沒有類別的排最後。
    /// </summary>
    Task<IReadOnlyList<ProcessInfo>> ListActiveAsync(string? categoryCode = null, CancellationToken token = default);
}

/// <summary>由引用工序的模組（PP 工藝路線、工單、計件）實作並註冊；MD 刪除前逐一詢問，不讀其他模組的表（ADR-042）。</summary>
public interface IProcessUsage
{
    /// <summary>目前公司是否有資料引用此工序代碼。</summary>
    Task<bool> IsInUseAsync(string processCode, CancellationToken token = default);
}
