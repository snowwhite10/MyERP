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
/// WU-FI-002a3b (ADR-113): SD billed return → FI credit source + GL credit journal inside the caller's transaction on
/// the scoped DbContext. Lock order follows BR-FI-013 like invoices: FI credit key → FI replay check → SD return read →
/// original FI invoice sources and receivables (ascending receivable ID) and committed prior allocations → pure
/// cumulative/open checks → GL source applock inside <see cref="IGlJoinedPostingService"/> → writes. Rejected results
/// leave the caller's transaction open and write nothing; every Fault rolls the whole caller transaction back.
/// </summary>
internal sealed class SalesCreditPostingService(
    MyErpDbContext db,
    ICurrentCompany company,
    ICustomerCreditService credit,
    IGlJoinedPostingService joined,
    IChartOfAccounts chart,
    IEnumerable<IFinanceSalesReturnSource> sources,
    IEnumerable<ISalesInvoiceAccountMapSource> maps) : ISalesCreditPostingService
{
    internal const string ReturnType = nameof(SalesSourceType.Return);

    public async Task<SalesCreditPostingResult> PostInCurrentTransactionAsync(SalesCreditPostingCommand? command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("FI credit posting requires the caller's open SQL transaction.");
        if (!SalesCreditPostingRules.IsWellFormed(command))
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

        var snapshot = await source.ReadReturnAsync(command.CompanyId, command.ReturnId, dbTransaction,
            cancellationToken);
        if (SalesCreditPostingRules.CheckSnapshot(command, snapshot) is { } snapshotFailure)
            return Rejected(snapshotFailure);

        var originals = await ReadOriginalsAsync(command.CompanyId, snapshot!, cancellationToken);
        if (originals is null)
            return Rejected(500, SalesPostingErrors.InconsistentState);
        var (priorByLine, priorByInvoice) = await ReadPriorAsync(command.CompanyId, snapshot!, originals.Values,
            cancellationToken);
        var originalsByInvoice = originals.Values.ToDictionary(x => x.Invoice.InvoiceId, x => x.Invoice);
        if (SalesCreditPostingRules.CheckAllocations(command, snapshot!, originalsByInvoice, priorByLine,
                priorByInvoice) is { } allocationFailure)
            return Rejected(allocationFailure);

        var map = SingleOrNull(maps) is { } mapSource
            ? await mapSource.ReadAsync(command.CompanyId, cancellationToken)
            : null;
        if (map is not { MappingVersion: > 0 })
            return Rejected(422, SalesPostingErrors.MappingMissing);
        var (draft, invalidLine) = await PostingJournalDraft.BuildAsync(chart, command.CompanyId, map.MappingVersion,
            SalesCreditPostingRules.JournalLegs(map, snapshot!), cancellationToken);
        if (draft is null)
            return new(SalesPostingOutcomeKind.Rejected, false, 422, SalesPostingErrors.AccountInvalid,
                InvalidLine: invalidLine);

        var request = new GlPostingRequest(command.CompanyId,
            new GlPostingSource(command.CompanyId, SalesInvoicePostingService.SourceModule, ReturnType,
                command.ReturnId, command.SourceVersion, command.Fingerprint),
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
                // Unchanged: GL already holds the permanent source key but FI has no credit source for it.
                return await FaultAsync(SalesPostingErrors.InconsistentState);
        }

        return await WriteAsync(command, snapshot, originals, posted.JournalId.Value, cancellationToken);
    }

    /// <summary>
    /// FI's permanent source key decides replay under the credit lock: identical claim → Unchanged with the committed
    /// receipt and allocations; another customer, version or fingerprint → 409. Absent → null (new posting).
    /// </summary>
    private async Task<SalesCreditPostingResult?> ReplayAsync(SalesCreditPostingCommand command,
        CancellationToken cancellationToken)
    {
        var saved = await db.Set<SalesSource>().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceType == SalesSourceType.Return
                        && x.SourceId == command.ReturnId)
            .SingleOrDefaultAsync(cancellationToken);
        if (saved is null)
            return null;
        if (saved.CustomerId != command.CustomerId || saved.SourceVersion != command.SourceVersion
            || !string.Equals(saved.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return Rejected(409, SalesPostingErrors.SourceConflict);
        var allocations = await (
                from allocation in db.Set<CreditAllocation>().AsNoTracking()
                join receivable in db.Set<PostedReceivable>().AsNoTracking()
                    on new { allocation.CompanyId, Id = allocation.ReceivableId }
                    equals new { receivable.CompanyId, receivable.Id }
                where allocation.CompanyId == command.CompanyId && allocation.CreditSourceId == saved.Id
                orderby allocation.Id
                select new SalesCreditAllocationReceipt(receivable.SourceId, allocation.InvoiceLineId,
                    allocation.ReceivableId, allocation.Quantity, allocation.GrossAmount, allocation.BaseGrossAmount))
            .ToListAsync(cancellationToken);
        if (allocations.Count == 0)
            return Rejected(500, SalesPostingErrors.InconsistentState);
        return new(SalesPostingOutcomeKind.Unchanged, false, 200, Receipt: Receipt(command, saved, allocations));
    }

    private sealed record Original(SalesCreditOriginalInvoice Invoice, PostedReceivable Receivable);

    /// <summary>
    /// FI-saved invoice sources referenced by the return, each with its receivable (tracked for the open reduction),
    /// read in ascending receivable ID order (BR-FI-013). An invoice FI never posted is simply absent (the rules report
    /// <c>ORIGINAL_NOT_POSTED</c>); a saved source without its receivable is inconsistent state (null).
    /// </summary>
    private async Task<Dictionary<long, Original>?> ReadOriginalsAsync(long companyId,
        FinanceSalesReturnSnapshot snapshot, CancellationToken cancellationToken)
    {
        var invoiceIds = snapshot.Lines.Select(x => x.InvoiceId).Distinct().ToList();
        var invoiceSources = await db.Set<SalesSource>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SourceType == SalesSourceType.Invoice
                        && invoiceIds.Contains(x.SourceId))
            .ToListAsync(cancellationToken);
        var receivables = await db.Set<PostedReceivable>()
            .Where(x => x.CompanyId == companyId && x.SourceType == SalesInvoicePostingService.InvoiceType
                        && invoiceIds.Contains(x.SourceId))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var result = new Dictionary<long, Original>(invoiceSources.Count);
        foreach (var invoice in invoiceSources)
        {
            var receivable = receivables.SingleOrDefault(x => x.SourceId == invoice.SourceId);
            if (receivable is null)
                return null;
            result[invoice.SourceId] = new(new SalesCreditOriginalInvoice(invoice.SourceId, invoice.CustomerId,
                invoice.Currency, invoice.BaseCurrency, invoice.NetAmount, invoice.TaxAmount, invoice.BaseNetAmount,
                invoice.BaseTaxAmount, receivable.Id, receivable.OpenBaseAmount), receivable);
        }
        return result;
    }

    /// <summary>
    /// Committed prior credits under the credit key lock (ADR-113): by <c>(company, invoice_line_id)</c> and by original
    /// receivable (mapped back to its invoice). The allocation table has no soft delete, so every historical credit counts.
    /// </summary>
    private async Task<(Dictionary<long, SalesCreditTotals> ByLine, Dictionary<long, SalesCreditTotals> ByInvoice)>
        ReadPriorAsync(long companyId, FinanceSalesReturnSnapshot snapshot, IEnumerable<Original> originals,
            CancellationToken cancellationToken)
    {
        var lineIds = snapshot.Lines.Select(x => x.InvoiceLineId).Distinct().ToList();
        var byLine = await db.Set<CreditAllocation>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && lineIds.Contains(x.InvoiceLineId))
            .GroupBy(x => x.InvoiceLineId)
            .Select(g => new
            {
                g.Key, Quantity = g.Sum(x => x.Quantity), Net = g.Sum(x => x.NetAmount), Tax = g.Sum(x => x.TaxAmount),
                BaseNet = g.Sum(x => x.BaseNetAmount), BaseTax = g.Sum(x => x.BaseTaxAmount)
            })
            .ToListAsync(cancellationToken);

        var invoiceByReceivable = originals.ToDictionary(x => x.Receivable.Id, x => x.Invoice.InvoiceId);
        var receivableIds = invoiceByReceivable.Keys.ToList();
        var byReceivable = await db.Set<CreditAllocation>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && receivableIds.Contains(x.ReceivableId))
            .GroupBy(x => x.ReceivableId)
            .Select(g => new
            {
                g.Key, Quantity = g.Sum(x => x.Quantity), Net = g.Sum(x => x.NetAmount), Tax = g.Sum(x => x.TaxAmount),
                BaseNet = g.Sum(x => x.BaseNetAmount), BaseTax = g.Sum(x => x.BaseTaxAmount)
            })
            .ToListAsync(cancellationToken);

        return (byLine.ToDictionary(x => x.Key, x => new SalesCreditTotals(x.Quantity, x.Net, x.Tax, x.BaseNet, x.BaseTax)),
            byReceivable.ToDictionary(x => invoiceByReceivable[x.Key],
                x => new SalesCreditTotals(x.Quantity, x.Net, x.Tax, x.BaseNet, x.BaseTax)));
    }

    /// <summary>
    /// Credit source first (its ID is the allocations' FK), then one allocation per return line and the open-AR
    /// reductions, all in the caller's transaction. Any write failure rolls the whole caller transaction back.
    /// </summary>
    private async Task<SalesCreditPostingResult> WriteAsync(SalesCreditPostingCommand command,
        FinanceSalesReturnSnapshot snapshot, Dictionary<long, Original> originals, long journalId,
        CancellationToken cancellationToken)
    {
        var source = new SalesSource
        {
            CustomerId = command.CustomerId,
            SourceType = SalesSourceType.Return,
            SourceId = command.ReturnId,
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
        var receipts = new List<SalesCreditAllocationReceipt>(snapshot.Lines.Count);
        try
        {
            db.Set<SalesSource>().Add(source);
            await db.SaveChangesAsync(cancellationToken);
            foreach (var line in snapshot.Lines)
            {
                var receivable = originals[line.InvoiceId].Receivable;
                db.Set<CreditAllocation>().Add(new CreditAllocation
                {
                    CreditSourceId = source.Id,
                    ReceivableId = receivable.Id,
                    InvoiceLineId = line.InvoiceLineId,
                    Quantity = line.Quantity,
                    NetAmount = line.NetAmount,
                    TaxAmount = line.TaxAmount,
                    GrossAmount = line.GrossAmount,
                    BaseNetAmount = line.BaseNetAmount,
                    BaseTaxAmount = line.BaseTaxAmount,
                    BaseGrossAmount = line.BaseGrossAmount
                });
                receivable.OpenBaseAmount -= line.BaseGrossAmount;
                receipts.Add(new(line.InvoiceId, line.InvoiceLineId, receivable.Id, line.Quantity, line.GrossAmount,
                    line.BaseGrossAmount));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException or OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            return await FaultAsync(SalesPostingErrors.WriteFailed);
        }

        return new(SalesPostingOutcomeKind.Posted, true, 200, Receipt: Receipt(command, source, receipts));
    }

    private static SalesCreditPostingReceipt Receipt(SalesCreditPostingCommand command, SalesSource source,
        IReadOnlyList<SalesCreditAllocationReceipt> allocations) =>
        new(command.CompanyId, source.SourceId, source.CustomerId, source.SourceVersion, source.Fingerprint,
            source.Currency, source.GrossAmount, source.BaseCurrency, source.BaseGrossAmount, source.Id,
            source.GlJournalId, allocations);

    private async Task<SalesCreditPostingResult> FaultAsync(string code)
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

    private static SalesCreditPostingResult Rejected(SalesCreditFailure failure) =>
        new(SalesPostingOutcomeKind.Rejected, false, failure.Status, failure.Code, InvalidLine: failure.Line);

    private static SalesCreditPostingResult Rejected(int status, string code) =>
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
