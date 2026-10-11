using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內以代碼識別、可選同公司分類的往來對象（客戶、供應商）；聯絡人與地址由後續工作單元擴充。</summary>
public abstract class BusinessPartner<TCategory> : MasterEntity where TCategory : MasterCategory
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public long? CategoryId { get; set; }
    public TCategory? Category { get; set; }
    public string? CustomFields { get; set; }
}

/// <summary>公司客戶主檔。</summary>
public sealed class Customer : BusinessPartner<CustomerCategory>;

/// <summary>公司供應商主檔。</summary>
public sealed class Supplier : BusinessPartner<SupplierCategory>;
