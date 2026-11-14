using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// PUR 採購發票的 FI 科目映射（gl_purchase_invoice_account_map，WU-FI-003a2a，ADR-116，BR-FI-018）。一間公司一列；
/// 列的 <see cref="BusinessEntity.Version"/> 就是 GL <c>PUR/PurchaseInvoice</c> 映射版本，每次修改由 interceptor 遞增。
/// 不可刪除、版本不可回退、公司不可改（SQL 51027）。科目代碼只存 MD 代碼，過帳時再經 MD 契約驗證與取快照，
/// 不建立跨模組外鍵。
/// </summary>
public sealed class PurchaseInvoiceAccountMapping : BusinessEntity
{
    public const int CodeMaxLength = 30;

    /// <summary>應付帳款科目（貸方，本位幣總額）。</summary>
    public string PayableAccountCode { get; set; } = "";

    /// <summary>進貨或費用（或進貨暫估）科目（借方，本位幣淨額）。</summary>
    public string PurchaseAccountCode { get; set; } = "";

    /// <summary>進項稅額科目（借方，本位幣稅額；零稅時不產生分錄）。</summary>
    public string InputTaxAccountCode { get; set; } = "";
}
