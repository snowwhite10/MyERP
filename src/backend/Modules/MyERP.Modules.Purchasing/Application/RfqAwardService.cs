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

public sealed record RfqAwardInput(int Version, long RfqLineId, long QuotationId, string? Reason);
public sealed record RfqAwardResult(long Id, long RfqId, long RfqLineId, long QuotationId,
    long QuotationLineId, long SupplierId, string Reason, decimal Quantity, decimal UnitPrice,
    string CurrencyCode, string BaseCurrencyCode, long TaxRateId, decimal TaxRatePercent,
    DateOnly TaxRateFrom, DateOnly? TaxRateTo, decimal ExchangeRate, long? ExchangeRateId,
    DateOnly ExchangeRateFrom, DateOnly? ExchangeRateTo, DateOnly ComparisonDate,
    decimal BaseNet, decimal BaseTax, decimal BaseGross, int RfqVersion);

/// <summary>REQ_F4114.6.2：在詢價行鎖和唯一約束下保存當日可比價格快照。</summary>
public sealed class RfqAwardService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IClock clock, ICurrencyLookup currencies,
    ITaxRateLookup taxes)
{
    public async Task<RfqAwardResult> AwardAsync(long rfqId, RfqAwardInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (input.Version < 1) errors.Add("version", ["Current RFQ version is required."]);
        if (input.RfqLineId <= 0) errors.Add("rfqLineId", ["RFQ line is required."]);
        if (input.QuotationId <= 0) errors.Add("quotationId", ["Quotation is required."]);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 500)
            errors.Add("reason", ["Reason must contain 1 to 500 characters."]);
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        var scope = user?.CompanyId == companyId && user.Has(PurchasingPermissions.RfqAward)
            ? user.FilterFor(PurchasingPermissions.RfqAward) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "RFQ award permission is required.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await db.Database.SqlQueryRaw<long>(
                "SELECT [id] AS [Value] FROM dbo.pur_rfq WITH (UPDLOCK, HOLDLOCK) " +
                "WHERE [id] = {0} AND [company_id] = {1} AND [is_deleted] = 0", rfqId, companyId)
                .ToListAsync(token);
            IQueryable<Rfq> query = db.Set<Rfq>();
            query = scope.EffectiveScope switch
            {
                DataScope.Company => query,
                DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
                DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
                _ => query.Where(x => x.OwnerId == scope.UserId)
            };
            var rfq = await query.SingleOrDefaultAsync(x => x.Id == rfqId, token)
                ?? throw new NotFoundException("PUR.RFQ.NOT_FOUND", "Request for quotation", rfqId);
            if (rfq.Version != input.Version) throw new ConflictException();
            if (rfq.Status != RfqStatus.Sent)
                throw new BusinessRuleException("PUR.RFQ.NOT_OPEN", "Only sent, open RFQs can be awarded.");
            if (await db.Set<RfqAward>().AnyAsync(x => x.RfqLineId == input.RfqLineId, token))
                throw new BusinessRuleException("PUR.RFQ.LINE_ALREADY_AWARDED", "RFQ line is already awarded.");

            // 再查目前 MD 快照；既有比價頁的舊結果不能充當決標證據。
            var comparison = await new QuotationComparisonService(db, access, authorization,
                clock, currencies, taxes).GetAsync(rfqId, PurchasingPermissions.RfqAward, token);
            var line = comparison.Lines.SingleOrDefault(x => x.RfqLineId == input.RfqLineId)
                ?? throw new NotFoundException("PUR.RFQ.LINE_NOT_FOUND", "RFQ line", input.RfqLineId);
            var offer = line.Offers.SingleOrDefault(x => x.QuotationId == input.QuotationId)
                ?? throw new NotFoundException("PUR.QUOTATION.NOT_FOUND", "Supplier quotation", input.QuotationId);
            if (!offer.IsComparable)
                throw new BusinessRuleException(offer.UnavailableReason!, "Selected quotation is not comparable.");
            var award = new RfqAward
            {
                CompanyId = companyId, RfqId = rfqId, RfqLineId = input.RfqLineId,
                QuotationId = offer.QuotationId, QuotationLineId = offer.QuotationLineId,
                SupplierId = offer.SupplierId, Reason = input.Reason!.Trim(),
                Quantity = line.Quantity, UnitPrice = offer.UnitPrice,
                CurrencyCode = offer.CurrencyCode, BaseCurrencyCode = comparison.BaseCurrencyCode,
                TaxRateId = offer.TaxRateId, TaxRatePercent = offer.TaxRatePercent!.Value,
                TaxRateFrom = offer.TaxRateFrom!.Value, TaxRateTo = offer.TaxRateTo,
                ExchangeRate = offer.ExchangeRate!.Value, ExchangeRateId = offer.ExchangeRateId,
                ExchangeRateFrom = offer.ExchangeRateFrom!.Value, ExchangeRateTo = offer.ExchangeRateTo,
                ComparisonDate = comparison.ComparisonDate, BaseNet = offer.BaseNet!.Value,
                BaseTax = offer.BaseTax!.Value, BaseGross = offer.BaseGross!.Value
            };
            var quotation = await db.Set<SupplierQuotation>().SingleAsync(x => x.Id == offer.QuotationId, token);
            quotation.Status = SupplierQuotationStatus.Awarded;
            db.Add(award);
            // 表頭版本隨每次決標推進，避免不同詢價行的同版提交都成功。
            db.Entry(rfq).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new RfqAwardResult(award.Id, rfqId, award.RfqLineId, award.QuotationId,
                award.QuotationLineId, award.SupplierId, award.Reason, award.Quantity, award.UnitPrice,
                award.CurrencyCode, award.BaseCurrencyCode, award.TaxRateId, award.TaxRatePercent,
                award.TaxRateFrom, award.TaxRateTo, award.ExchangeRate, award.ExchangeRateId,
                award.ExchangeRateFrom, award.ExchangeRateTo, award.ComparisonDate,
                award.BaseNet, award.BaseTax, award.BaseGross, rfq.Version);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
                && sql.Number is 2601 or 2627) throw new ConflictException();
            throw;
        }
    }
}
