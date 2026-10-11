using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內的倉庫；庫存數量只由 INV 異動帳維護（ADR-036）。</summary>
public sealed class Warehouse : MasterCategory
{
    public const int AddressMaxLength = 300;
    public const int ContactNameMaxLength = 100;
    public const int PhoneMaxLength = 50;

    public string? Address { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
}

/// <summary>倉庫內的儲位，只有一層；代碼在同一倉庫內唯一，區／排／層以代碼表達（ADR-036）。</summary>
public sealed class StorageLocation : MasterEntity
{
    public long WarehouseId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Warehouse? Warehouse { get; set; }
}
