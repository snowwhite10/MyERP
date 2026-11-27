using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Client-editable other payment fields (REQ_F4116.4.2, BR-FI-059). Company, currency (MD base), number, status and
/// actor are server-derived; category and bank are MD IDs of the current company. There is no supplier, invoice or
/// payable target: the document is a non-AP expense.
/// </summary>
public sealed record OtherPaymentDocumentInput(string? PayeeDescription, long ExpenseCategoryId, long BankAccountId,
    string? BankReference, DateOnly BusinessDate, decimal Amount, string? Remark);

/// <summary>
/// Saved other payment Draft. Category and fund account labels come from MD contracts and are null when MD no longer
/// has a current-company record (ADR-077). <see cref="JournalId"/> is set once a Posted source exists (WU-FI-008a4).
/// </summary>
public sealed record OtherPaymentDocumentDetail(long Id, string DocumentNo, string PayeeDescription,
    long ExpenseCategoryId, string? ExpenseCategoryCode, string? ExpenseCategoryName, long BankAccountId,
    string? BankAccountCode, string? BankAccountName, string BankReference, DateOnly BusinessDate, string Currency,
    decimal Amount, string? Remark, OtherPaymentDocumentStatus Status, int Version, DateTime CreatedAt, long CreatedBy,
    DateTime? UpdatedAt, long? UpdatedBy, long? JournalId);

public static class OtherPaymentDocumentErrors
{
    public const string NotFound = "FI.OTHER_PAYMENT.NOT_FOUND";
    /// <summary>422: only Draft or Rejected documents are edited or deleted (BR-FI-064).</summary>
    public const string InvalidStatus = "FI.OTHER_PAYMENT.INVALID_STATUS";
    /// <summary>409: a live Draft or a Posted source (including released Draft history) uses the reference (BR-FI-062).</summary>
    public const string BankReferenceDuplicate = "FI.OTHER_PAYMENT.BANK_REFERENCE_DUPLICATE";
    public const string ExpenseCategoryNotFound = "FI.OTHER_PAYMENT.EXPENSE_CATEGORY_NOT_FOUND";
    public const string ExpenseCategoryInactive = "FI.OTHER_PAYMENT.EXPENSE_CATEGORY_INACTIVE";
    /// <summary>422: an Income category cannot classify a payment (BR-FI-059/060).</summary>
    public const string ExpenseCategoryNotExpense = "FI.OTHER_PAYMENT.EXPENSE_CATEGORY_NOT_EXPENSE";
    public const string FundAccountNotFound = "FI.OTHER_PAYMENT.FUND_ACCOUNT_NOT_FOUND";
    public const string FundAccountInactive = "FI.OTHER_PAYMENT.FUND_ACCOUNT_INACTIVE";
    /// <summary>422: other payments are base-currency only (BR-FI-059, BR-FI-066 excludes FX).</summary>
    public const string FundAccountCurrencyMismatch = "FI.OTHER_PAYMENT.FUND_ACCOUNT_CURRENCY_MISMATCH";
    public const string BaseCurrencyMissing = "FI.OTHER_PAYMENT.BASE_CURRENCY_MISSING";
}

