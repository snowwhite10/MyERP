using MyERP.Modules.Finance.Domain;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

public sealed record GlPeriodWriteResult(GlPeriodRead Period, bool Changed);

/// <summary>Authorised HTTP commands over the company-locked GL period writer.</summary>
public sealed class GlPeriodCommandService(GlPeriodWriteService writer, ICurrentCompany company,
    ICurrentAuthorization authorization)
{
    public const string CreatePermission = "GL.PERIOD.CREATE";
    public const string EditPermission = "GL.PERIOD.EDIT";
    public const string ClosePermission = "GL.PERIOD.CLOSE";
    public const string ReopenPermission = "GL.PERIOD.REOPEN";
    public const string DeletePermission = "GL.PERIOD.DELETE";

    public async Task<GlPeriodWriteResult> CreateAsync(DateOnly start, DateOnly end, CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, CreatePermission, token);
        ValidateDates(start, end);
        try { return new(ToRead(await writer.CreateAsync(companyId, start, end, true, token)), true); }
        catch (GlPeriodWriteException error) { throw Map(error, 0); }
    }

    public async Task<GlPeriodWriteResult> EditAsync(long id, int version, DateOnly start, DateOnly end,
        CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, EditPermission, token);
        ValidateIdVersion(id, version);
        ValidateDates(start, end);
        // Date edits cannot change state; the writer's current state is read under the lock.
        return await WriteAsync(companyId, id, version, start, end, null, token);
    }

    public async Task<GlPeriodWriteResult> SetOpenAsync(long id, int version, bool open, CancellationToken token)
    {
        var permission = open ? ReopenPermission : ClosePermission;
        var companyId = await FinanceAccess.RequireAsync(company, authorization, permission, token);
        ValidateIdVersion(id, version);
        return await WriteAsync(companyId, id, version, null, null, open, token);
    }

    public async Task DeleteAsync(long id, int version, CancellationToken token)
    {
        var companyId = await FinanceAccess.RequireAsync(company, authorization, DeletePermission, token);
        ValidateIdVersion(id, version);
        try { await writer.DeleteAsync(companyId, id, version, token); }
        catch (GlPeriodWriteException error) { throw Map(error, id); }
    }

    private async Task<GlPeriodWriteResult> WriteAsync(long companyId, long id, int version,
        DateOnly? start, DateOnly? end, bool? open, CancellationToken token)
    {
        try
        {
            var saved = await writer.UpdateAsync(companyId, id, version, start, end, open, token);
            return new(ToRead(saved), saved.Version != version);
        }
        catch (GlPeriodWriteException error) { throw Map(error, id); }
    }

    private static GlPeriodRead ToRead(GlPeriod x) => new(x.Id, x.StartDate, x.EndDate, x.Status,
        x.Version, x.CreatedAt, x.CreatedBy, x.UpdatedAt, x.UpdatedBy);

    private static void ValidateIdVersion(long id, int version)
    {
        if (id <= 0) throw new RequestValidationException("id", "Period id must be positive.");
        if (version <= 0) throw new RequestValidationException("version", "Version must be positive.");
    }

    private static void ValidateDates(DateOnly start, DateOnly end)
    {
        if (start == default) throw new RequestValidationException("startDate", "Start date is required.");
        if (end == default || start > end)
            throw new RequestValidationException("endDate", "End date must not precede start date.");
    }

    private static Exception Map(GlPeriodWriteException error, long id) => error.Status switch
    {
        GlPeriodWriteStatus.IdentityMismatch => new NotFoundException(GlReadService.PeriodNotFound, "GL period", id),
        GlPeriodWriteStatus.InvalidRange => new RequestValidationException("endDate", "Invalid period dates."),
        GlPeriodWriteStatus.StaleVersion => new ConflictException(),
        GlPeriodWriteStatus.Overlap => new ConflictException("FI.GL_PERIOD.OVERLAP", "Accounting periods overlap."),
        GlPeriodWriteStatus.ExcludesPostedDate => new BusinessRuleException("FI.GL_PERIOD.EXCLUDES_POSTED", "Posted journal dates must remain in the period."),
        GlPeriodWriteStatus.ContainsPostedJournal => new BusinessRuleException("FI.GL_PERIOD.CONTAINS_POSTED", "A period with Posted journals cannot be deleted."),
        _ => new InvalidOperationException("Unexpected GL period write result.", error)
    };
}
