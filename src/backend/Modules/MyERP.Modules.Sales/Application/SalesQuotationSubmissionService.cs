using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Approvals;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

public sealed record SalesQuotationSubmissionResult(SalesQuotation Quotation, ApprovalState Approval);

/// <summary>Submits one saved quotation and its SYS approval round in one transaction.</summary>
public sealed class SalesQuotationSubmissionService(MyErpDbContext db, SalesActionAccess access,
    SalesCompanyAccess company, ICustomerLookup customers, ISalesItemLookup items,
    ICurrencyLookup currencies, ITaxRateLookup taxes, IApprovalWorkflowService workflow,
    IClock clock)
{
    public async Task<SalesQuotationSubmissionResult> SubmitAsync(long id, int version,
        CancellationToken token = default)
    {
        var scope = await access.RequireScopeAsync("SAL.QUOTATION.SUBMIT", token);
        var companyId = company.RequireCompanyId();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.sal_quotation WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
                id, companyId).ToListAsync(token);
            var quote = await scope.Apply(db.Set<SalesQuotation>())
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
            if (version < 1 || quote.Version != version) throw new ConflictException();
            SalesQuotationLifecycle.Next(quote.Status, SalesQuotationAction.SubmitPending,
                quote.ExpiryDate, false, clock);
            await db.Set<SalesQuotationLine>().FromSqlInterpolated($"""
                SELECT * FROM dbo.sal_quotation_line
                WITH (INDEX(ix_sal_quotation_line_company_id_quotation_id_line_no), FORCESEEK)
                WHERE company_id = {companyId} AND quotation_id = {id}
                """).LoadAsync(token);
            if (quote.Lines.Count is < 1 or > 1000)
                throw new BusinessRuleException("SAL.QUOTATION.EMPTY", "A saved quotation needs 1 to 1000 lines.");

            await customers.RequireApprovedAsync(quote.CustomerId, token);
            var currency = await currencies.FindCurrencyAsync(quote.CurrencyCode, token)
                ?? throw new NotFoundException("MD.CURRENCY.NOT_FOUND", "Currency", quote.CurrencyCode);
            if (!currency.IsActive)
                throw new BusinessRuleException("MD.CURRENCY.INACTIVE", "Currency is inactive.");
            var baseCurrency = await currencies.GetBaseCurrencyAsync(token)
                ?? throw new BusinessRuleException("MD.EXCHANGE_RATE.NO_BASE_CURRENCY",
                    "The company has no base currency.");
            if (baseCurrency.Code != quote.BaseCurrencyCode)
                throw new BusinessRuleException("SAL.QUOTATION.BASE_CURRENCY_CHANGED",
                    "The company base currency changed after the quotation was saved.");
            foreach (var line in quote.Lines.OrderBy(x => x.LineNo))
            {
                await items.RequireActiveAsync(line.ItemId, token);
                await taxes.GetRateSnapshotAsync(line.TaxRateId, TaxDirection.Output,
                    quote.BusinessDate, token);
            }

            quote.Status = SalesQuotationStatus.Pending;
            quote.SubmittedAt = clock.LocalNow;
            var approval = await workflow.SubmitAsync(new ApprovalSubmission(
                "SAL.QUOTATION", quote.Id, quote.DocumentNo, quote.BaseGross,
                quote.Remark is { Length: > 200 } ? quote.Remark[..200] : quote.Remark), token);
            if (approval.DocumentType != "SAL.QUOTATION" || approval.DocumentId != quote.Id ||
                approval.InstanceId <= 0 || approval.Status is not (ApprovalStatus.Pending or ApprovalStatus.Approved))
                throw new ConflictException("SAL.QUOTATION.APPROVAL_RESULT_INVALID",
                    "SYS returned an invalid quotation approval state.");
            quote.ApprovalInstanceId = approval.InstanceId;
            if (approval.Status == ApprovalStatus.Approved)
            {
                quote.Status = SalesQuotationLifecycle.Next(SalesQuotationStatus.Draft,
                    SalesQuotationAction.SubmitApproved, quote.ExpiryDate, false, clock);
                quote.ApprovedAt = clock.LocalNow;
            }
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new SalesQuotationSubmissionResult(quote, approval);
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
