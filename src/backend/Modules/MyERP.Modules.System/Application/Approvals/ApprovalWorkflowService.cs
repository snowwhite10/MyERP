using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Auditing;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;
using ICurrentUser = MyERP.Shared.Security.ICurrentUser;

namespace MyERP.Modules.System.Application.Approvals;

/// <summary>核准或駁回，須帶讀取時實例的 <c>version</c>；駁回必須填寫意見。</summary>
public sealed record ApprovalDecision(string? Comment, int Version);

/// <summary>待辦：目前用戶可處理（本人或代理）的審批步驟。<c>OnBehalfOfUserId</c>／<c>OnBehalfOfUsername</c> 為代理時的被代理人。</summary>
public sealed record ApprovalTask(
    long InstanceId, string DocumentType, long DocumentId, string DocumentNo, string? Summary, decimal Amount,
    long SubmittedBy, string SubmittedByUsername, DateTime SubmittedAt, int StepNo, int StepCount, string StepName,
    long? OnBehalfOfUserId, string? OnBehalfOfUsername, int Version);

public sealed record ApprovalInstanceSummary(
    long Id, string DocumentType, long DocumentId, string DocumentNo, string? Summary, decimal Amount, ApprovalStatus Status,
    int? CurrentStepNo, long SubmittedBy, string SubmittedByUsername, DateTime SubmittedAt, DateTime? CompletedAt, int Version);

public sealed record ApprovalInstanceStepDetail(
    int StepNo, string Name, ApproverType ApproverType, long? RoleId, string? RoleName, long? UserId, string? Username, decimal? MinAmount,
    ApprovalStepStatus Status, long? ActedBy, string? ActedByUsername, long? OnBehalfOfUserId, string? OnBehalfOfUsername, DateTime? ActedAt, string? Comment);

public sealed record ApprovalInstanceDetail(
    long Id, string DocumentType, long DocumentId, string DocumentNo, string? Summary, decimal Amount, ApprovalStatus Status,
    int? CurrentStepNo, bool AllowSelfApproval, long SubmittedBy, string SubmittedByUsername, DateTime SubmittedAt, DateTime? CompletedAt,
    IReadOnlyList<ApprovalInstanceStepDetail> Steps, int Version);

/// <summary>審批歷史篩選：單據類型、單據 Id、狀態。</summary>
public sealed record ApprovalInstanceQuery : PagedRequest
{
    public string? DocumentType { get; init; }

    public long? DocumentId { get; init; }

    public ApprovalStatus? Status { get; init; }
}

/// <summary>待辦篩選：單據類型。關鍵字搜尋單號與摘要。</summary>
public sealed record ApprovalTaskQuery : PagedRequest
{
    public string? DocumentType { get; init; }
}

