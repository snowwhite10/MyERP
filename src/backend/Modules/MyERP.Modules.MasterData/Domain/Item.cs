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
