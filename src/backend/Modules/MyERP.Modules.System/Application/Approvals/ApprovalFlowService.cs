using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Application.Approvals;

/// <summary>流程步驟輸入：角色步驟填 <c>RoleId</c>，用戶步驟填 <c>UserId</c>；<c>MinAmount</c> 為金額門檻（null = 一律需要）。</summary>
public sealed record ApprovalStepInput(string Name, ApproverType ApproverType, long? RoleId, long? UserId, decimal? MinAmount);

/// <summary>建立審批流程。</summary>
public sealed record ApprovalFlowInput(string DocumentType, string Name, bool AllowSelfApproval, IReadOnlyList<ApprovalStepInput> Steps);

/// <summary>修改審批流程（整組取代步驟），須帶讀取時的 <c>version</c>。單據類型不可修改。</summary>
public sealed record ApprovalFlowUpdate(string Name, bool AllowSelfApproval, bool IsActive, IReadOnlyList<ApprovalStepInput> Steps, int Version);

public sealed record ApprovalStepDetail(int StepNo, string Name, ApproverType ApproverType, long? RoleId, long? UserId, decimal? MinAmount);

public sealed record ApprovalFlowDetail(
    long Id, string DocumentType, string Name, bool AllowSelfApproval, bool IsActive, IReadOnlyList<ApprovalStepDetail> Steps, int Version);

/// <summary>審批流程維護（REQ_F4118.29.1–2）。只處理目前公司的流程；呼叫端以權限代碼保護。</summary>
public sealed class ApprovalFlowService(MyErpDbContext db)
{
    public const string NotFound = "SYS.APPROVAL_FLOW.NOT_FOUND";
    public const string DuplicateType = "SYS.APPROVAL_FLOW.DUPLICATE_TYPE";

