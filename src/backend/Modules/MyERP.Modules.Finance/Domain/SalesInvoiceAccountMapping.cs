using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// SD 發票的 FI 科目映射（gl_sales_invoice_account_map，WU-FI-002a4a，ADR-109，BR-FI-009）。一間公司一列；
/// 列的 <see cref="BusinessEntity.Version"/> 就是映射版本，每次修改由 interceptor 遞增，所以 GL 的 SD/Invoice 映射版本
/// 與發票過帳讀到的科目必然出自同一版本。不可刪除（刪後重建會令舊版本號對應另一組科目）；科目代碼只存 MD 代碼，
/// 過帳時再經 MD 契約驗證與取快照，不建立跨模組外鍵。
/// </summary>
public sealed class SalesInvoiceAccountMapping : BusinessEntity
{
    public const int CodeMaxLength = 30;

    /// <summary>應收帳款科目（借方，本位幣總額）。</summary>
    public string ReceivableAccountCode { get; set; } = "";

    /// <summary>銷貨收入科目（貸方，本位幣淨額）。</summary>
    public string RevenueAccountCode { get; set; } = "";

    /// <summary>銷項稅額科目（貸方，本位幣稅額；零稅時不產生分錄）。</summary>
    public string OutputTaxAccountCode { get; set; } = "";
}
