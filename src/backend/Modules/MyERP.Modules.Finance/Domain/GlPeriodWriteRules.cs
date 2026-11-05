namespace MyERP.Modules.Finance.Domain;

public enum GlPeriodWriteStatus
{
    Allowed,
    InvalidRange,
    StaleVersion,
    IdentityMismatch,
    Overlap,
    ExcludesPostedDate,
    ContainsPostedJournal
}

/// <summary>
/// Combines pure period checks. The caller must read all inputs while holding the company SQL lock;
/// this decision does not acquire a lock or persist a period.
/// </summary>
public static class GlPeriodWriteRules
{
    public static GlPeriodWriteStatus Create(GlPeriodWindow candidate, IEnumerable<GlPeriodWindow> savedPeriods)
    {
        if (candidate.Id != 0 || candidate.CompanyId <= 0)
            return GlPeriodWriteStatus.IdentityMismatch;
        if (!GlPeriodDateRules.IsValid(candidate.StartDate, candidate.EndDate))
            return GlPeriodWriteStatus.InvalidRange;
        return GlPeriodDateRules.HasConflictingPeriod(candidate, savedPeriods)
            ? GlPeriodWriteStatus.Overlap : GlPeriodWriteStatus.Allowed;
    }

    public static GlPeriodWriteStatus Update(
        GlPeriodWindow saved,
        GlPeriodWindow candidate,
        long expectedVersion,
        long currentVersion,
        IEnumerable<GlPeriodWindow> savedPeriods,
        IEnumerable<DateOnly> postedBusinessDates)
    {
        if (expectedVersion != currentVersion)
            return GlPeriodWriteStatus.StaleVersion;
        var change = GlPeriodDateRules.ValidateDateChange(saved, candidate, postedBusinessDates);
        if (change != GlPeriodChangeStatus.Allowed)
            return change switch
            {
                GlPeriodChangeStatus.InvalidRange => GlPeriodWriteStatus.InvalidRange,
                GlPeriodChangeStatus.IdentityMismatch => GlPeriodWriteStatus.IdentityMismatch,
                GlPeriodChangeStatus.ExcludesPostedDate => GlPeriodWriteStatus.ExcludesPostedDate,
                _ => throw new InvalidOperationException("Unexpected period date decision.")
            };
        return GlPeriodDateRules.HasConflictingPeriod(candidate, savedPeriods)
            ? GlPeriodWriteStatus.Overlap : GlPeriodWriteStatus.Allowed;
    }

    public static GlPeriodWriteStatus Delete(
        long expectedVersion,
        long currentVersion,
        IEnumerable<DateOnly> postedBusinessDates)
    {
        if (expectedVersion != currentVersion)
            return GlPeriodWriteStatus.StaleVersion;
        return GlPeriodDateRules.ValidateDeletion(postedBusinessDates) == GlPeriodChangeStatus.ContainsPostedJournal
            ? GlPeriodWriteStatus.ContainsPostedJournal : GlPeriodWriteStatus.Allowed;
    }
}
