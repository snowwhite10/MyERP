using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

public sealed record GlPeriodQuery : PagedRequest
{
    public GlPeriodStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed record GlJournalQuery : PagedRequest
{
    public long? PeriodId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? SourceModule { get; init; }
    public string? SourceType { get; init; }
    public long? SourceId { get; init; }
    public string? AccountCode { get; init; }
}

public sealed record GlPeriodRead(long Id, DateOnly StartDate, DateOnly EndDate, GlPeriodStatus Status,
    int Version, DateTime CreatedAt, long CreatedBy, DateTime? UpdatedAt, long? UpdatedBy);

public sealed record GlJournalRead(long Id, long PeriodId, DateOnly BusinessDate, string BaseCurrency,
    GlJournalStatus Status, decimal TotalDebit, decimal TotalCredit, string SourceModule, string SourceType,
    long SourceId, int SourceVersion, DateTime CreatedAt, long CreatedBy);

public sealed record GlJournalLineRead(int Sequence, long AccountId, string AccountCode, string AccountName,
    int AccountVersion, int MappingVersion, decimal Debit, decimal Credit);

public sealed record GlJournalDetail(GlJournalRead Header, IReadOnlyList<GlJournalLineRead> Lines);

/// <summary>Company-wide GL read model. Periods and journals carry no branch, department or owner dimension.</summary>
public sealed class GlReadService(MyErpDbContext db, ICurrentCompany company, ICurrentAuthorization authorization)
{
    public const string PeriodViewPermission = "GL.PERIOD.VIEW";
    public const string JournalViewPermission = "GL.JOURNAL.VIEW";
    public const string PeriodNotFound = "FI.GL_PERIOD.NOT_FOUND";
    public const string JournalNotFound = "FI.GL_JOURNAL.NOT_FOUND";

    public async Task<PagedResult<GlPeriodRead>> ListPeriodsAsync(GlPeriodQuery request, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, PeriodViewPermission, token);
        request.EnsureValid();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            throw new RequestValidationException("keyword", "Keyword search is not supported for periods.");
        if (request.From > request.To) throw new RequestValidationException("to", "End date precedes start date.");
        if (request.Status is { } status && !Enum.IsDefined(status))
            throw new RequestValidationException("status", "Unknown period status.");
        var sort = SortSpec.Parse(request.Sort, ["startDate", "endDate"], "-startDate");
        if (sort.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = db.Set<GlPeriod>().AsNoTracking();
        if (request.Status is { } state) query = query.Where(x => x.Status == state);
        if (request.From is { } from) query = query.Where(x => x.EndDate >= from);
        if (request.To is { } to) query = query.Where(x => x.StartDate <= to);
        query = (sort[0].Field, sort[0].Descending) switch
        {
            ("endDate", false) => query.OrderBy(x => x.EndDate).ThenBy(x => x.Id),
            ("endDate", true) => query.OrderByDescending(x => x.EndDate).ThenByDescending(x => x.Id),
            (_, false) => query.OrderBy(x => x.StartDate).ThenBy(x => x.Id),
            _ => query.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize).Select(x =>
            new GlPeriodRead(x.Id, x.StartDate, x.EndDate, x.Status, x.Version, x.CreatedAt,
                x.CreatedBy, x.UpdatedAt, x.UpdatedBy)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<GlPeriodRead> GetPeriodAsync(long id, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, PeriodViewPermission, token);
        return await db.Set<GlPeriod>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new GlPeriodRead(x.Id, x.StartDate, x.EndDate, x.Status, x.Version,
                x.CreatedAt, x.CreatedBy, x.UpdatedAt, x.UpdatedBy)).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(PeriodNotFound, "GL period", id);
    }

