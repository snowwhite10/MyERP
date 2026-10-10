namespace MyERP.Shared.Numbering;

/// <summary>
/// 共用單號產生服務（MASTER_PROMPT 第 5 節「單號規則」、REQ_F4118.28）。各模組建立單據時呼叫，
/// 依目前公司的單據類型規則（前綴、日期格式、流水號長度、重設週期）產生單號，併發下不重號。
/// </summary>
/// <remarks>
/// 取號使用與呼叫端相同的 DbContext 連線與交易：在單據交易內取號，交易回滾時流水號一併回滾（不跳號）；
/// 交易期間同一規則同一週期的其他取號會等待提交（ADR-021）。沒有交易時取號立即提交，之後單據儲存失敗會留下空號。
/// </remarks>
public interface INumberGenerator
{
    /// <summary>
    /// 取下一個單號。<paramref name="documentType"/> 為 <c>{PREFIX}.{DOCUMENT}</c>（例如 <c>SAL.ORDER</c>），
    /// <paramref name="documentDate"/> 為單據的業務日期，決定日期部分與重設週期。
    /// 規則不存在或已停用、流水號用盡時拋出業務規則例外（422）。
    /// </summary>
    Task<string> NextAsync(string documentType, DateOnly documentDate, CancellationToken cancellationToken = default);
}
