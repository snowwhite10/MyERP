using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

/// <summary>Saved Draft return plus whether it was an identical replay of an earlier request (no new change).</summary>
public sealed record SalesReturnDraftOutcome(SalesReturn Return, bool Replayed);

/// <summary>
/// WU-SD-008a2a (REQ_F4112.7.1, BR-SD-043/044/046～049, ADR-185): creates one Draft sales return from Posted delivery
/// lines of one visible Approved order. In its own Read Committed transaction it replays an identical request key,
/// locks the order row, the delivery lines and the invoice lines in ascending ID order, checks the returnable, unbilled
/// and billed balances against committed Posted returns, snapshots the amounts (billed portions from the invoice line,
/// unbilled portions from the order line, remaining-proportional with exact final remainder), validates every
/// destination through MD under its shared lock and saves the Draft with a permanent number. The source checks, locks
/// and snapshot live in <see cref="SalesReturnDraftBuilder"/>, shared with Draft edit (ADR-186). A Draft reserves
/// nothing and never calls INV or FI; submission (WU-SD-008a5) rechecks every balance under the same locks.
/// </summary>
public sealed class SalesReturnCreateService(MyErpDbContext db, SalesActionAccess access, SalesCompanyAccess company,
    INumberGenerator numbers, SalesReturnDraftBuilder builder)
{
    public const string DocumentType = "SAL.RETURN";
    public const string CreatePermission = "SAL.RETURN.CREATE";
    public const string SourceNotFound = "SAL.RETURN.SOURCE_NOT_FOUND";
    public const string SourceMixed = "SAL.RETURN.SOURCE_MIXED";
    public const string OrderNotApproved = "SAL.RETURN.ORDER_NOT_APPROVED";
    public const string DeliveryNotPosted = "SAL.RETURN.DELIVERY_NOT_POSTED";
    public const string InvoiceLineInvalid = "SAL.RETURN.INVOICE_LINE_INVALID";
    public const string DateBeforeDelivery = "SAL.RETURN.DATE_BEFORE_DELIVERY";
    public const string DateBeforeInvoice = "SAL.RETURN.DATE_BEFORE_INVOICE";
    public const string TrackingUnsupported = "SAL.RETURN.TRACKING_UNSUPPORTED";
    public const string ReturnExceeded = "SAL.RETURN.RETURN_EXCEEDED";
    public const string UnbilledExceeded = "SAL.RETURN.UNBILLED_EXCEEDED";
    public const string BilledExceeded = "SAL.RETURN.BILLED_EXCEEDED";
    public const string RequestConflict = "SAL.RETURN.REQUEST_CONFLICT";

    public async Task<SalesReturnDraftOutcome> CreateAsync(SalesReturnDraftInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var scope = await access.RequireScopeAsync(CreatePermission, token);
        var companyId = company.RequireCompanyId();
        var fingerprint = input.Fingerprint();
        var lines = input.Lines!.OrderBy(x => x.LineNo).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var outcome = await ReplayAsync(input.RequestKey!, fingerprint, scope, token)
                ?? await SaveAsync(companyId, input, fingerprint, lines, scope, token);
            await transaction.CommitAsync(token);
            return outcome;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<SalesReturnDraftOutcome> SaveAsync(long companyId, SalesReturnDraftInput input,
        string fingerprint, SalesReturnLineInput[] lines, DataScopeFilter scope, CancellationToken token)
    {
        // Unlocked resolution: hidden or mixed sources are named before any lock is taken.
        var orderId = await builder.ResolveOrderAsync(companyId, lines, null, token);
        if (!await scope.Apply(db.Set<SalesOrder>()).AnyAsync(x => x.Id == orderId, token))
            throw new NotFoundException(SourceNotFound, "Sales return source", lines[0].DeliveryLineId);

        await builder.LockOrderAsync(companyId, orderId, token);
        await builder.LockLinesAsync(companyId, lines, token);

        // A concurrent identical request may have committed while this one waited for the locks.
        if (await ReplayAsync(input.RequestKey!, fingerprint, scope, token) is { } late)
            return late;

        var order = await scope.Apply(db.Set<SalesOrder>()).AsNoTracking()
            .Where(x => x.Id == orderId).Include(x => x.Lines).AsSplitQuery().SingleOrDefaultAsync(token)
            ?? throw new NotFoundException(SourceNotFound, "Sales return source", lines[0].DeliveryLineId);
        var saved = new SalesReturn
        {
            CompanyId = companyId,
            DocumentNo = string.Empty,
            RequestKey = input.RequestKey!,
            RequestFingerprint = fingerprint,
            CustomerId = order.CustomerId,
            OrderId = order.Id,
            BusinessDate = input.BusinessDate,
            CurrencyCode = order.CurrencyCode,
            BaseCurrencyCode = order.BaseCurrencyCode,
            ExchangeRate = order.ExchangeRate,
            Remark = input.NormalizedRemark
        };
        saved.Lines.AddRange(await builder.BuildLinesAsync(companyId, order, input.BusinessDate, lines, token));
        SalesReturnDraftBuilder.ApplyTotals(saved);

        await builder.GuardDestinationsAsync(saved.Lines, async () =>
        {
            saved.DocumentNo = await numbers.NextAsync(DocumentType, input.BusinessDate, token);
            db.Add(saved);
            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException error) when (IsRequestKeyDuplicate(error))
            {
                // Same key raced in from another order's sources: the stored payload necessarily differs.
                throw new ConflictException(RequestConflict, "The request key was already used with different content.");
            }
        }, token);
        return new SalesReturnDraftOutcome(saved, false);
    }

    /// <summary>
    /// BR-SD-048: the key covers soft-deleted history. An identical payload on a live, visible return replays it; a
    /// different payload, a deleted Draft or a return whose order this user cannot see is 409 and reveals nothing.
    /// </summary>
    private async Task<SalesReturnDraftOutcome?> ReplayAsync(string requestKey, string fingerprint,
        DataScopeFilter scope, CancellationToken token)
    {
        var existing = await db.Set<SalesReturn>().IgnoreQueryFilters([ErpQueryFilters.SoftDelete]).AsNoTracking()
            .Include(x => x.Lines).ThenInclude(x => x.InvoiceAllocations)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.RequestKey == requestKey, token);
        if (existing is null)
            return null;
        var visible = await scope.Apply(db.Set<SalesOrder>()).AnyAsync(x => x.Id == existing.OrderId, token);
        if (existing.IsDeleted || !visible ||
            !string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new ConflictException(RequestConflict, "The request key was already used with different content.");
        SortForOutput(existing);
        return new SalesReturnDraftOutcome(existing, true);
    }

    /// <summary>Lines by line number and their billed portions by invoice line, as every response shows them.</summary>
    internal static void SortForOutput(SalesReturn saved)
    {
        saved.Lines.Sort((a, b) => a.LineNo.CompareTo(b.LineNo));
        foreach (var line in saved.Lines)
            line.InvoiceAllocations.Sort((a, b) => a.InvoiceLineId.CompareTo(b.InvoiceLineId));
    }

    private static bool IsRequestKeyDuplicate(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 } sql &&
        sql.Message.Contains("request_key", StringComparison.OrdinalIgnoreCase);
}