    public async Task<PagedResult<GlJournalRead>> ListJournalsAsync(GlJournalQuery request, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, JournalViewPermission, token);
        ValidateJournalQuery(request);
        var sort = SortSpec.Parse(request.Sort, ["businessDate", "id"], "-businessDate");
        if (sort.Count != 1) throw new RequestValidationException("sort", "Choose one sort field.");
        var query = (from journal in db.Set<GlJournal>().AsNoTracking()
                     join source in db.Set<GlSourceKey>().AsNoTracking()
                         on new { journal.CompanyId, Id = journal.SourceKeyId }
                         equals new { source.CompanyId, source.Id }
                     select new { Journal = journal, Source = source })
            .Where(x => x.Journal.Status == GlJournalStatus.Posted);
        if (request.PeriodId is { } period) query = query.Where(x => x.Journal.PeriodId == period);
        if (request.From is { } from) query = query.Where(x => x.Journal.BusinessDate >= from);
        if (request.To is { } to) query = query.Where(x => x.Journal.BusinessDate <= to);
        if (request.SourceModule is { } module) query = query.Where(x => x.Source.SourceModule == module);
        if (request.SourceType is { } type) query = query.Where(x => x.Source.SourceType == type);
        if (request.SourceId is { } sourceId) query = query.Where(x => x.Source.SourceId == sourceId);
        if (request.AccountCode is { } code)
            query = query.Where(x => db.Set<GlJournalLine>().Any(line =>
                line.JournalId == x.Journal.Id && line.AccountCode == code));
        query = (sort[0].Field, sort[0].Descending) switch
        {
            ("id", false) => query.OrderBy(x => x.Journal.Id),
            ("id", true) => query.OrderByDescending(x => x.Journal.Id),
            (_, false) => query.OrderBy(x => x.Journal.BusinessDate).ThenBy(x => x.Journal.Id),
            _ => query.OrderByDescending(x => x.Journal.BusinessDate).ThenByDescending(x => x.Journal.Id)
        };
        var count = await query.LongCountAsync(token);
        var items = await query.Skip(request.Skip).Take(request.PageSize).Select(x => new GlJournalRead(
            x.Journal.Id, x.Journal.PeriodId, x.Journal.BusinessDate, x.Journal.BaseCurrency,
            x.Journal.Status, x.Journal.TotalDebit, x.Journal.TotalCredit, x.Source.SourceModule,
            x.Source.SourceType, x.Source.SourceId, x.Source.SourceVersion, x.Journal.CreatedAt,
            x.Journal.CreatedBy)).ToListAsync(token);
        return PagedResult.Create(items, request, count);
    }

    public async Task<GlJournalDetail> GetJournalAsync(long id, CancellationToken token)
    {
        await FinanceAccess.RequireAsync(company, authorization, JournalViewPermission, token);
        var rows = from journal in db.Set<GlJournal>().AsNoTracking()
                   join source in db.Set<GlSourceKey>().AsNoTracking()
                       on new { journal.CompanyId, Id = journal.SourceKeyId }
                       equals new { source.CompanyId, source.Id }
                   select new { Journal = journal, Source = source };
        var header = await rows.Where(x => x.Journal.Id == id && x.Journal.Status == GlJournalStatus.Posted)
            .Select(x => new GlJournalRead(x.Journal.Id, x.Journal.PeriodId, x.Journal.BusinessDate,
                x.Journal.BaseCurrency, x.Journal.Status, x.Journal.TotalDebit, x.Journal.TotalCredit,
                x.Source.SourceModule, x.Source.SourceType, x.Source.SourceId, x.Source.SourceVersion,
                x.Journal.CreatedAt, x.Journal.CreatedBy)).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(JournalNotFound, "GL journal", id);
        var lines = await db.Set<GlJournalLine>().AsNoTracking().Where(x => x.JournalId == id)
            .OrderBy(x => x.Sequence).Select(x => new GlJournalLineRead(x.Sequence, x.AccountId,
                x.AccountCode, x.AccountName, x.AccountVersion, x.MappingVersion, x.Debit, x.Credit))
            .ToListAsync(token);
        return new GlJournalDetail(header, lines);
    }

    private static void ValidateJournalQuery(GlJournalQuery request)
    {
        request.EnsureValid();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
            throw new RequestValidationException("keyword", "Keyword search is not supported for journals.");
        if (request.From > request.To) throw new RequestValidationException("to", "End date precedes start date.");
        if (request.PeriodId is <= 0) throw new RequestValidationException("periodId", "Period must be positive.");
        if (request.SourceId is <= 0) throw new RequestValidationException("sourceId", "Source must be positive.");
        if (request.SourceModule is { Length: > 20 } or "")
            throw new RequestValidationException("sourceModule", "Source module must contain 1 to 20 characters.");
        if (request.SourceType is { Length: > 30 } or "")
            throw new RequestValidationException("sourceType", "Source type must contain 1 to 30 characters.");
        if (request.AccountCode is { Length: > 50 } or "")
            throw new RequestValidationException("accountCode", "Account code must contain 1 to 50 characters.");
    }

}
