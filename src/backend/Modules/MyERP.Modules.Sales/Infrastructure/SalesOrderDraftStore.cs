using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>
/// Persists prepared manual order snapshots inside the caller's transaction. The caller
/// resolves MD references, allocates the SYS number and checks action scope first.
/// </summary>
public sealed class SalesOrderDraftStore(MyErpDbContext db, IClock clock)
{
    public async Task CreateAsync(SalesOrder draft, CancellationToken token = default)
    {
        RequireTransaction();
        EnsureManual(draft);
        EnsureCompany(draft);
        if (draft.Status != SalesOrderStatus.Draft || draft.IsDeleted)
            throw new BusinessRuleException("SAL.ORDER.INVALID_STATUS", "Only a manual Draft can be created.");
        foreach (var line in draft.Lines) line.CompanyId = draft.CompanyId;
        db.Add(draft);
        await db.SaveChangesAsync(token);
    }

    public async Task ReplaceAsync(long id, int version, SalesOrder replacement,
        CancellationToken token = default)
    {
        RequireTransaction();
        EnsureManual(replacement);
        EnsureCompany(replacement);
        var saved = await LoadAsync(id, version, token);
        if (saved.SourceQuotationId is not null)
            throw new BusinessRuleException("SAL.ORDER.SOURCE_IMMUTABLE", "A converted order cannot be repriced.");
        saved.Status = SalesOrderLifecycle.Next(saved.Status, SalesOrderAction.Edit, false);
        db.RemoveRange(saved.Lines);
        saved.Lines.Clear();
        foreach (var line in replacement.Lines)
        {
            line.CompanyId = saved.CompanyId;
            saved.Lines.Add(line);
        }
        saved.CustomerId = replacement.CustomerId;
        saved.CustomerCode = replacement.CustomerCode;
        saved.CustomerName = replacement.CustomerName;
        saved.BusinessDate = replacement.BusinessDate;
        saved.CurrencyCode = replacement.CurrencyCode;
        saved.BaseCurrencyCode = replacement.BaseCurrencyCode;
        saved.ExchangeRate = replacement.ExchangeRate;
        saved.OriginalNet = replacement.OriginalNet;
        saved.OriginalTax = replacement.OriginalTax;
        saved.OriginalGross = replacement.OriginalGross;
        saved.BaseNet = replacement.BaseNet;
        saved.BaseTax = replacement.BaseTax;
        saved.BaseGross = replacement.BaseGross;
        saved.Remark = replacement.Remark;
        // A lines-only replacement also advances the header's optimistic version.
        db.Entry(saved).Property(x => x.Remark).IsModified = true;
        await db.SaveChangesAsync(token);
    }

    public async Task SoftDeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireTransaction();
        var saved = await LoadAsync(id, version, token);
        saved.Status = SalesOrderLifecycle.Next(saved.Status, SalesOrderAction.Delete, false);
        saved.IsDeleted = true;
        saved.DeletedAt = clock.LocalNow;
        await db.SaveChangesAsync(token);
    }

    private async Task<SalesOrder> LoadAsync(long id, int version, CancellationToken token)
    {
        // Serialize mutations of the same header before loading the version and lines.
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_order WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, db.CurrentCompanyId ?? 0L).ToListAsync(token);
        var saved = await db.Set<SalesOrder>().Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", id);
        if (version < 1 || saved.Version != version) throw new ConflictException();
        return saved;
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned order transaction is required.");
    }

    private static void EnsureManual(SalesOrder draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.SourceQuotationId is not null || draft.Lines.Count is < 1 or > 1000 ||
            draft.Lines.Any(x => x.SourceQuotationLineId is not null))
            throw new BusinessRuleException("SAL.ORDER.INVALID_SOURCE", "A manual order needs 1 to 1000 manual lines.");
    }

    private void EnsureCompany(SalesOrder draft)
    {
        if (db.CurrentCompanyId is not long companyId || companyId <= 0 ||
            draft.CompanyId != companyId)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current order company is required.");
    }
}
