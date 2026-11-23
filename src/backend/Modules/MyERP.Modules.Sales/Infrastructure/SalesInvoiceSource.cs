using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>
/// WU-SD-007a1 (ADR-176, BR-SD-040): SD's implementation of the FI-owned <see cref="IFinanceSalesInvoiceSource"/>.
/// FI calls it inside the SD ISSUE transaction on the shared scoped DbContext; SD reads only its own
/// <c>sal_invoice</c> row and reports the saved totals, never recalculating them. A missing or foreign-company invoice
/// is null. SYS action scope is enforced by the SD ISSUE command before FI is called, so the port does not repeat it.
/// </summary>
internal sealed class SalesInvoiceSource(MyErpDbContext db) : IFinanceSalesInvoiceSource
{
    public async Task<FinanceSalesInvoiceSnapshot?> ReadInvoiceAsync(long companyId, long invoiceId,
        DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!ReferenceEquals(db.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw new InvalidOperationException(
                "The SD invoice source must be read inside the caller's transaction on the shared DbContext.");
        if (companyId <= 0 || invoiceId <= 0 || db.CurrentCompanyId != companyId)
            return null;

        var row = await db.Set<SalesInvoice>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == invoiceId)
            .Select(x => new
            {
                x.RequestFingerprint, x.CustomerId, x.Status, x.BusinessDate, x.CurrencyCode, x.BaseCurrencyCode,
                x.ExchangeRate, x.OriginalNet, x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var state = row.Status == SalesInvoiceStatus.Posted
            ? FinanceSalesSourceState.Posted
            : FinanceSalesSourceState.Issuing;
        return new FinanceSalesInvoiceSnapshot(companyId, invoiceId, SalesInvoice.SourceRevision,
            row.RequestFingerprint, row.CustomerId, state, row.BusinessDate, row.CurrencyCode, row.BaseCurrencyCode,
            row.ExchangeRate, row.OriginalNet, row.OriginalTax, row.OriginalGross,
            row.BaseNet, row.BaseTax, row.BaseGross);
    }
}
