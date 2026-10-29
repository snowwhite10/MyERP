using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>Voids an approved quotation using the distinct VOID action scope.</summary>
public sealed class SalesQuotationVoidService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, IClock clock)
{
    public async Task<SalesQuotation> VoidAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.VOID", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_quotation WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token);
            var quote = await scope.Apply(db.Set<SalesQuotation>()).SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
            if (version < 1 || quote.Version != version) throw new ConflictException();
            var hasOrder = await db.Set<SalesOrder>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.SourceQuotationId == id, token);
            quote.Status = SalesQuotationLifecycle.Next(quote.Status, SalesQuotationAction.Void,
                quote.ExpiryDate, hasOrder, clock);
            quote.VoidedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return quote;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }
}
