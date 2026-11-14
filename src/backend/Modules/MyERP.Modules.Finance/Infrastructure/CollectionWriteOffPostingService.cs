using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-005a2c (ADR-122): approved FI collection write-off → GL + FI settlement/allocations inside the caller's
/// transaction on the scoped DbContext. Lock order follows BR-FI-036: FI credit key, FI replay check (safe under the
/// credit key: every settlement of the customer serializes on it), the saved settlement read, then the receipt key
/// (<c>ar_collection:{company}:{receipt document}</c>, shared with receipt posting), each receivable key
/// (<c>ar_receivable:{company}:{receivable}</c>) in ascending ID order, the settlement key
/// (<c>ar_collection_writeoff:{company}:{settlement}</c>) and the GL source applock inside
/// <see cref="IGlJoinedPostingService"/>. Balances are re-read after the locks. Rejected results leave the caller's
/// transaction open and write nothing; every Fault rolls the whole caller transaction back.
/// </summary>
internal sealed class CollectionWriteOffPostingService(
    MyErpDbContext db,
    ICurrentCompany company,
    ICustomerCreditService credit,
    IGlJoinedPostingService joined,
    IChartOfAccounts chart,
    IEnumerable<IFinanceCollectionWriteOffSource> sources,
    IEnumerable<ISalesCollectionAccountMapSource> maps) : ICollectionWriteOffPostingService
{
    /// <summary>Maximum wait for another transaction holding the same receipt, receivable or settlement; tests shorten it.</summary>
    internal int LockTimeoutMilliseconds { get; set; } = 15_000;

    internal static string ReceivableLockResource(long companyId, long receivableId) =>
        string.Create(CultureInfo.InvariantCulture, $"ar_receivable:{companyId}:{receivableId}");

    internal static string SettlementLockResource(long companyId, long settlementId) =>
        string.Create(CultureInfo.InvariantCulture, $"ar_collection_writeoff:{companyId}:{settlementId}");

    public async Task<CollectionWriteOffPostingResult> PostInCurrentTransactionAsync(
        CollectionWriteOffPostingCommand? command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("FI write-off posting requires the caller's open SQL transaction.");
        if (!CollectionWriteOffPostingRules.IsWellFormed(command))
            return Rejected(400, CollectionWriteOffPostingErrors.InvalidInput);
        if (company.CompanyId != command!.CompanyId || db.CurrentCompanyId != command.CompanyId)
            return Rejected(404, CollectionWriteOffPostingErrors.SourceNotFound);
        var source = PostingServiceSupport.SingleOrNull(sources);
        if (source is null)
            return Rejected(500, CollectionWriteOffPostingErrors.SourceContractUnavailable);

        var dbTransaction = transaction.GetDbTransaction();
        try
        {
            await credit.AcquireCreditLockAsync(command.CompanyId, command.CustomerId, dbTransaction, cancellationToken);
        }
        catch (ConflictException ex) when (ex.ErrorCode == FinanceModuleContracts.CreditLockTimeout)
        {
            return Rejected(409, FinanceModuleContracts.CreditLockTimeout);
        }

        var replay = await ReplayAsync(command, cancellationToken);
        if (replay is not null)
            return replay;

        var snapshot = await source.ReadWriteOffAsync(command.CompanyId, command.SettlementId, dbTransaction,
            cancellationToken);
        if (CollectionWriteOffPostingRules.CheckSnapshot(command, snapshot) is { } failure)
            return Rejected(failure.Status, failure.Code);

        var receivableIds = snapshot!.Lines.Select(x => x.ReceivableId).Order().ToArray();
        var keys = new List<string> { CollectionReceiptPostingService.SourceLockResource(command.CompanyId, snapshot.ReceiptId) };
        keys.AddRange(receivableIds.Select(id => ReceivableLockResource(command.CompanyId, id)));
        keys.Add(SettlementLockResource(command.CompanyId, command.SettlementId));
        foreach (var key in keys)
        {
            if (!await TryLockAsync(key, cancellationToken))
                return Rejected(409, CollectionWriteOffPostingErrors.LockTimeout);
        }

        var receipt = await db.Set<CollectionReceipt>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == CollectionReceipt.SalesCollectionType
                        && x.SourceId == snapshot.ReceiptId)
            .SingleOrDefaultAsync(cancellationToken);
        if (CollectionWriteOffPostingRules.CheckReceipt(snapshot, receipt is null
                ? null
                : new(receipt.CustomerId, receipt.BaseCurrency, receipt.BusinessDate, receipt.UnappliedAmount)) is { } bad)
            return Rejected(bad.Status, bad.Code);

        var receivables = await db.Set<PostedReceivable>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && receivableIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var lineErrors = CollectionWriteOffPostingRules.CheckLines(snapshot, receivables.ToDictionary(x => x.Key,
            x => new CollectionWriteOffReceivable(x.Value.Id, x.Value.CustomerId, x.Value.SourceType,
                x.Value.BaseCurrency, x.Value.OpenBaseAmount, x.Value.PostingDate)));
        if (lineErrors.Count > 0)
            return new(CollectionPostingOutcomeKind.Rejected, false, 422, CollectionWriteOffPostingErrors.AllocationInvalid,
                LineErrors: lineErrors);

        var map = PostingServiceSupport.SingleOrNull(maps) is { } mapSource
            ? await mapSource.ReadAsync(command.CompanyId, cancellationToken)
            : null;
        if (map is not { MappingVersion: > 0 })
            return Rejected(422, CollectionWriteOffPostingErrors.MappingMissing);

        // BR-FI-037 base-currency journal: Dr unapplied-cash clearing, Cr AR control, settlement total.
        var (draft, invalidLine) = await PostingJournalDraft.BuildAsync(chart, command.CompanyId, map.MappingVersion,
            [(map.UnappliedCashAccountCode, snapshot.TotalAmount, 0m), (map.ReceivableAccountCode, 0m, snapshot.TotalAmount)],
            cancellationToken);
        if (draft is null)
            return new(CollectionPostingOutcomeKind.Rejected, false, 422, CollectionWriteOffPostingErrors.AccountInvalid,
                InvalidLine: invalidLine);

        var request = new GlPostingRequest(command.CompanyId,
            new GlPostingSource(command.CompanyId, SalesCollectionAccountMapStore.CollectionSourceModule,
                CollectionSettlement.CollectionWriteOffType, command.SettlementId, command.SourceVersion,
                command.Fingerprint),
            snapshot.BusinessDate, snapshot.BaseCurrency, draft, map.MappingVersion);
        var posted = await joined.PostInCurrentTransactionAsync(request, cancellationToken);
        switch (posted.Kind)
        {
            case GlPostingOutcomeKind.Posted when posted.JournalId is > 0:
                break;
            case GlPostingOutcomeKind.Rejected:
                return new(CollectionPostingOutcomeKind.Rejected, false, posted.HttpStatus ?? 500, posted.ErrorCode,
                    InvalidLine: posted.InvalidLine);
            case GlPostingOutcomeKind.Fault:
                return new(CollectionPostingOutcomeKind.Fault, false, posted.HttpStatus ?? 500, posted.ErrorCode);
            default:
                // Unchanged: GL already holds the permanent source key but FI has no settlement row for it.
                return await FaultAsync(CollectionWriteOffPostingErrors.InconsistentState);
        }

        return await WriteAsync(command, snapshot, receipt!.Id, receivableIds, posted.JournalId.Value, cancellationToken);
    }

    private Task<bool> TryLockAsync(string resource, CancellationToken cancellationToken) =>
        PostingServiceSupport.TryLockAsync(db, resource, LockTimeoutMilliseconds, cancellationToken);

    /// <summary>
    /// FI's permanent settlement key decides replay under the credit key: identical claim → Unchanged with the committed
    /// rows (the document is not read again); another customer, version or fingerprint → 409. Absent → null.
    /// </summary>
    private async Task<CollectionWriteOffPostingResult?> ReplayAsync(CollectionWriteOffPostingCommand command,
        CancellationToken cancellationToken)
    {
        var saved = await db.Set<CollectionSettlement>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == CollectionSettlement.CollectionWriteOffType
                        && x.SourceId == command.SettlementId)
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is null)
            return null;
        if (saved.CustomerId != command.CustomerId || saved.SourceVersion != command.SourceVersion
            || !string.Equals(saved.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return Rejected(409, CollectionWriteOffPostingErrors.SourceConflict);
        var receipt = await db.Set<CollectionReceipt>().AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == saved.ReceiptId, cancellationToken);
        var allocations = await (
                from a in db.Set<CollectionAllocation>().AsNoTracking()
                join r in db.Set<PostedReceivable>().AsNoTracking()
                    on new { a.CompanyId, Id = a.ReceivableId } equals new { r.CompanyId, r.Id }
                where a.CompanyId == command.CompanyId && a.SettlementId == saved.Id
                orderby a.ReceivableId
                select new CollectionWriteOffAllocationReceipt(a.ReceivableId, a.Amount, r.OpenBaseAmount))
            .ToListAsync(cancellationToken);
        return new(CollectionPostingOutcomeKind.Unchanged, false, 200,
            Receipt: Receipt(command.CompanyId, saved, receipt, allocations));
    }

    private async Task<CollectionWriteOffPostingResult> WriteAsync(CollectionWriteOffPostingCommand command,
        FinanceCollectionWriteOffSnapshot snapshot, long collectionReceiptId, long[] receivableIds,
        long journalId, CancellationToken cancellationToken)
    {
        var amounts = snapshot.Lines.ToDictionary(x => x.ReceivableId, x => x.Amount);
        var settlement = new CollectionSettlement
        {
            CustomerId = command.CustomerId,
            SourceId = command.SettlementId,
            SourceVersion = command.SourceVersion,
            Fingerprint = command.Fingerprint,
            GlJournalId = journalId,
            ReceiptId = collectionReceiptId,
            BusinessDate = snapshot.BusinessDate,
            BaseCurrency = snapshot.BaseCurrency,
            TotalAmount = snapshot.TotalAmount
        };
        CollectionReceipt receipt;
        var allocations = new List<CollectionWriteOffAllocationReceipt>(receivableIds.Length);
        try
        {
            // Tracked only from here on, after every check passed, so a rejection never leaves tracked FI rows behind.
            receipt = await db.Set<CollectionReceipt>()
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == collectionReceiptId, cancellationToken);
            var receivables = await db.Set<PostedReceivable>()
                .Where(x => x.CompanyId == command.CompanyId && receivableIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            db.Set<CollectionSettlement>().Add(settlement);
            await db.SaveChangesAsync(cancellationToken);
            receipt.UnappliedAmount -= snapshot.TotalAmount;
            foreach (var id in receivableIds)
            {
                var receivable = receivables[id];
                db.Set<CollectionAllocation>().Add(new CollectionAllocation
                {
                    SettlementId = settlement.Id,
                    ReceiptId = collectionReceiptId,
                    ReceivableId = id,
                    Amount = amounts[id]
                });
                receivable.OpenBaseAmount -= amounts[id];
                allocations.Add(new(id, amounts[id], receivable.OpenBaseAmount));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException or OperationCanceledException
                                       or InvalidOperationException or KeyNotFoundException)
        {
            db.ChangeTracker.Clear();
            return await FaultAsync(CollectionWriteOffPostingErrors.WriteFailed);
        }

        return new(CollectionPostingOutcomeKind.Posted, true, 200,
            Receipt: Receipt(command.CompanyId, settlement, receipt, allocations));
    }

    private static CollectionWriteOffPostingReceipt Receipt(long companyId, CollectionSettlement settlement,
        CollectionReceipt receipt, IReadOnlyList<CollectionWriteOffAllocationReceipt> allocations) =>
        new(companyId, settlement.SourceId, settlement.CustomerId, settlement.SourceVersion, settlement.Fingerprint,
            settlement.BaseCurrency, settlement.TotalAmount, receipt.SourceId, receipt.Id, receipt.UnappliedAmount,
            settlement.Id, settlement.GlJournalId, allocations);

    private async Task<CollectionWriteOffPostingResult> FaultAsync(string code)
    {
        await PostingServiceSupport.RollbackCallerTransactionAsync(db);
        return new(CollectionPostingOutcomeKind.Fault, false, 500, code);
    }

    private static CollectionWriteOffPostingResult Rejected(int status, string code) =>
        new(CollectionPostingOutcomeKind.Rejected, false, status, code);

}
