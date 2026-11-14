using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Finance.Application;

/// <summary>One requested allocation: FI posted receivable ID and the positive base amount to settle.</summary>
public sealed record CollectionSettlementLineInput(long ReceivableId, decimal Amount);

/// <summary>
/// Client-editable settlement fields (REQ_F4116.5.1). <see cref="ReceiptId"/> is the FI receipt document ID of a posted
/// receipt; customer, currency (MD base), total (sum of lines), number, status and actor are server-derived.
/// </summary>
public sealed record CollectionSettlementDocumentInput(long ReceiptId, DateOnly BusinessDate,
    IReadOnlyList<CollectionSettlementLineInput>? Lines, string? Remark);

/// <summary>
/// One saved line in receivable ID order. <see cref="ReceivableOpenAmount"/> is the receivable's current open amount when
/// read (it may change before posting; posting re-checks).
/// </summary>
public sealed record CollectionSettlementDocumentLineDetail(int LineNo, long ReceivableId, decimal Amount,
    decimal ReceivableOpenAmount);

/// <summary>
/// Saved settlement document. <see cref="ReceiptDocumentNo"/> is the receipt document number when the FI receipt document
/// exists; <see cref="ReceiptUnappliedAmount"/> is the posted receipt's current unapplied amount (null if not posted).
/// </summary>
public sealed record CollectionSettlementDocumentDetail(long Id, string DocumentNo, long CustomerId,
    DateOnly BusinessDate, string Currency, long ReceiptId, string? ReceiptDocumentNo, decimal? ReceiptUnappliedAmount,
    decimal TotalAmount, string? Remark, FinanceCollectionWriteOffState Status, int Version, DateTime CreatedAt,
    long CreatedBy, DateTime? UpdatedAt, long? UpdatedBy, string? CustomerCode, string? CustomerName,
    IReadOnlyList<CollectionSettlementDocumentLineDetail> Lines);

public static class CollectionSettlementDocumentErrors
{
    public const string NotFound = "FI.COLLECTION.SETTLEMENT_NOT_FOUND";
    /// <summary>422: only Draft or Rejected settlements are edited or deleted (BR-FI-032).</summary>
    public const string InvalidStatus = "FI.COLLECTION.SETTLEMENT_INVALID_STATUS";
}

