namespace MyERP.Modules.MasterData.Contracts;

/// <summary>已核准且啟用的客戶識別資料；交易單據須保存自己的客戶快照。</summary>
public sealed record CustomerIdentity(long Id, string Code, string Name);

/// <summary>供 SD 等使用方檢查客戶可用性，不直接讀取 MD 資料表。</summary>
public interface ICustomerLookup
{
    Task<CustomerIdentity> RequireApprovedAsync(long id, CancellationToken token = default);
}
