namespace MyERP.Modules.MasterData.Contracts;

/// <summary>
/// 盤點等使用方以物料類別／物料圈定範圍（ADR-062 第 3 點）；只讀目前公司，不讓使用方 JOIN MD 資料表。
/// </summary>
public interface IItemCategoryScope
{
    /// <summary>
    /// 驗證類別與物料都屬目前公司、未刪除且啟用（否則 404／422），回傳範圍內的啟用物料 ID（升冪）：
    /// 類別展開含全部下層類別；同時指定類別與物料時取交集；兩者皆空時回 <c>null</c> 表示不限物料。
    /// </summary>
    Task<IReadOnlyList<long>?> ResolveItemsAsync(IReadOnlyCollection<long> categoryIds, IReadOnlyCollection<long> itemIds,
        CancellationToken token = default);
}
