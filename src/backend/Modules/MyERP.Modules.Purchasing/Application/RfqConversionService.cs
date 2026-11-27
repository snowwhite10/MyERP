using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

public sealed record RfqConversionResult(long OrderId, string DocumentNo, string Status,
    int LineCount, IReadOnlyList<long> AwardIds, int RfqVersion);

/// <summary>REQ_F4114.6.3：決標來源、草稿與 SYS 單號在同一交易提交。</summary>
public sealed class RfqConversionService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IClock clock)
{
    public const string DocumentType = "PUR.ORDER";

    public async Task<RfqConversionResult> ConvertAsync(long rfqId, RfqConversionInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        if (rfqId <= 0) throw new RequestValidationException("id", "RFQ ID must be positive.");
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        var scope = user?.CompanyId == companyId && user.Has(PurchasingPermissions.RfqConvert)
            ? user.FilterFor(PurchasingPermissions.RfqConvert) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "RFQ conversion permission is required.");

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
                throw new BusinessRuleException("PUR.RFQ.NOT_OPEN", "Only sent RFQs can be converted.");

            var selectedIds = input.AwardIds!.OrderBy(x => x).ToArray();
            var awards = await db.Set<RfqAward>().AsNoTracking()
                .Where(x => selectedIds.Contains(x.Id) && x.RfqId == rfqId)
                .OrderBy(x => x.Id).ToListAsync(token);
            if (awards.Count != selectedIds.Length)
                throw new NotFoundException("PUR.RFQ.AWARD_NOT_FOUND", "RFQ award", rfqId);
            if (awards.Any(x => x.SupplierId != input.SupplierId))
                throw new BusinessRuleException("PUR.RFQ.SUPPLIER_MISMATCH", "Selected awards must belong to the requested supplier.");
            var first = awards[0];
            if (awards.Any(x => x.CurrencyCode != first.CurrencyCode ||
                x.BaseCurrencyCode != first.BaseCurrencyCode))
                throw new BusinessRuleException("PUR.RFQ.CURRENCY_MISMATCH", "Selected awards must use one currency.");
            if (awards.Any(x => x.ExchangeRate != first.ExchangeRate))
                throw new BusinessRuleException("PUR.RFQ.RATE_MISMATCH", "Selected awards have different saved exchange rates.");
            var converted = await db.Set<PurchaseOrderAwardSource>().AsNoTracking()
                .Where(x => selectedIds.Contains(x.AwardId)).Select(x => x.AwardId).ToListAsync(token);
            if (converted.Count > 0)
                throw new BusinessRuleException("PUR.RFQ.AWARD_ALREADY_CONVERTED",
                    $"Award {converted.Min()} was already converted.");

            var lineIds = awards.Select(x => x.RfqLineId).ToArray();
            var rfqLines = await db.Set<RfqLine>().AsNoTracking()
                .Where(x => x.RfqId == rfqId && lineIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, token);
            if (rfqLines.Count != awards.Count)
                throw new BusinessRuleException("PUR.RFQ.LINE_NOT_FOUND", "An awarded RFQ line is missing.");
            var date = DateOnly.FromDateTime(clock.LocalNow);
            var order = new PurchaseOrderSource
            {
                CompanyId = companyId, SupplierId = input.SupplierId,
                DocumentNo = await numbers.NextAsync(DocumentType, date, token),
                Currency = first.CurrencyCode, BaseCurrencyCode = first.BaseCurrencyCode,
                ExchangeRate = first.ExchangeRate, Status = "Draft", RfqId = rfqId,
                BusinessDate = date, OwnerId = user!.UserId, BranchId = user.BranchId,
                DepartmentId = user.DepartmentId,
                BaseNet = awards.Sum(x => x.BaseNet), BaseTax = awards.Sum(x => x.BaseTax),
                BaseGross = awards.Sum(x => x.BaseGross)
            };
            db.Add(order);
            await db.SaveChangesAsync(token);
            var orderLines = awards.Select((award, index) => new PurchaseOrderSourceLine
            {
                CompanyId = companyId, PurchaseOrderId = order.Id, LineNo = index + 1,
                MaterialId = rfqLines[award.RfqLineId].ItemId,
                RequisitionLineId = rfqLines[award.RfqLineId].RequisitionLineId,
                Quantity = award.Quantity, UnitPrice = award.UnitPrice,
                TaxRateId = award.TaxRateId, TaxRatePercent = award.TaxRatePercent,
                ExchangeRate = award.ExchangeRate, BaseNet = award.BaseNet,
                BaseTax = award.BaseTax, BaseGross = award.BaseGross
            }).ToArray();
            db.AddRange(orderLines);
            await db.SaveChangesAsync(token);
            for (var i = 0; i < awards.Count; i++)
                db.Add(new PurchaseOrderAwardSource
                {
                    CompanyId = companyId, PurchaseOrderLineId = orderLines[i].Id,
                    AwardId = awards[i].Id
                });
            // Advancing the parent version also serializes a retry after the first conversion.
            db.Entry(rfq).Property(x => x.Status).IsModified = true;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return new RfqConversionResult(order.Id, order.DocumentNo!, order.Status,
                awards.Count, selectedIds, rfq.Version);
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
