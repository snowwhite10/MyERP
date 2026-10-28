using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Application;

/// <summary>Mutates quotation drafts within the exact SYS action scope.</summary>
public sealed class SalesQuotationDraftCommandService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, SalesQuotationSnapshotBuilder snapshots, SalesQuotationDraftStore store)
{
    public async Task<SalesQuotation> ReplaceAsync(long id, int version, SalesQuotationInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.EDIT", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await LockHeaderAsync(id, companyId, token);
            var saved = await scope.Apply(db.Set<SalesQuotation>()).AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
            if (version < 1 || saved.Version != version) throw new ConflictException();
            if (saved.Status is not (SalesQuotationStatus.Draft or SalesQuotationStatus.Rejected))
                throw new BusinessRuleException("SAL.QUOTATION.INVALID_STATUS", "Only Draft or Rejected quotations can be edited.");
            var replacement = await snapshots.BuildAsync(companyId, saved.DocumentNo, input, token);
            await store.ReplaceAsync(id, version, replacement, token);
            await transaction.CommitAsync(token);
            return await db.Set<SalesQuotation>().AsNoTracking().Include(x => x.Lines)
                .SingleAsync(x => x.Id == id, token);
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

    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.DELETE", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            // Serialize commands before the scoped read to prevent shared-to-update lock deadlocks.
            await LockHeaderAsync(id, companyId, token);
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

    private async Task LockHeaderAsync(long id, long companyId, CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_quotation WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, companyId).ToListAsync(token);
    }
}
