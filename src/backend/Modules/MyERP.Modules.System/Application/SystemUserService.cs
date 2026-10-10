using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.System.Application;

/// <summary>登入身分內部服務。對外 API 須等 WU-SYS-004 權限檢查完成後接入。</summary>
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

    [GeneratedRegex("^[A-Za-z0-9._-]{3,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
