using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceMatchSaved(long Id, int Version, string Status,
    PurchaseInvoiceAmountDecision Amount);

/// <summary>Matches one saved invoice and reserves its receipt quantities in one PUR transaction.</summary>
public sealed class PurchaseInvoiceMatchService(RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, MyErpDbContext db,
    IPurchaseInvoiceDraftSourceReader sources)
{
    public async Task<PurchaseInvoiceMatchSaved> MatchAsync(long id, int version,
        CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var grant = await authorization.GetAsync(token);
        if (grant is null || grant.CompanyId != companyId ||
            grant.FilterFor(PurchasingPermissions.InvoiceMatch)?.EffectiveScope != DataScope.Company)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Company-wide invoice permission is required.");
        if (id <= 0 || version <= 0)
            throw new RequestValidationException("version", "Invoice ID and version must be positive.");

        // Resolve the supplier before the serializable transaction to avoid holding a shared row
        // lock while another matcher waits for the transaction-owned supplier lock.
        var supplierId = await db.Set<PurchaseInvoice>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == id)
            .Select(x => (long?)x.SupplierId).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            // The draft writer uses this same transaction lock, so source checks and edits cannot race.
            var resource = PurchaseInvoiceDraftSqlSession.LockResource(companyId, supplierId);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'Purchase invoice match lock unavailable.', 1;
                """, token);
            var invoice = await db.Set<PurchaseInvoice>()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token);
            if (invoice is null || invoice.IsDeleted)
                throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);
            if (invoice.SupplierId != supplierId)
                throw new ConflictException();
            if (invoice.Version != version)
                throw new ConflictException();
            var lines = await db.Set<PurchaseInvoiceLine>()
                .Where(x => x.CompanyId == companyId && x.InvoiceId == id)
                .OrderBy(x => x.LineNo).ToArrayAsync(token);
            if (lines.Length == 0)
                throw new BusinessRuleException("PUR.INVOICE.INVALID_REQUEST_QUANTITY", "Invoice has no lines.");
            var ids = lines.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();
            var order = await sources.ReadOrderAsync(companyId, invoice.SupplierId,
                invoice.PurchaseOrderId, token);
            var receipts = await sources.ReadReceiptsAsync(companyId, ids, token);
            var prices = await sources.ReadPricesAsync(companyId, ids, token);
            if (prices.Count != ids.Length || prices.Any(x => x is null || x.ReceiptLineId <= 0) ||
                prices.Select(x => x.ReceiptLineId).Distinct().Count() != prices.Count)
                throw new BusinessRuleException("PUR.INVOICE.INVALID_SOURCE_IDENTITY",
                    "Saved receipt prices are incomplete or duplicated.");
            var priceByReceipt = prices.ToDictionary(x => x.ReceiptLineId);
            var matchLines = lines.Select(line =>
            {
                if (!priceByReceipt.TryGetValue(line.ReceiptLineId, out var price))
                    throw new BusinessRuleException("PUR.INVOICE.INVALID_SOURCE_IDENTITY",
                        "A saved receipt no longer has an order price.");
                return new PurchaseInvoiceMatchLine(line.ReceiptLineId,
                    new PurchaseInvoiceAmountLine(line.LineNo, line.Quantity,
                        price.UnitPrice, price.TaxRatePercent, line.UnitPrice,
                        line.TaxRatePercent, line.Net, line.Tax, line.Gross));
            }).ToArray();
            var quantityRows = await db.Set<PurchaseReceiptSourceLine>()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
                .OrderBy(x => x.Id).ToArrayAsync(token);
            var quantities = quantityRows.Select(row => new PurchaseInvoiceReceiptSource(row.Id,
                new PurchaseInvoiceSourceQuantities(row.PostedReceived, row.PostedReturned,
                    row.PendingReturnReserved, row.PostedInvoiced, row.MatchedReserved, 0))).ToArray();
            var decision = PurchaseInvoiceMatch.Check(invoice.Status.ToString(), matchLines,
                new PurchaseInvoiceIdentity(companyId, invoice.SupplierId, invoice.PurchaseOrderId,
                    invoice.Currency), order, receipts, quantities);
            if (decision.Status == "Shortage")
                throw new BusinessRuleException("PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY",
                    "One or more receipt lines have insufficient quantity.");
            var requested = matchLines.Select(x => new PurchaseInvoiceReceiptRequest(x.ReceiptLineId,
                x.Amount.Quantity)).ToArray();
            var changes = PurchaseInvoiceReservationPlan.ForMatch(decision.Status, requested, quantities);
            var rowsById = quantityRows.ToDictionary(x => x.Id);
            foreach (var change in changes)
                rowsById[change.ReceiptLineId].MatchedReserved += change.Change;
            invoice.Status = decision.Status == "Matched"
                ? PurchaseInvoiceStatus.Matched : PurchaseInvoiceStatus.Disputed;
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return new(invoice.Id, invoice.Version, decision.Status, decision.Amount!);
        }
        catch (SqlException error) when (PurchaseInvoiceDraftSqlSession.IsDraftLockUnavailable(error.Number))
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new ConflictException("PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE",
                "Purchase invoice lock is unavailable; retry the match.");
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
