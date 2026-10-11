using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Time;

namespace MyERP.Modules.System.Application;

/// <summary>用戶列表項目；不含密碼雜湊、失敗次數及鎖定時間（只回是否鎖定）。</summary>
public sealed record UserSummary(
    long Id, string Username, bool IsActive, bool IsLocked, DateTime PasswordChangedAt, bool IsPasswordExpired, int Version);

/// <summary>登入身分服務；管理端點見 <c>Api/UsersController</c>（範圍為目前公司，ADR-016）。</summary>
public sealed partial class SystemUserService(
    MyErpDbContext db,
    IPasswordHasher<SystemUser> hasher,
    IClock clock,
    PasswordPolicy policy)
{
    public async Task<SystemUser> CreateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var name = username?.Trim() ?? "";
        if (!UsernamePattern().IsMatch(name))
        {
            throw new RequestValidationException("username", "Username must contain 3-100 ASCII letters, digits, dots, hyphens or underscores.");
        }
        policy.ValidatePassword(password);
        var normalized = name.ToUpperInvariant();
        if (await db.Set<SystemUser>().IgnoreQueryFilters().AnyAsync(x => x.NormalizedUsername == normalized, cancellationToken))
        {
            throw new ConflictException("SYS.USER.DUPLICATE", "Username already exists.");
        }

        var user = new SystemUser
        {
            Username = name,
            NormalizedUsername = normalized,
            PasswordHash = "",
            PasswordChangedAt = clock.LocalNow,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("SYS.USER.DUPLICATE", "Username already exists.");
        }
        return user;
    }

    /// <summary>
    /// 在公司內建立用戶：同一交易建立身分及該公司的用戶公司關聯（預設公司、尚無角色），
    /// 令新用戶出現在該公司的用戶列表（ADR-016）。
    /// </summary>
    public async Task<UserSummary> CreateInCompanyAsync(
        string username, string password, long companyId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await CreateAsync(username, password, cancellationToken);
        db.Add(new SystemUserCompany { UserId = user.Id, CompanyId = companyId, IsDefault = true });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSummary(user);
    }

    /// <summary>
    /// 公司的用戶（有用戶公司關聯，含已停用用戶及已停用關聯）：分頁、關鍵字（帳號包含，不分大小寫）、
    /// 排序 <c>username</c>（可加 <c>-</c> 降序）。
    /// </summary>
    public async Task<PagedResult<UserSummary>> ListInCompanyAsync(
        PagedRequest request, long companyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        var fields = SortSpec.Parse(request.Sort, ["username"], "username");
        if (fields.Count != 1)
        {
            throw new RequestValidationException("sort", "Choose one user sort field.");
        }

        var query = InCompany(companyId).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToUpperInvariant();
            query = query.Where(x => x.NormalizedUsername.Contains(keyword));
        }
        query = fields[0].Descending
            ? query.OrderByDescending(x => x.NormalizedUsername).ThenBy(x => x.Id)
            : query.OrderBy(x => x.NormalizedUsername).ThenBy(x => x.Id);
        var count = await query.LongCountAsync(cancellationToken);
        var users = await query.Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        return PagedResult.Create([.. users.Select(ToSummary)], request, count);
    }

    /// <summary>啟用／停用用戶（須帶 <c>version</c>）；不可停用自己；他公司用戶回 404。</summary>
    public async Task<UserSummary> UpdateInCompanyAsync(
        long id, bool isActive, int version, long companyId, long actingUserId, CancellationToken cancellationToken = default)
    {
        var user = await FindInCompanyAsync(id, companyId, cancellationToken);
        if (user.Version != version)
        {
            throw new ConflictException();
        }

        if (!isActive && id == actingUserId)
        {
            throw new BusinessRuleException("SYS.USER.SELF_DEACTIVATION", "You cannot deactivate your own account.");
        }

        user.IsActive = isActive;
        await SaveUpdateAsync(user, version, cancellationToken);
        return ToSummary(user);
    }

    /// <summary>管理員重設密碼：依密碼政策驗證，重設到期起算時間並清除失敗計數與鎖定。</summary>
    public async Task ResetPasswordInCompanyAsync(long id, string password, long companyId, CancellationToken cancellationToken = default)
    {
        var user = await FindInCompanyAsync(id, companyId, cancellationToken);
        policy.ValidatePassword(password);
        user.PasswordHash = hasher.HashPassword(user, password);
        user.PasswordChangedAt = clock.LocalNow;
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        await SaveUpdateAsync(user, user.Version, cancellationToken);
    }

    /// <summary>
    /// 本人修改密碼：呼叫端須先以 <see cref="VerifyPasswordAsync"/> 驗證目前密碼及以密碼政策驗證新密碼。
    /// 重新起算到期並清除失敗計數；與呼叫端已加入的安全稽核同一次儲存。修改時間之前簽發的 Refresh Token 隨之失效（ADR-029）。
    /// </summary>
    public async Task<SystemUser> ChangeOwnPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await db.Set<SystemUser>().SingleAsync(x => x.Id == userId, cancellationToken);
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        user.PasswordChangedAt = clock.LocalNow;
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        await SaveUpdateAsync(user, user.Version, cancellationToken);
        return user;
    }

    /// <summary>解除鎖定並清除失敗計數（未鎖定時亦成功，可重複呼叫）。</summary>
    public async Task UnlockInCompanyAsync(long id, long companyId, CancellationToken cancellationToken = default)
    {
        var user = await FindInCompanyAsync(id, companyId, cancellationToken);
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        await SaveUpdateAsync(user, user.Version, cancellationToken);
    }

    public async Task<bool> VerifyPasswordAsync(long userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await db.Set<SystemUser>().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.LockoutEnd is { } until && until > clock.LocalNow)
        {
            return false;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        var now = clock.LocalNow;
        var currentHash = user.PasswordHash;
        var eligible = db.Set<SystemUser>().Where(x => x.Id == userId && x.IsActive &&
            x.PasswordHash == currentHash && (x.LockoutEnd == null || x.LockoutEnd <= now));
        if (result == PasswordVerificationResult.Failed)
        {
            var lockUntil = now.AddMinutes(15);
            await eligible.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.FailedLoginCount, x => x.LockoutEnd != null ? 1 : x.FailedLoginCount + 1)
                .SetProperty(x => x.LockoutEnd, x => x.LockoutEnd != null ? (DateTime?)null :
                    x.FailedLoginCount >= 4 ? lockUntil : (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            await db.Entry(user).ReloadAsync(cancellationToken);
            return false;
        }

        var updatedHash = result == PasswordVerificationResult.SuccessRehashNeeded
            ? hasher.HashPassword(user, password)
            : currentHash;
        var changed = await eligible.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.FailedLoginCount, 0)
            .SetProperty(x => x.LockoutEnd, (DateTime?)null)
            .SetProperty(x => x.PasswordHash, updatedHash)
            .SetProperty(x => x.UpdatedAt, now)
            .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
        await db.Entry(user).ReloadAsync(cancellationToken);
        return changed == 1;
    }

    /// <summary>登入流程須在簽發 token 前檢查此值，逾期時轉到改密碼流程。</summary>
    public bool IsPasswordExpired(SystemUser user) =>
        policy.ExpirationDays > 0 && user.PasswordChangedAt.AddDays(policy.ExpirationDays) <= clock.LocalNow;

    private IQueryable<SystemUser> InCompany(long companyId) =>
        db.Set<SystemUser>().Where(u => db.Set<SystemUserCompany>().Any(uc => uc.UserId == u.Id && uc.CompanyId == companyId));

    private async Task<SystemUser> FindInCompanyAsync(long id, long companyId, CancellationToken cancellationToken) =>
        await InCompany(companyId).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("SYS.USER.NOT_FOUND", "User", id);

    private async Task SaveUpdateAsync(SystemUser user, int version, CancellationToken cancellationToken)
    {
        db.Entry(user).Property(x => x.Version).OriginalValue = version;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException();
        }
    }

    private UserSummary ToSummary(SystemUser user) => new(
        user.Id, user.Username, user.IsActive, user.LockoutEnd is { } until && until > clock.LocalNow,
        user.PasswordChangedAt, IsPasswordExpired(user), user.Version);

    [GeneratedRegex("^[A-Za-z0-9._-]{3,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
