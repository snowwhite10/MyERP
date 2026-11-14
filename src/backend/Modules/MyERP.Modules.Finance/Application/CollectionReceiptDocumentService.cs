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
/// Client-editable receipt fields (REQ_F4116.1.1). Company, currency (MD base), number, status and actor are
/// server-derived; <see cref="BankAccountId"/> is an MD fund account ID of the current company.
/// </summary>
public sealed record CollectionReceiptDocumentInput(long CustomerId, DateOnly BusinessDate, decimal Amount,
    long BankAccountId, string? BankReference, string? Remark);

/// <summary>
/// Saved receipt document. Customer and fund account labels come from MD contracts and are null when MD has no
/// current-company record (ADR-077). <see cref="UnappliedAmount"/> and <see cref="JournalId"/> are set once Posted
/// (FI posted receipt row, WU-FI-005b4a).
/// </summary>
public sealed record CollectionReceiptDocumentDetail(long Id, string DocumentNo, long CustomerId, DateOnly BusinessDate,
    string Currency, decimal Amount, long BankAccountId, string BankReference, string? Remark,
    FinanceCollectionReceiptState Status, int Version, DateTime CreatedAt, long CreatedBy, DateTime? UpdatedAt,
    long? UpdatedBy, string? CustomerCode, string? CustomerName, string? BankAccountCode, string? BankAccountName,
    decimal? UnappliedAmount = null, long? JournalId = null);

public static class CollectionReceiptDocumentErrors
{
    public const string NotFound = "FI.COLLECTION.RECEIPT_NOT_FOUND";
    /// <summary>422: only Draft or Rejected receipts are edited or deleted (BR-FI-032).</summary>
    public const string InvalidStatus = "FI.COLLECTION.RECEIPT_INVALID_STATUS";
    /// <summary>409: a live receipt document or a posted receipt already uses the bank reference (BR-FI-033).</summary>
    public const string BankReferenceDuplicate = "FI.COLLECTION.BANK_REFERENCE_DUPLICATE";
    public const string FundAccountNotFound = "FI.COLLECTION.FUND_ACCOUNT_NOT_FOUND";
    public const string FundAccountInactive = "FI.COLLECTION.FUND_ACCOUNT_INACTIVE";
    /// <summary>422: receipts are base-currency only (BR-FI-031); the fund account must hold the base currency.</summary>
    public const string FundAccountCurrencyMismatch = "FI.COLLECTION.FUND_ACCOUNT_CURRENCY_MISMATCH";
    public const string BaseCurrencyMissing = "FI.COLLECTION.BASE_CURRENCY_MISSING";
}

