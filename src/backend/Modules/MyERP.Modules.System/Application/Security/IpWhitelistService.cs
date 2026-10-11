using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;
using SystemUser = MyERP.Modules.System.Domain.SystemUser;

namespace MyERP.Modules.System.Application.Security;

public sealed record IpWhitelistInput(IpAccessScope Scope, long? TargetId, string IpRange,
    DateOnly? ValidFrom, DateOnly? ValidTo, string? Description);

public sealed record IpWhitelistUpdate(DateOnly? ValidFrom, DateOnly? ValidTo, string? Description,
    bool IsActive, int Version);

public sealed record IpWhitelistDetail(long Id, IpAccessScope Scope, long? TargetId, string IpRange,
    DateOnly? ValidFrom, DateOnly? ValidTo, string? Description, bool IsActive, int Version);

/// <summary>全系統 IP 白名單維護；只能停用、不能刪除，保留稽核歷史。</summary>
public sealed class IpWhitelistService(MyErpDbContext db, IpWhitelistCache cache)
{
    public const string NotFound = "SYS.IP_WHITELIST.NOT_FOUND";
    public const string Duplicate = "SYS.IP_WHITELIST.DUPLICATE";

    public async Task<PagedResult<IpWhitelistDetail>> ListAsync(PagedRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var sort = SortSpec.Parse(request.Sort, ["id", "ipRange"], "id");
        if (sort.Count != 1) throw new RequestValidationException("sort", "Choose one IP whitelist sort field.");
        var query = db.Set<IpWhitelistEntry>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.IpRange.Contains(keyword) || (x.Description != null && x.Description.Contains(keyword)));
        }
        query = sort[0] switch
        {
            { Field: "ipRange", Descending: true } => query.OrderByDescending(x => x.IpRange).ThenBy(x => x.Id),
            { Field: "ipRange" } => query.OrderBy(x => x.IpRange).ThenBy(x => x.Id),
            { Descending: true } => query.OrderByDescending(x => x.Id),
            _ => query.OrderBy(x => x.Id),
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize).Select(x =>
            new IpWhitelistDetail(x.Id, x.Scope,
                x.Scope == IpAccessScope.Role ? x.RoleId : x.UserId,
                x.IpRange, x.ValidFrom, x.ValidTo, x.Description, x.IsActive, x.Version)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<IpWhitelistDetail> GetAsync(long id, CancellationToken token = default) =>
        ToDetail(await FindAsync(id, false, token));

    public async Task<IpWhitelistDetail> CreateAsync(IpWhitelistInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.Scope)) throw new RequestValidationException("scope", "Unknown IP whitelist scope.");
        var range = IpRange.TryParse(input.IpRange, out var parsed) ? parsed.Value.ToString()
            : throw new RequestValidationException("ipRange", "Use a complete IPv4/IPv6 address or CIDR network.");
        ValidateDates(input.ValidFrom, input.ValidTo);
        var target = await ValidateTargetAsync(input.Scope, input.TargetId, token);
        var entry = new IpWhitelistEntry
        {
            Scope = input.Scope,
            RoleId = input.Scope == IpAccessScope.Role ? target : null,
            UserId = input.Scope == IpAccessScope.User ? target : null,
            IpRange = range,
            ValidFrom = input.ValidFrom,
            ValidTo = input.ValidTo,
            Description = ValidateDescription(input.Description),
        };
        db.Add(entry);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(Duplicate, "This IP range already exists for the target.");
        }
        cache.Invalidate();
        return ToDetail(entry);
    }

    public async Task<IpWhitelistDetail> UpdateAsync(long id, IpWhitelistUpdate input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateDates(input.ValidFrom, input.ValidTo);
        var description = ValidateDescription(input.Description);
        var entry = await FindAsync(id, true, token);
        if (input.Version < 0 || entry.Version != input.Version) throw new ConflictException();
        entry.ValidFrom = input.ValidFrom;
        entry.ValidTo = input.ValidTo;
        entry.Description = description;
        entry.IsActive = input.IsActive;
        db.Entry(entry).Property(x => x.Version).OriginalValue = input.Version;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException(); }
        cache.Invalidate();
        return ToDetail(entry);
    }

    private async Task<IpWhitelistEntry> FindAsync(long id, bool tracking, CancellationToken token)
    {
        var query = tracking ? db.Set<IpWhitelistEntry>() : db.Set<IpWhitelistEntry>().AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(NotFound, "IP whitelist entry", id);
    }

    private async Task<long?> ValidateTargetAsync(IpAccessScope scope, long? targetId, CancellationToken token)
    {
        if (scope == IpAccessScope.System)
        {
            if (targetId is not null) throw new RequestValidationException("targetId", "System scope has no target.");
            return null;
        }
        if (targetId is null or <= 0) throw new RequestValidationException("targetId", "A role or user is required.");
        var exists = scope == IpAccessScope.Role
            ? await db.Set<SystemRole>().AnyAsync(x => x.Id == targetId && x.IsActive, token)
            : await db.Set<SystemUser>().AnyAsync(x => x.Id == targetId && x.IsActive, token);
        if (!exists) throw new RequestValidationException("targetId", "The target is unavailable.");
        return targetId;
    }

    private static void ValidateDates(DateOnly? from, DateOnly? to)
    {
        if (from > to) throw new RequestValidationException("validTo", "End date must be on or after start date.");
    }

    private static string? ValidateDescription(string? text)
    {
        var value = text?.Trim();
        if (value?.Length > IpWhitelistEntry.MaxDescriptionLength)
            throw new RequestValidationException("description", "Description is too long.");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static IpWhitelistDetail ToDetail(IpWhitelistEntry x) =>
        new(x.Id, x.Scope, x.TargetId, x.IpRange, x.ValidFrom, x.ValidTo, x.Description, x.IsActive, x.Version);
}
