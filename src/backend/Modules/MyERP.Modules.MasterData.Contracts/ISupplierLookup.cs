namespace MyERP.Modules.MasterData.Contracts;

/// <summary>已核准且啟用的供應商識別資料；交易單據須保存自己的供應商快照。</summary>
public sealed record SupplierIdentity(long Id, string Code, string Name);

/// <summary>供 PUR 等使用方檢查供應商可用性，不直接讀取 MD 資料表。</summary>
public interface ISupplierLookup
{
    Task<SupplierIdentity> RequireApprovedAsync(long id, CancellationToken token = default);
}
