namespace MyERP.Modules.Inventory.Application;

/// <summary>
/// ADR-154（WU-SD-004c2b2d）：庫存／批號／序號／追蹤拒絕帶出來源行號，供呼叫來源（PUR／SD 等）在畫面標示行。
/// <c>lineNo</c> 為最小行號（與 SD 既有單行錯誤相容），<c>lineNos</c> 為同一結存鍵所有相關行號（升冪、不重複）。
/// 行號就是呼叫者傳入的 <c>InventoryPostingLine.LineNo</c>，不含其他公司或單據的資料。
/// </summary>
internal static class StockLineRefusal
{
    public static IReadOnlyDictionary<string, object?> Lines(IEnumerable<int> lineNos)
    {
        var numbers = lineNos.Distinct().Order().ToArray();
        return numbers.Length == 0
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal) { ["lineNo"] = numbers[0], ["lineNos"] = numbers };
    }
}
