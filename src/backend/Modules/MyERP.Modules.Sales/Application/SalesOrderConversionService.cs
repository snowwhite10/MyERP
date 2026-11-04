using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>Converts one complete, saved quotation under a caller-owned SQL transaction.</summary>
public sealed class SalesOrderConversionService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, INumberGenerator numbers, SalesOrderDraftStore store, IClock clock)
{
    public async Task<SalesOrder> CreateAsync(long quotationId, CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.ORDER.CREATE", token);
        var companyId = company.RequireCompanyId();
        if (quotationId <= 0)
            throw new NotFoundException("SAL.DOCUMENT.NOT_FOUND", "Sales quotation", quotationId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            // The update lock serializes both conversion attempts before either reads the
            // source or allocates a SYS number. The unique index is the final guard.
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_quotation WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                quotationId, companyId).ToListAsync(token);
            var quote = await scope.Apply(db.Set<SalesQuotation>()).AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == quotationId, token)
                ?? throw new NotFoundException("SAL.DOCUMENT.NOT_FOUND", "Sales quotation", quotationId);
            var hasSourceOrder = await db.Set<SalesOrder>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.SourceQuotationId == quotationId, token);
            if (hasSourceOrder) throw new ConflictException();
            var savedLineCount = await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM dbo.sal_quotation_line " +
                "WITH (INDEX(ix_sal_quotation_line_company_id_quotation_id_line_no), FORCESEEK, HOLDLOCK) " +
                "WHERE [company_id] = {0} AND [quotation_id] = {1}",
                companyId, quotationId).SingleAsync(token);
            var lines = await db.Set<SalesQuotationLine>().FromSqlInterpolated($"""
                SELECT * FROM dbo.sal_quotation_line
                WITH (INDEX(ix_sal_quotation_line_company_id_quotation_id_line_no), FORCESEEK, HOLDLOCK)
                WHERE company_id = {companyId} AND quotation_id = {quotationId}
                """).AsNoTracking().ToListAsync(token);
            quote.Lines.AddRange(lines);
            // Validate the source before allocating the number; Build validates the
            // complete saved snapshot again with the final number.
            SalesOrderQuotationSnapshot.Build(companyId, "VALIDATION", quote,
                savedLineCount, hasSourceOrder, clock);
            var number = await numbers.NextAsync(SalesOrderCreateService.DocumentType,
                quote.BusinessDate, token);
            var draft = SalesOrderQuotationSnapshot.Build(companyId, number, quote,
                savedLineCount, hasSourceOrder, clock);
            draft.BranchId = scope.BranchId;
            draft.DepartmentId = scope.DepartmentId;
            draft.OwnerUserId = scope.UserId;
            await store.CreateAsync(draft, token);
            await transaction.CommitAsync(token);
            return draft;
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql &&
            sql.Number is 2601 or 2627)
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
