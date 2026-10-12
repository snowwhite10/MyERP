using MyERP.Shared.Domain;

namespace MyERP.Modules.MasterData.Domain;

/// <summary>公司內以代碼識別、可選同公司分類的往來對象（客戶、供應商）；聯絡人與地址為子表（ADR-043）。</summary>
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

/// <summary>
/// 往來對象的子資料（聯絡人、地址）。每個往來對象最多一筆預設，預設資料必須啟用且未刪除（ADR-043）。
/// <see cref="PartnerId"/> 依子表對應 <c>customer_id</c> 或 <c>supplier_id</c>。
/// </summary>
public abstract class PartnerChild : MasterEntity
{
    public long PartnerId { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>往來對象聯絡人；單據應保存聯絡人姓名與電話快照，修改聯絡人不回溯。</summary>
public abstract class PartnerContact : PartnerChild
{
    public const int NameMaxLength = 100;
    public const int PositionMaxLength = 100;
    public const int PhoneMaxLength = 50;
    public const int EmailMaxLength = 200;
    public const int MessagingIdMaxLength = 100;
    public const int NoteMaxLength = 500;

    public required string Name { get; set; }
    public string? Position { get; set; }
    public string? Mobile { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? MessagingId { get; set; }
    public string? Note { get; set; }
}

/// <summary>客戶聯絡人（BlueWhale ClientContact）。</summary>
public sealed class CustomerContact : PartnerContact;

/// <summary>供應商聯絡人（BlueWhale VendorLinkman）；供應商不設地址子表（ADR-043）。</summary>
public sealed class SupplierContact : PartnerContact;

/// <summary>客戶送貨地址（BlueWhale ClientAddress）；銷售單據保存地址快照。</summary>
public sealed class CustomerAddress : PartnerChild
{
    public const int RecipientMaxLength = 100;
    public const int PhoneMaxLength = 50;
    public const int PostalCodeMaxLength = 20;
    public const int AddressMaxLength = 300;

    public required string Address { get; set; }
    public string? PostalCode { get; set; }
    public string? Recipient { get; set; }
    public string? Phone { get; set; }
}
