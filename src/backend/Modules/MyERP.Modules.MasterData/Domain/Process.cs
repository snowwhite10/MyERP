namespace MyERP.Modules.MasterData.Domain;

/// <summary>工序類別（BlueWhale Process Category）；單層清單，按排序號顯示（ADR-042）。</summary>
public sealed class ProcessCategory : MasterCategory
{
    public const int MaxSortOrder = 9999;

    public int SortOrder { get; set; }
}

/// <summary>工序（BlueWhale Process）；類別與計件單位可空，標準工價只是單據預設值（ADR-042）。</summary>
public sealed class Process : MasterCategory
{
    public long? CategoryId { get; set; }
    public long? UomId { get; set; }

    /// <summary>本位幣標準計件工價，≥ 0，最多 6 位小數。</summary>
    public decimal StandardRate { get; set; }

    public int SortOrder { get; set; }
}
