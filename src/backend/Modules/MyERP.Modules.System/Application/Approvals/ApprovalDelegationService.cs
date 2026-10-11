using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;

namespace MyERP.Modules.System.Application.Approvals;

/// <summary>建立審批代理。</summary>
public sealed record ApprovalDelegationInput(long DelegatorUserId, long DelegateUserId, DateOnly StartDate, DateOnly EndDate, string? Remark);

/// <summary>修改審批代理的期間、備註或啟用狀態，須帶 <c>version</c>。被代理人與代理人不可修改。</summary>
public sealed record ApprovalDelegationUpdate(DateOnly StartDate, DateOnly EndDate, string? Remark, bool IsActive, int Version);

public sealed record ApprovalDelegationDetail(
    long Id, long DelegatorUserId, string DelegatorUsername, long DelegateUserId, string DelegateUsername,
    DateOnly StartDate, DateOnly EndDate, string? Remark, bool IsActive, int Version);

/// <summary>篩選：<c>UserId</c> 為被代理人或代理人；<c>ActiveOn</c> 只列出該日有效（啟用且日期涵蓋）的代理。</summary>
public sealed record ApprovalDelegationQuery : PagedRequest
{
    public long? UserId { get; init; }

    public DateOnly? ActiveOn { get; init; }
}

/// <summary>審批代理維護（REQ_F4118.29.3）。只處理目前公司的代理；呼叫端以權限代碼保護。</summary>
public sealed class ApprovalDelegationService(MyErpDbContext db)
{
    public const string NotFound = "SYS.APPROVAL_DELEGATION.NOT_FOUND";
    public const string Overlap = "SYS.APPROVAL_DELEGATION.OVERLAP";

    public async Task<PagedResult<ApprovalDelegationDetail>> ListAsync(ApprovalDelegationQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        var fields = SortSpec.Parse(request.Sort, ["startDate"], "-startDate");
        if (fields.Count != 1)
            throw new RequestValidationException("sort", "Choose one delegation sort field.");

        var query = db.Set<ApprovalDelegation>().AsNoTracking();
        if (request.UserId is { } userId)
            query = query.Where(x => x.DelegatorUserId == userId || x.DelegateUserId == userId);
        if (request.ActiveOn is { } day)
            query = query.Where(x => x.IsActive && x.StartDate <= day && x.EndDate >= day);
        query = fields[0].Descending
            ? query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            : query.OrderBy(x => x.StartDate).ThenBy(x => x.Id);
        var count = await query.LongCountAsync(cancellationToken);
        var items = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var names = await UsernamesAsync(items.SelectMany(x => new[] { x.DelegatorUserId, x.DelegateUserId }), cancellationToken);
        return PagedResult.Create(items.Select(x => ToDetail(x, names)).ToList(), request, count);
    }

    public async Task<ApprovalDelegationDetail> CreateAsync(ApprovalDelegationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var companyId = await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        if (input.DelegatorUserId == input.DelegateUserId)
            throw new RequestValidationException("delegateUserId", "The delegate must be another user.");
        if (!await ApprovalScope.IsActiveMemberAsync(db, input.DelegatorUserId, companyId, cancellationToken))
            throw new RequestValidationException("delegatorUserId", "The user is not an active user of this company.");
        if (!await ApprovalScope.IsActiveMemberAsync(db, input.DelegateUserId, companyId, cancellationToken))
            throw new RequestValidationException("delegateUserId", "The user is not an active user of this company.");
        ValidatePeriod(input.StartDate, input.EndDate);

        var delegation = new ApprovalDelegation
        {
            CompanyId = companyId,
            DelegatorUserId = input.DelegatorUserId,
            DelegateUserId = input.DelegateUserId,
            StartDate = input.StartDate,
            EndDate = input.EndDate,
            Remark = ValidateRemark(input.Remark),
        };
        await EnsureNoOverlapAsync(delegation, cancellationToken);
        db.Add(delegation);
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(delegation, await UsernamesAsync([delegation.DelegatorUserId, delegation.DelegateUserId], cancellationToken));
    }

    public async Task<ApprovalDelegationDetail> UpdateAsync(long id, ApprovalDelegationUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await ApprovalScope.ActiveCompanyIdAsync(db, cancellationToken);
        ValidatePeriod(input.StartDate, input.EndDate);
        var remark = ValidateRemark(input.Remark);
        var delegation = await db.Set<ApprovalDelegation>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException(NotFound, "Approval delegation", id);
        if (input.Version < 0 || delegation.Version != input.Version) throw new ConflictException();

        delegation.StartDate = input.StartDate;
        delegation.EndDate = input.EndDate;
        delegation.Remark = remark;
        delegation.IsActive = input.IsActive;
        await EnsureNoOverlapAsync(delegation, cancellationToken);
        db.Entry(delegation).Property(x => x.Version).OriginalValue = input.Version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }

        return ToDetail(delegation, await UsernamesAsync([delegation.DelegatorUserId, delegation.DelegateUserId], cancellationToken));
    }

    /// <summary>同一被代理人啟用中的代理期間不可重疊，避免同一天有兩位代理人時權責不清。</summary>
    private async Task EnsureNoOverlapAsync(ApprovalDelegation delegation, CancellationToken token)
    {
        if (!delegation.IsActive) return;
        var overlaps = await db.Set<ApprovalDelegation>().AnyAsync(x => x.Id != delegation.Id && x.IsActive
            && x.DelegatorUserId == delegation.DelegatorUserId
            && x.StartDate <= delegation.EndDate && x.EndDate >= delegation.StartDate, token);
        if (overlaps)
            throw new ConflictException(Overlap, "The user already has an active delegation in this period.");
    }

    private static void ValidatePeriod(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new RequestValidationException("endDate", "The end date must not be earlier than the start date.");
    }

    private static string? ValidateRemark(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed?.Length > 200)
            throw new RequestValidationException("remark", "Maximum 200 characters.");
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private async Task<Dictionary<long, string>> UsernamesAsync(IEnumerable<long> ids, CancellationToken token)
    {
        var distinct = ids.Distinct().ToArray();
        return await db.Set<SystemUser>().AsNoTracking().IgnoreQueryFilters()
            .Where(u => distinct.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, token);
    }

    private static ApprovalDelegationDetail ToDetail(ApprovalDelegation x, Dictionary<long, string> names) => new(
        x.Id, x.DelegatorUserId, names.GetValueOrDefault(x.DelegatorUserId, ""), x.DelegateUserId, names.GetValueOrDefault(x.DelegateUserId, ""),
        x.StartDate, x.EndDate, x.Remark, x.IsActive, x.Version);
}
