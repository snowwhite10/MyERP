using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// Scoped hand-off of the expense category being posted to <see cref="OtherPaymentMappingVersionSource"/> (ADR-211):
/// the GL mapping-version contract is per company, while the other payment map is per company and category.
/// </summary>
public sealed class OtherPaymentPostingScope
{
    public long ExpenseCategoryId { get; set; }
}

/// <summary>
/// GL <c>FI/OtherPayment</c> mapping-version source (ADR-211): the version of the <c>gl_other_payment_account_map</c>
/// row for the company and the category the current scope is posting. No category in scope, another company or no row
/// → 0 (GL rejects the posting). Reads on the scoped DbContext, so inside the caller's transaction.
/// </summary>
public sealed class OtherPaymentMappingVersionSource(MyErpDbContext db, OtherPaymentPostingScope scope)
    : IGlPostingMappingVersionSource
{
    public string SourceModule => FinanceModuleContracts.ModuleCode;

    public string SourceType => OtherPaymentSource.PaymentType;

    public async Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        companyId <= 0 || db.CurrentCompanyId != companyId || scope.ExpenseCategoryId <= 0
            ? 0
            : await db.Set<OtherPaymentAccountMapping>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ExpenseCategoryId == scope.ExpenseCategoryId)
                .Select(x => x.Version).SingleOrDefaultAsync(cancellationToken);
}

