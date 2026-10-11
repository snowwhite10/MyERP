namespace MyERP.Modules.MasterData.Contracts;

/// <summary>其他模組換算數量的唯一入口（不得直接讀 md_uom／md_uom_conversion）。</summary>
public interface IUomConverter
{
    /// <summary>
    /// 依目前公司的換算路徑把 <paramref name="quantity"/> 由來源單位換成目標單位，
    /// 以目標單位的小數位數四捨五入（遠離零）。單位不存在回 404 <c>MD.UOM.NOT_FOUND</c>；
    /// 無換算路徑回 422 <c>MD.UOM_CONVERSION.NO_PATH</c>。
    /// </summary>
    Task<decimal> ConvertAsync(long fromUomId, long toUomId, decimal quantity, CancellationToken token = default);

    /// <summary>
    /// 按物料換算數量（ADR-035）：物料專屬換算優先，其他單位以公司級路徑換到基本單位；
    /// 任一端換不到基本單位時退回公司級直接路徑。捨入與錯誤碼同 <see cref="ConvertAsync"/>，
    /// 物料不存在（含別公司）回 404 <c>MD.ITEM.NOT_FOUND</c>。
    /// </summary>
    Task<decimal> ConvertForItemAsync(long itemId, long fromUomId, long toUomId, decimal quantity, CancellationToken token = default);
}
