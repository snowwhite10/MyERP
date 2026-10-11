using System.Text.Json.Serialization;
using MyERP.Shared.Approvals;
using MyERP.Shared.Domain;

namespace MyERP.Modules.System.Domain;

/// <summary>審批步驟的審批人類型：指定角色（目前公司任一擁有該角色的用戶）或指定用戶。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApproverType>))]
public enum ApproverType
{
    Role,
    User,
}

/// <summary>審批實例步驟的狀態。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalStepStatus>))]
public enum ApprovalStepStatus
{
    /// <summary>尚未輪到。</summary>
    Waiting,

    /// <summary>目前步驟，等待審批。</summary>
    Pending,

    Approved,
    Rejected,

    /// <summary>實例被駁回或撤回時尚未處理的步驟。</summary>
    Skipped,
}

/// <summary>
/// 審批流程定義（sys_approval_flow，REQ_DS43.97）：每間公司每個單據類型一條，步驟依序執行。
/// 沒有刪除，以停用代替；停用或沒有流程的單據類型送審即自動核准。
/// </summary>
public sealed class ApprovalFlow : BusinessEntity
{
    public const int MaxSteps = 10;

    public required string DocumentType { get; set; }
    public required string Name { get; set; }

    /// <summary>允許送審人審批自己的單據；預設不允許（職責分離）。</summary>
    public bool AllowSelfApproval { get; set; }
    public bool IsActive { get; set; } = true;
    public List<ApprovalFlowStep> Steps { get; } = [];
}

/// <summary>
/// 審批流程步驟（sys_approval_flow_step，REQ_DS43.98）。<see cref="MinAmount"/> 為金額門檻：
/// 送審金額 ≥ 門檻才需經過此步驟，null 表示一律需要。
/// </summary>
public sealed class ApprovalFlowStep : BusinessEntity
{
    public long FlowId { get; set; }
    public int StepNo { get; set; }
    public required string Name { get; set; }
    public ApproverType ApproverType { get; set; }
    public long? RoleId { get; set; }
    public long? UserId { get; set; }
    public decimal? MinAmount { get; set; }

    public bool AppliesTo(decimal amount) => MinAmount is null || amount >= MinAmount.Value;
}

/// <summary>
/// 審批代理（sys_approval_delegation，REQ_DS43.99）：在起訖日期內（含頭尾，以業務日期判斷），
/// 代理人可代被代理人處理其用戶步驟與其角色步驟。不連鎖代理。
/// </summary>
public sealed class ApprovalDelegation : BusinessEntity
{
    public long DelegatorUserId { get; set; }
    public long DelegateUserId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Remark { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 審批實例（sys_approval_instance，REQ_DS43.100）：一次送審。步驟於送審時依金額複製到
/// <see cref="ApprovalInstanceStep"/>，之後修改流程不影響審批中的實例。
/// </summary>
public sealed class ApprovalInstance : BusinessEntity
{
    /// <summary>送審時使用的流程；沒有流程而自動核准時為 null。</summary>
    public long? FlowId { get; set; }
    public required string DocumentType { get; set; }
    public long DocumentId { get; set; }
    public required string DocumentNo { get; set; }
    public string? Summary { get; set; }
    public decimal Amount { get; set; }
    public ApprovalStatus Status { get; set; }
    public int? CurrentStepNo { get; set; }
    public bool AllowSelfApproval { get; set; }
    public long SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ApprovalInstanceStep> Steps { get; } = [];
}

/// <summary>
/// 審批實例步驟（sys_approval_instance_step，REQ_DS43.101）：送審時的步驟快照與處理結果。
/// <see cref="OnBehalfOfUserId"/> 為代理審批時的被代理人。
/// </summary>
public sealed class ApprovalInstanceStep : BusinessEntity
{
    public long InstanceId { get; set; }
    public int StepNo { get; set; }
    public required string Name { get; set; }
    public ApproverType ApproverType { get; set; }
    public long? RoleId { get; set; }
    public long? UserId { get; set; }
    public decimal? MinAmount { get; set; }
    public ApprovalStepStatus Status { get; set; }
    public long? ActedBy { get; set; }
    public long? OnBehalfOfUserId { get; set; }
    public DateTime? ActedAt { get; set; }
    public string? Comment { get; set; }
}
