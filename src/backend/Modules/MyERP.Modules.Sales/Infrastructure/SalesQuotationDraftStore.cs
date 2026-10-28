using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Infrastructure;

/// <summary>Persists prepared quotation snapshots inside a caller-owned transaction.</summary>
public sealed class SalesQuotationDraftStore(MyErpDbContext db, IClock clock)
{
    public async Task CreateAsync(SalesQuotation draft, CancellationToken token = default)
    {
        RequireTransaction();
        EnsureDraft(draft);
        EnsureCompany(draft);
        foreach (var line in draft.Lines) line.CompanyId = draft.CompanyId;
        db.Add(draft);
        await db.SaveChangesAsync(token);
    }

    public async Task ReplaceAsync(long id, int version, SalesQuotation replacement,
        CancellationToken token = default)
    {
        RequireTransaction();
        EnsureDraft(replacement);
        EnsureCompany(replacement);
        var saved = await LoadAsync(id, version, token);
        saved.Status = SalesQuotationLifecycle.Next(saved.Status, SalesQuotationAction.Edit,
            saved.ExpiryDate, false, clock);
        db.RemoveRange(saved.Lines);
        saved.Lines.Clear();
        foreach (var line in replacement.Lines)
        {
            line.CompanyId = saved.CompanyId;
            saved.Lines.Add(line);
        }
        saved.CustomerId = replacement.CustomerId;
        saved.CustomerCode = replacement.CustomerCode;
        saved.CustomerName = replacement.CustomerName;
        saved.BusinessDate = replacement.BusinessDate;
        saved.ExpiryDate = replacement.ExpiryDate;
        saved.CurrencyCode = replacement.CurrencyCode;
        saved.BaseCurrencyCode = replacement.BaseCurrencyCode;
        saved.ExchangeRate = replacement.ExchangeRate;
        saved.OriginalNet = replacement.OriginalNet;
        saved.OriginalTax = replacement.OriginalTax;
        saved.OriginalGross = replacement.OriginalGross;
        saved.BaseNet = replacement.BaseNet;
        saved.BaseTax = replacement.BaseTax;
        saved.BaseGross = replacement.BaseGross;
        saved.Remark = replacement.Remark;
        db.Entry(saved).Property(x => x.Remark).IsModified = true;
        await db.SaveChangesAsync(token);
    }

    public async Task SoftDeleteAsync(long id, int version, CancellationToken token = default)
    {
        RequireTransaction();
        var saved = await LoadAsync(id, version, token);
        if (saved.Status != SalesQuotationStatus.Draft)
            throw new BusinessRuleException("SAL.QUOTATION.INVALID_STATUS", "Only a Draft quotation can be deleted.");
        saved.IsDeleted = true;
        saved.DeletedAt = clock.LocalNow;
        await db.SaveChangesAsync(token);
    }

    private async Task<SalesQuotation> LoadAsync(long id, int version, CancellationToken token)
    {
        await db.Database.SqlQueryRaw<long>(
            "SELECT [id] AS [Value] FROM dbo.sal_quotation WITH (UPDLOCK, HOLDLOCK) " +
            "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0",
            id, db.CurrentCompanyId ?? 0L).ToListAsync(token);
        var saved = await db.Set<SalesQuotation>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("SAL.QUOTATION.NOT_FOUND", "Sales quotation", id);
        if (version < 1 || saved.Version != version) throw new ConflictException();
        await db.Set<SalesQuotationLine>().FromSqlInterpolated($"""
            SELECT * FROM dbo.sal_quotation_line
            WITH (INDEX(ix_sal_quotation_line_company_id_quotation_id_line_no), FORCESEEK)
            WHERE company_id = {saved.CompanyId} AND quotation_id = {saved.Id}
            """).LoadAsync(token);
        return saved;
    }

    private void EnsureCompany(SalesQuotation draft)
    {
        if (db.CurrentCompanyId is not long companyId || companyId <= 0 || draft.CompanyId != companyId)
            throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current quotation company is required.");
    }

    private static void EnsureDraft(SalesQuotation draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Status != SalesQuotationStatus.Draft || draft.IsDeleted ||
            draft.Lines.Count is < 1 or > 1000)
            throw new BusinessRuleException("SAL.QUOTATION.INVALID_STATUS", "A quotation Draft needs 1 to 1000 lines.");
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller-owned quotation transaction is required.");
    }
}
