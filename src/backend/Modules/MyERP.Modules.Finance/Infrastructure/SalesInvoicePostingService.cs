using System.Data.Common;
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
/// WU-FI-002a2b (ADR-107/108): SD invoice → FI AR + GL inside the caller's transaction on the scoped DbContext.
/// Lock order follows BR-FI-013: the FI credit key first, then FI's own replay check (permanent source key), then the
/// SD source read, then the GL source applock inside <see cref="IGlJoinedPostingService"/>. Rejected results leave the
/// caller's transaction open and write nothing; every Fault rolls the whole caller transaction back.
/// </summary>
internal sealed class SalesInvoicePostingService(
    MyErpDbContext db,
    ICurrentCompany company,
    ICustomerCreditService credit,
    IGlJoinedPostingService joined,
    IChartOfAccounts chart,
    IEnumerable<IFinanceSalesInvoiceSource> sources,
    IEnumerable<ISalesInvoiceAccountMapSource> maps) : ISalesInvoicePostingService
{
    internal const string SourceModule = "SD";
    internal const string InvoiceType = nameof(SalesSourceType.Invoice);

    public async Task<SalesInvoicePostingResult> PostInCurrentTransactionAsync(SalesInvoicePostingCommand? command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("FI invoice posting requires the caller's open SQL transaction.");
        if (!SalesInvoicePostingRules.IsWellFormed(command))
            return Rejected(400, SalesPostingErrors.InvalidInput);
        if (company.CompanyId != command!.CompanyId || db.CurrentCompanyId != command.CompanyId)
            return Rejected(404, SalesPostingErrors.SourceNotFound);
        var source = SingleOrNull(sources);
        if (source is null)
            return Rejected(500, SalesPostingErrors.SourceContractUnavailable);

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

        var snapshot = await source.ReadInvoiceAsync(command.CompanyId, command.InvoiceId, dbTransaction,
            cancellationToken);
        if (SalesInvoicePostingRules.CheckSnapshot(command, snapshot) is { } failure)
            return Rejected(failure.Status, failure.Code);

        var map = SingleOrNull(maps) is { } mapSource
            ? await mapSource.ReadAsync(command.CompanyId, cancellationToken)
            : null;
        if (map is not { MappingVersion: > 0 })
            return Rejected(422, SalesPostingErrors.MappingMissing);
        var (draft, invalidLine) = await DraftAsync(command.CompanyId, map, snapshot!, cancellationToken);
        if (draft is null)
            return new(SalesPostingOutcomeKind.Rejected, false, 422, SalesPostingErrors.AccountInvalid,
                InvalidLine: invalidLine);

        var request = new GlPostingRequest(command.CompanyId,
            new GlPostingSource(command.CompanyId, SourceModule, InvoiceType, command.InvoiceId, command.SourceVersion,
                command.Fingerprint),
            snapshot!.BusinessDate, snapshot.BaseCurrency, draft, map.MappingVersion);
        var posted = await joined.PostInCurrentTransactionAsync(request, cancellationToken);
        switch (posted.Kind)
        {
            case GlPostingOutcomeKind.Posted when posted.JournalId is > 0:
                break;
            case GlPostingOutcomeKind.Rejected:
                return new(SalesPostingOutcomeKind.Rejected, false, posted.HttpStatus ?? 500, posted.ErrorCode,
                    InvalidLine: posted.InvalidLine);
            case GlPostingOutcomeKind.Fault:
                return new(SalesPostingOutcomeKind.Fault, false, posted.HttpStatus ?? 500, posted.ErrorCode);
            default:
                // Unchanged: GL already holds the permanent source key but FI has no sales source for it.
                return await FaultAsync(SalesPostingErrors.InconsistentState);
        }

        return await WriteAsync(command, snapshot, posted.JournalId.Value, cancellationToken);
    }

    /// <summary>
    /// FI's permanent source key decides replay under the credit lock: identical claim → Unchanged with the committed
    /// receipt; another customer, version or fingerprint → 409. Absent → null (new posting).
    /// </summary>
    private async Task<SalesInvoicePostingResult?> ReplayAsync(SalesInvoicePostingCommand command,
        CancellationToken cancellationToken)
    {
        var saved = await db.Set<SalesSource>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == SalesSourceType.Invoice
                        && x.SourceId == command.InvoiceId)
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is null)
            return null;
        if (saved.CustomerId != command.CustomerId || saved.SourceVersion != command.SourceVersion
            || !string.Equals(saved.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return Rejected(409, SalesPostingErrors.SourceConflict);
        var receivableId = await db.Set<PostedReceivable>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == InvoiceType && x.SourceId == command.InvoiceId)
            .Select(x => (long?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (receivableId is null)
            return Rejected(500, SalesPostingErrors.InconsistentState);
        return new(SalesPostingOutcomeKind.Unchanged, false, 200, Receipt: Receipt(command, saved, receivableId.Value));
    }

    /// <summary>
    /// BR-FI-009 base-currency journal: Dr AR base gross; Cr revenue base net; Cr output tax base tax. Zero legs are
    /// omitted (no zero tax line). Accounts are checked through MD's public contract.
    /// </summary>
    private async Task<(GlJournalLineDraftResult? Draft, int? InvalidLine)> DraftAsync(long companyId,
        SalesInvoiceAccountMap map, FinanceSalesInvoiceSnapshot snapshot, CancellationToken cancellationToken)
    {
        var legs = new List<(string Code, decimal Debit, decimal Credit)>
        {
            (map.ReceivableAccountCode, snapshot.BaseGrossAmount, 0m)
        };
        if (snapshot.BaseNetAmount > 0m)
            legs.Add((map.RevenueAccountCode, 0m, snapshot.BaseNetAmount));
        if (snapshot.BaseTaxAmount > 0m)
            legs.Add((map.OutputTaxAccountCode, 0m, snapshot.BaseTaxAmount));

        var candidates = new List<GlJournalLineCandidate>(legs.Count);
        for (var index = 0; index < legs.Count; index++)
        {
            var (code, debit, credit) = legs[index];
            var account = string.IsNullOrEmpty(code) ? null : await chart.FindAsync(code, cancellationToken);
            var snapshotResult = GlAccountSnapshotRules.Validate(companyId, account?.Id ?? 0, code,
                map.MappingVersion, map.MappingVersion, account);
            if (snapshotResult.Status != GlAccountSnapshotStatus.Allowed)
                return (null, index + 1);
            candidates.Add(new(snapshotResult, debit, credit));
        }

        var draft = GlJournalLineDraftRules.Build(companyId, map.MappingVersion, candidates);
        return draft.Status == GlJournalLineDraftStatus.Allowed ? (draft, null) : (null, draft.InvalidLine);
    }

    private async Task<SalesInvoicePostingResult> WriteAsync(SalesInvoicePostingCommand command,
        FinanceSalesInvoiceSnapshot snapshot, long journalId, CancellationToken cancellationToken)
    {
        var source = new SalesSource
        {
            CustomerId = command.CustomerId,
            SourceType = SalesSourceType.Invoice,
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
        var receivable = new PostedReceivable
        {
            CustomerId = command.CustomerId,
            SourceType = InvoiceType,
            SourceId = command.InvoiceId,
            BaseCurrency = snapshot.BaseCurrency,
            OriginalBaseAmount = snapshot.BaseGrossAmount,
            OpenBaseAmount = snapshot.BaseGrossAmount,
            PostingDate = snapshot.BusinessDate
        };
        db.Set<SalesSource>().Add(source);
        db.Set<PostedReceivable>().Add(receivable);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException or OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            return await FaultAsync(SalesPostingErrors.WriteFailed);
        }

        return new(SalesPostingOutcomeKind.Posted, true, 200, Receipt: Receipt(command, source, receivable.Id));
    }

    private static SalesInvoicePostingReceipt Receipt(SalesInvoicePostingCommand command, SalesSource source,
        long receivableId) =>
        new(command.CompanyId, source.SourceId, source.CustomerId, source.SourceVersion, source.Fingerprint,
            source.Currency, source.GrossAmount, source.BaseCurrency, source.BaseGrossAmount, source.Id, receivableId,
            source.GlJournalId);

    private async Task<SalesInvoicePostingResult> FaultAsync(string code)
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
        return new(SalesPostingOutcomeKind.Fault, false, 500, code);
    }

    private static SalesInvoicePostingResult Rejected(int status, string code) =>
        new(SalesPostingOutcomeKind.Rejected, false, status, code);

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
