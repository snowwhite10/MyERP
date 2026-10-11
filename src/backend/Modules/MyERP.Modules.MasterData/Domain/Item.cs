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
