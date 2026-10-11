using MyERP.Shared.Domain;

namespace MyERP.Modules.System.Domain;

/// <summary>圖片種類（Domain 不引用 Contracts；與 <c>Contracts.CompanyImageKind</c> 同名同值，於 Application 對應）。</summary>
public enum CompanyImageType
{
    Logo,
    Stamp,
}

/// <summary>
/// 公司的 Logo／印章圖片（sys_company_image，REQ_DS43.103；BlueWhale Logo &amp; Stamp）。每間公司每種最多一張，
/// 內容存在資料庫（ADR-028）；類型與尺寸由上傳時解析檔案內容取得，不信任副檔名或宣告的 Content-Type。
/// </summary>
public sealed class CompanyImageRecord : BusinessEntity
{
    public const int MaxFileNameLength = 200;

    public CompanyImageType ImageKind { get; set; }

    public required string ContentType { get; set; }

    public required string FileName { get; set; }

    public int SizeBytes { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public required byte[] Content { get; set; }
}