    public async Task<PagedResult<ApprovalFlowDetail>> ListAsync(PagedRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var fields = SortSpec.Parse(request.Sort, ["documentType", "name"], "documentType");
        if (fields.Count != 1)
            throw new RequestValidationException("sort", "Choose one approval flow sort field.");

        var query = db.Set<ApprovalFlow>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentType.Contains(keyword) || x.Name.Contains(keyword));
        }

        query = query.OrderByKeyOrName(fields[0], x => x.DocumentType, x => x.Name);
        var count = await query.LongCountAsync(cancellationToken);
        var flows = await query.Include(x => x.Steps).AsSplitQuery()
            .Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return PagedResult.Create(flows.Select(ToDetail).ToList(), request, count);
    }

    public async Task<ApprovalFlowDetail> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        return ToDetail(await FindAsync(id, false, cancellationToken));
    }

    public async Task<ApprovalFlowDetail> CreateAsync(ApprovalFlowInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var companyId = await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var flow = new ApprovalFlow
        {
            CompanyId = companyId,
            DocumentType = NumberRule.NormalizeDocumentType(input.DocumentType),
            Name = ValidateName(input.Name, "name"),
            AllowSelfApproval = input.AllowSelfApproval,
        };
        var steps = await ValidateStepsAsync(companyId, input.Steps, cancellationToken);
        if (await db.Set<ApprovalFlow>().AnyAsync(x => x.DocumentType == flow.DocumentType, cancellationToken))
            throw new ConflictException(DuplicateType, "An approval flow already exists for this document type.");

        flow.Steps.AddRange(steps);
        db.Add(flow);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(DuplicateType, "An approval flow already exists for this document type.");
        }

        return ToDetail(flow);
    }

    /// <summary>修改流程並整組取代步驟。審批中的實例使用送審時的步驟快照，不受影響。</summary>
    public async Task<ApprovalFlowDetail> UpdateAsync(long id, ApprovalFlowUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var companyId = await SystemQueries.ActiveCompanyIdAsync(db, cancellationToken);
        var name = ValidateName(input.Name, "name");
        var steps = await ValidateStepsAsync(companyId, input.Steps, cancellationToken);
        var flow = await FindAsync(id, true, cancellationToken);
        if (input.Version < 0 || flow.Version != input.Version) throw new ConflictException();

        flow.Name = name;
        flow.AllowSelfApproval = input.AllowSelfApproval;
        flow.IsActive = input.IsActive;
        // 先刪除舊步驟再新增，避免 (flow_id, step_no) 唯一索引在同一批次內衝突
        db.RemoveRange(flow.Steps);
        flow.Steps.Clear();
        // 只改步驟時表頭也要遞增 version，令併發的步驟修改互相衝突
        db.Entry(flow).Property(x => x.Name).IsModified = true;
        db.Entry(flow).Property(x => x.Version).OriginalValue = input.Version;
        try
        {
            await using var transaction = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await db.SaveChangesAsync(cancellationToken);
            flow.Steps.AddRange(steps);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        return ToDetail(flow);
    }

    private async Task<List<ApprovalFlowStep>> ValidateStepsAsync(long companyId, IReadOnlyList<ApprovalStepInput>? steps, CancellationToken token)
    {
        if (steps is null || steps.Count is < 1 or > ApprovalFlow.MaxSteps)
            throw new RequestValidationException("steps", $"Define 1 to {ApprovalFlow.MaxSteps} steps.");

        // 先逐步檢查格式，再以兩次查詢批次確認角色與用戶，避免迴圈內查詢（SYS-B09）
        var result = new List<ApprovalFlowStep>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i] ?? throw new RequestValidationException($"steps[{i}]", "Required.");
            var field = $"steps[{i}]";
            if (!Enum.IsDefined(step.ApproverType))
                throw new RequestValidationException($"{field}.approverType", "Unknown approver type.");
            if (step.MinAmount is { } min && (min < 0 || min >= 1_000_000_000_000_000m || decimal.Round(min, 4) != min))
                throw new RequestValidationException($"{field}.minAmount", "Use a non-negative amount with at most 4 decimal places.");
            if (step.ApproverType == ApproverType.Role && (step.RoleId is null || step.UserId is not null))
                throw new RequestValidationException($"{field}.roleId", "A role step needs a role and no user.");
            if (step.ApproverType == ApproverType.User && (step.UserId is null || step.RoleId is not null))
                throw new RequestValidationException($"{field}.userId", "A user step needs a user and no role.");

            result.Add(new ApprovalFlowStep
            {
                CompanyId = companyId,
                StepNo = i + 1,
                Name = ValidateName(step.Name, $"{field}.name"),
                ApproverType = step.ApproverType,
                RoleId = step.RoleId,
                UserId = step.UserId,
                MinAmount = step.MinAmount,
            });
        }

        var roleIds = result.Where(s => s.RoleId is not null).Select(s => s.RoleId!.Value).Distinct().ToArray();
        var activeRoles = roleIds.Length == 0
            ? []
            : (await db.Set<SystemRole>().AsNoTracking().Where(r => roleIds.Contains(r.Id) && r.IsActive)
                .Select(r => r.Id).ToListAsync(token)).ToHashSet();
        var userIds = result.Where(s => s.UserId is not null).Select(s => s.UserId!.Value).Distinct().ToArray();
        var activeMembers = userIds.Length == 0
            ? []
            : (await ApprovalScope.ActiveMemberIdsAsync(db, userIds, companyId, token)).ToHashSet();
        for (var i = 0; i < result.Count; i++)
        {
            if (result[i].RoleId is { } roleId && !activeRoles.Contains(roleId))
                throw new RequestValidationException($"steps[{i}].roleId", "The role does not exist or is inactive.");
            if (result[i].UserId is { } userId && !activeMembers.Contains(userId))
                throw new RequestValidationException($"steps[{i}].userId", "The user is not an active user of this company.");
        }

        return result;
    }

    private async Task<ApprovalFlow> FindAsync(long id, bool tracking, CancellationToken token)
    {
        var query = tracking ? db.Set<ApprovalFlow>() : db.Set<ApprovalFlow>().AsNoTracking();
        return await query.Include(x => x.Steps).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFound, "Approval flow", id);
    }

    internal static string ValidateName(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 100)
            throw new RequestValidationException(field, "Required; maximum 100 characters.");
        return trimmed;
    }

    private static ApprovalFlowDetail ToDetail(ApprovalFlow flow) => new(
        flow.Id, flow.DocumentType, flow.Name, flow.AllowSelfApproval, flow.IsActive,
        flow.Steps.OrderBy(s => s.StepNo).Select(s => new ApprovalStepDetail(s.StepNo, s.Name, s.ApproverType, s.RoleId, s.UserId, s.MinAmount)).ToList(),
        flow.Version);
}

/// <summary>審批相關服務共用的公司與成員檢查。</summary>
internal static class ApprovalScope
{
    /// <summary>用戶啟用、未刪除，且在該公司的授權啟用。</summary>
    public static Task<bool> IsActiveMemberAsync(MyErpDbContext db, long userId, long companyId, CancellationToken token) =>
        ActiveMembers(db, [userId], companyId).AnyAsync(token);

    /// <summary>傳回 <paramref name="userIds"/> 中符合 <see cref="IsActiveMemberAsync"/> 條件的用戶（一次查詢）。</summary>
    public static Task<List<long>> ActiveMemberIdsAsync(MyErpDbContext db, IReadOnlyCollection<long> userIds, long companyId, CancellationToken token) =>
        ActiveMembers(db, userIds, companyId).ToListAsync(token);

    private static IQueryable<long> ActiveMembers(MyErpDbContext db, IReadOnlyCollection<long> userIds, long companyId) =>
        db.Set<SystemUserCompany>().AsNoTracking()
            .Where(uc => userIds.Contains(uc.UserId) && uc.CompanyId == companyId && uc.IsActive
                && db.Set<SystemUser>().Any(u => u.Id == uc.UserId && u.IsActive))
            .Select(uc => uc.UserId);
}
