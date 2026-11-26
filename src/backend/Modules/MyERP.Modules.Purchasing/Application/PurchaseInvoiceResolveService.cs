using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>RESOLVE body: one decision per disputed invoice line.</summary>
public sealed record PurchaseInvoiceResolveInput(IReadOnlyList<PurchaseInvoiceLineResolution>? Lines);

public sealed record PurchaseInvoiceResolveSaved(long Id, int Version, string Status,
    IReadOnlyList<PurchaseInvoiceResolvedLine> Decisions);

/// <summary>
/// Resolves a Disputed invoice line by line (BR-PUR-039a). Accept-all keeps the reservation after
/// rechecking availability; any rejection returns the invoice to Draft and releases exactly its own
/// reservation (BR-PUR-037f). Decisions, status and reservation change commit in one transaction.
/// </summary>
public sealed class PurchaseInvoiceResolveService(RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, MyErpDbContext db,
    IPurchaseInvoiceDraftSourceReader sources)
{
    public async Task<PurchaseInvoiceResolveSaved> ResolveAsync(long id, int version,
        PurchaseInvoiceResolveInput? input, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var grant = await authorization.GetAsync(token);
        if (grant is null || grant.CompanyId != companyId ||
            grant.FilterFor(PurchasingPermissions.InvoiceResolve)?.EffectiveScope != DataScope.Company)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Company-wide invoice permission is required.");
        if (id <= 0 || version <= 0)
            throw new RequestValidationException("version", "Invoice ID and version must be positive.");
        if (input?.Lines is not { Count: > 0 } decisions)
            throw new RequestValidationException("lines", "At least one line decision is required.");

        // Same ordering as MATCH: supplier first, then the transaction-owned supplier lock.
        var supplierId = await db.Set<PurchaseInvoice>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == id)
            .Select(x => (long?)x.SupplierId).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var resource = PurchaseInvoiceDraftSqlSession.LockResource(companyId, supplierId);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'Purchase invoice resolve lock unavailable.', 1;
                """, token);
            var invoice = await db.Set<PurchaseInvoice>()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token);
            if (invoice is null || invoice.IsDeleted)
                throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);
            if (invoice.SupplierId != supplierId || invoice.Version != version)
                throw new ConflictException();
            if (invoice.Status != PurchaseInvoiceStatus.Disputed)
                throw new BusinessRuleException("PUR.INVOICE.INVALID_RESOLVE_STATUS",
                    "Only disputed invoices can be resolved.");
            var lines = await db.Set<PurchaseInvoiceLine>()
                .Where(x => x.CompanyId == companyId && x.InvoiceId == id)
                .OrderBy(x => x.LineNo).ToArrayAsync(token);
            if (lines.Length == 0)
                throw new BusinessRuleException("PUR.INVOICE.INVALID_REQUEST_QUANTITY", "Invoice has no lines.");
            var ids = lines.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();
            var requested = lines.Select(x => new PurchaseInvoiceReceiptRequest(x.ReceiptLineId,
                x.Quantity)).ToArray();

            // Recheck the saved sources: a stale identity or price basis must not be resolved.
            var order = await sources.ReadOrderAsync(companyId, invoice.SupplierId,
                invoice.PurchaseOrderId, token);
            var receipts = await sources.ReadReceiptsAsync(companyId, ids, token);
            PurchaseInvoiceSourceIdentity.Check(requested,
                new PurchaseInvoiceIdentity(companyId, invoice.SupplierId, invoice.PurchaseOrderId,
                    invoice.Currency), order, receipts);
            var amount = PurchaseInvoiceAmountMatch.Check(PurchaseInvoicePriceBasis.MatchLines(lines,
                    PurchaseInvoicePriceBasis.Require(ids, await sources.ReadPricesAsync(companyId, ids, token)))
                .Select(x => x.Amount).ToArray());
            var decision = PurchaseInvoiceResolution.Check(invoice.Status.ToString(), amount, decisions);

            // This invoice's own reservation equals its aggregated saved quantity per receipt line.
            var ownReserved = PurchaseInvoiceAvailability.AggregateRequested(requested)
                .ToDictionary(x => x.ReceiptLineId, x => x.Quantity);
            var quantityRows = await db.Set<PurchaseReceiptSourceLine>()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
                .OrderBy(x => x.Id).ToArrayAsync(token);
            var quantities = quantityRows.Select(row => new PurchaseInvoiceReceiptSource(row.Id,
                new PurchaseInvoiceSourceQuantities(row.PostedReceived, row.PostedReturned,
                    row.PendingReturnReserved, row.PostedInvoiced, row.MatchedReserved,
                    ownReserved.GetValueOrDefault(row.Id)))).ToArray();
            if (decision.Status == "Matched" &&
                !PurchaseInvoiceAvailability.CheckRequested(requested, quantities).CanMatch)
                throw new BusinessRuleException("PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY",
                    "One or more receipt lines have insufficient quantity.");
            var changes = PurchaseInvoiceReservationPlan.ForResolve(decision.Status, requested, quantities);
            var rowsById = quantityRows.ToDictionary(x => x.Id);
            foreach (var change in changes)
                rowsById[change.ReceiptLineId].MatchedReserved += change.Change;

            foreach (var line in decision.Decisions)
                db.Add(new PurchaseInvoiceResolutionRecord
                {
                    CompanyId = companyId,
                    InvoiceId = invoice.Id,
                    InvoiceVersion = version,
                    LineNo = line.LineNo,
                    Action = line.Action,
                    Reason = line.Reason,
                    NetDifference = DifferenceOf(line, "NET"),
                    TaxDifference = DifferenceOf(line, "TAX"),
                    GrossDifference = DifferenceOf(line, "GROSS"),
                });
            invoice.Status = decision.Status == "Matched"
                ? PurchaseInvoiceStatus.Matched : PurchaseInvoiceStatus.Draft;
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return new(invoice.Id, invoice.Version, decision.Status, decision.Decisions);
        }
        catch (SqlException error) when (PurchaseInvoiceDraftSqlSession.IsDraftLockUnavailable(error.Number))
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new ConflictException("PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE",
                "Purchase invoice lock is unavailable; retry the resolution.");
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static decimal DifferenceOf(PurchaseInvoiceResolvedLine line, string kind) =>
        line.Differences.Where(x => x.Kind == kind).Sum(x => x.Difference);
}
