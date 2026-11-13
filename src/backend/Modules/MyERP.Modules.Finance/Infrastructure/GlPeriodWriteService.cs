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

/// <summary>Serializes company period writes and reads Posted dates before applying the domain rules.</summary>
public sealed class GlPeriodWriteService(MyErpDbContext db, IClock clock, ICurrentUser currentUser)
{
    public async Task<GlPeriod> CreateAsync(long companyId, DateOnly startDate, DateOnly endDate,
        bool isOpen, CancellationToken cancellationToken = default)
    {
        RequireCompany(companyId);
        var candidate = new GlPeriodWindow(0, companyId, startDate, endDate, isOpen);
        if (!GlPeriodDateRules.IsValid(startDate, endDate))
            throw new GlPeriodWriteException(GlPeriodWriteStatus.InvalidRange);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockCompanyAsync(companyId, cancellationToken);

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

    public async Task<GlPeriod> UpdateAsync(long companyId, long periodId, int expectedVersion,
        DateOnly startDate, DateOnly endDate, bool isOpen, CancellationToken cancellationToken = default)
        => await UpdateAsync(companyId, periodId, expectedVersion, (DateOnly?)startDate,
            (DateOnly?)endDate, (bool?)isOpen, cancellationToken);

    /// <summary>Null values preserve dates/state read under the company lock.</summary>
    public async Task<GlPeriod> UpdateAsync(long companyId, long periodId, int expectedVersion,
        DateOnly? startDate, DateOnly? endDate, bool? isOpen, CancellationToken cancellationToken = default)
    {
        RequireCompany(companyId);
        if (periodId <= 0) throw new GlPeriodWriteException(GlPeriodWriteStatus.IdentityMismatch);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockCompanyAsync(companyId, cancellationToken);
        var period = await db.Set<GlPeriod>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == periodId, cancellationToken)
            ?? throw new GlPeriodWriteException(GlPeriodWriteStatus.IdentityMismatch);
        var saved = await ReadWindowsAsync(companyId, cancellationToken);
        var postedDates = await ReadPostedDatesAsync(companyId, periodId, cancellationToken);
        var savedWindow = new GlPeriodWindow(period.Id, companyId, period.StartDate, period.EndDate,
            period.Status == GlPeriodStatus.Open);
        var candidate = new GlPeriodWindow(period.Id, companyId, startDate ?? period.StartDate,
            endDate ?? period.EndDate, isOpen ?? (period.Status == GlPeriodStatus.Open));
        var decision = GlPeriodWriteRules.Update(savedWindow, candidate, expectedVersion, period.Version,
            saved, postedDates);
        if (decision is not (GlPeriodWriteStatus.Allowed or GlPeriodWriteStatus.Unchanged))
            throw new GlPeriodWriteException(decision);
        if (decision == GlPeriodWriteStatus.Allowed)
        {
            DetachTrackedPeriod(periodId);
            db.Set<GlPeriod>().Attach(period);
            period.StartDate = candidate.StartDate;
            period.EndDate = candidate.EndDate;
            period.Status = candidate.IsOpen ? GlPeriodStatus.Open : GlPeriodStatus.Closed;
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return period;
    }

    public async Task DeleteAsync(long companyId, long periodId, int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        RequireCompany(companyId);
        if (periodId <= 0) throw new GlPeriodWriteException(GlPeriodWriteStatus.IdentityMismatch);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockCompanyAsync(companyId, cancellationToken);
        var period = await db.Set<GlPeriod>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == periodId, cancellationToken)
            ?? throw new GlPeriodWriteException(GlPeriodWriteStatus.IdentityMismatch);
        var postedDates = await ReadPostedDatesAsync(companyId, periodId, cancellationToken);
        var decision = GlPeriodWriteRules.Delete(expectedVersion, period.Version, postedDates);
        if (decision != GlPeriodWriteStatus.Allowed) throw new GlPeriodWriteException(decision);
        DetachTrackedPeriod(periodId);
        db.Set<GlPeriod>().Attach(period);
        db.Set<GlPeriod>().Remove(period);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void RequireCompany(long companyId)
    {
        if (companyId <= 0 || db.CurrentCompanyId != companyId)
            throw new GlPeriodWriteException(GlPeriodWriteStatus.IdentityMismatch);
    }

    private void DetachTrackedPeriod(long periodId)
    {
        foreach (var entry in db.ChangeTracker.Entries<GlPeriod>().Where(x => x.Entity.Id == periodId))
        {
            if (entry.State is not EntityState.Unchanged)
                throw new InvalidOperationException("A pending period edit must be saved before another period write.");
            entry.State = EntityState.Detached;
        }
    }

    private async Task LockCompanyAsync(long companyId, CancellationToken token)
    {
        // Shared with GlPostingWriter (Shared mode) so a period edit waits for in-flight postings and vice versa.
        var resource = GlPostingWriter.PeriodLockResource(companyId);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
            IF @result < 0 THROW 51020, 'GL period lock was not granted.', 1;
            """, token);
    }

    private Task<List<GlPeriodWindow>> ReadWindowsAsync(long companyId, CancellationToken token) =>
        db.Set<GlPeriod>().AsNoTracking().Where(x => x.CompanyId == companyId)
            .Select(x => new GlPeriodWindow(x.Id, x.CompanyId, x.StartDate, x.EndDate,
                x.Status == GlPeriodStatus.Open)).ToListAsync(token);

    private Task<List<DateOnly>> ReadPostedDatesAsync(long companyId, long periodId, CancellationToken token) =>
        db.Set<GlJournal>().AsNoTracking().Where(x => x.CompanyId == companyId && x.PeriodId == periodId
            && x.Status == GlJournalStatus.Posted).Select(x => x.BusinessDate).ToListAsync(token);
}