/// <summary>
/// WU-FI-008a4 (ADR-211; BR-FI-060/061/062/063): posts a saved other payment inside the caller's open transaction on
/// the scoped DbContext. Lock order: bank-reference key <c>ap_other_payment_bank:{company}:{bank}:{REFERENCE}</c>
/// (withdrawal direction, distinct from collection deposits), source key <c>ap_other_payment:{company}:{payment}</c>,
/// then the GL period/source keys inside <see cref="IGlJoinedPostingService"/>. Replay is decided by FI's permanent
/// source row before the document is re-read under UPDLOCK. Journal: Dr mapped expense, Cr fund account ledger, same
/// base amount. Rejected results write nothing and keep the caller's transaction open; every Fault rolls it back.
/// Creates no AP payable, allocation or unapplied payment.
/// </summary>
internal sealed class OtherPaymentPostingService(
    MyErpDbContext db,
    ICurrentCompany company,
    IGlJoinedPostingService joined,
    IChartOfAccounts chart,
    IFundAccountLookup funds,
    IIncomeExpenseCategoryLookup categories,
    ICurrencyLookup currencies,
    OtherPaymentPostingScope scope) : IOtherPaymentPostingService
{
    /// <summary>Maximum wait for another transaction holding the same payment or reference; tests shorten it.</summary>
    internal int LockTimeoutMilliseconds { get; set; } = 15_000;

    internal static string SourceLockResource(long companyId, long paymentId) =>
        string.Create(CultureInfo.InvariantCulture, $"ap_other_payment:{companyId}:{paymentId}");

    internal static string BankLockResource(long companyId, long bankAccountId, string reference) =>
        string.Create(CultureInfo.InvariantCulture,
            $"ap_other_payment_bank:{companyId}:{bankAccountId}:{CollectionReceiptPostingRules.BankReferenceKey(reference)}");

    public async Task<OtherPaymentPostingResult> PostInCurrentTransactionAsync(OtherPaymentPostingCommand? command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("FI other payment posting requires the caller's open SQL transaction.");
        if (!OtherPaymentPostingRules.IsWellFormed(command))
            return Rejected(400, OtherPaymentPostingErrors.InvalidInput);
        if (company.CompanyId != command!.CompanyId || db.CurrentCompanyId != command.CompanyId)
            return Rejected(404, OtherPaymentPostingErrors.SourceNotFound);

        // Unlocked first read only names the bank-reference key; everything is decided after the locks.
        var first = await ReadDocumentAsync(command, cancellationToken);
        if (first is null)
            return Rejected(404, OtherPaymentPostingErrors.SourceNotFound);
        if (!await TryLockAsync(BankLockResource(command.CompanyId, first.BankAccountId, first.BankReference),
                cancellationToken)
            || !await TryLockAsync(SourceLockResource(command.CompanyId, command.PaymentId), cancellationToken))
            return Rejected(409, OtherPaymentPostingErrors.LockTimeout);

        var replay = await ReplayAsync(command, cancellationToken);
        if (replay is not null)
            return replay;

        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.ap_other_payment_doc WITH (UPDLOCK, HOLDLOCK) WHERE [id] = {0} AND [company_id] = {1}",
            command.PaymentId, command.CompanyId).ToListAsync(cancellationToken);
        var document = await ReadDocumentAsync(command, cancellationToken);
        if (document is null)
            return Rejected(404, OtherPaymentPostingErrors.SourceNotFound);
        if (!OtherPaymentPostingRules.IsPostable(document))
            return Rejected(422, OtherPaymentPostingErrors.SourceNotPostable);
        if (document.Version != command.SourceVersion
            || !string.Equals(OtherPaymentFingerprint.Create(document), command.Fingerprint, StringComparison.Ordinal)
            || document.BankAccountId != first.BankAccountId
            || !string.Equals(CollectionReceiptPostingRules.BankReferenceKey(document.BankReference),
                CollectionReceiptPostingRules.BankReferenceKey(first.BankReference), StringComparison.Ordinal))
            return Rejected(409, OtherPaymentPostingErrors.SourceMismatch);
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(cancellationToken))?.Code;
        if (!OtherPaymentPostingRules.IsUsable(document, baseCurrency))
            return Rejected(422, OtherPaymentPostingErrors.InvalidSource);

        // Compared by the column's case-insensitive collation, like the permanent unique index.
        if (await db.Set<OtherPaymentSource>().AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId
                && x.BankAccountId == document.BankAccountId && x.BankReference == document.BankReference,
                cancellationToken))
            return Rejected(409, OtherPaymentPostingErrors.BankReferenceDuplicate);

        var category = await categories.FindByIdAsync(document.ExpenseCategoryId, cancellationToken);
        if (category is not { IsActive: true, Direction: IncomeExpenseDirection.Expense })
            return Rejected(422, OtherPaymentPostingErrors.CategoryInvalid);
        var fund = await funds.FindByIdAsync(document.BankAccountId, cancellationToken);
        if (fund is not { IsActive: true, LedgerAccountCode.Length: > 0 }
            || !string.Equals(fund.CurrencyCode, baseCurrency, StringComparison.Ordinal))
            return Rejected(422, OtherPaymentPostingErrors.FundAccountInvalid);

        var map = await db.Set<OtherPaymentAccountMapping>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId
                                       && x.ExpenseCategoryId == document.ExpenseCategoryId, cancellationToken);
        if (map is not { Version: > 0 })
            return Rejected(422, OtherPaymentPostingErrors.MappingMissing);
        var expense = await chart.FindByIdAsync(map.ExpenseAccountId, cancellationToken);
        if (!OtherPaymentPostingRules.IsExpenseAccount(expense))
            return new(OtherPaymentPostingOutcomeKind.Rejected, false, 422, OtherPaymentPostingErrors.AccountInvalid,
                InvalidLine: 1);
        if (string.Equals(fund.LedgerAccountCode, expense!.Code, StringComparison.OrdinalIgnoreCase))
            return new(OtherPaymentPostingOutcomeKind.Rejected, false, 422, OtherPaymentPostingErrors.AccountInvalid,
                InvalidLine: 2);

        // BR-FI-061 base-currency journal: Dr mapped expense, Cr fund account ledger, same amount.
        var (draft, invalidLine) = await PostingJournalDraft.BuildAsync(chart, command.CompanyId, map.Version,
            [(expense.Code, document.Amount, 0m), (fund.LedgerAccountCode!, 0m, document.Amount)], cancellationToken);
        if (draft is null)
            return new(OtherPaymentPostingOutcomeKind.Rejected, false, 422, OtherPaymentPostingErrors.AccountInvalid,
                InvalidLine: invalidLine);

        scope.ExpenseCategoryId = document.ExpenseCategoryId;
        var request = new GlPostingRequest(command.CompanyId,
            new GlPostingSource(command.CompanyId, FinanceModuleContracts.ModuleCode, OtherPaymentSource.PaymentType,
                command.PaymentId, command.SourceVersion, command.Fingerprint!),
            document.BusinessDate, baseCurrency!, draft, map.Version);
        var posted = await joined.PostInCurrentTransactionAsync(request, cancellationToken);
        switch (posted.Kind)
        {
            case GlPostingOutcomeKind.Posted when posted.JournalId is > 0:
                break;
            case GlPostingOutcomeKind.Rejected:
                return new(OtherPaymentPostingOutcomeKind.Rejected, false, posted.HttpStatus ?? 500, posted.ErrorCode,
                    InvalidLine: posted.InvalidLine);
            case GlPostingOutcomeKind.Fault:
                return new(OtherPaymentPostingOutcomeKind.Fault, false, posted.HttpStatus ?? 500, posted.ErrorCode);
            default:
                // Unchanged: GL already holds the permanent source key but FI has no source row for it.
                return await FaultAsync(OtherPaymentPostingErrors.InconsistentState);
        }

        return await WriteAsync(command, document, expense.Id, map.Version, baseCurrency!, posted.JournalId.Value,
            cancellationToken);
    }

    private Task<bool> TryLockAsync(string resource, CancellationToken cancellationToken) =>
        PostingServiceSupport.TryLockAsync(db, resource, LockTimeoutMilliseconds, cancellationToken);

    /// <summary>Saved document including soft-deleted rows (a deleted one answers 422, not 404).</summary>
    private Task<OtherPaymentDocument?> ReadDocumentAsync(OtherPaymentPostingCommand command,
        CancellationToken cancellationToken) =>
        db.Set<OtherPaymentDocument>().AsNoTracking().IgnoreQueryFilters([ErpQueryFilters.SoftDelete])
            .SingleOrDefaultAsync(x => x.Id == command.PaymentId && x.CompanyId == command.CompanyId, cancellationToken);

    /// <summary>
    /// FI's permanent source row decides replay under the locks: identical version and fingerprint → Unchanged with the
    /// committed IDs (no GL, document or audit write); another claim → 409. Absent → null (new posting).
    /// </summary>
    private async Task<OtherPaymentPostingResult?> ReplayAsync(OtherPaymentPostingCommand command,
        CancellationToken cancellationToken)
    {
        var saved = await db.Set<OtherPaymentSource>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceType == OtherPaymentSource.PaymentType
                                       && x.SourceId == command.PaymentId, cancellationToken);
        if (saved is null)
            return null;
        if (saved.SourceVersion != command.SourceVersion
            || !string.Equals(saved.Fingerprint, command.Fingerprint, StringComparison.Ordinal))
            return Rejected(409, OtherPaymentPostingErrors.SourceConflict);
        return new(OtherPaymentPostingOutcomeKind.Unchanged, false, 200, Receipt: Receipt(saved));
    }

    private async Task<OtherPaymentPostingResult> WriteAsync(OtherPaymentPostingCommand command,
        OtherPaymentDocument snapshot, long expenseAccountId, int mappingVersion, string baseCurrency, long journalId,
        CancellationToken cancellationToken)
    {
        var row = new OtherPaymentSource
        {
            SourceId = command.PaymentId,
            SourceVersion = command.SourceVersion,
            Fingerprint = command.Fingerprint!,
            GlJournalId = journalId,
            BusinessDate = snapshot.BusinessDate,
            ExpenseCategoryId = snapshot.ExpenseCategoryId,
            ExpenseAccountId = expenseAccountId,
            MappingVersion = mappingVersion,
            BankAccountId = snapshot.BankAccountId,
            BankReference = snapshot.BankReference,
            BaseCurrency = baseCurrency,
            Amount = snapshot.Amount
        };
        try
        {
            db.Set<OtherPaymentSource>().Add(row);
            var document = await db.Set<OtherPaymentDocument>()
                .SingleAsync(x => x.Id == command.PaymentId && x.CompanyId == command.CompanyId, cancellationToken);
            document.Status = OtherPaymentDocumentStatus.Posted;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException or InvalidOperationException
                                       or OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            return await FaultAsync(OtherPaymentPostingErrors.WriteFailed);
        }

        return new(OtherPaymentPostingOutcomeKind.Posted, true, 200, Receipt: Receipt(row));
    }

    private static OtherPaymentPostingReceipt Receipt(OtherPaymentSource row) =>
        new(row.CompanyId, row.SourceId, row.SourceVersion, row.Fingerprint, row.Id, row.GlJournalId, row.BusinessDate,
            row.ExpenseCategoryId, row.ExpenseAccountId, row.MappingVersion, row.BankAccountId, row.BankReference,
            row.BaseCurrency, row.Amount);

    private async Task<OtherPaymentPostingResult> FaultAsync(string code)
    {
        await PostingServiceSupport.RollbackCallerTransactionAsync(db);
        return new(OtherPaymentPostingOutcomeKind.Fault, false, 500, code);
    }

    private static OtherPaymentPostingResult Rejected(int status, string code) =>
        new(OtherPaymentPostingOutcomeKind.Rejected, false, status, code);
}
