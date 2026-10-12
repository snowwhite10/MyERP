namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// 已核准且啟用的客戶識別資料；交易單據須保存自己的客戶快照。預設付款條件與結算方式以代碼帶出，
/// 使用方經 <see cref="IPaymentTermLookup"/>／<see cref="ISettlementMethodLookup"/> 驗證是否仍啟用（ADR-047）。
/// </summary>
public sealed record CustomerIdentity(long Id, string Code, string Name,
    string? DefaultPaymentTermCode = null, string? DefaultSettlementMethodCode = null);

/// <summary>供 SD 等使用方檢查客戶可用性，不直接讀取 MD 資料表。</summary>
public interface ICustomerLookup
{
    Task<CustomerIdentity> RequireApprovedAsync(long id, CancellationToken token = default);
}