/// <summary>
/// 審批流程引擎（REQ_F4118.29.4–8、ADR-022）：送審建立實例與步驟快照、審批人核准／駁回（含代理）、待辦清單與歷史查詢。
/// 最終結果在同一交易內分派給單據模組的 <see cref="IApprovalResultHandler"/>。
/// </summary>
public sealed class ApprovalWorkflowService(
    MyErpDbContext db,
    IClock clock,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    IEnumerable<IApprovalResultHandler> resultHandlers) : IApprovalWorkflowService
{
    public const string NotFound = "SYS.APPROVAL.NOT_FOUND";
    public const string AlreadyPending = "SYS.APPROVAL.ALREADY_PENDING";
    public const string NotPending = "SYS.APPROVAL.NOT_PENDING";
    public const string NotApprover = "SYS.APPROVAL.NOT_APPROVER";
    public const string SelfApproval = "SYS.APPROVAL.SELF_APPROVAL";
    public const string UserRequired = "SYS.APPROVAL.USER_REQUIRED";

    private const decimal MaxAmount = 1_000_000_000_000_000m;

    public async Task<ApprovalState> SubmitAsync(ApprovalSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var companyId = await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var userId = RequireUser();
        var type = NumberRule.NormalizeDocumentType(submission.DocumentType);
        if (submission.DocumentId <= 0)
            throw new RequestValidationException("documentId", "Required.");
        var documentNo = submission.DocumentNo?.Trim();
        if (string.IsNullOrEmpty(documentNo) || documentNo.Length > 30)
            throw new RequestValidationException("documentNo", "Required; maximum 30 characters.");
        if (submission.Amount < 0 || submission.Amount >= MaxAmount || decimal.Round(submission.Amount, 4) != submission.Amount)
            throw new RequestValidationException("amount", "Use a non-negative amount with at most 4 decimal places.");
        var summary = submission.Summary?.Trim();
        if (summary?.Length > 200)
            throw new RequestValidationException("summary", "Maximum 200 characters.");

        if (await db.Set<ApprovalInstance>().AnyAsync(x => x.DocumentType == type && x.DocumentId == submission.DocumentId
                && x.Status == ApprovalStatus.Pending, cancellationToken))
            throw AlreadyPendingError();

        var flow = await db.Set<ApprovalFlow>().AsNoTracking().Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.DocumentType == type && x.IsActive, cancellationToken);
        var applicable = flow?.Steps.Where(s => s.AppliesTo(submission.Amount)).OrderBy(s => s.StepNo).ToList() ?? [];
        var now = clock.LocalNow;
        var instance = new ApprovalInstance
        {
            CompanyId = companyId,
            FlowId = flow?.Id,
            DocumentType = type,
            DocumentId = submission.DocumentId,
            DocumentNo = documentNo,
            Summary = string.IsNullOrEmpty(summary) ? null : summary,
            Amount = submission.Amount,
            AllowSelfApproval = flow?.AllowSelfApproval ?? false,
            SubmittedBy = userId,
            SubmittedAt = now,
            // 沒有啟用中的流程或沒有適用步驟：自動核准
            Status = applicable.Count == 0 ? ApprovalStatus.Approved : ApprovalStatus.Pending,
            CurrentStepNo = applicable.Count == 0 ? null : 1,
            CompletedAt = applicable.Count == 0 ? now : null,
        };
        instance.Steps.AddRange(applicable.Select((s, i) => new ApprovalInstanceStep
        {
            CompanyId = companyId,
            StepNo = i + 1,
            Name = s.Name,
            ApproverType = s.ApproverType,
            RoleId = s.RoleId,
            UserId = s.UserId,
            MinAmount = s.MinAmount,
            Status = i == 0 ? ApprovalStepStatus.Pending : ApprovalStepStatus.Waiting,
        }));
        db.Add(instance);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // 併發送審：篩選唯一索引只容許一個審批中的實例
            db.Entry(instance).State = EntityState.Detached;
            foreach (var step in instance.Steps) db.Entry(step).State = EntityState.Detached;
            throw AlreadyPendingError();
        }

        return ToState(instance);
    }

    public async Task<ApprovalState> CancelAsync(string documentType, long documentId, CancellationToken cancellationToken = default)
    {
        await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var type = NumberRule.NormalizeDocumentType(documentType);
        var instance = await db.Set<ApprovalInstance>().Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.DocumentType == type && x.DocumentId == documentId && x.Status == ApprovalStatus.Pending, cancellationToken)
            ?? throw new BusinessRuleException(NotPending, "The document has no pending approval.");

        Complete(instance, ApprovalStatus.Cancelled);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        return ToState(instance);
    }

    public async Task<ApprovalState?> GetStateAsync(string documentType, long documentId, CancellationToken cancellationToken = default)
    {
        await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var type = NumberRule.NormalizeDocumentType(documentType);
        var instance = await db.Set<ApprovalInstance>().AsNoTracking().Include(x => x.Steps)
            .Where(x => x.DocumentType == type && x.DocumentId == documentId)
            .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return instance is null ? null : ToState(instance);
    }

    /// <summary>目前用戶的待辦（REQ_F4118.29.6）：本人或代理可處理、且不是自己送審（流程允許自審除外）的目前步驟，依送審時間排序。</summary>
    public async Task<PagedResult<ApprovalTask>> ListTasksAsync(ApprovalTaskQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var companyId = await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var userId = RequireUser();
        var fields = SortSpec.Parse(request.Sort, ["submittedAt"], "submittedAt");
        if (fields.Count != 1)
            throw new RequestValidationException("sort", "Choose one task sort field.");

        var actors = await ActorsAsync(userId, companyId, cancellationToken);
        var userIds = actors.UserIds;
        var roleIds = actors.RoleIds;
        var query =
            from s in db.Set<ApprovalInstanceStep>().AsNoTracking()
            join i in db.Set<ApprovalInstance>().AsNoTracking() on s.InstanceId equals i.Id
            where s.Status == ApprovalStepStatus.Pending && i.Status == ApprovalStatus.Pending
                && (i.AllowSelfApproval || i.SubmittedBy != userId)
                && ((s.UserId != null && userIds.Contains(s.UserId.Value)) || (s.RoleId != null && roleIds.Contains(s.RoleId.Value)))
            select new { Step = s, Instance = i };
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            var type = NumberRule.NormalizeDocumentType(request.DocumentType);
            query = query.Where(x => x.Instance.DocumentType == type);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.Instance.DocumentNo.Contains(keyword) || (x.Instance.Summary != null && x.Instance.Summary.Contains(keyword)));
        }

        query = fields[0].Descending
            ? query.OrderByDescending(x => x.Instance.SubmittedAt).ThenByDescending(x => x.Instance.Id)
            : query.OrderBy(x => x.Instance.SubmittedAt).ThenBy(x => x.Instance.Id);
        var count = await query.LongCountAsync(cancellationToken);
        var rows = await query.Skip(request.Skip).Take(request.PageSize)
            .Select(x => new
            {
                x.Step,
                x.Instance,
                StepCount = db.Set<ApprovalInstanceStep>().Count(t => t.InstanceId == x.Instance.Id),
            })
            .ToListAsync(cancellationToken);
        var names = await UsernamesAsync(rows.Select(r => r.Instance.SubmittedBy).Concat(actors.UserIds), cancellationToken);
        var items = rows.Select(r =>
        {
            var onBehalfOf = actors.OnBehalfOf(r.Step);
            return new ApprovalTask(
                r.Instance.Id, r.Instance.DocumentType, r.Instance.DocumentId, r.Instance.DocumentNo, r.Instance.Summary, r.Instance.Amount,
                r.Instance.SubmittedBy, names.GetValueOrDefault(r.Instance.SubmittedBy, ""), r.Instance.SubmittedAt,
                r.Step.StepNo, r.StepCount, r.Step.Name, onBehalfOf, onBehalfOf is { } id ? names.GetValueOrDefault(id) : null, r.Instance.Version);
        }).ToList();
        return PagedResult.Create(items, request, count);
    }

    /// <summary>審批歷史（REQ_F4118.29.7）：目前公司的實例，可依單據類型、單據 Id、狀態篩選，預設新到舊。</summary>
    public async Task<PagedResult<ApprovalInstanceSummary>> ListAsync(ApprovalInstanceQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var fields = SortSpec.Parse(request.Sort, ["submittedAt"], "-submittedAt");
        if (fields.Count != 1)
            throw new RequestValidationException("sort", "Choose one approval sort field.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown status.");

        var query = db.Set<ApprovalInstance>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.DocumentType))
        {
            var type = NumberRule.NormalizeDocumentType(request.DocumentType);
            query = query.Where(x => x.DocumentType == type);
        }

        if (request.DocumentId is { } documentId) query = query.Where(x => x.DocumentId == documentId);
        if (request.Status is { } wanted) query = query.Where(x => x.Status == wanted);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword) || (x.Summary != null && x.Summary.Contains(keyword)));
        }

        query = fields[0].Descending
            ? query.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id)
            : query.OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id);
        var count = await query.LongCountAsync(cancellationToken);
        var rows = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var names = await UsernamesAsync(rows.Select(r => r.SubmittedBy), cancellationToken);
        return PagedResult.Create(rows.Select(x => new ApprovalInstanceSummary(
            x.Id, x.DocumentType, x.DocumentId, x.DocumentNo, x.Summary, x.Amount, x.Status, x.CurrentStepNo,
            x.SubmittedBy, names.GetValueOrDefault(x.SubmittedBy, ""), x.SubmittedAt, x.CompletedAt, x.Version)).ToList(), request, count);
    }

    public async Task<ApprovalInstanceDetail> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var instance = await db.Set<ApprovalInstance>().AsNoTracking().Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(NotFound, "Approval", id);
        return await ToDetailAsync(instance, cancellationToken);
    }

    /// <summary>核准目前步驟；最後一步核准即完成並通知單據模組（REQ_F4118.29.5）。</summary>
    public Task<ApprovalInstanceDetail> ApproveAsync(long id, ApprovalDecision decision, CancellationToken cancellationToken = default) =>
        DecideAsync(id, decision, approve: true, cancellationToken);

    /// <summary>駁回目前步驟，實例即為駁回並通知單據模組（REQ_F4118.29.5）。</summary>
    public Task<ApprovalInstanceDetail> RejectAsync(long id, ApprovalDecision decision, CancellationToken cancellationToken = default) =>
        DecideAsync(id, decision, approve: false, cancellationToken);

    private async Task<ApprovalInstanceDetail> DecideAsync(long id, ApprovalDecision decision, bool approve, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var companyId = await ApprovalScope.ActiveCompanyIdAsync(db, token);
        var userId = RequireUser();
        var comment = decision.Comment?.Trim();
        if (comment?.Length > 500)
            throw new RequestValidationException("comment", "Maximum 500 characters.");
        if (!approve && string.IsNullOrEmpty(comment))
            throw new RequestValidationException("comment", "A comment is required when rejecting.");

        var instance = await db.Set<ApprovalInstance>().Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFound, "Approval", id);
        if (instance.Status != ApprovalStatus.Pending)
            throw new BusinessRuleException(NotPending, "The approval has already been completed.");
        if (decision.Version < 0 || instance.Version != decision.Version) throw new ConflictException();
        if (!instance.AllowSelfApproval && instance.SubmittedBy == userId)
            throw new BusinessRuleException(SelfApproval, "Users cannot approve documents they submitted.");

        var step = instance.Steps.Single(s => s.StepNo == instance.CurrentStepNo);
        var actors = await ActorsAsync(userId, companyId, token);
        if (!actors.CanAct(step))
            throw new ForbiddenException(NotApprover, "You are not an approver of the current step.");

        step.Status = approve ? ApprovalStepStatus.Approved : ApprovalStepStatus.Rejected;
        step.ActedBy = userId;
        step.OnBehalfOfUserId = actors.OnBehalfOf(step);
        step.ActedAt = clock.LocalNow;
        step.Comment = string.IsNullOrEmpty(comment) ? null : comment;
        var next = instance.Steps.SingleOrDefault(s => s.StepNo == step.StepNo + 1);
        if (approve && next is not null)
        {
            next.Status = ApprovalStepStatus.Pending;
            instance.CurrentStepNo = next.StepNo;
        }
        else
        {
            Complete(instance, approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected);
        }

        auditTrail.MarkAction(instance, approve ? AuditActions.Approve : AuditActions.Reject);
        db.Entry(instance).Property(x => x.Version).OriginalValue = decision.Version;
        try
        {
            await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(token) : null;
            if (instance.Status != ApprovalStatus.Pending)
            {
                // 交易內事件：單據模組更新自己的單據，失敗即整個審批動作回滾
                var result = new ApprovalCompleted(instance.Id, instance.DocumentType, instance.DocumentId, instance.Status, userId, step.Comment);
                foreach (var handler in resultHandlers.Where(h => string.Equals(h.DocumentType, instance.DocumentType, StringComparison.Ordinal)))
                    await handler.HandleAsync(result, token);
            }

            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        return await ToDetailAsync(instance, token);
    }

    private void Complete(ApprovalInstance instance, ApprovalStatus status)
    {
        instance.Status = status;
        instance.CurrentStepNo = null;
        instance.CompletedAt = clock.LocalNow;
        foreach (var step in instance.Steps.Where(s => s.Status is ApprovalStepStatus.Pending or ApprovalStepStatus.Waiting))
            step.Status = ApprovalStepStatus.Skipped;
    }

    /// <summary>
    /// 目前用戶可代表的身分：本人，加上今天（業務日期）有效代理的被代理人；兩者都須是目前公司的啟用用戶。
    /// 角色只計算啟用角色、啟用的用戶公司授權。
    /// </summary>
    private async Task<Actors> ActorsAsync(long userId, long companyId, CancellationToken token)
    {
        var today = clock.Today;
        var delegators = await db.Set<ApprovalDelegation>().AsNoTracking()
            .Where(d => d.DelegateUserId == userId && d.IsActive && d.StartDate <= today && d.EndDate >= today)
            .OrderBy(d => d.Id)
            .Select(d => d.DelegatorUserId)
            .ToListAsync(token);
        var candidates = delegators.Prepend(userId).Distinct().ToArray();
        var members = await db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => candidates.Contains(uc.UserId) && uc.CompanyId == companyId && uc.IsActive
                && db.Set<SystemUser>().Any(u => u.Id == uc.UserId && u.IsActive))
            .Select(uc => uc.UserId)
            .ToListAsync(token);
        var activeIds = candidates.Where(members.Contains).ToArray();
        var roles = await db.Set<SystemUserRole>().AsNoTracking()
            .Where(ur => activeIds.Contains(ur.UserId) && ur.CompanyId == companyId
                && db.Set<SystemRole>().Any(r => r.Id == ur.RoleId && r.IsActive))
            .Select(ur => new { ur.UserId, ur.RoleId })
            .ToListAsync(token);
        return new Actors(userId, activeIds, roles.Select(r => (r.UserId, r.RoleId)).ToList());
    }

    private long RequireUser() =>
        currentUser.UserId ?? throw new UnauthenticatedException(UserRequired, "A signed-in user is required for approvals.");

    private static ConflictException AlreadyPendingError() =>
        new(AlreadyPending, "The document already has a pending approval.");

    private async Task<Dictionary<long, string>> UsernamesAsync(IEnumerable<long> ids, CancellationToken token)
    {
        var distinct = ids.Distinct().ToArray();
        return distinct.Length == 0
            ? []
            : await db.Set<SystemUser>().AsNoTracking().IgnoreQueryFilters()
                .Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, token);
    }

    private async Task<ApprovalInstanceDetail> ToDetailAsync(ApprovalInstance instance, CancellationToken token)
    {
        var steps = instance.Steps.OrderBy(s => s.StepNo).ToList();
        var names = await UsernamesAsync(steps.SelectMany(s => new[] { s.UserId, s.ActedBy, s.OnBehalfOfUserId })
            .Where(x => x is not null).Select(x => x!.Value).Append(instance.SubmittedBy), token);
        var roleIds = steps.Where(s => s.RoleId is not null).Select(s => s.RoleId!.Value).Distinct().ToArray();
        var roleNames = roleIds.Length == 0
            ? []
            : await db.Set<SystemRole>().AsNoTracking().IgnoreQueryFilters()
                .Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, token);
        string? Name(Dictionary<long, string> map, long? key) => key is { } k ? map.GetValueOrDefault(k) : null;
        return new ApprovalInstanceDetail(
            instance.Id, instance.DocumentType, instance.DocumentId, instance.DocumentNo, instance.Summary, instance.Amount, instance.Status,
            instance.CurrentStepNo, instance.AllowSelfApproval, instance.SubmittedBy, names.GetValueOrDefault(instance.SubmittedBy, ""),
            instance.SubmittedAt, instance.CompletedAt,
            steps.Select(s => new ApprovalInstanceStepDetail(
                s.StepNo, s.Name, s.ApproverType, s.RoleId, Name(roleNames, s.RoleId), s.UserId, Name(names, s.UserId), s.MinAmount, s.Status,
                s.ActedBy, Name(names, s.ActedBy), s.OnBehalfOfUserId, Name(names, s.OnBehalfOfUserId), s.ActedAt, s.Comment)).ToList(),
            instance.Version);
    }

    private static ApprovalState ToState(ApprovalInstance instance) => new(
        instance.Id, instance.DocumentType, instance.DocumentId, instance.Status, instance.CurrentStepNo, instance.Steps.Count);

    /// <summary>用戶本人與被代理人（皆已確認為啟用成員）及其角色。</summary>
    private sealed class Actors(long userId, IReadOnlyList<long> userIds, IReadOnlyList<(long UserId, long RoleId)> roles)
    {
        public long[] UserIds { get; } = [.. userIds];

        public long[] RoleIds { get; } = [.. roles.Select(r => r.RoleId).Distinct()];

        public bool CanAct(ApprovalInstanceStep step) => Resolve(step).Allowed;

        /// <summary>本人即可處理時為 null；否則為代理的被代理人（依代理建立次序取第一位）。</summary>
        public long? OnBehalfOf(ApprovalInstanceStep step) => Resolve(step).OnBehalfOf;

        private (bool Allowed, long? OnBehalfOf) Resolve(ApprovalInstanceStep step)
        {
            bool Holds(long id) => step.ApproverType == ApproverType.User
                ? step.UserId == id
                : roles.Any(r => r.UserId == id && r.RoleId == step.RoleId);

            foreach (var id in UserIds)
            {
                if (Holds(id)) return (true, id == userId ? null : id);
            }

            return (false, null);
        }
    }
}
