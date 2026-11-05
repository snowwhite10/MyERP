using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Finance.Infrastructure;

public sealed class GlPeriodWriteException(GlPeriodWriteStatus status) : Exception($"GL period write rejected: {status}")
{
    public GlPeriodWriteStatus Status { get; } = status;
}

/// <summary>Serializes period creation for one company before checking inclusive overlap.</summary>
public sealed class GlPeriodWriteService(MyErpDbContext db, IClock clock, ICurrentUser currentUser)
{
    public async Task<GlPeriod> CreateAsync(long companyId, DateOnly startDate, DateOnly endDate,
        bool isOpen, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0 || db.CurrentCompanyId != companyId)
            throw new GlPeriodWriteException(GlPeriodWriteStatus.IdentityMismatch);
        var candidate = new GlPeriodWindow(0, companyId, startDate, endDate, isOpen);
        if (!GlPeriodDateRules.IsValid(startDate, endDate))
            throw new GlPeriodWriteException(GlPeriodWriteStatus.InvalidRange);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var resource = $"gl_period:{companyId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51020, 'GL period lock was not granted.', 1;
            """, cancellationToken);

        var saved = await db.Set<GlPeriod>().AsNoTracking()
            .Where(period => period.CompanyId == companyId)
            .Select(period => new GlPeriodWindow(period.Id, period.CompanyId, period.StartDate,
                period.EndDate, period.Status == GlPeriodStatus.Open))
            .ToListAsync(cancellationToken);
        var decision = GlPeriodWriteRules.Create(candidate, saved);
        if (decision != GlPeriodWriteStatus.Allowed)
            throw new GlPeriodWriteException(decision);

        var period = new GlPeriod
        {
            CompanyId = companyId,
            StartDate = startDate,
            EndDate = endDate,
            Status = isOpen ? GlPeriodStatus.Open : GlPeriodStatus.Closed,
            Version = 1,
            CreatedAt = clock.LocalNow,
            CreatedBy = currentUser.UserId ?? SystemUser.Id
        };
        db.Set<GlPeriod>().Add(period);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return period;
    }
}
