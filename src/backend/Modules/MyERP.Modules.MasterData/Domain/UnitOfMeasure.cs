using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內的計量單位；與分類共用代碼、名稱與停用規則，另加數量小數位數。</summary>
public sealed class UnitOfMeasure : MasterCategory
{
    public const int MaxDecimalPlaces = 6;

    /// <summary>以此單位表示數量時保留的小數位數（0–6）。</summary>
    public int DecimalPlaces { get; set; }
}

/// <summary>單位換算：1 個 <see cref="FromUomId"/> = <see cref="Factor"/> 個 <see cref="ToUomId"/>；反方向以除法推得。</summary>
public sealed class UomConversion : BusinessEntity, ISoftDeletable
{
    // 最多 15 位有效數字：前端以 JSON 數字傳遞時可無損往返（ADR-034）。
    public const int FactorPrecision = 15;
    public const int FactorScale = 6;
    public const decimal MaxFactor = 999_999_999.999999m;

    public long FromUomId { get; set; }
    public long ToUomId { get; set; }
    public decimal Factor { get; set; }
    public bool IsDeleted { get; set; }
    public UnitOfMeasure? FromUom { get; set; }
    public UnitOfMeasure? ToUom { get; set; }
}

/// <summary>以公司內的換算邊建立無向圖；兩單位之間最多一條路徑，因此換算結果唯一。</summary>
public static class UomConversionGraph
{
    /// <summary>找出 from → to 的換算比率（分子、分母分開累乘以減少中途捨入）；無路徑時回 null。</summary>
    public static (decimal Numerator, decimal Denominator)? FindRatio(
        IEnumerable<(long From, long To, decimal Factor)> edges, long from, long to)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (from == to) return (1m, 1m);
        var adjacency = new Dictionary<long, List<(long Next, decimal Multiply, decimal Divide)>>();
        foreach (var (a, b, factor) in edges)
        {
            Neighbors(a).Add((b, factor, 1m));
            Neighbors(b).Add((a, 1m, factor));
        }
        var ratios = new Dictionary<long, (decimal Numerator, decimal Denominator)> { [from] = (1m, 1m) };
        var queue = new Queue<long>([from]);
        while (queue.TryDequeue(out var current))
        {
            if (!adjacency.TryGetValue(current, out var next)) continue;
            var (numerator, denominator) = ratios[current];
            foreach (var (node, multiply, divide) in next)
            {
                if (ratios.ContainsKey(node)) continue;
                ratios[node] = (numerator * multiply, denominator * divide);
                if (node == to) return ratios[node];
                queue.Enqueue(node);
            }
        }
        return null;

        List<(long, decimal, decimal)> Neighbors(long id) =>
            adjacency.TryGetValue(id, out var list) ? list : adjacency[id] = [];
    }
}
