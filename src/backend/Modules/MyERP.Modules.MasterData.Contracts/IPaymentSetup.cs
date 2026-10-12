using System.Text.Json.Serialization;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>付款條件的到期規則（ADR-040）。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentTermDueType>))]
public enum PaymentTermDueType
{
    /// <summary>單據日後 N 天（N = 0 為即期／貨到付款）。</summary>
    NetDays,

    /// <summary>月結 N 天：單據所屬帳期的結帳日後 N 天。</summary>
    ClosingDays,

    /// <summary>結帳日後第 M 個月的 D 日。</summary>
    ClosingMonthDay,
}

/// <summary>不適用於 <see cref="DueType"/> 的欄位為 null。</summary>
public sealed record PaymentTermInfo(string Code, string Name, PaymentTermDueType DueType, int? Days, int? ClosingDay,
    int? Months, int? PaymentDay, bool IsActive);

public sealed record SettlementMethodInfo(string Code, string Name, bool IsActive);

/// <summary>其他模組讀取目前公司付款條件的唯一入口（不得直接讀 md_payment_term，ADR-040）。</summary>
public interface IPaymentTermLookup
{
    /// <summary>依代碼找付款條件，含停用；不存在回 null。</summary>
    Task<PaymentTermInfo?> FindAsync(string code, CancellationToken token = default);

    /// <summary>
    /// 單據日期對應的到期日；不存在 404 <c>MD.PAYMENT_TERM.NOT_FOUND</c>，停用 422 <c>MD.PAYMENT_TERM.INACTIVE</c>。
    /// 單據應保存算出的到期日，條件日後修改不回溯。
    /// </summary>
    Task<DateOnly> CalculateDueDateAsync(string code, DateOnly documentDate, CancellationToken token = default);
}

/// <summary>其他模組讀取目前公司結算方式的唯一入口（不得直接讀 md_settlement_method，ADR-040）。</summary>
public interface ISettlementMethodLookup
{
    /// <summary>依代碼找結算方式，含停用；不存在回 null。</summary>
    Task<SettlementMethodInfo?> FindAsync(string code, CancellationToken token = default);

    /// <summary>新單據選用前驗證：不存在 404 <c>MD.SETTLEMENT_METHOD.NOT_FOUND</c>，停用 422 <c>MD.SETTLEMENT_METHOD.INACTIVE</c>。</summary>
    Task<SettlementMethodInfo> RequireActiveAsync(string code, CancellationToken token = default);
}

/// <summary>由引用付款條件的模組（SD、PUR、FI）實作並註冊；MD 刪除前逐一詢問，不讀其他模組的表（ADR-040）。</summary>
public interface IPaymentTermUsage
{
    /// <summary>目前公司是否有資料引用此付款條件代碼。</summary>
    Task<bool> IsInUseAsync(string paymentTermCode, CancellationToken token = default);
}

/// <summary>由引用結算方式的模組（FI 收付款）實作並註冊；MD 刪除前逐一詢問（ADR-040）。</summary>
public interface ISettlementMethodUsage
{
    /// <summary>目前公司是否有資料引用此結算方式代碼。</summary>
    Task<bool> IsInUseAsync(string settlementMethodCode, CancellationToken token = default);
}
