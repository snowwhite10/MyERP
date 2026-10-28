using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>Mutates quotation drafts within the exact SYS action scope.</summary>
public sealed class SalesQuotationDraftCommandService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesQuotationDraftStore store)
{
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.DELETE", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            // Serialize commands before the scoped read to prevent shared-to-update lock deadlocks.
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_quotation WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token);
            if (!await scope.Apply(db.Set<SalesQuotation>()).AsNoTracking()
                    .AnyAsync(x => x.Id == id, token))
                throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
            await store.SoftDeleteAsync(id, version, token);
            await transaction.CommitAsync(token);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw new ConflictException();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
