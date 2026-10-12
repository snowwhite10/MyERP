namespace MyERP.Modules.MasterData.Contracts;

/// <summary>使用供應商的單據模組實作；MD 不查詢其他模組資料表。</summary>
public interface ISupplierUsage
{
    Task<bool> IsUsedAsync(long supplierId, CancellationToken cancellationToken = default);
}
