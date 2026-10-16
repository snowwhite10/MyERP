using System.Text.Json.Serialization;

namespace MyERP.Shared.Approvals;

/// <summary>審批實例狀態。API 與資料庫皆以名稱字串表示。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStatus>))]
public enum ApprovalStatus
{
    /// <summary>審批中。</summary>
    Pending,

    /// <summary>全部步驟核准（或沒有適用步驟而自動核准）。</summary>
    Approved,

    /// <summary>任一步驟駁回。</summary>
    Rejected,

    /// <summary>送審人或單據模組撤回。</summary>
    Cancelled,
}

/// <summary>
/// 送審內容。<paramref name="Amount"/> 為本位幣金額（≥ 0），決定適用哪些步驟（金額門檻）；
/// <paramref name="DocumentNo"/> 與 <paramref name="Summary"/> 只用於待辦清單顯示。
/// </summary>
public sealed record ApprovalSubmission(string DocumentType, long DocumentId, string DocumentNo, decimal Amount, string? Summary = null);

/// <summary>單據目前的審批狀態。<c>CurrentStepNo</c> 在審批完成後為 null；<c>StepCount</c> 為本次適用的步驟數（自動核准為 0）。</summary>
public sealed record ApprovalState(
    long InstanceId, string DocumentType, long DocumentId, ApprovalStatus Status, int? CurrentStepNo, int StepCount);

/// <summary>審批人作出最終決定（最後一步核准，或任一步駁回）時分派給單據模組的結果。</summary>
public sealed record ApprovalCompleted(
    long InstanceId, string DocumentType, long DocumentId, ApprovalStatus Status, long ActedBy, string? Comment);

/// <summary>單據擁有模組可向讀者展示的歷次審批與逐步意見；由 SYS 查詢並套用目前公司隔離。</summary>
public sealed record ApprovalDocumentStep(int StepNo, string Name, string Status,
    long? ActedBy, DateTime? ActedAt, string? Comment);
public sealed record ApprovalDocumentHistory(long InstanceId, ApprovalStatus Status,
    DateTime SubmittedAt, IReadOnlyList<ApprovalDocumentStep> Steps);

/// <summary>
/// 共用審批流程引擎（MASTER_PROMPT 第 5 節、REQ_F4118.29、ADR-022）。各模組送審時在進程內呼叫，
/// 引擎依目前公司該單據類型的審批流程（多級、金額門檻）建立審批實例；審批人經 SYS 的審批 API 核准或駁回。
/// </summary>
/// <remarks>
/// 方法使用與呼叫端相同的 scoped DbContext 並呼叫 SaveChanges，因此呼叫端尚未儲存的單據變更會一起提交；
/// 呼叫端開了交易時一併在交易內。沒有啟用中的流程或沒有適用步驟時，實例立即為 <see cref="ApprovalStatus.Approved"/>。
/// </remarks>
public interface IApprovalWorkflowService
{
    /// <summary>
    /// 送審。同一單據已有審批中的實例時拋出 409 <c>SYS.APPROVAL.ALREADY_PENDING</c>；被駁回或撤回後可再次送審（建立新實例，保留歷史）。
    /// 立即核准時不會呼叫 <see cref="IApprovalResultHandler"/>，呼叫端依回傳狀態處理。
    /// </summary>
    Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>撤回審批中的實例；沒有審批中的實例時拋出 422 <c>SYS.APPROVAL.NOT_PENDING</c>。不呼叫結果處理器。</summary>
    Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default);

    /// <summary>單據最近一次送審的狀態；從未送審時為 null。</summary>
    Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default);

}

/// <summary>單據模組經受權單據查詢後讀取 SYS 的審批記錄。</summary>
public interface IApprovalHistoryReader
{
    /// <summary>按最新送審在前回傳同一單據的所有審批輪次。</summary>
    Task<IReadOnlyList<ApprovalDocumentHistory>> GetHistoryAsync(string documentType, long documentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 單據模組實作：接收審批結果並更新自己的單據（例如核准 → 已審核、駁回 → 退回草稿）。
/// 在審批動作的同一交易內、SaveChanges 之前呼叫（交易內事件）；拋出例外即整個審批動作回滾。
/// </summary>
public interface IApprovalResultHandler
{
    /// <summary>處理的單據類型 <c>{PREFIX}.{DOCUMENT}</c>（大寫）。</summary>
    string DocumentType { get; }

    Task HandleAsync(ApprovalCompleted result, CancellationToken cancellationToken);
}
