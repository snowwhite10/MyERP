using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>
/// WU-SD-008a4 (ADR-189, BR-SD-056): SD's implementation of the FI-owned <see cref="IFinanceSalesReturnSource"/>.
/// FI calls it inside the SD posting transaction (008a5) on the shared scoped DbContext. SD reports only the
/// <b>billed</b> portions of the return: one FI line per saved invoice allocation, in return line number then allocation
/// order, each carrying the SD-saved original invoice line totals as its cumulative cap. The header amounts are the sums
/// of those allocations (the unbilled portion is released by SD without FI). Nothing is recalculated.
/// State: soft-deleted → Deleted; Pending → Issuing (the only state FI may post, because SQL allows Draft → Pending →
/// Posted only); Draft, Rejected and Posted as stored. The source version is the header row version. A missing or
/// foreign-company return is null; SYS action scope is enforced by the SD command before FI is called.
/// </summary>
internal sealed class SalesReturnSource(MyErpDbContext db) : IFinanceSalesReturnSource
{
    public async Task<FinanceSalesReturnSnapshot?> ReadReturnAsync(long companyId, long returnId,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(db.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw new InvalidOperationException(
                "The SD return source must be read inside the caller's transaction on the shared DbContext.");
        if (companyId <= 0 || returnId <= 0 || db.CurrentCompanyId != companyId)
            return null;

        var header = await db.Set<SalesReturn>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == returnId)
            .Select(x => new
            {
                x.Version, x.RequestFingerprint, x.CustomerId, x.Status, x.IsDeleted, x.BusinessDate, x.CurrencyCode,
                x.BaseCurrencyCode, x.ExchangeRate
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (header is null)
            return null;

        var lines = await (
                from line in db.Set<SalesReturnLine>().AsNoTracking()
                join allocation in db.Set<SalesReturnInvoiceAllocation>().AsNoTracking()
                    on new { line.CompanyId, ReturnLineId = line.Id }
                    equals new { allocation.CompanyId, allocation.ReturnLineId }
                join invoiceLine in db.Set<SalesInvoiceLine>().AsNoTracking()
                    on new { allocation.CompanyId, Id = allocation.InvoiceLineId }
                    equals new { invoiceLine.CompanyId, invoiceLine.Id }
                where line.CompanyId == companyId && line.ReturnId == returnId
                orderby line.LineNo, allocation.Id
                select new FinanceSalesReturnLine(allocation.InvoiceId, allocation.InvoiceLineId, allocation.Quantity,
                    allocation.OriginalNet, allocation.OriginalTax, allocation.OriginalGross,
                    allocation.BaseNet, allocation.BaseTax, allocation.BaseGross,
                    new FinanceSalesInvoiceLineCap(invoiceLine.Quantity, invoiceLine.OriginalNet,
                        invoiceLine.OriginalTax, invoiceLine.BaseNet, invoiceLine.BaseTax)))
            .ToListAsync(cancellationToken);

        return new FinanceSalesReturnSnapshot(companyId, returnId, header.Version, header.RequestFingerprint,
            header.CustomerId, State(header.Status, header.IsDeleted), header.BusinessDate, header.CurrencyCode,
            header.BaseCurrencyCode, header.ExchangeRate,
            lines.Sum(x => x.NetAmount), lines.Sum(x => x.TaxAmount), lines.Sum(x => x.GrossAmount),
            lines.Sum(x => x.BaseNetAmount), lines.Sum(x => x.BaseTaxAmount), lines.Sum(x => x.BaseGrossAmount),
            lines);
    }

    internal static FinanceSalesSourceState State(SalesReturnStatus status, bool isDeleted) =>
        isDeleted
            ? FinanceSalesSourceState.Deleted
            : status switch
            {
                SalesReturnStatus.Draft => FinanceSalesSourceState.Draft,
                SalesReturnStatus.Pending => FinanceSalesSourceState.Issuing,
                SalesReturnStatus.Rejected => FinanceSalesSourceState.Rejected,
                SalesReturnStatus.Posted => FinanceSalesSourceState.Posted,
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
            };
}
