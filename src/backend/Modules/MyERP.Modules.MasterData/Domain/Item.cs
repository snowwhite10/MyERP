using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司物料主檔。庫存數量與期初餘額由 INV 異動帳管理。</summary>
public sealed class Item : MasterEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Specification { get; set; }
    public long BaseUomId { get; set; }
    public long? BrandId { get; set; }
    public long? CategoryId { get; set; }
    public UnitOfMeasure? BaseUom { get; set; }
    public ItemBrand? Brand { get; set; }
    public ItemCategory? Category { get; set; }
}

/// <summary>
/// 物料圖片（md_item_image，ADR-045）：內容存在資料庫，類型與寬高由上傳時解析檔案內容取得。
/// 有圖片的物料恰好一張主圖；列表按主圖、排序號、id。
/// </summary>
public sealed class ItemImage : BusinessEntity
{
    public const int MaxFileNameLength = 200;
    public const int MaxSortOrder = 9999;

    public long ItemId { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; }
    public int SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public required byte[] Content { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public Item? Item { get; set; }
}

/// <summary>公司級物料屬性定義（md_item_attribute，ADR-045），例如顏色、尺寸；按排序號顯示，被物料使用時只可停用。</summary>
public sealed class ItemAttributeDefinition : MasterCategory
{
    public const int MaxSortOrder = 9999;

    public int SortOrder { get; set; }
}

/// <summary>物料的屬性值（md_item_attribute_value，ADR-045）：同一物料同一屬性只有一個值；隨物料屬性整批取代而實體刪除。</summary>
public sealed class ItemAttributeValue : BusinessEntity
{
    public const int MaxValueLength = 200;

    public long ItemId { get; set; }
    public long AttributeId { get; set; }
    public required string Value { get; set; }
    public Item? Item { get; set; }
    public ItemAttributeDefinition? Attribute { get; set; }
}

/// <summary>
/// 物料專屬換算：1 個 <see cref="UomId"/> = <see cref="Factor"/> 個物料基本單位（ADR-035）。
/// 換算時覆寫同一單位的公司級換算。
/// </summary>
public sealed class ItemUomConversion : BusinessEntity, ISoftDeletable
{
    public const int FactorPrecision = UomConversion.FactorPrecision;
    public const int FactorScale = UomConversion.FactorScale;

    public long ItemId { get; set; }
    public long UomId { get; set; }
    public decimal Factor { get; set; }
    public bool IsDeleted { get; set; }
    public Item? Item { get; set; }
    public UnitOfMeasure? Uom { get; set; }
}
