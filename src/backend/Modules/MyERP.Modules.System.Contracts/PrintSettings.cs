namespace MyERP.Modules.System.Contracts;

/// <summary>
/// 目前公司的列印設定（REQ_F4118.18.3、ADR-027）。文字欄位沒有設定時為空字串；
/// <see cref="CompanyName"/> 沒有設定時為公司主檔名稱。
/// </summary>
public sealed record PrintSettings(
    string CompanyName,
    string Address,
    string PostalCode,
    string Phone,
    string Fax,
    string PurchaseOrderFooter,
    string SalesDeliveryFooter,
    bool ShowLogo,
    bool ShowStamp);

/// <summary>列印單據的模組經此介面讀取列印抬頭與頁尾；沒有公司上下文時拋出 403。</summary>
public interface IPrintSettingsReader
{
    Task<PrintSettings> GetAsync(CancellationToken cancellationToken = default);
}
