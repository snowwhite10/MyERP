namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// 由引用物料的模組（INV 庫存、SD／PUR 單據、PP BOM）實作並註冊；MD 改基本單位前逐一詢問，
/// 不讀其他模組的表（ADR-049）。
/// </summary>
public interface IItemUsage
{
    /// <summary>目前公司是否有庫存、交易或單據引用此物料代碼。</summary>
    Task<bool> IsInUseAsync(string itemCode, CancellationToken token = default);
}

/// <summary>由引用倉庫的模組（INV、SD 出貨、PUR 收貨）實作並註冊；MD 刪除倉庫前逐一詢問（ADR-049）。</summary>
public interface IWarehouseUsage
{
    /// <summary>目前公司是否有庫存、交易或單據引用此倉庫代碼。</summary>
    Task<bool> IsInUseAsync(string warehouseCode, CancellationToken token = default);
}

/// <summary>由引用儲位的模組（INV）實作並註冊；MD 刪除儲位前逐一詢問（ADR-049）。</summary>
public interface IStorageLocationUsage
{
    /// <summary>目前公司是否有庫存或交易引用此倉庫下的儲位代碼。</summary>
    Task<bool> IsInUseAsync(string warehouseCode, string locationCode, CancellationToken token = default);
}
