using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內以代碼識別的扁平分類；代碼在軟刪除後仍保留。</summary>
public abstract class MasterCategory : MasterEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? CustomFields { get; set; }
}

/// <summary>公司內的客戶分類。</summary>
public sealed class CustomerCategory : MasterCategory;

/// <summary>公司內的供應商分類。</summary>
public sealed class SupplierCategory : MasterCategory;
