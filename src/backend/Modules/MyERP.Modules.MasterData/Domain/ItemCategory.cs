namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內的物料分類；以 <see cref="ParentId"/> 組成最多 <see cref="MaxDepth"/> 層的樹。</summary>
public sealed class ItemCategory : MasterCategory
{
    public const int MaxDepth = 5;

    /// <summary>同公司的上層分類；null 為根分類。</summary>
    public long? ParentId { get; set; }
}

/// <summary>物料分類樹的掛載檢查；節點以 id → 上層 id 表示。</summary>
public static class ItemCategoryTree
{
    /// <summary>
    /// 檢查把 <paramref name="nodeId"/>（新節點為 null）掛到 <paramref name="parentId"/> 底下是否可行：
    /// 不可掛到自己或子孫下（<c>Cycle</c>），掛載後整棵子樹不可超過 <see cref="ItemCategory.MaxDepth"/> 層（<c>TooDeep</c>）。
    /// 呼叫前須確認上層存在。
    /// </summary>
    public static ItemCategoryPlacement Check(IReadOnlyDictionary<long, long?> parents, long? nodeId, long parentId)
    {
        ArgumentNullException.ThrowIfNull(parents);
        var parentDepth = 0;
        for (long? cursor = parentId; cursor is not null; cursor = parents.GetValueOrDefault(cursor.Value))
        {
            if (cursor == nodeId) return ItemCategoryPlacement.Cycle;
            if (++parentDepth > parents.Count) return ItemCategoryPlacement.Cycle;
        }
        var height = nodeId is null ? 1 : Height(parents, nodeId.Value);
        return parentDepth + height > ItemCategory.MaxDepth ? ItemCategoryPlacement.TooDeep : ItemCategoryPlacement.Ok;
    }

    private static int Height(IReadOnlyDictionary<long, long?> parents, long rootId)
    {
        var children = parents.Where(x => x.Value is not null).ToLookup(x => x.Value!.Value, x => x.Key);
        var height = 0;
        var level = new List<long> { rootId };
        while (level.Count > 0 && height <= parents.Count)
        {
            height++;
            level = [.. level.SelectMany(id => children[id])];
        }
        return height;
    }
}

public enum ItemCategoryPlacement
{
    Ok,
    Cycle,
    TooDeep,
}
