namespace MyERP.Modules.Finance.Domain;

/// <summary>A saved company accounting period. The database transaction enforces interval exclusion.</summary>
public sealed record GlPeriodWindow(long Id, long CompanyId, DateOnly StartDate, DateOnly EndDate, bool IsOpen);

public enum GlPeriodResolutionStatus { Found, Missing, Closed, Ambiguous }

public sealed record GlPeriodResolution(GlPeriodResolutionStatus Status, long? PeriodId);

public enum GlPeriodChangeStatus { Allowed, InvalidRange, IdentityMismatch, ExcludesPostedDate, ContainsPostedJournal }

/// <summary>Pure inclusive-date policy used by the FI period write and GL posting transactions.</summary>
public static class GlPeriodDateRules
{
    public static bool IsValid(DateOnly startDate, DateOnly endDate) => startDate <= endDate;

    public static bool Overlaps(GlPeriodWindow left, GlPeriodWindow right) =>
        left.CompanyId == right.CompanyId && left.StartDate <= right.EndDate && right.StartDate <= left.EndDate;

    /// <summary>Call with the current company rows read inside the period write lock.</summary>
    public static bool HasConflictingPeriod(GlPeriodWindow candidate, IEnumerable<GlPeriodWindow> existingPeriods)
    {
        ArgumentNullException.ThrowIfNull(existingPeriods);
        return existingPeriods.Any(existing =>
            (candidate.Id <= 0 || existing.Id != candidate.Id) && Overlaps(candidate, existing));
    }

    /// <summary>Call under the company lock with dates read from saved Posted journals for this period.</summary>
    public static GlPeriodChangeStatus ValidateDateChange(
        GlPeriodWindow saved, GlPeriodWindow candidate, IEnumerable<DateOnly> postedBusinessDates)
    {
        ArgumentNullException.ThrowIfNull(postedBusinessDates);
        if (saved.Id != candidate.Id || saved.CompanyId != candidate.CompanyId)
            return GlPeriodChangeStatus.IdentityMismatch;
        if (!IsValid(candidate.StartDate, candidate.EndDate))
            return GlPeriodChangeStatus.InvalidRange;
        return postedBusinessDates.Any(date => date < candidate.StartDate || date > candidate.EndDate)
            ? GlPeriodChangeStatus.ExcludesPostedDate : GlPeriodChangeStatus.Allowed;
    }

    public static GlPeriodChangeStatus ValidateDeletion(IEnumerable<DateOnly> postedBusinessDates)
    {
        ArgumentNullException.ThrowIfNull(postedBusinessDates);
        return postedBusinessDates.Any()
            ? GlPeriodChangeStatus.ContainsPostedJournal : GlPeriodChangeStatus.Allowed;
    }

    public static GlPeriodResolution Resolve(long companyId, DateOnly businessDate, IEnumerable<GlPeriodWindow> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var matches = periods.Where(period => period.CompanyId == companyId
            && period.StartDate <= businessDate && businessDate <= period.EndDate).Take(2).ToArray();
        return matches.Length switch
        {
            0 => new(GlPeriodResolutionStatus.Missing, null),
            > 1 => new(GlPeriodResolutionStatus.Ambiguous, null),
            _ when !matches[0].IsOpen => new(GlPeriodResolutionStatus.Closed, null),
            _ => new(GlPeriodResolutionStatus.Found, matches[0].Id)
        };
    }
}
