using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-003a2b (ADR-117): PUR purchase invoice → FI AP + GL inside the caller's transaction on the scoped DbContext.
/// Lock order follows BR-FI-020: PUR holds its own locks before calling; FI then takes its source-key applock
/// (<c>ap_purchase:{company}:{invoice}</c>, transaction-owned), checks its own replay (permanent source key), reads the
/// saved PUR source through the FI-owned port, and takes the GL source applock inside
/// <see cref="IGlJoinedPostingService"/>. FI never calls back for PUR locks after its source lock. Rejected results
/// leave the caller's transaction open and write nothing; every Fault rolls the whole caller transaction back.
/// </summary>
internal sealed class PurchaseInvoicePostingService(
    MyErpDbContext db,
    ICurrentCompany company,
    IGlJoinedPostingService joined,
    IChartOfAccounts chart,
    IEnumerable<IFinancePurchaseInvoiceSource> sources,
    IEnumerable<IPurchaseInvoiceAccountMapSource> maps) : IPurchaseInvoicePostingService
{
    private const int LockNotGranted = 51028;
    private const int SqlLockTimeout = 1222;

    /// <summary>Maximum wait for another transaction posting the same invoice; race tests shorten it.</summary>
    internal int LockTimeoutMilliseconds { get; set; } = 15_000;

    internal static string LockResource(long companyId, long invoiceId) =>
        string.Create(CultureInfo.InvariantCulture, $"ap_purchase:{companyId}:{invoiceId}");

    public async Task<PurchaseInvoicePostingResult> PostInCurrentTransactionAsync(
        PurchaseInvoicePostingCommand? command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("FI purchase posting requires the caller's open SQL transaction.");
        if (!PurchaseInvoicePostingRules.IsWellFormed(command))
            return Rejected(400, PurchasePostingErrors.InvalidInput);
        if (company.CompanyId != command!.CompanyId || db.CurrentCompanyId != command.CompanyId)
            return Rejected(404, PurchasePostingErrors.SourceNotFound);
        var source = SingleOrNull(sources);
        if (source is null)
            return Rejected(500, PurchasePostingErrors.SourceContractUnavailable);

        var dbTransaction = transaction.GetDbTransaction();
        if (!await TryLockAsync(command, cancellationToken))
            return Rejected(409, PurchasePostingErrors.LockTimeout);

        var replay = await ReplayAsync(command, cancellationToken);
        if (replay is not null)
            return replay;

        var snapshot = await source.ReadInvoiceAsync(command.CompanyId, command.InvoiceId, dbTransaction,
            cancellationToken);
        if (PurchaseInvoicePostingRules.CheckSnapshot(command, snapshot) is { } failure)
            return new(PurchasePostingOutcomeKind.Rejected, false, failure.Status, failure.Code,
                InvalidSourceLine: failure.SourceLine);

        var map = SingleOrNull(maps) is { } mapSource
            ? await mapSource.ReadAsync(command.CompanyId, cancellationToken)
            : null;
        if (map is not { MappingVersion: > 0 })
            return Rejected(422, PurchasePostingErrors.MappingMissing);
        var (draft, invalidLine) = await DraftAsync(command.CompanyId, map, snapshot!, cancellationToken);
        if (draft is null)
            return new(PurchasePostingOutcomeKind.Rejected, false, 422, PurchasePostingErrors.AccountInvalid,
                InvalidLine: invalidLine);

        var request = new GlPostingRequest(command.CompanyId,
            new GlPostingSource(command.CompanyId, PurchaseInvoiceAccountMapStore.PurchaseSourceModule,
                PurchaseInvoiceAccountMapStore.PurchaseInvoiceType, command.InvoiceId, command.SourceVersion,
                command.Fingerprint),
            snapshot!.BusinessDate, snapshot.BaseCurrency, draft, map.MappingVersion);
        var posted = await joined.PostInCurrentTransactionAsync(request, cancellationToken);
        switch (posted.Kind)
        {
            case GlPostingOutcomeKind.Posted when posted.JournalId is > 0:
                break;
            case GlPostingOutcomeKind.Rejected:
                return new(PurchasePostingOutcomeKind.Rejected, false, posted.HttpStatus ?? 500, posted.ErrorCode,
                    InvalidLine: posted.InvalidLine);
            case GlPostingOutcomeKind.Fault:
                return new(PurchasePostingOutcomeKind.Fault, false, posted.HttpStatus ?? 500, posted.ErrorCode);
            default:
                // Unchanged: GL already holds the permanent source key but FI has no purchase source for it.
                return await FaultAsync(PurchasePostingErrors.InconsistentState);
        }

        return await WriteAsync(command, snapshot, posted.JournalId.Value, cancellationToken);
    }

    /// <summary>
    /// BR-FI-020: the FI source key of this invoice, owned by the caller's transaction (released by its commit or
    /// rollback). A timeout or deadlock victim is a 409 the caller may retry; the transaction stays open.
    /// </summary>
    private async Task<bool> TryLockAsync(PurchaseInvoicePostingCommand command, CancellationToken cancellationToken)
    {
        var resource = LockResource(command.CompanyId, command.InvoiceId);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {LockTimeoutMilliseconds};
                IF @result < 0 THROW 51028, 'FI purchase source lock was not granted.', 1;
                """, cancellationToken);
            return true;
        }
        catch (SqlException ex) when (ex.Number is LockNotGranted or SqlLockTimeout)
        {
            return false;
        }
    }

    /// <summary>
    /// FI's permanent source key decides replay under the source lock: identical claim → Unchanged with the committed
    /// receipt (PUR is not read again); another supplier, version or fingerprint → 409. Absent → null (new posting).
    /// </summary>
    private async Task<PurchaseInvoicePostingResult?> ReplayAsync(PurchaseInvoicePostingCommand command,
        CancellationToken cancellationToken)
    {
        var saved = await db.Set<PurchaseSource>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == PurchaseSourceType.PurchaseInvoice
                        && x.SourceId == command.InvoiceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is null)
            return null;
        if (saved.SupplierId != command.SupplierId || saved.SourceVersion != command.SourceVersion
            || !string.Equals(saved.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return Rejected(409, PurchasePostingErrors.SourceConflict);
        var payableId = await db.Set<PostedPayable>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.PurchaseSourceId == saved.Id)
            .Select(x => (long?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (payableId is null)
            return Rejected(500, PurchasePostingErrors.InconsistentState);
        return new(PurchasePostingOutcomeKind.Unchanged, false, 200, Receipt: Receipt(command, saved, payableId.Value));
    }

    /// <summary>
    /// BR-FI-018 base-currency journal: Dr purchases/expense base net; Dr input tax base tax; Cr AP control base gross.
    /// Zero legs are omitted (no zero tax line). Accounts are checked through MD's public contract.
    /// </summary>
    private async Task<(GlJournalLineDraftResult? Draft, int? InvalidLine)> DraftAsync(long companyId,
        PurchaseInvoiceAccountMap map, FinancePurchaseInvoiceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var legs = new List<(string Code, decimal Debit, decimal Credit)>();
        if (snapshot.BaseNetAmount > 0m)
            legs.Add((map.PurchaseAccountCode, snapshot.BaseNetAmount, 0m));
        if (snapshot.BaseTaxAmount > 0m)
            legs.Add((map.InputTaxAccountCode, snapshot.BaseTaxAmount, 0m));
        legs.Add((map.PayableAccountCode, 0m, snapshot.BaseGrossAmount));
        return await PostingJournalDraft.BuildAsync(chart, companyId, map.MappingVersion, legs, cancellationToken);
    }

    private async Task<PurchaseInvoicePostingResult> WriteAsync(PurchaseInvoicePostingCommand command,
        FinancePurchaseInvoiceSnapshot snapshot, long journalId, CancellationToken cancellationToken)
    {
        var source = new PurchaseSource
        {
            SupplierId = command.SupplierId,
            SourceType = PurchaseSourceType.PurchaseInvoice,
            SourceId = command.InvoiceId,
            SourceVersion = command.SourceVersion,
            Fingerprint = command.Fingerprint,
            GlJournalId = journalId,
            BusinessDate = snapshot.BusinessDate,
            Currency = snapshot.Currency,
            BaseCurrency = snapshot.BaseCurrency,
            ExchangeRate = snapshot.ExchangeRate,
            NetAmount = snapshot.NetAmount,
            TaxAmount = snapshot.TaxAmount,
            GrossAmount = snapshot.GrossAmount,
            BaseNetAmount = snapshot.BaseNetAmount,
            BaseTaxAmount = snapshot.BaseTaxAmount,
            BaseGrossAmount = snapshot.BaseGrossAmount
        };
        PostedPayable payable;
        try
        {
            // The payable references the source through a same-company composite FK and the origin trigger reads the
            // saved source, so the source row is saved first; both stay inside the caller's transaction.
            db.Set<PurchaseSource>().Add(source);
            await db.SaveChangesAsync(cancellationToken);
            payable = new PostedPayable
            {
                SupplierId = command.SupplierId,
                PurchaseSourceId = source.Id,
                PostingDate = snapshot.BusinessDate,
                Currency = snapshot.Currency,
                BaseCurrency = snapshot.BaseCurrency,
                OriginalAmount = snapshot.GrossAmount,
                OpenAmount = snapshot.GrossAmount,
                OriginalBaseAmount = snapshot.BaseGrossAmount,
                OpenBaseAmount = snapshot.BaseGrossAmount
            };
            db.Set<PostedPayable>().Add(payable);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException or OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            return await FaultAsync(PurchasePostingErrors.WriteFailed);
        }

        return new(PurchasePostingOutcomeKind.Posted, true, 200, Receipt: Receipt(command, source, payable.Id));
    }

    private static PurchaseInvoicePostingReceipt Receipt(PurchaseInvoicePostingCommand command, PurchaseSource source,
        long payableId) =>
        new(command.CompanyId, source.SourceId, source.SupplierId, source.SourceVersion, source.Fingerprint,
            source.Currency, source.GrossAmount, source.BaseCurrency, source.BaseGrossAmount, source.Id, payableId,
            source.GlJournalId);

    private async Task<PurchaseInvoicePostingResult> FaultAsync(string code)
    {
        if (db.Database.CurrentTransaction is { } current)
        {
            try
            {
                await current.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollback) when (rollback is DbException or InvalidOperationException)
            {
                // A broken connection has already discarded the uncommitted work on the server.
            }
        }
        return new(PurchasePostingOutcomeKind.Fault, false, 500, code);
    }

    private static PurchaseInvoicePostingResult Rejected(int status, string code) =>
        new(PurchasePostingOutcomeKind.Rejected, false, status, code);

    private static T? SingleOrNull<T>(IEnumerable<T> items) where T : class
    {
        T? match = null;
        foreach (var item in items)
        {
            if (match is not null)
                return null;
            match = item;
        }
        return match;
    }
}
