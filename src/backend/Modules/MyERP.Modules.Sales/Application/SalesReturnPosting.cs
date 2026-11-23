using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// INV or FI answered the return posting in a way the user cannot fix (FI fault, unexpected FI result, a receipt that
/// does not match the saved return, or an INV result that does not cover every line). The whole transaction — SYS
/// decision, INV inbound, FI credit and SD status — has been rolled back; the global handler returns HTTP 500 keeping the
/// error code (ADR-179).
/// </summary>
public sealed class SalesReturnPostingException(string errorCode, string message)
    : InternalErrorException(errorCode, message);

/// <summary>
/// WU-SD-008a5a (ADR-190, BR-SD-044/045/046/047): the recheck and final posting step shared by SUBMIT (zero-step SYS
/// approval) and the SYS final decision (008a5b). The caller owns the Read Committed transaction and has already taken the
/// FI customer credit lock, the order row lock and the return row lock, in that order.
/// <list type="number">
/// <item><see cref="RecheckAsync"/> locks the delivery and invoice lines, reruns the Draft source and balance checks
/// against committed Posted returns (a Pending return reserves nothing) and rebuilds the amount snapshot; any difference
/// from the saved snapshot is 409 <see cref="SnapshotStale"/> (the saved lines are frozen once Pending, so the user must
/// revise the Draft).</item>
/// <item><see cref="PostAsync"/> posts the saved lines as an INV <c>SALES_RETURN</c> inbound, stamps every line's movement
/// while the header is still Pending, asks FI to credit the billed portions (skipped when wholly unbilled), verifies the
/// receipt and only then marks the return Posted with the FI credit note. The unbilled portions release SD commitment by
/// derivation (<see cref="SalesOrderCreditCheck"/> subtracts Posted unbilled return amounts) in the same commit.</item>
/// </list>
/// Any refusal throws so SYS, SD, INV and FI changes roll back together.
/// </summary>
public sealed class SalesReturnPosting(MyErpDbContext db, SalesReturnDraftBuilder builder,
    IExternalInventoryPostingService inventory, ISalesCreditPostingService credit, IClock clock)
{
    public const string SnapshotStale = "SAL.RETURN.SNAPSHOT_STALE";
    public const string InventoryConflict = "SAL.RETURN.INV_POSTING_CONFLICT";
    public const string ReceiptMismatch = "SAL.RETURN.FI_RECEIPT_MISMATCH";
    public const string PostingFailed = "SAL.RETURN.FI_POSTING_FAILED";

    /// <summary>
    /// Locks the saved source lines and rechecks eligibility, balances and the amount snapshot of the tracked
    /// <paramref name="saved"/> return (lines and allocations loaded). <paramref name="order"/> is the source order with
    /// its lines, read after the order lock.
    /// </summary>
    public async Task RecheckAsync(SalesReturn saved, SalesOrder order, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(order);
        RequireTransaction();
        if (saved.Lines.Count == 0)
            throw new ConflictException(SnapshotStale, "The sales return has no saved lines.");
        var inputs = ToInputs(saved);
        await builder.LockLinesAsync(saved.CompanyId, inputs, token);
        var rebuilt = await builder.BuildLinesAsync(saved.CompanyId, order, saved.BusinessDate, inputs, token);
        if (!SameSnapshot(saved.Lines, rebuilt))
            throw new ConflictException(SnapshotStale,
                "Balances or amounts of the return sources changed after the Draft was saved; edit the Draft again.");
    }

    /// <summary>
    /// Posts the tracked Pending <paramref name="saved"/> return (lines and allocations loaded) through INV and FI and
    /// marks it Posted. The header must already be saved as Pending so FI sees the issuing source and its version.
    /// </summary>
    public async Task PostAsync(SalesReturn saved, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(saved);
        RequireTransaction();
        if (saved.Status != SalesReturnStatus.Pending || saved.Lines.Count == 0)
            throw new InvalidOperationException("Only a saved Pending sales return with lines can be posted.");

        var ordered = saved.Lines.OrderBy(x => x.LineNo).ToArray();
        var inbound = await inventory.PostSalesReturnInboundAsync(saved.Id, saved.BusinessDate,
            ordered.Select(x => new SalesReturnInboundLine(x.LineNo, x.OriginalOutboundMovementId, x.ItemId,
                x.WarehouseId, x.LocationId, x.Quantity)).ToArray(), token);
        // An INV replay means stock already came back for this return while SD never recorded it.
        if (inbound is null || inbound.Replayed || inbound.Movements.Count != ordered.Length ||
            !inbound.Movements.Select(x => (x.LineNo, x.OriginalMovementId)).Order()
                .SequenceEqual(ordered.Select(x => (x.LineNo, x.OriginalOutboundMovementId))) ||
            inbound.Movements.Any(x => x.MovementId <= 0))
            throw new ConflictException(InventoryConflict,
                "Inventory already holds a different posting for this return; it cannot be posted again.");
        var movements = inbound.Movements.ToDictionary(x => x.LineNo, x => x.MovementId);
        foreach (var line in ordered)
            line.InvMovementId = movements[line.LineNo];
        // ADR-184 §3: lines take their movement while the header is Pending, before the header becomes Posted.
        await db.SaveChangesAsync(token);

        var allocations = ordered.SelectMany(x => x.InvoiceAllocations).ToArray();
        if (allocations.Length > 0)
        {
            var command = new SalesCreditPostingCommand(saved.CompanyId, saved.Id, saved.CustomerId, saved.Version,
                saved.RequestFingerprint);
            var result = await credit.PostInCurrentTransactionAsync(command, token);
            if (result.Kind != SalesPostingOutcomeKind.Posted)
                throw Failure(result);
            if (result.Receipt is not { } receipt || !Matches(receipt, command, saved, allocations))
                throw new SalesReturnPostingException(ReceiptMismatch,
                    "The FI credit receipt does not match the saved sales return.");
            saved.FiCreditNoteId = receipt.SalesSourceId;
        }

        saved.Status = SalesReturnStatus.Posted;
        saved.PostedAt = clock.LocalNow;
        await db.SaveChangesAsync(token);
    }

    /// <summary>BR-SD-046 / BR-FI-010: the receipt must prove source identity, currency, totals and every allocation.</summary>
    public static bool Matches(SalesCreditPostingReceipt receipt, SalesCreditPostingCommand command, SalesReturn saved,
        IReadOnlyCollection<SalesReturnInvoiceAllocation> allocations) =>
        receipt.CompanyId == command.CompanyId && receipt.ReturnId == command.ReturnId
        && receipt.CustomerId == command.CustomerId && receipt.SourceVersion == command.SourceVersion
        && string.Equals(receipt.Fingerprint, command.Fingerprint, StringComparison.Ordinal)
        && string.Equals(receipt.Currency, saved.CurrencyCode, StringComparison.Ordinal)
        && receipt.GrossAmount == allocations.Sum(x => x.OriginalGross)
        && string.Equals(receipt.BaseCurrency, saved.BaseCurrencyCode, StringComparison.Ordinal)
        && receipt.BaseGrossAmount == allocations.Sum(x => x.BaseGross)
        && receipt is { SalesSourceId: > 0, JournalId: > 0 }
        && receipt.Allocations.Count == allocations.Count
        && receipt.Allocations.OrderBy(x => x.InvoiceLineId)
            .Select(x => (x.InvoiceId, x.InvoiceLineId, x.Quantity, x.GrossAmount, x.BaseGrossAmount))
            .SequenceEqual(allocations.OrderBy(x => x.InvoiceLineId)
                .Select(x => (x.InvoiceId, x.InvoiceLineId, x.Quantity, x.OriginalGross, x.BaseGross)));

    /// <summary>
    /// Maps a non-Posted FI result. Rejected 404/409/422 keep FI's code; anything else — a Fault, Unchanged for a return
    /// that was Draft a moment ago, or a status that means SD built a bad command — is an internal failure.
    /// </summary>
    public static Exception Failure(SalesCreditPostingResult result) => result switch
    {
        { Kind: SalesPostingOutcomeKind.Rejected, HttpStatus: 404, ErrorCode: { } code } =>
            new NotFoundException(code, "Sales return FI source", "return"),
        { Kind: SalesPostingOutcomeKind.Rejected, HttpStatus: 409, ErrorCode: { } code } =>
            new ConflictException(code, "FI refused the return credit because of a conflict."),
        { Kind: SalesPostingOutcomeKind.Rejected, HttpStatus: 422, ErrorCode: { } code } =>
            new BusinessRuleException(code, "FI refused the return credit.")
            {
                Extensions = result.InvalidLine is { } line
                    ? new Dictionary<string, object?> { ["creditLineNo"] = line }
                    : null
            },
        _ => new SalesReturnPostingException(result.ErrorCode ?? PostingFailed,
            $"FI return credit failed ({result.Kind}, {result.HttpStatus}).")
    };

    /// <summary>The saved lines as the request that built them (destination and billed/unbilled partition).</summary>
    internal static SalesReturnLineInput[] ToInputs(SalesReturn saved) =>
        saved.Lines.OrderBy(x => x.LineNo)
            .Select(x => new SalesReturnLineInput(x.LineNo, x.DeliveryLineId, x.WarehouseId, x.LocationId,
                x.UnbilledQuantity,
                x.InvoiceAllocations.OrderBy(a => a.InvoiceLineId)
                    .Select(a => new SalesReturnBilledInput(a.InvoiceLineId, a.Quantity)).ToArray()))
            .ToArray();

    /// <summary>Every saved source reference, quantity and amount equals the rebuilt snapshot.</summary>
    internal static bool SameSnapshot(IEnumerable<SalesReturnLine> saved, IEnumerable<SalesReturnLine> rebuilt)
    {
        // Decimal tuple equality compares values, so SQL scale (1.500000) equals a computed 1.5.
        static object Line(SalesReturnLine x) => (x.LineNo, x.DeliveryId, x.DeliveryLineId, x.OrderLineId, x.ItemId,
            x.OriginalOutboundMovementId, x.WarehouseId, x.LocationId, x.Quantity, x.UnbilledQuantity,
            (x.OriginalNet, x.OriginalTax, x.OriginalGross, x.BaseNet, x.BaseTax, x.BaseGross));
        static IEnumerable<object> Allocations(IEnumerable<SalesReturnLine> lines) =>
            lines.OrderBy(x => x.LineNo).SelectMany(x => x.InvoiceAllocations.OrderBy(a => a.InvoiceLineId)
                .Select(a => (object)(x.LineNo, a.InvoiceId, a.InvoiceLineId, a.Quantity,
                    (a.OriginalNet, a.OriginalTax, a.OriginalGross, a.BaseNet, a.BaseTax, a.BaseGross))));
        var before = saved.ToArray();
        var after = rebuilt.ToArray();
        return before.OrderBy(x => x.LineNo).Select(Line).SequenceEqual(after.OrderBy(x => x.LineNo).Select(Line))
            && Allocations(before).SequenceEqual(Allocations(after));
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned sales return transaction is required.");
    }
}
