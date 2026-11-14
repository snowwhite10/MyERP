using MyERP.Shared.Domain;

namespace MyERP.Modules.Finance.Domain;

/// <summary>
/// 銷售收款與應收核銷的 FI 科目映射（gl_sales_collection_account_map，WU-FI-005a2a，ADR-120，BR-FI-033／037）。
/// 一間公司一列；列的 <see cref="BusinessEntity.Version"/> 就是 GL <c>FI/SalesCollection</c> 與 <c>FI/CollectionWriteOff</c>
/// 的映射版本，每次修改由 interceptor 遞增。不可刪除、版本不可回退、公司不可改（SQL 51030）。銀行借方科目不在本表，
/// 由 MD 資金帳戶的對應科目提供（ADR-041）。科目代碼只存 MD 代碼，不建立跨模組外鍵。
/// </summary>
public sealed class SalesCollectionAccountMapping : BusinessEntity
{
    public const int CodeMaxLength = 30;

    /// <summary>未核銷款暫收科目：收款過帳貸方、核銷過帳借方（本位幣）。</summary>
    public string UnappliedCashAccountCode { get; set; } = "";

    /// <summary>應收帳款控制科目：核銷過帳貸方（本位幣）。不得與暫收科目相同。</summary>
    public string ReceivableAccountCode { get; set; } = "";
}