/// <summary>
/// WU-FI-008a3: other payment Draft create, versioned replace, soft delete and detail read (BR-FI-059/062/064).
/// Edit and delete accept Draft or Rejected only; an edited Rejected document returns to Draft. Company-wide data
/// scope like other FI documents (ADR-075). Writes lock the row (UPDLOCK) before the version check. The withdrawal
/// reference is checked against live Drafts and the permanent Posted source; the filtered unique index (2601/2627)
/// and the Draft trigger (SQL 51039) still decide concurrent writes and map to the same named 409. Creates no GL,
/// AP payable, allocation or balance; submission and posting come in WU-FI-008a4/008b.
/// </summary>
public sealed class OtherPaymentDocumentService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, INumberGenerator numbers, ICurrencyLookup currencies,
    IFundAccountLookup funds, IIncomeExpenseCategoryLookup categories, IClock clock)
{
    public const string DocumentType = "AP.OTHER_PAYMENT";
    public const string ViewPermission = "AP.OTHER_PAYMENT.VIEW";
    public const string CreatePermission = "AP.OTHER_PAYMENT.CREATE";
    public const string EditPermission = "AP.OTHER_PAYMENT.EDIT";
    public const string DeletePermission = "AP.OTHER_PAYMENT.DELETE";
    /// <summary>Submit/withdraw (<see cref="OtherPaymentSubmissionService"/>, WU-FI-008b2); registered in 008b1.</summary>
    public const string SubmitPermission = "AP.OTHER_PAYMENT.SUBMIT";
    public const int PayeeMaxLength = 200;
    public const int RemarkMaxLength = 500;
    /// <summary>SQL error raised by <c>tr_ap_other_payment_doc_posted_reference</c> (WU-FI-008a2).</summary>
    internal const int PostedReferenceGuardError = 51039;

    public async Task<OtherPaymentDocumentDetail> CreateAsync(OtherPaymentDocumentInput input,
        CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(CreatePermission, token);
        var clean = Validate(input);
        var currency = await PrepareAsync(clean, token);
        long id;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            try
            {
                await EnsureReferenceFreeAsync(companyId, clean, null, token);
                var document = new OtherPaymentDocument
                {
                    CompanyId = companyId,
                    DocumentNo = await numbers.NextAsync(DocumentType, clean.BusinessDate, token),
                    Currency = currency,
                    PayeeDescription = clean.PayeeDescription!,
                    BankReference = clean.BankReference!,
                };
                Apply(document, clean);
                db.Add(document);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                id = document.Id;
            }
            catch (Exception ex)
            {
                await RollbackAsync(transaction);
                throw Translate(ex);
            }
        }
        return await ReadAsync(id, token);
    }

    public async Task<OtherPaymentDocumentDetail> ReplaceAsync(long id, int version, OtherPaymentDocumentInput input,
        CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(EditPermission, token);
        var clean = Validate(input);
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            try
            {
                var document = await LoadEditableAsync(companyId, id, version, token);
                var currency = await PrepareAsync(clean, token);
                await EnsureReferenceFreeAsync(companyId, clean, id, token);
                Apply(document, clean);
                document.Currency = currency;
                document.Status = OtherPaymentDocumentStatus.Draft;
                // A zero-change save is still a Draft save: mark it so version and audit advance consistently.
                db.Entry(document).Property(x => x.Remark).IsModified = true;
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            catch (Exception ex)
            {
                await RollbackAsync(transaction);
                throw Translate(ex);
            }
        }
        return await ReadAsync(id, token);
    }

    /// <summary>Soft delete: keeps the number history and releases the live reference (BR-FI-064).</summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(DeletePermission, token);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var document = await LoadEditableAsync(companyId, id, version, token);
            document.IsDeleted = true;
            document.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (Exception ex)
        {
            await RollbackAsync(transaction);
            throw Translate(ex);
        }
    }

    public async Task<OtherPaymentDocumentDetail> GetAsync(long id, CancellationToken token = default)
    {
        await AuthorizeAsync(ViewPermission, token);
        return await ReadAsync(id, token);
    }

    /// <summary>400 for every invalid field at once; returns the input with trimmed text (blank remark → null).</summary>
    internal static OtherPaymentDocumentInput Validate(OtherPaymentDocumentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        var payee = input.PayeeDescription?.Trim();
        var reference = input.BankReference?.Trim();
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (string.IsNullOrEmpty(payee) || payee.Length > PayeeMaxLength || payee.Any(char.IsControl))
            Fail("payeeDescription", "Payee is required, at most 200 characters, without control characters.");
        if (input.ExpenseCategoryId <= 0) Fail("expenseCategoryId", "Expense category is required.");
        if (input.BankAccountId <= 0) Fail("bankAccountId", "Fund account is required.");
        if (!CollectionReceiptPostingRules.IsCanonicalBankReference(reference))
            Fail("bankReference", "Withdrawal reference is required, at most 50 characters, without control characters.");
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (input.Amount <= 0m || input.Amount > CollectionReceiptDocumentService.MaxAmount
            || decimal.Round(input.Amount, 4) != input.Amount)
            Fail("amount", "Amount must be greater than 0 and at most 999,999,999,999,999.9999 with at most 4 decimals.");
        if (remark is { Length: > RemarkMaxLength }) Fail("remark", "Remark must be at most 500 characters.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return input with { PayeeDescription = payee, BankReference = reference, Remark = remark };
    }

    private Task<long> AuthorizeAsync(string permission, CancellationToken token) =>
        FinanceAccess.RequireAsync(currentCompany, authorization, permission, token);

    /// <summary>
    /// MD checks through public contracts (ADR-041): base currency, active Expense category and active base-currency
    /// fund account of the current company. Foreign-company IDs look missing (422, not a leak of existence).
    /// </summary>
    internal async Task<string> PrepareAsync(OtherPaymentDocumentInput input, CancellationToken token)
    {
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(token))?.Code
            ?? throw new BusinessRuleException(OtherPaymentDocumentErrors.BaseCurrencyMissing,
                "The company base currency is not set.");
        var category = await categories.FindByIdAsync(input.ExpenseCategoryId, token)
            ?? throw new BusinessRuleException(OtherPaymentDocumentErrors.ExpenseCategoryNotFound,
                "The expense category does not exist in the current company.");
        if (category.Direction != IncomeExpenseDirection.Expense)
            throw new BusinessRuleException(OtherPaymentDocumentErrors.ExpenseCategoryNotExpense,
                "Choose an expense category; income categories cannot classify a payment.");
        if (!category.IsActive)
            throw new BusinessRuleException(OtherPaymentDocumentErrors.ExpenseCategoryInactive,
                "The expense category is inactive.");
        var fund = await funds.FindByIdAsync(input.BankAccountId, token)
            ?? throw new BusinessRuleException(OtherPaymentDocumentErrors.FundAccountNotFound,
                "The fund account does not exist in the current company.");
        if (!fund.IsActive)
            throw new BusinessRuleException(OtherPaymentDocumentErrors.FundAccountInactive,
                "The fund account is inactive.");
        if (!string.Equals(fund.CurrencyCode, baseCurrency, StringComparison.Ordinal))
            throw new BusinessRuleException(OtherPaymentDocumentErrors.FundAccountCurrencyMismatch,
                "Other payments are recorded in the company base currency; choose a fund account in that currency.");
        return baseCurrency;
    }

    private static void Apply(OtherPaymentDocument document, OtherPaymentDocumentInput input)
    {
        document.PayeeDescription = input.PayeeDescription!;
        document.ExpenseCategoryId = input.ExpenseCategoryId;
        document.BankAccountId = input.BankAccountId;
        document.BankReference = input.BankReference!;
        document.BusinessDate = input.BusinessDate;
        document.Amount = input.Amount;
        document.Remark = input.Remark;
    }

    /// <summary>Locks the live row first, then 404 → 409 (version) → 422 (status).</summary>
    private async Task<OtherPaymentDocument> LoadEditableAsync(long companyId, long id, int version,
        CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.ap_other_payment_doc WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, companyId).ToListAsync(token);
        var document = await db.Set<OtherPaymentDocument>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(OtherPaymentDocumentErrors.NotFound, "Other payment", id);
        if (version < 1 || document.Version != version) throw new ConflictException();
        if (document.Status is not (OtherPaymentDocumentStatus.Draft or OtherPaymentDocumentStatus.Rejected))
            throw new BusinessRuleException(OtherPaymentDocumentErrors.InvalidStatus,
                "Only a Draft or Rejected other payment can be changed or deleted.");
        return document;
    }

    /// <summary>
    /// Friendly 409 before the write: another live Draft or a Posted source of the same company/bank uses the
    /// reference (SQL default collation compares case-insensitively, like the index and trigger).
    /// </summary>
    private async Task EnsureReferenceFreeAsync(long companyId, OtherPaymentDocumentInput input, long? selfId,
        CancellationToken token)
    {
        var reference = input.BankReference!;
        var used = await db.Set<OtherPaymentDocument>().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.BankAccountId == input.BankAccountId && x.BankReference == reference
                && (selfId == null || x.Id != selfId), token)
            || await db.Set<OtherPaymentSource>().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.BankAccountId == input.BankAccountId && x.BankReference == reference, token);
        if (used) throw ReferenceConflict();
    }

    private static ConflictException ReferenceConflict() =>
        new(OtherPaymentDocumentErrors.BankReferenceDuplicate,
            "This withdrawal reference is already used for the fund account.");

    /// <summary>
    /// A trigger THROW dooms the SQL transaction; rolling back a server-completed transaction must not hide the
    /// original error.
    /// </summary>
    private async Task RollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Already rolled back by SQL Server (XACT_ABORT inside the trigger).
        }
        db.ChangeTracker.Clear();
    }

    internal static Exception Translate(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => new ConflictException(),
        DbUpdateException { InnerException: SqlException { Number: PostedReferenceGuardError } } => ReferenceConflict(),
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } sql }
            when sql.Message.Contains("bank_reference", StringComparison.OrdinalIgnoreCase) => ReferenceConflict(),
        _ => ex,
    };

    private async Task<OtherPaymentDocumentDetail> ReadAsync(long id, CancellationToken token)
    {
        var x = await db.Set<OtherPaymentDocument>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, token)
            ?? throw new NotFoundException(OtherPaymentDocumentErrors.NotFound, "Other payment", id);
        var category = await categories.FindByIdAsync(x.ExpenseCategoryId, token);
        var fund = await funds.FindByIdAsync(x.BankAccountId, token);
        var journalId = x.Status == OtherPaymentDocumentStatus.Posted
            ? await db.Set<OtherPaymentSource>().AsNoTracking()
                .Where(s => s.SourceType == OtherPaymentSource.PaymentType && s.SourceId == x.Id)
                .Select(s => (long?)s.GlJournalId).SingleOrDefaultAsync(token)
            : null;
        return new(x.Id, x.DocumentNo, x.PayeeDescription, x.ExpenseCategoryId, category?.Code, category?.Name,
            x.BankAccountId, fund?.Code, fund?.Name, x.BankReference, x.BusinessDate, x.Currency, x.Amount, x.Remark,
            x.Status, x.Version, x.CreatedAt, x.CreatedBy, x.UpdatedAt, x.UpdatedBy, journalId);
    }
}
