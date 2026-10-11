using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內的客戶分類；代碼在軟刪除後仍保留。</summary>
public sealed class CustomerCategory : MasterEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? CustomFields { get; set; }
}