/// <summary>
/// WU-FI-005b2 (ADR-127): settlement document Draft create, versioned replace (lines replaced as a whole) and soft
/// delete, and detail read. The receipt must be posted, the receivables must be open invoice receivables of the
/// receipt's customer in the base currency, and the request is checked with the same pure rules and error codes as the
/// write-off posting service (<see cref="CollectionWriteOffPostingRules"/>), so a draft that saves is one posting would
/// accept at that moment; posting re-checks under locks (BR-FI-035/040). Company-wide data scope (ADR-075).
/// </summary>
public sealed class CollectionSettlementDocumentService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, INumberGenerator numbers, ICurrencyLookup currencies,
    ICustomerLabels labels, IClock clock)
{
    public const string DocumentType = "AR.SETTLEMENT";
    public const string ViewPermission = "AR.SETTLEMENT.VIEW";
    public const string CreatePermission = "AR.SETTLEMENT.CREATE";
    public const string EditPermission = "AR.SETTLEMENT.EDIT";
    public const string DeletePermission = "AR.SETTLEMENT.DELETE";
    public const int RemarkMaxLength = 500;
    public const string LineErrorsExtension = "lineErrors";

    public async Task<CollectionSettlementDocumentDetail> CreateAsync(CollectionSettlementDocumentInput input,
        CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(CreatePermission, token);
        var clean = Validate(input);
        long id;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            try
            {
                var (customerId, currency) = await PrepareAsync(companyId, clean, token);
                var document = new CollectionSettlementDocument
                {
                    CompanyId = companyId,
                    DocumentNo = await numbers.NextAsync(DocumentType, clean.BusinessDate, token),
                    Currency = currency,
                };
                Apply(document, clean, customerId, currency);
                db.Add(document);
                await db.SaveChangesAsync(token);
                AddLines(companyId, document.Id, clean.Lines!);
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

    public async Task<CollectionSettlementDocumentDetail> ReplaceAsync(long id, int version,
        CollectionSettlementDocumentInput input, CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(EditPermission, token);
        var clean = Validate(input);
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token))
        {
            try
            {
                var document = await LoadEditableAsync(companyId, id, version, token);
                var (customerId, currency) = await PrepareAsync(companyId, clean, token);
                Apply(document, clean, customerId, currency);
                document.Status = CollectionSettlementDocumentStatus.Draft;
                // 零變更保存仍是一次草稿保存：明確標記，令版本與稽核一致遞增。
                db.Entry(document).Property(x => x.Remark).IsModified = true;
                db.RemoveRange(await db.Set<CollectionSettlementDocumentLine>()
                    .Where(x => x.SettlementDocumentId == id).ToListAsync(token));
                await db.SaveChangesAsync(token);
                AddLines(companyId, id, clean.Lines!);
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

    /// <summary>Soft delete: keeps the number history and the lines (BR-FI-032).</summary>
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

    public async Task<CollectionSettlementDocumentDetail> GetAsync(long id, CancellationToken token = default)
    {
        await AuthorizeAsync(ViewPermission, token);
        return await ReadAsync(id, token);
    }

    /// <summary>
    /// 400 for every invalid field at once (line fields as <c>lines[i].receivableId</c>／<c>lines[i].amount</c> in request
    /// order); returns the input with lines in receivable ID order and a trimmed remark (blank → null).
    /// </summary>
    internal static CollectionSettlementDocumentInput Validate(CollectionSettlementDocumentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        var remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim();
        if (input.ReceiptId <= 0) Fail("receiptId", "Receipt is required.");
        if (input.BusinessDate == default) Fail("businessDate", "Business date is required.");
        if (remark is { Length: > RemarkMaxLength }) Fail("remark", "Remark must be at most 500 characters.");
        var lines = input.Lines ?? [];
        if (lines.Count is 0 or > CollectionWriteOffPostingRules.MaxLines)
            Fail("lines", "A settlement needs 1 to 1000 lines.");
        else
        {
            var seen = new HashSet<long>();
            decimal total = 0m;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line is null)
                {
                    Fail($"lines[{i}]", "Line is required.");
                    continue;
                }
                if (line.ReceivableId <= 0) Fail($"lines[{i}].receivableId", "Receivable is required.");
                else if (!seen.Add(line.ReceivableId))
                    Fail($"lines[{i}].receivableId", "Each receivable may appear only once.");
                if (line.Amount <= 0m || line.Amount > CollectionReceiptDocumentService.MaxAmount
                    || decimal.Round(line.Amount, 4) != line.Amount)
                    Fail($"lines[{i}].amount",
                        "Amount must be greater than 0 and at most 999,999,999,999,999.9999 with at most 4 decimals.");
                else
                    total += line.Amount;
            }
            if (total > CollectionReceiptDocumentService.MaxAmount)
                Fail("lines", "The settlement total must be at most 999,999,999,999,999.9999.");
        }
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return input with { Lines = lines.OrderBy(x => x.ReceivableId).ToArray(), Remark = remark };
    }

    private Task<long> AuthorizeAsync(string permission, CancellationToken token) =>
        FinanceAccess.RequireAsync(currentCompany, authorization, permission, token);

    /// <summary>
    /// MD base currency (422), then the posted receipt and every line through the posting rules (same codes as posting):
    /// receipt not posted / other customer, currency or later date / insufficient unapplied (422), and all bad lines at
    /// once in one 422 <see cref="CollectionWriteOffPostingErrors.AllocationInvalid"/> with <c>lineErrors</c>.
    /// Returns the receipt's customer and the base currency.
    /// </summary>
    internal async Task<(long CustomerId, string Currency)> PrepareAsync(long companyId,
        CollectionSettlementDocumentInput input, CancellationToken token)
    {
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(token))?.Code
            ?? throw new BusinessRuleException(CollectionReceiptDocumentErrors.BaseCurrencyMissing,
                "The company base currency is not set.");
        var receipt = await db.Set<CollectionReceipt>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SourceType == CollectionReceipt.SalesCollectionType
                        && x.SourceId == input.ReceiptId)
            .Select(x => new CollectionWriteOffReceiptBalance(x.CustomerId, x.BaseCurrency, x.BusinessDate,
                x.UnappliedAmount))
            .SingleOrDefaultAsync(token);
        var lines = input.Lines!;
        var snapshot = new FinanceCollectionWriteOffSnapshot(companyId, 0, 1, "", receipt?.CustomerId ?? 0,
            FinanceCollectionWriteOffState.Draft, input.BusinessDate, baseCurrency, baseCurrency, input.ReceiptId,
            lines.Sum(x => x.Amount), lines.Select(x => new FinanceCollectionWriteOffLine(x.ReceivableId, x.Amount)).ToArray());
        if (CollectionWriteOffPostingRules.CheckReceipt(snapshot, receipt) is { } bad)
            throw new BusinessRuleException(bad.Code, ReceiptMessage(bad.Code));

        var ids = lines.Select(x => x.ReceivableId).ToArray();
        var receivables = await db.Set<PostedReceivable>().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .Select(x => new CollectionWriteOffReceivable(x.Id, x.CustomerId, x.SourceType, x.BaseCurrency,
                x.OpenBaseAmount, x.PostingDate))
            .ToDictionaryAsync(x => x.Id, token);
        var lineErrors = CollectionWriteOffPostingRules.CheckLines(snapshot, receivables);
        if (lineErrors.Count > 0)
            throw new BusinessRuleException(CollectionWriteOffPostingErrors.AllocationInvalid,
                "One or more lines cannot be settled.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { [LineErrorsExtension] = lineErrors }
            };
        return (receipt!.CustomerId, baseCurrency);
    }

    private static string ReceiptMessage(string code) => code switch
    {
        CollectionWriteOffPostingErrors.ReceiptNotPosted => "The receipt is not posted in the current company.",
        CollectionWriteOffPostingErrors.ReceiptInsufficient => "The settlement total exceeds the receipt's unapplied amount.",
        _ => "The receipt belongs to another customer or currency, or is dated after the settlement.",
    };

    private static void Apply(CollectionSettlementDocument document, CollectionSettlementDocumentInput input,
        long customerId, string currency)
    {
        document.CustomerId = customerId;
        document.BusinessDate = input.BusinessDate;
        document.Currency = currency;
        document.ReceiptId = input.ReceiptId;
        document.TotalAmount = input.Lines!.Sum(x => x.Amount);
        document.Remark = input.Remark;
    }

    private void AddLines(long companyId, long documentId, IReadOnlyList<CollectionSettlementLineInput> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            db.Add(new CollectionSettlementDocumentLine
            {
                CompanyId = companyId,
                SettlementDocumentId = documentId,
                LineNo = i + 1,
                ReceivableId = lines[i].ReceivableId,
                Amount = lines[i].Amount,
            });
        }
    }

    /// <summary>Locks the live row first, then 404 → 409 (version) → 422 (status).</summary>
    private async Task<CollectionSettlementDocument> LoadEditableAsync(long companyId, long id, int version,
        CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.ar_collection_settlement_doc WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, companyId).ToListAsync(token);
        var document = await db.Set<CollectionSettlementDocument>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(CollectionSettlementDocumentErrors.NotFound, "Collection settlement", id);
        if (version < 1 || document.Version != version) throw new ConflictException();
        if (document.Status is not (CollectionSettlementDocumentStatus.Draft or CollectionSettlementDocumentStatus.Rejected))
            throw new BusinessRuleException(CollectionSettlementDocumentErrors.InvalidStatus,
                "Only a Draft or Rejected settlement can be changed or deleted.");
        return document;
    }

    private static Exception Translate(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException => new ConflictException(),
        _ => ex,
    };

    private async Task<CollectionSettlementDocumentDetail> ReadAsync(long id, CancellationToken token)
    {
        var x = await db.Set<CollectionSettlementDocument>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, token)
            ?? throw new NotFoundException(CollectionSettlementDocumentErrors.NotFound, "Collection settlement", id);
        var lines = await (
                from line in db.Set<CollectionSettlementDocumentLine>().AsNoTracking()
                join receivable in db.Set<PostedReceivable>().AsNoTracking()
                    on new { line.CompanyId, Id = line.ReceivableId } equals new { receivable.CompanyId, receivable.Id }
                where line.SettlementDocumentId == id
                orderby line.LineNo
                select new CollectionSettlementDocumentLineDetail(line.LineNo, line.ReceivableId, line.Amount,
                    receivable.OpenBaseAmount))
            .ToListAsync(token);
        var receiptNo = await db.Set<CollectionReceiptDocument>().AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.CompanyId == x.CompanyId && r.Id == x.ReceiptId).Select(r => r.DocumentNo)
            .SingleOrDefaultAsync(token);
        var unapplied = await db.Set<CollectionReceipt>().AsNoTracking()
            .Where(r => r.SourceType == CollectionReceipt.SalesCollectionType && r.SourceId == x.ReceiptId)
            .Select(r => (decimal?)r.UnappliedAmount).SingleOrDefaultAsync(token);
        var customer = (await labels.GetLabelsAsync([x.CustomerId], token)).GetValueOrDefault(x.CustomerId);
        return new(x.Id, x.DocumentNo, x.CustomerId, x.BusinessDate, x.Currency, x.ReceiptId, receiptNo, unapplied,
            x.TotalAmount, x.Remark, ToState(x.Status), x.Version, x.CreatedAt, x.CreatedBy, x.UpdatedAt, x.UpdatedBy,
            customer?.Code, customer?.Name, lines);
    }

    /// <summary>Saved status → public state; a deleted row is never read here (query filter), see 005b3 source port.</summary>
    internal static FinanceCollectionWriteOffState ToState(CollectionSettlementDocumentStatus status) => status switch
    {
        CollectionSettlementDocumentStatus.Draft => FinanceCollectionWriteOffState.Draft,
        CollectionSettlementDocumentStatus.Pending => FinanceCollectionWriteOffState.Pending,
        CollectionSettlementDocumentStatus.Rejected => FinanceCollectionWriteOffState.Rejected,
        CollectionSettlementDocumentStatus.Approved => FinanceCollectionWriteOffState.Approved,
        CollectionSettlementDocumentStatus.Posted => FinanceCollectionWriteOffState.Posted,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown settlement status."),
    };
}
