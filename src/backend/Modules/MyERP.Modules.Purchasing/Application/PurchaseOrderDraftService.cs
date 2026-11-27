using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Numbering;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseOrderDetail(long Id, string DocumentNo, string Status, int Version,
    long SupplierId, DateOnly BusinessDate, string Currency, string BaseCurrencyCode,
    decimal ExchangeRate, decimal BaseNet, decimal BaseTax, decimal BaseGross, string? Remark,
    IReadOnlyList<PurchaseOrderLineDetail> Lines);

public sealed record PurchaseOrderLineDetail(long Id, int LineNo, long ItemId, long? RequisitionLineId,
    decimal Quantity, decimal UnitPrice, long TaxRateId, decimal TaxRatePercent,
    decimal OriginalNet, decimal OriginalTax, decimal OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>REQ_F4114.1.2：人工草稿、MD 參照與 SYS 發號在同一交易保存；請購量留給 SUBMIT 消耗。</summary>
public sealed class PurchaseOrderDraftService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IRequisitionItemGuard items,
    ISupplierLookup suppliers, ICurrencyLookup currencies, ITaxRateLookup taxes)
{
    public async Task<PurchaseOrderDetail> CreateAsync(PurchaseOrderInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        if (user is null || user.CompanyId != companyId || !user.Has(PurchasingPermissions.OrderCreate))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order create permission is required.");
        var lines = input.Lines!.OrderBy(x => x.LineNo).ToArray();
        if (lines.Select(x => x.CurrencyCode).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new RequestValidationException("lines", "All order lines must use one currency.");
        var currencyCode = lines[0].CurrencyCode;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            return await items.RunAsync(lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                await suppliers.RequireApprovedAsync(input.SupplierId, token);
                var baseCurrency = await currencies.GetBaseCurrencyAsync(token)
                    ?? throw new BusinessRuleException("PUR.ORDER.NO_BASE_CURRENCY", "Company base currency is required.");
                if (!baseCurrency.IsActive)
                    throw new BusinessRuleException("PUR.ORDER.BASE_CURRENCY_INACTIVE", "Company base currency is inactive.");
                var currency = await currencies.FindCurrencyAsync(currencyCode, token);
                if (currency is null || !currency.IsActive)
                    throw new BusinessRuleException("PUR.ORDER.CURRENCY_UNAVAILABLE", "Order currency is not active.");
                var exchange = await currencies.GetRateSnapshotAsync(currencyCode, input.BusinessDate, token);
                if (currencyCode == baseCurrency.Code && exchange.Rate != 1m)
                    throw new BusinessRuleException("PUR.ORDER.INVALID_RATE", "Base currency exchange rate must be one.");
                var sourceIds = lines.Where(x => x.RequisitionLineId.HasValue)
                    .Select(x => x.RequisitionLineId!.Value).Distinct().ToArray();
                var sources = await (from line in db.Set<RequisitionLine>().AsNoTracking()
                    join parent in db.Set<Requisition>().AsNoTracking() on
                        new { line.CompanyId, Id = line.RequisitionId } equals new { parent.CompanyId, parent.Id }
                    where sourceIds.Contains(line.Id)
                    select new { line.Id, line.ItemId, parent.Status, parent.IsDeleted })
                    .ToDictionaryAsync(x => x.Id, token);
                foreach (var line in lines.Where(x => x.RequisitionLineId.HasValue))
                {
                    if (!sources.TryGetValue(line.RequisitionLineId!.Value, out var source) ||
                        source.IsDeleted || source.Status != RequisitionStatus.Approved || source.ItemId != line.ItemId)
                        throw new BusinessRuleException("PUR.ORDER.INVALID_REQUISITION_SOURCE",
                            $"Line {line.LineNo} must reference an approved, same-company requisition line for the same item.");
                }
                var taxRates = new Dictionary<long, decimal>();
                foreach (var taxId in lines.Select(x => x.TaxRateId).Distinct())
                    taxRates[taxId] = (await taxes.GetRateSnapshotAsync(taxId, TaxDirection.Input,
                        input.BusinessDate, token)).RatePercent;
                var amounts = PurchaseOrderAmounts.Calculate(lines.Select(x => new PurchaseOrderAmountInput(
                    x.LineNo, x.Quantity, x.UnitPrice, currencyCode, taxRates[x.TaxRateId], exchange.Rate))
                    .ToArray(), baseCurrency.Code);
                var order = new PurchaseOrderSource
                {
                    CompanyId = companyId, SupplierId = input.SupplierId,
                    DocumentNo = await numbers.NextAsync(RfqConversionService.DocumentType, input.BusinessDate, token),
                    BusinessDate = input.BusinessDate, Currency = currencyCode, BaseCurrencyCode = baseCurrency.Code,
                    ExchangeRate = exchange.Rate, Status = "Draft", OwnerId = user.UserId,
                    BranchId = user.BranchId, DepartmentId = user.DepartmentId,
                    Remark = string.IsNullOrWhiteSpace(input.Remark) ? null : input.Remark.Trim(),
                    BaseNet = amounts.BaseNet, BaseTax = amounts.BaseTax, BaseGross = amounts.BaseGross
                };
                db.Add(order);
                await db.SaveChangesAsync(token);
                var savedLines = lines.Select((line, index) =>
                {
                    var amount = amounts.Lines[index];
                    return new PurchaseOrderSourceLine
                    {
                        CompanyId = companyId, PurchaseOrderId = order.Id, LineNo = line.LineNo,
                        MaterialId = line.ItemId, RequisitionLineId = line.RequisitionLineId,
                        Quantity = line.Quantity, UnitPrice = line.UnitPrice, TaxRateId = line.TaxRateId,
                        TaxRatePercent = taxRates[line.TaxRateId], ExchangeRate = exchange.Rate,
                        OriginalNet = amount.OriginalNet, OriginalTax = amount.OriginalTax,
                        OriginalGross = amount.OriginalGross, BaseNet = amount.BaseNet,
                        BaseTax = amount.BaseTax, BaseGross = amount.BaseGross
                    };
                }).ToArray();
                db.AddRange(savedLines);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return new PurchaseOrderDetail(order.Id, order.DocumentNo!, order.Status, order.Version,
                    order.SupplierId, input.BusinessDate, currencyCode, baseCurrency.Code, exchange.Rate,
                    amounts.BaseNet, amounts.BaseTax, amounts.BaseGross, order.Remark,
                    savedLines.Select(x => new PurchaseOrderLineDetail(x.Id, x.LineNo, x.MaterialId,
                        x.RequisitionLineId, x.Quantity, x.UnitPrice, x.TaxRateId!.Value,
                        x.TaxRatePercent, x.OriginalNet!.Value, x.OriginalTax!.Value,
                        x.OriginalGross!.Value, x.BaseNet!.Value, x.BaseTax!.Value,
                        x.BaseGross!.Value)).ToArray());
            }, token);
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
