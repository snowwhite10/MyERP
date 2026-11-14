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
/// WU-FI-005a2b (ADR-121): approved FI sales collection receipt → GL + FI unapplied cash inside the caller's transaction on
/// the scoped DbContext. Lock order follows BR-FI-036: FI credit key, then the receipt source key
/// (<c>ar_collection:{company}:{receipt}</c>), FI replay check, the saved receipt read, then the bank-reference key
/// (<c>ar_collection_bank:{company}:{fund account}:{REFERENCE}</c>) and the GL source applock inside
/// <see cref="IGlJoinedPostingService"/>. All applocks are transaction-owned. Rejected results leave the caller's
/// transaction open and write nothing; every Fault rolls the whole caller transaction back. Open AR is never changed.
/// </summary>
internal sealed class CollectionReceiptPostingService(
    MyErpDbContext db,
    ICurrentCompany company,
    ICustomerCreditService credit,
    IGlJoinedPostingService joined,
    IChartOfAccounts chart,
    IFundAccountLookup funds,
    IEnumerable<IFinanceCollectionReceiptSource> sources,
    IEnumerable<ISalesCollectionAccountMapSource> maps) : ICollectionReceiptPostingService
{
    /// <summary>Maximum wait for another transaction holding the same receipt or bank reference; tests shorten it.</summary>
    internal int LockTimeoutMilliseconds { get; set; } = 15_000;

    internal static string SourceLockResource(long companyId, long receiptId) =>
        string.Create(CultureInfo.InvariantCulture, $"ar_collection:{companyId}:{receiptId}");

    internal static string BankLockResource(long companyId, long bankAccountId, string reference) =>
        string.Create(CultureInfo.InvariantCulture,
            $"ar_collection_bank:{companyId}:{bankAccountId}:{CollectionReceiptPostingRules.BankReferenceKey(reference)}");

    public async Task<CollectionReceiptPostingResult> PostInCurrentTransactionAsync(
        CollectionReceiptPostingCommand? command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("FI collection posting requires the caller's open SQL transaction.");
        if (!CollectionReceiptPostingRules.IsWellFormed(command))
            return Rejected(400, CollectionPostingErrors.InvalidInput);
        if (company.CompanyId != command!.CompanyId || db.CurrentCompanyId != command.CompanyId)
            return Rejected(404, CollectionPostingErrors.SourceNotFound);
        var source = PostingServiceSupport.SingleOrNull(sources);
        if (source is null)
            return Rejected(500, CollectionPostingErrors.SourceContractUnavailable);

        var dbTransaction = transaction.GetDbTransaction();
        try
        {
            await credit.AcquireCreditLockAsync(command.CompanyId, command.CustomerId, dbTransaction, cancellationToken);
        }
        catch (ConflictException ex) when (ex.ErrorCode == FinanceModuleContracts.CreditLockTimeout)
        {
            return Rejected(409, FinanceModuleContracts.CreditLockTimeout);
        }
        if (!await TryLockAsync(SourceLockResource(command.CompanyId, command.ReceiptId), cancellationToken))
            return Rejected(409, CollectionPostingErrors.LockTimeout);

        var replay = await ReplayAsync(command, cancellationToken);
        if (replay is not null)
            return replay;

        var snapshot = await source.ReadReceiptAsync(command.CompanyId, command.ReceiptId, dbTransaction,
            cancellationToken);
        if (CollectionReceiptPostingRules.CheckSnapshot(command, snapshot) is { } failure)
            return Rejected(failure.Status, failure.Code);

        if (!await TryLockAsync(BankLockResource(command.CompanyId, snapshot!.BankAccountId, snapshot.BankReference),
                cancellationToken))
            return Rejected(409, CollectionPostingErrors.LockTimeout);
        // Compared by the column's case-insensitive collation, like the permanent unique index.
        if (await db.Set<CollectionReceipt>().AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId
                && x.BankAccountId == snapshot.BankAccountId && x.BankReference == snapshot.BankReference,
                cancellationToken))
            return Rejected(409, CollectionPostingErrors.BankReferenceDuplicate);

        var map = PostingServiceSupport.SingleOrNull(maps) is { } mapSource
            ? await mapSource.ReadAsync(command.CompanyId, cancellationToken)
            : null;
        if (map is not { MappingVersion: > 0 })
            return Rejected(422, CollectionPostingErrors.MappingMissing);
        var fund = await funds.FindByIdAsync(snapshot.BankAccountId, cancellationToken);
        if (fund is not { IsActive: true, LedgerAccountCode.Length: > 0 }
            || !string.Equals(fund.CurrencyCode, snapshot.Currency, StringComparison.Ordinal))
            return Rejected(422, CollectionPostingErrors.FundAccountInvalid);
        if (string.Equals(fund.LedgerAccountCode, map.UnappliedCashAccountCode, StringComparison.OrdinalIgnoreCase))
            return new(CollectionPostingOutcomeKind.Rejected, false, 422, CollectionPostingErrors.AccountInvalid,
                InvalidLine: 2);

        // BR-FI-033 base-currency journal: Dr fund account ledger, Cr unapplied-cash clearing, same amount.
        var (draft, invalidLine) = await PostingJournalDraft.BuildAsync(chart, command.CompanyId, map.MappingVersion,
            [(fund.LedgerAccountCode!, snapshot.Amount, 0m), (map.UnappliedCashAccountCode, 0m, snapshot.Amount)],
            cancellationToken);
        if (draft is null)
            return new(CollectionPostingOutcomeKind.Rejected, false, 422, CollectionPostingErrors.AccountInvalid,
                InvalidLine: invalidLine);

        var request = new GlPostingRequest(command.CompanyId,
            new GlPostingSource(command.CompanyId, SalesCollectionAccountMapStore.CollectionSourceModule,
                SalesCollectionAccountMapStore.ReceiptType, command.ReceiptId, command.SourceVersion, command.Fingerprint),
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
                // Unchanged: GL already holds the permanent source key but FI has no receipt row for it.
                return await FaultAsync(CollectionPostingErrors.InconsistentState);
        }

        return await WriteAsync(command, snapshot, posted.JournalId.Value, cancellationToken);
    }

    private Task<bool> TryLockAsync(string resource, CancellationToken cancellationToken) =>
        PostingServiceSupport.TryLockAsync(db, resource, LockTimeoutMilliseconds, cancellationToken);

    /// <summary>
    /// FI's permanent source key decides replay under the locks: identical claim → Unchanged with the committed receipt
    /// (the document is not read again); another customer, version or fingerprint → 409. Absent → null (new posting).
    /// </summary>
    private async Task<CollectionReceiptPostingResult?> ReplayAsync(CollectionReceiptPostingCommand command,
        CancellationToken cancellationToken)
    {
        var saved = await db.Set<CollectionReceipt>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == CollectionReceipt.SalesCollectionType
                        && x.SourceId == command.ReceiptId)
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is null)
            return null;
        if (saved.CustomerId != command.CustomerId || saved.SourceVersion != command.SourceVersion
            || !string.Equals(saved.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return Rejected(409, CollectionPostingErrors.SourceConflict);
        return new(CollectionPostingOutcomeKind.Unchanged, false, 200, Receipt: Receipt(command.CompanyId, saved));
    }

    private async Task<CollectionReceiptPostingResult> WriteAsync(CollectionReceiptPostingCommand command,
        FinanceCollectionReceiptSnapshot snapshot, long journalId, CancellationToken cancellationToken)
    {
        var row = new CollectionReceipt
        {
            CustomerId = command.CustomerId,
            SourceId = command.ReceiptId,
            SourceVersion = command.SourceVersion,
            Fingerprint = command.Fingerprint,
            GlJournalId = journalId,
            BusinessDate = snapshot.BusinessDate,
            BankAccountId = snapshot.BankAccountId,
            BankReference = snapshot.BankReference,
            BaseCurrency = snapshot.BaseCurrency,
            Amount = snapshot.Amount,
            UnappliedAmount = snapshot.Amount
        };
        try
        {
            db.Set<CollectionReceipt>().Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException or OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            return await FaultAsync(CollectionPostingErrors.WriteFailed);
        }

        return new(CollectionPostingOutcomeKind.Posted, true, 200, Receipt: Receipt(command.CompanyId, row));
    }

    private static CollectionReceiptPostingReceipt Receipt(long companyId, CollectionReceipt row) =>
        new(companyId, row.SourceId, row.CustomerId, row.SourceVersion, row.Fingerprint, row.BaseCurrency, row.Amount,
            row.UnappliedAmount, row.BankAccountId, row.BankReference, row.Id, row.GlJournalId);

    private async Task<CollectionReceiptPostingResult> FaultAsync(string code)
    {
        await PostingServiceSupport.RollbackCallerTransactionAsync(db);
        return new(CollectionPostingOutcomeKind.Fault, false, 500, code);
    }

    private static CollectionReceiptPostingResult Rejected(int status, string code) =>
        new(CollectionPostingOutcomeKind.Rejected, false, status, code);

}
