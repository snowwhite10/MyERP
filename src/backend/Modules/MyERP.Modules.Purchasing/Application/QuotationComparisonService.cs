using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Monetary;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

public sealed record ComparisonOffer(long QuotationId, long SupplierId, long QuotationLineId,
    decimal UnitPrice, string CurrencyCode, long TaxRateId, bool IsComparable,
    string? UnavailableReason, decimal? ExchangeRate, long? ExchangeRateId,
    DateOnly? ExchangeRateFrom, DateOnly? ExchangeRateTo, decimal? TaxRatePercent,
    DateOnly? TaxRateFrom, DateOnly? TaxRateTo, decimal? BaseNet, decimal? BaseTax,
    decimal? BaseGross, SupplierQuotationStatus Status);
public sealed record ComparisonLine(long RfqLineId, int LineNo, decimal Quantity,
    IReadOnlyList<ComparisonOffer> Offers);
public sealed record QuotationComparison(long RfqId, DateOnly ComparisonDate,
    string BaseCurrencyCode, IReadOnlyList<ComparisonLine> Lines);

/// <summary>REQ_F4114.6.1：比價為唯讀即時視圖，MD 快照不寫入報價歷史。</summary>
public sealed class QuotationComparisonService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IClock clock, ICurrencyLookup currencies,
    ITaxRateLookup taxes)
{
    public async Task<QuotationComparison> GetAsync(long rfqId, CancellationToken token = default)
    {
        access.RequireCompanyId();
        var scope = (await authorization.GetAsync(token))?.FilterFor(PurchasingPermissions.RfqCompare)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "RFQ compare permission is required.");
        IQueryable<Rfq> query = db.Set<Rfq>().AsNoTracking();
        query = scope.EffectiveScope switch
        {
            DataScope.Company => query,
            DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
            DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
            _ => query.Where(x => x.OwnerId == scope.UserId)
        };
        var rfq = await query.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == rfqId, token)
            ?? throw new NotFoundException("PUR.RFQ.NOT_FOUND", "Request for quotation", rfqId);
        if (rfq.Status == RfqStatus.Draft)
            throw new BusinessRuleException("PUR.RFQ.NOT_SENT", "Only sent RFQs can be compared.");
        var date = DateOnly.FromDateTime(clock.LocalNow);
        var baseCurrency = await currencies.GetBaseCurrencyAsync(token)
            ?? throw new BusinessRuleException("MD.EXCHANGE_RATE.NO_BASE_CURRENCY", "The company has no base currency.");
        var precision = await currencies.GetPrecisionAsync(token);
        var quotations = await db.Set<SupplierQuotation>().AsNoTracking()
            .Where(x => x.RfqId == rfqId)
            .Include(x => x.Revisions).ThenInclude(x => x.Lines)
            .AsSplitQuery().ToListAsync(token);
        var exchangeCache = new Dictionary<string, (ExchangeRateSnapshot? Snapshot, string? Error)>(StringComparer.OrdinalIgnoreCase);
        var taxCache = new Dictionary<long, (TaxRateSnapshot? Snapshot, string? Error)>();
        var lines = new List<ComparisonLine>();
        foreach (var rfqLine in rfq.Lines.OrderBy(x => x.LineNo))
        {
            var offers = new List<ComparisonOffer>();
            foreach (var quotation in quotations)
            {
                var revision = quotation.Revisions.Single(x => x.RevisionNo == quotation.CurrentRevisionNo);
                var quoteLine = revision.Lines.Single(x => x.RfqLineId == rfqLine.Id);
                var reason = quotation.Status switch
                {
                    SupplierQuotationStatus.Withdrawn => "PUR.QUOTATION.WITHDRAWN",
                    _ when revision.ValidUntil < date => "PUR.QUOTATION.EXPIRED",
                    _ when revision.QuoteDate > date => "PUR.QUOTATION.NOT_YET_VALID",
                    _ => null
                };
                ExchangeRateSnapshot? exchange = null;
                TaxRateSnapshot? tax = null;
                if (reason is null)
                {
                    if (!exchangeCache.TryGetValue(quoteLine.CurrencyCode, out var cachedExchange))
                    {
                        try { cachedExchange = (await currencies.GetRateSnapshotAsync(quoteLine.CurrencyCode, date, token), null); }
                        catch (ErpException ex) when (ex.ErrorCode.StartsWith("MD.CURRENCY.", StringComparison.Ordinal) ||
                            ex.ErrorCode.StartsWith("MD.EXCHANGE_RATE.", StringComparison.Ordinal))
                        { cachedExchange = (null, ex.ErrorCode); }
                        exchangeCache.Add(quoteLine.CurrencyCode, cachedExchange);
                    }
                    (exchange, reason) = cachedExchange;
                    if (reason is null)
                    {
                        if (!taxCache.TryGetValue(quoteLine.TaxRateId, out var cachedTax))
                        {
                            try { cachedTax = (await taxes.GetRateSnapshotAsync(quoteLine.TaxRateId, TaxDirection.Input, date, token), null); }
                            catch (ErpException ex) when (ex.ErrorCode.StartsWith("MD.TAX_RATE.", StringComparison.Ordinal))
                            { cachedTax = (null, ex.ErrorCode); }
                            taxCache.Add(quoteLine.TaxRateId, cachedTax);
                        }
                        (tax, reason) = cachedTax;
                    }
                }
                decimal? net = null, taxAmount = null, gross = null;
                if (reason is null)
                {
                    try
                    {
                        net = Money.Round(rfqLine.Quantity * quoteLine.UnitPrice * exchange!.Rate,
                            baseCurrency.Code, precision);
                        taxAmount = Money.Round(net.Value * tax!.RatePercent / 100m,
                            baseCurrency.Code, precision);
                        gross = checked(net.Value + taxAmount.Value);
                    }
                    catch (OverflowException) { reason = "PUR.QUOTATION.AMOUNT_OVERFLOW"; }
                }
                offers.Add(new ComparisonOffer(quotation.Id, quotation.SupplierId, quoteLine.Id,
                    quoteLine.UnitPrice, quoteLine.CurrencyCode, quoteLine.TaxRateId, reason is null,
                    reason, exchange?.Rate, exchange?.RateId, exchange?.EffectiveFrom, exchange?.EffectiveTo,
                    tax?.RatePercent, tax?.EffectiveFrom, tax?.EffectiveTo, net, taxAmount, gross,
                    quotation.Status));
            }
            lines.Add(new ComparisonLine(rfqLine.Id, rfqLine.LineNo, rfqLine.Quantity,
                [.. offers.OrderBy(x => x.IsComparable ? 0 : 1).ThenBy(x => x.BaseGross)
                    .ThenBy(x => x.SupplierId).ThenBy(x => x.QuotationId)]));
        }
        return new QuotationComparison(rfqId, date, baseCurrency.Code, lines);
    }
}
