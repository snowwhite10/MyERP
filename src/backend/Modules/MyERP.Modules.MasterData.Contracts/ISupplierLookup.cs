namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// 已核准且啟用的供應商識別資料；交易單據須保存自己的供應商快照。預設付款條件與結算方式以代碼帶出，
/// 使用方經 <see cref="IPaymentTermLookup"/>／<see cref="ISettlementMethodLookup"/> 驗證是否仍啟用（ADR-047）。
/// </summary>
public sealed record SupplierIdentity(long Id, string Code, string Name,
    string? DefaultPaymentTermCode = null, string? DefaultSettlementMethodCode = null);

/// <summary>供 PUR 等使用方檢查供應商可用性，不直接讀取 MD 資料表。</summary>
public interface ISupplierLookup
{
    Task<SupplierIdentity> RequireApprovedAsync(long id, CancellationToken token = default);
}
