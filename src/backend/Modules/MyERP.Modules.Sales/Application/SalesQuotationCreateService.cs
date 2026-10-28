using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;

namespace MyERP.Modules.Sales.Application;

/// <summary>Creates a quotation Draft with its SYS number and MD snapshot atomically.</summary>
public sealed class SalesQuotationCreateService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, INumberGenerator numbers, SalesQuotationSnapshotBuilder snapshots,
    SalesQuotationDraftStore store)
{
    public const string DocumentType = "SAL.QUOTATION";

    public async Task<SalesQuotation> CreateAsync(SalesQuotationInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.CREATE", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var number = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
            var draft = await snapshots.BuildAsync(companyId, number, input, token);
            draft.BranchId = scope.BranchId;
            draft.DepartmentId = scope.DepartmentId;
            draft.OwnerUserId = scope.UserId;
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
