using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>POST result: the saved Posted invoice and the FI references written in the same transaction.</summary>
public sealed record PurchaseInvoicePostSaved(long Id, int Version, string Status, bool Replayed,
    long FiPurchaseSourceId, long FiPayableId, long FiJournalId, DateTime PostedAt);

/// <summary>
/// The FI posting contract failed in a way the user cannot fix (FI fault, unexpected FI result or a receipt that does
/// not match the saved invoice). The whole POST transaction has been rolled back; HTTP 500 keeps the error code.
/// </summary>
public sealed class PurchaseInvoicePostingException(string errorCode, string message)
    : InternalErrorException(errorCode, message);

/// <summary>
/// WU-PUR-008b4 (REQ_F4114.8.2～5, BR-PUR-037/040/043, ADR-208): posts one Matched invoice to FI AP/GL. In one
/// Serializable transaction: supplier invoice lock (shared with Draft/MATCH/RESOLVE) → saved invoice → order header →
/// receipt lines by ID → PUR preflight and 008a10 fingerprint → order rate snapshot (PUR 422 before FI) → FI
/// <see cref="IPurchaseInvoicePostingService"/> (FI source key lock) → receipt comparison → reservation moved to
/// invoiced quantity, invoice Posted with FI IDs → commit. Nothing is modified before FI returns Posted, so FI's
/// SaveChanges never flushes half a PUR change. A Posted invoice is a replay: the saved FI IDs are returned without
/// calling FI (Posted is immutable).
/// </summary>
public sealed class PurchaseInvoicePostService(RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, MyErpDbContext db, IPurchaseInvoiceDraftSourceReader sources,
    IPurchaseInvoiceExchangeSource exchanges, ICurrencyLookup currencies,
    IPurchaseInvoicePostingService posting, IClock clock)
{
    public const string InvalidPostStatus = "PUR.INVOICE.INVALID_POST_STATUS";
    public const string ReceiptMismatch = "PUR.INVOICE.FI_RECEIPT_MISMATCH";
    public const string PostingFailed = "PUR.INVOICE.FI_POSTING_FAILED";

    public async Task<PurchaseInvoicePostSaved> PostAsync(long id, int version, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        var grant = await authorization.GetAsync(token);
        if (grant is null || grant.CompanyId != companyId ||
            grant.FilterFor(PurchasingPermissions.InvoicePost)?.EffectiveScope != DataScope.Company)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Company-wide invoice permission is required.");
        if (id <= 0 || version <= 0)
            throw new RequestValidationException("version", "Invoice ID and version must be positive.");
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Invoice POST owns its transaction; do not call it inside another.");

        // Supplier first, outside the transaction, like MATCH/RESOLVE (no shared row lock while waiting).
        var supplierId = await db.Set<PurchaseInvoice>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == id)
            .Select(x => (long?)x.SupplierId).SingleOrDefaultAsync(token)
            ?? throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var committed = false;
        try
        {
            var resource = PurchaseInvoiceDraftSqlSession.LockResource(companyId, supplierId);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource},
                    @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000;
                IF @result < 0 THROW 51001, 'Purchase invoice post lock unavailable.', 1;
                """, token);
            var invoice = await db.Set<PurchaseInvoice>()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, token);
            if (invoice is null || invoice.IsDeleted)
                throw new NotFoundException("PUR.INVOICE.NOT_FOUND", "Purchase invoice", id);
            if (invoice.SupplierId != supplierId)
                throw new ConflictException();

            if (invoice.Status == PurchaseInvoiceStatus.Posted)
            {
                // Replay of the posting request (version FI posted) or of the Posted result (current version).
                if (version != invoice.Version && version != invoice.PostedFromVersion)
                    throw new ConflictException();
                return Saved(invoice, replayed: true);
            }
            if (invoice.Version != version)
                throw new ConflictException();
            if (invoice.Status != PurchaseInvoiceStatus.Matched)
                throw new BusinessRuleException(InvalidPostStatus, "Only matched invoices can be posted.");

            var lines = await db.Set<PurchaseInvoiceLine>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.InvoiceId == id)
                .OrderBy(x => x.LineNo).ToArrayAsync(token);
            if (lines.Length == 0)
                throw new BusinessRuleException("PUR.INVOICE.INVALID_REQUEST_QUANTITY", "Invoice has no lines.");
            var ids = lines.Select(x => x.ReceiptLineId).Distinct().Order().ToArray();

            // BR-PUR-043 order inside the supplier lock: order header, receipt lines by ID, then FI's source key.
            var order = await sources.ReadOrderAsync(companyId, supplierId, invoice.PurchaseOrderId, token);
            var receipts = await sources.ReadReceiptsAsync(companyId, ids, token);
            var quantityRows = await db.Set<PurchaseReceiptSourceLine>()
                .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
                .OrderBy(x => x.Id).ToArrayAsync(token);
            var prices = PurchaseInvoicePriceBasis.Require(ids,
                await sources.ReadPricesAsync(companyId, ids, token));
            var matchLines = PurchaseInvoicePriceBasis.MatchLines(lines, prices);

            // A Matched invoice's own reservation equals its aggregated saved quantity per receipt line (BR-PUR-037).
            var ownReserved = PurchaseInvoiceAvailability.AggregateRequested(matchLines
                    .Select(x => new PurchaseInvoiceReceiptRequest(x.ReceiptLineId, x.Amount.Quantity)).ToArray())
                .ToDictionary(x => x.ReceiptLineId, x => x.Quantity);
            var postingSources = quantityRows.Select(row => new PurchaseInvoicePostingSource(row.Id,
                new PurchaseInvoiceSourceQuantities(row.PostedReceived, row.PostedReturned,
                    row.PendingReturnReserved, row.PostedInvoiced, row.MatchedReserved,
                    ownReserved.GetValueOrDefault(row.Id)), 0m)).ToArray();

            var resolutions = await db.Set<PurchaseInvoiceResolutionRecord>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.InvoiceId == id)
                .Select(x => new PurchaseInvoiceSavedResolution(x.InvoiceVersion, x.LineNo, x.Action, x.Reason,
                    x.NetDifference, x.TaxDifference, x.GrossDifference))
                .ToArrayAsync(token);
            var accepted = PurchaseInvoiceFinanceEvidence.Accepted(
                PurchaseInvoiceAmountMatch.Check(matchLines.Select(x => x.Amount).ToArray()), resolutions);
            var identity = new PurchaseInvoiceIdentity(companyId, supplierId, invoice.PurchaseOrderId,
                invoice.Currency);
            var assembly = PurchaseInvoicePostAssembly.Build(companyId, id,
                new PurchaseInvoicePostSnapshot(id, invoice.Status.ToString(), invoice.BusinessDate, identity,
                    matchLines, accepted),
                order, receipts, postingSources);

            // Rate problems are PUR's own 422 before FI takes its lock (BR-PUR-043b).
            var exchange = await exchanges.ReadAsync(companyId, invoice.PurchaseOrderId, invoice.Currency, token)
                ?? throw new BusinessRuleException("PUR.INVOICE.INVALID_POSTING_SNAPSHOT",
                    "Saved invoice purchase order is missing.");
            var expected = PurchaseInvoiceFinanceSnapshot.Build(new PurchaseInvoiceFinanceSaved(companyId, id,
                    invoice.Version, supplierId, invoice.Status, invoice.IsDeleted, invoice.BusinessDate,
                    invoice.Currency, invoice.TotalNet, invoice.TotalTax, invoice.TotalGross,
                    lines.Select(x => new PurchaseInvoiceFinanceSavedLine(x.LineNo, x.Net, x.Tax, x.Gross))
                        .ToArray()),
                assembly.Payable.SnapshotHash, exchange, await currencies.GetPrecisionAsync(token));

            var command = new PurchaseInvoicePostingCommand(companyId, id, supplierId, invoice.Version,
                assembly.Payable.SnapshotHash);
            var result = await posting.PostInCurrentTransactionAsync(command, token);
            if (result.Kind != PurchasePostingOutcomeKind.Posted)
                throw Failure(result);
            if (result.Receipt is not { } receipt || !Matches(receipt, command, expected))
                throw new PurchaseInvoicePostingException(ReceiptMismatch,
                    "The FI posting receipt does not match the saved invoice.");

            var rows = quantityRows.ToDictionary(x => x.Id);
            foreach (var change in assembly.Posting.Changes)
            {
                var row = rows[change.ReceiptLineId];
                row.MatchedReserved = change.TargetReserved;
                row.PostedInvoiced = change.TargetInvoiced;
            }
            invoice.Status = PurchaseInvoiceStatus.Posted;
            invoice.PostedAt = clock.LocalNow;
            invoice.PostedFromVersion = invoice.Version;
            invoice.FiPurchaseSourceId = receipt.PurchaseSourceId;
            invoice.FiPayableId = receipt.PayableId;
            invoice.FiJournalId = receipt.JournalId;
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            committed = true;
            return Saved(invoice, replayed: false);
        }
        catch (SqlException error) when (PurchaseInvoiceDraftSqlSession.IsDraftLockUnavailable(error.Number))
        {
            throw new ConflictException("PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE",
                "Purchase invoice lock is unavailable; retry the posting.");
        }
        finally
        {
            if (!committed)
            {
                // A replay only read; an FI Fault has already rolled the transaction back.
                if (db.Database.CurrentTransaction is not null)
                    await tx.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
            }
        }
    }

    private static PurchaseInvoicePostSaved Saved(PurchaseInvoice invoice, bool replayed) =>
        invoice is { PostedAt: { } at, FiPurchaseSourceId: { } source, FiPayableId: { } payable, FiJournalId: { } journal }
            ? new(invoice.Id, invoice.Version, invoice.Status.ToString(), replayed, source, payable, journal, at)
            : throw new PurchaseInvoicePostingException(PostingFailed, "Posted invoice has no FI references.");

    /// <summary>BR-FI-019: the receipt must prove identity, version, fingerprint and both gross amounts.</summary>
    public static bool Matches(PurchaseInvoicePostingReceipt receipt, PurchaseInvoicePostingCommand command,
        FinancePurchaseInvoiceSnapshot expected) =>
        receipt.CompanyId == command.CompanyId && receipt.InvoiceId == command.InvoiceId
        && receipt.SupplierId == command.SupplierId && receipt.SourceVersion == command.SourceVersion
        && string.Equals(receipt.Fingerprint, command.Fingerprint, StringComparison.Ordinal)
        && string.Equals(receipt.Currency, expected.Currency, StringComparison.Ordinal)
        && receipt.GrossAmount == expected.GrossAmount
        && string.Equals(receipt.BaseCurrency, expected.BaseCurrency, StringComparison.Ordinal)
        && receipt.BaseGrossAmount == expected.BaseGrossAmount
        && receipt is { PurchaseSourceId: > 0, PayableId: > 0, JournalId: > 0 };

    /// <summary>
    /// Rejected 404/409/422 keep FI's code (the user can act on it); a Fault, Unchanged for a Matched invoice or any
    /// other status means PUR built a bad command or FI is inconsistent → 500.
    /// </summary>
    public static Exception Failure(PurchaseInvoicePostingResult result) => result switch
    {
        { Kind: PurchasePostingOutcomeKind.Rejected, HttpStatus: 404, ErrorCode: { } code } =>
            new NotFoundException(code, "Purchase invoice FI source", "invoice"),
        { Kind: PurchasePostingOutcomeKind.Rejected, HttpStatus: 409, ErrorCode: { } code } =>
            new ConflictException(code, "FI refused the invoice posting because of a conflict."),
        { Kind: PurchasePostingOutcomeKind.Rejected, HttpStatus: 422, ErrorCode: { } code } =>
            new BusinessRuleException(code, "FI refused the invoice posting.")
            {
                Extensions = result.InvalidLine is { } line
                    ? new Dictionary<string, object?> { ["journalLineNo"] = line }
                    : result.InvalidSourceLine is { } sourceLine
                        ? new Dictionary<string, object?> { ["lineNo"] = sourceLine }
                        : null
            },
        _ => new PurchaseInvoicePostingException(result.ErrorCode ?? PostingFailed,
            $"FI invoice posting failed ({result.Kind}, {result.HttpStatus}).")
    };
}
