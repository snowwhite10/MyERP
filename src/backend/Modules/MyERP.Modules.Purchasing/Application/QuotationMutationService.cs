using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

public sealed record QuotationLineInput(long RfqLineId, decimal UnitPrice, string CurrencyCode, long TaxRateId);
public sealed record QuotationInput(long SupplierId, DateOnly QuoteDate, DateOnly ValidUntil,
    string? Remark, IReadOnlyList<QuotationLineInput>? Lines);

/// <summary>詢價表頭鎖、版本與 MD 公開契約檢查後，原子新增不可變報價修訂。</summary>
public sealed class QuotationMutationService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, ICurrencyLookup currencies, ITaxRateLookup taxes, IClock clock)
{
    public async Task<QuotationDetail> CreateAsync(long rfqId, QuotationInput input, CancellationToken token = default)
    {
        var lines = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var rfq = await LoadRfqAsync(rfqId, token);
            RequireSent(rfq);
            if (!rfq.Invitations.Any(x => x.SupplierId == input.SupplierId))
                throw new BusinessRuleException("PUR.QUOTATION.NOT_INVITED", "Supplier was not invited to this RFQ.");
            if (await db.Set<SupplierQuotation>().AnyAsync(x => x.RfqId == rfqId && x.SupplierId == input.SupplierId, token))
                throw new ConflictException();
            await ValidateLinesAsync(rfq, input.QuoteDate, lines, token);
            var quotation = new SupplierQuotation { CompanyId = rfq.CompanyId, RfqId = rfqId,
                SupplierId = input.SupplierId, CurrentRevisionNo = 1 };
            quotation.Revisions.Add(NewRevision(rfq.CompanyId, 1, input, lines));
            db.Add(quotation);
            await db.SaveChangesAsync(token);
            return Detail(quotation);
        }, token);
    }

    public async Task<QuotationDetail> ReviseAsync(long rfqId, long quotationId, int version,
        QuotationInput input, CancellationToken token = default)
    {
        var lines = Validate(input);
        return await InTransactionAsync(async () =>
        {
            var rfq = await LoadRfqAsync(rfqId, token);
            RequireSent(rfq);
            var quotation = await db.Set<SupplierQuotation>().Include(x => x.Revisions).ThenInclude(x => x.Lines)
                .SingleOrDefaultAsync(x => x.Id == quotationId && x.RfqId == rfqId, token)
                ?? throw new NotFoundException("PUR.QUOTATION.NOT_FOUND", "Supplier quotation", quotationId);
            if (version < 1 || quotation.Version != version) throw new ConflictException();
            if (quotation.SupplierId != input.SupplierId)
                throw new RequestValidationException("supplierId", "Supplier cannot change between revisions.");
            if (quotation.Status != SupplierQuotationStatus.Active)
                throw new BusinessRuleException("PUR.QUOTATION.NOT_EDITABLE", "Only active quotations can be revised.");
            var current = quotation.Revisions.Single(x => x.RevisionNo == quotation.CurrentRevisionNo);
            if (current.ValidUntil < DateOnly.FromDateTime(clock.LocalNow))
                throw new BusinessRuleException("PUR.QUOTATION.EXPIRED", "Expired quotation cannot be revised.");
            await ValidateLinesAsync(rfq, input.QuoteDate, lines, token);
            quotation.CurrentRevisionNo++;
            quotation.Revisions.Add(NewRevision(rfq.CompanyId, quotation.CurrentRevisionNo, input, lines));
            await db.SaveChangesAsync(token);
            return Detail(quotation);
        }, token);
    }

    /// <summary>REQ_F4114.5.4：按版本撤回目前有效報價，保留所有不可變修訂。</summary>
    public async Task<QuotationDetail> WithdrawAsync(long rfqId, long quotationId, int version,
        CancellationToken token = default) => await InTransactionAsync(async () =>
    {
        var rfq = await LoadRfqAsync(rfqId, token);
        RequireSent(rfq);
        var quotation = await db.Set<SupplierQuotation>().Include(x => x.Revisions).ThenInclude(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == quotationId && x.RfqId == rfqId, token)
            ?? throw new NotFoundException("PUR.QUOTATION.NOT_FOUND", "Supplier quotation", quotationId);
        if (version < 1 || quotation.Version != version) throw new ConflictException();
        if (quotation.Status != SupplierQuotationStatus.Active)
            throw new BusinessRuleException("PUR.QUOTATION.NOT_EDITABLE", "Only active quotations can be withdrawn.");
        if (quotation.Revisions.Single(x => x.RevisionNo == quotation.CurrentRevisionNo).ValidUntil
            < DateOnly.FromDateTime(clock.LocalNow))
            throw new BusinessRuleException("PUR.QUOTATION.EXPIRED", "Expired quotation cannot be withdrawn.");
        quotation.Status = SupplierQuotationStatus.Withdrawn;
        await db.SaveChangesAsync(token);
        return Detail(quotation);
    }, token);

    private async Task<Rfq> LoadRfqAsync(long id, CancellationToken token)
    {
        var companyId = access.RequireCompanyId();
        var owner = await authorization.GetAsync(token);
        var scope = owner?.CompanyId == companyId && owner.Has(PurchasingPermissions.QuotationQuote)
            ? owner.FilterFor(PurchasingPermissions.QuotationQuote) : null;
        if (scope is null) throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Quotation permission is required.");
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.pur_rfq WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0", id, companyId).ToListAsync(token);
        IQueryable<Rfq> query = db.Set<Rfq>();
        query = scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
        return await query.Include(x => x.Lines).Include(x => x.Invitations).AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("PUR.RFQ.NOT_FOUND", "Request for quotation", id);
    }

    private static void RequireSent(Rfq rfq)
    {
        if (rfq.Status != RfqStatus.Sent)
            throw new BusinessRuleException("PUR.RFQ.NOT_OPEN", "Only sent, open RFQs accept quotations.");
    }

    private async Task ValidateLinesAsync(Rfq rfq, DateOnly date, IReadOnlyList<QuotationLineInput> lines,
        CancellationToken token)
    {
        if (lines.Count != rfq.Lines.Count || !lines.Select(x => x.RfqLineId).Order()
            .SequenceEqual(rfq.Lines.Select(x => x.Id).Order()))
            throw new BusinessRuleException("PUR.QUOTATION.LINE_MISMATCH", "Quote exactly the lines of this RFQ.");
        foreach (var line in lines)
        {
            var currency = await currencies.FindCurrencyAsync(line.CurrencyCode, token);
            if (currency is null) throw new NotFoundException("MD.CURRENCY.NOT_FOUND", "Currency", line.CurrencyCode);
            if (!currency.IsActive)
                throw new BusinessRuleException("MD.CURRENCY.INACTIVE", "Currency is inactive.");
            await taxes.RequireRateAsync(line.TaxRateId, TaxDirection.Input, date, token);
        }
    }

    private static QuotationRevision NewRevision(long companyId, int no, QuotationInput input,
        IReadOnlyList<QuotationLineInput> lines)
    {
        var revision = new QuotationRevision { CompanyId = companyId, RevisionNo = no,
            QuoteDate = input.QuoteDate, ValidUntil = input.ValidUntil,
            Remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim() };
        foreach (var line in lines)
            revision.Lines.Add(new QuotationLine { CompanyId = companyId, RfqLineId = line.RfqLineId,
                UnitPrice = line.UnitPrice, CurrencyCode = line.CurrencyCode.Trim().ToUpperInvariant(),
                TaxRateId = line.TaxRateId });
        return revision;
    }

    internal static IReadOnlyList<QuotationLineInput> Validate(QuotationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.SupplierId <= 0) Fail("supplierId", "Supplier is required.");
        if (input.QuoteDate == default) Fail("quoteDate", "Quotation date is required.");
        if (input.ValidUntil == default || input.ValidUntil < input.QuoteDate)
            Fail("validUntil", "Valid-until date must not precede quotation date.");
        if (input.Remark?.Trim().Length > 500) Fail("remark", "Remark must not exceed 500 characters.");
        if (input.Lines is not { Count: >= 1 and <= 1000 }) Fail("lines", "Quotation needs 1 to 1000 lines.");
        var lines = input.Lines?.ToArray() ?? [];
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line is null) { Fail($"lines[{i}]", "Line is required."); continue; }
            if (line.RfqLineId <= 0) Fail($"lines[{i}].rfqLineId", "RFQ line is required.");
            if (line.UnitPrice < 0 || line.UnitPrice >= 10_000_000_000_000m ||
                decimal.Round(line.UnitPrice, 6) != line.UnitPrice)
                Fail($"lines[{i}].unitPrice", "Price must be nonnegative, below 10^13, with at most 6 decimals.");
            if (line.CurrencyCode?.Trim().Length != 3 || !line.CurrencyCode.Trim().All(char.IsAsciiLetter))
                Fail($"lines[{i}].currencyCode", "Use a three-letter currency code.");
            if (line.TaxRateId <= 0) Fail($"lines[{i}].taxRateId", "Tax rate is required.");
        }
        if (lines.Where(x => x is not null).Select(x => x.RfqLineId).Distinct().Count() != lines.Length)
            Fail("lines", "RFQ lines must be unique.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
        return lines;
    }

    private QuotationDetail Detail(SupplierQuotation quotation) => new(quotation.Id,
        quotation.RfqId, quotation.SupplierId, quotation.Status, quotation.CurrentRevisionNo,
        quotation.Version, [.. quotation.Revisions.OrderBy(x => x.RevisionNo).Select(r =>
            new QuotationRevisionDetail(r.Id, r.RevisionNo, r.QuoteDate, r.ValidUntil, r.Remark,
                [.. r.Lines.OrderBy(x => x.RfqLineId).Select(x => new QuotationLineDetail(x.Id,
                    x.RfqLineId, x.UnitPrice, x.CurrencyCode, x.TaxRateId))]))],
        quotation.Revisions.Single(x => x.RevisionNo == quotation.CurrentRevisionNo).ValidUntil
            < DateOnly.FromDateTime(clock.LocalNow));

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try { var result = await action(); await tx.CommitAsync(token); return result; }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
                && sql.Number is 2601 or 2627) throw new ConflictException();
            throw;
        }
    }
}
