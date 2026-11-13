namespace MyERP.Shared.Infrastructure.Seeding;

/// <summary>
/// 種子程序的公司上下文（WU-FI-004c4a、ADR-105）。<see cref="Web.HttpCurrentCompany"/> 只在<strong>沒有 HTTP 請求</strong>時
/// 讀取這裡的值，因此 API 請求中的程式碼無法藉此切換公司；種子程序以 <see cref="Enter"/> 在指定公司內經各模組的正常服務寫入資料。
/// 值以 <see cref="AsyncLocal{T}"/> 保存，只流向同一非同步流程；Dispose 後恢復進入前的值。
/// </summary>
public static class SeedCompanyContext
{
    private static readonly AsyncLocal<long?> Current = new();

    /// <summary>目前非同步流程的種子公司；不在 <see cref="Enter"/> 範圍內時為 null。</summary>
    public static long? CompanyId => Current.Value;

    public static IDisposable Enter(long companyId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(companyId);
        var previous = Current.Value;
        Current.Value = companyId;
        return new Scope(previous);
    }

    private sealed class Scope(long? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Current.Value = previous;
        }
    }
}

/// <summary>種子公司（由 SYS 種子程序建立）；其他模組的種子程序經此介面取得公司 Id 與本位幣，不直接讀 SYS 資料表。</summary>
public sealed record SeedCompanyRef(long Id, string Code, string BaseCurrency);

/// <summary>SYS 實作；只回傳已存在的種子公司，依代碼排序。</summary>
public interface ISeedCompanyDirectory
{
    Task<IReadOnlyList<SeedCompanyRef>> ListAsync(CancellationToken cancellationToken);
}