/// <summary>
/// WU-FI-005b1 (ADR-126): receipt document Draft create, versioned replace and soft delete, and detail read. Edit and
/// delete accept Draft or Rejected only; an edited Rejected receipt returns to Draft (BR-FI-032). Company-wide data
/// scope like other FI configuration (ADR-075). Writes lock the row (UPDLOCK) before the version check; a filtered
/// unique index guards concurrent duplicate bank references. Submission and approval come in WU-FI-005b3.
/// </summary>
public sealed class CollectionReceiptDocumentService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, INumberGenerator numbers, ICustomerLookup customers,
    ICurrencyLookup currencies, IFundAccountLookup funds, ICustomerLabels labels, IClock clock)
{
    public const string DocumentType = "AR.COLLECTION";
    public const string ViewPermission = "AR.COLLECTION.VIEW";
    public const string CreatePermission = "AR.COLLECTION.CREATE";
    public const string EditPermission = "AR.COLLECTION.EDIT";
    public const string DeletePermission = "AR.COLLECTION.DELETE";
    public const int RemarkMaxLength = 500;
    internal const decimal MaxAmount = 999_999_999_999_999.9999m;

    public async Task<CollectionReceiptDocumentDetail> CreateAsync(CollectionReceiptDocumentInput input,
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
                var document = new CollectionReceiptDocument
                {
                    CompanyId = companyId,
                    DocumentNo = await numbers.NextAsync(DocumentType, clean.BusinessDate, token),
                    Currency = currency,
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
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw Translate(ex);
            }
        }
        return await ReadAsync(id, token);
    }

    public async Task<CollectionReceiptDocumentDetail> ReplaceAsync(long id, int version,
        CollectionReceiptDocumentInput input, CancellationToken token = default)
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
                document.Status = CollectionReceiptDocumentStatus.Draft;
                // 零變更保存仍是一次草稿保存：明確標記，令版本與稽核一致遞增。
                db.Entry(document).Property(x => x.Remark).IsModified = true;
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                throw Translate(ex);
            }
        }
        return await ReadAsync(id, token);
    }

    /// <summary>Soft delete: keeps the number history and releases the bank reference (BR-FI-032/033).</summary>
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
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw Translate(ex);
        }
    }

    public async Task<CollectionReceiptDocumentDetail> GetAsync(long id, CancellationToken token = default)
    {
        await AuthorizeAsync(ViewPermission, token);
        return await ReadAsync(id, token);
    }

    /// <summary>
    /// 400 for every invalid field at once; returns the input with trimmed reference and remark (blank remark → null).
    /// </summary>
    internal static CollectionReceiptDocumentInput Validate(CollectionReceiptDocumentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        var reference = input.BankReference?.Trim();
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (input.CustomerId <= 0) Fail("customerId", "Customer is required.");
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (input.Amount <= 0m || input.Amount > MaxAmount || decimal.Round(input.Amount, 4) != input.Amount)
            Fail("amount", "Amount must be greater than 0 and at most 999,999,999,999,999.9999 with at most 4 decimals.");
        if (input.BankAccountId <= 0) Fail("bankAccountId", "Fund account is required.");
        if (!CollectionReceiptPostingRules.IsCanonicalBankReference(reference))
            Fail("bankReference", "Bank reference is required, at most 50 characters, without control characters.");
        if (remark is { Length: > RemarkMaxLength }) Fail("remark", "Remark must be at most 500 characters.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return input with { BankReference = reference, Remark = remark };
    }

    private Task<long> AuthorizeAsync(string permission, CancellationToken token) =>
        FinanceAccess.RequireAsync(currentCompany, authorization, permission, token);

    /// <summary>MD checks through public contracts: base currency, approved customer, active base-currency fund account.</summary>
    internal async Task<string> PrepareAsync(CollectionReceiptDocumentInput input, CancellationToken token)
    {
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(token))?.Code
            ?? throw new BusinessRuleException(CollectionReceiptDocumentErrors.BaseCurrencyMissing,
                "The company base currency is not set.");
        await customers.RequireApprovedAsync(input.CustomerId, token);
        var fund = await funds.FindByIdAsync(input.BankAccountId, token)
            ?? throw new BusinessRuleException(CollectionReceiptDocumentErrors.FundAccountNotFound,
                "The fund account does not exist in the current company.");
        if (!fund.IsActive)
            throw new BusinessRuleException(CollectionReceiptDocumentErrors.FundAccountInactive,
                "The fund account is inactive.");
        if (!string.Equals(fund.CurrencyCode, baseCurrency, StringComparison.Ordinal))
            throw new BusinessRuleException(CollectionReceiptDocumentErrors.FundAccountCurrencyMismatch,
                "Receipts are recorded in the company base currency; choose a fund account in that currency.");
        return baseCurrency;
    }

    private static void Apply(CollectionReceiptDocument document, CollectionReceiptDocumentInput input)
    {
        document.CustomerId = input.CustomerId;
        document.BusinessDate = input.BusinessDate;
        document.Amount = input.Amount;
        document.BankAccountId = input.BankAccountId;
        document.BankReference = input.BankReference!;
        document.Remark = input.Remark;
    }

    /// <summary>Locks the live row first, then 404 → 409 (version) → 422 (status).</summary>
    private async Task<CollectionReceiptDocument> LoadEditableAsync(long companyId, long id, int version,
        CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.ar_collection_receipt_doc WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, companyId).ToListAsync(token);
        var document = await db.Set<CollectionReceiptDocument>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(CollectionReceiptDocumentErrors.NotFound, "Collection receipt", id);
        if (version < 1 || document.Version != version) throw new ConflictException();
        if (document.Status is not (CollectionReceiptDocumentStatus.Draft or CollectionReceiptDocumentStatus.Rejected))
            throw new BusinessRuleException(CollectionReceiptDocumentErrors.InvalidStatus,
                "Only a Draft or Rejected receipt can be changed or deleted.");
        return document;
    }

    /// <summary>
    /// Friendly 409 before the write: another live document or a posted receipt uses the reference (SQL default
    /// collation compares case-insensitively). The filtered unique index still decides concurrent inserts.
    /// </summary>
    private async Task EnsureReferenceFreeAsync(long companyId, CollectionReceiptDocumentInput input, long? selfId,
        CancellationToken token)
    {
        var reference = input.BankReference!;
        var used = await db.Set<CollectionReceiptDocument>().AsNoTracking().AnyAsync(x =>
                x.BankAccountId == input.BankAccountId && x.BankReference == reference
                && (selfId == null || x.Id != selfId), token)
            || await db.Set<CollectionReceipt>().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.BankAccountId == input.BankAccountId && x.BankReference == reference, token);
        if (used)
            throw new ConflictException(CollectionReceiptDocumentErrors.BankReferenceDuplicate,
                "This bank reference is already used for the fund account.");
    }

    private static Exception Translate(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => new ConflictException(),
        DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } sql }
            when sql.Message.Contains("bank_reference", StringComparison.OrdinalIgnoreCase) =>
            new ConflictException(CollectionReceiptDocumentErrors.BankReferenceDuplicate,
                "This bank reference is already used for the fund account."),
        _ => ex,
    };

    private async Task<CollectionReceiptDocumentDetail> ReadAsync(long id, CancellationToken token)
    {
        var x = await db.Set<CollectionReceiptDocument>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, token)
            ?? throw new NotFoundException(CollectionReceiptDocumentErrors.NotFound, "Collection receipt", id);
        var customer = (await labels.GetLabelsAsync([x.CustomerId], token)).GetValueOrDefault(x.CustomerId);
        var fund = await funds.FindByIdAsync(x.BankAccountId, token);
        var posted = x.Status == CollectionReceiptDocumentStatus.Posted
            ? await db.Set<CollectionReceipt>().AsNoTracking()
                .Where(r => r.SourceType == CollectionReceipt.SalesCollectionType && r.SourceId == x.Id)
                .Select(r => new { r.UnappliedAmount, r.GlJournalId }).SingleOrDefaultAsync(token)
            : null;
        return new(x.Id, x.DocumentNo, x.CustomerId, x.BusinessDate, x.Currency, x.Amount, x.BankAccountId,
            x.BankReference, x.Remark, ToState(x.Status), x.Version, x.CreatedAt, x.CreatedBy, x.UpdatedAt, x.UpdatedBy,
            customer?.Code, customer?.Name, fund?.Code, fund?.Name, posted?.UnappliedAmount, posted?.GlJournalId);
    }

    /// <summary>Saved status → public state; a deleted row is never read here (query filter), see 005b3 source port.</summary>
    internal static FinanceCollectionReceiptState ToState(CollectionReceiptDocumentStatus status) => status switch
    {
        CollectionReceiptDocumentStatus.Draft => FinanceCollectionReceiptState.Draft,
        CollectionReceiptDocumentStatus.Pending => FinanceCollectionReceiptState.Pending,
        CollectionReceiptDocumentStatus.Rejected => FinanceCollectionReceiptState.Rejected,
        CollectionReceiptDocumentStatus.Approved => FinanceCollectionReceiptState.Approved,
        CollectionReceiptDocumentStatus.Posted => FinanceCollectionReceiptState.Posted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown receipt status."),
    };
}
