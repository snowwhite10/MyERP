using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.Modules.Sales.Application;

/// <summary>Creates a manual Draft with its SYS number and MD snapshot in one transaction.</summary>
public sealed class SalesOrderCreateService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, INumberGenerator numbers, SalesOrderSnapshotBuilder snapshots,
    SalesOrderDraftStore store)
{
    public const string DocumentType = "SAL.ORDER";

    public async Task<SalesOrder> CreateAsync(SalesOrderDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        await access.RequireScopeAsync("SAL.ORDER.CREATE", token);
        var companyId = company.RequireCompanyId();
        // MD reference reads and the SYS sequence must remain stable until the SD draft commits.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var number = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
            var draft = await snapshots.BuildManualAsync(companyId, number, input, token);
            await store.CreateAsync(draft, token);
            await transaction.CommitAsync(token);
            return draft;
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
