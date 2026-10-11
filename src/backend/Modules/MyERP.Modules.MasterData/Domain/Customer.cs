using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司客戶主檔；聯絡人與地址由後續工作單元擴充。</summary>
public sealed class Customer : MasterEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public long? CategoryId { get; set; }
    public CustomerCategory? Category { get; set; }
    public string? CustomFields { get; set; }
}
