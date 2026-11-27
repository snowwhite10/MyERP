using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    decimal? OriginalNet, decimal? OriginalTax, decimal? OriginalGross,
    decimal BaseNet, decimal BaseTax, decimal BaseGross);

/// <summary>REQ_F4114.1.2～3：人工草稿、MD 參照與 SYS 發號在同一交易保存或修訂；請購量留給 SUBMIT 消耗。</summary>
public sealed class PurchaseOrderDraftService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, INumberGenerator numbers, IRequisitionItemGuard items,
    ISupplierLookup suppliers, ICurrencyLookup currencies, ITaxRateLookup taxes)
{
    /// <summary>每行稅率百分比按輸入行順序；決標行沿用保存快照，人工行取 MD 現值。</summary>
    private sealed record Pricing(string BaseCurrencyCode, decimal ExchangeRate,
        IReadOnlyList<decimal> TaxRatePercents, PurchaseOrderAmountResult Amounts);

    private static readonly IReadOnlyDictionary<int, PurchaseOrderSourceLine> NoLockedLines =
        new Dictionary<int, PurchaseOrderSourceLine>();

    public async Task<PurchaseOrderDetail> CreateAsync(PurchaseOrderInput input, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        if (user is null || user.CompanyId != companyId || !user.Has(PurchasingPermissions.OrderCreate))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order create permission is required.");
        var (lines, currencyCode) = SortedSingleCurrency(input);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            return await items.RunAsync(lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                var pricing = await PriceAsync(input, lines, currencyCode, null, NoLockedLines, token);
                var order = new PurchaseOrderSource
                {
                    CompanyId = companyId, SupplierId = input.SupplierId,
                    DocumentNo = await numbers.NextAsync(RfqConversionService.DocumentType, input.BusinessDate, token),
                    BusinessDate = input.BusinessDate, Currency = currencyCode,
                    BaseCurrencyCode = pricing.BaseCurrencyCode, ExchangeRate = pricing.ExchangeRate,
                    Status = "Draft", OwnerId = user.UserId,
                    BranchId = user.BranchId, DepartmentId = user.DepartmentId,
                    Remark = NormalizeRemark(input.Remark),
                    BaseNet = pricing.Amounts.BaseNet, BaseTax = pricing.Amounts.BaseTax,
                    BaseGross = pricing.Amounts.BaseGross
                };
                db.Add(order);
                await db.SaveChangesAsync(token);
                var savedLines = lines.Select((line, index) =>
                {
                    var row = new PurchaseOrderSourceLine
                    {
                        CompanyId = companyId, PurchaseOrderId = order.Id, LineNo = line.LineNo
                    };
                    Apply(row, line, index, pricing);
                    return row;
                }).ToArray();
                db.AddRange(savedLines);
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return Detail(order, savedLines);
            }, token);
        }
        catch (Exception ex)
        {
            await RollbackAsync(transaction, ex);
            throw;
        }
    }

    /// <summary>
    /// REQ_F4114.1.3／BR-PUR-022：整組修訂 Draft 或 Rejected（保存後回 Draft）。版本不符 409；其他狀態 422。
    /// 決標來源行的物料、數量、單價、稅率、請購來源及保存金額不可改，也不可刪；含決標行訂單的供應商、
    /// 幣別與匯率快照不重估。人工行按業務日重驗 MD 並重算價稅。請購已轉量不變。
    /// </summary>
    public async Task<PurchaseOrderDetail> UpdateAsync(long id, int version, PurchaseOrderInput input,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (id <= 0) throw new RequestValidationException("id", "Purchase order ID must be positive.");
        input.Validate();
        var companyId = access.RequireCompanyId();
        var user = await authorization.GetAsync(token);
        var scope = user is not null && user.CompanyId == companyId && user.Has(PurchasingPermissions.OrderEdit)
            ? user.FilterFor(PurchasingPermissions.OrderEdit) : null;
        if (scope is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order edit permission is required.");
        var (lines, currencyCode) = SortedSingleCurrency(input);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            IQueryable<PurchaseOrderSource> query = db.Set<PurchaseOrderSource>()
                .Where(x => x.CompanyId == companyId);
            query = scope.EffectiveScope switch
            {
                DataScope.Company => query,
                DataScope.Branch => query.Where(x => x.BranchId == scope.BranchId),
                DataScope.Department => query.Where(x => x.DepartmentId == scope.DepartmentId),
                _ => query.Where(x => x.OwnerId == scope.UserId)
            };
            var order = await query.SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("PUR.ORDER.NOT_FOUND", "Purchase order", id);
            if (version < 1 || order.Version != version) throw new ConflictException();
            if (order.Status is not ("Draft" or "Rejected"))
                throw new BusinessRuleException("PUR.ORDER.NOT_EDITABLE",
                    "Only draft or rejected purchase orders can be edited.");
            var existing = await db.Set<PurchaseOrderSourceLine>()
                .Where(x => x.CompanyId == companyId && x.PurchaseOrderId == id).ToListAsync(token);
            var existingIds = existing.Select(x => x.Id).ToArray();
            var awardLineIds = (await db.Set<PurchaseOrderAwardSource>().AsNoTracking()
                .Where(x => x.CompanyId == companyId && existingIds.Contains(x.PurchaseOrderLineId))
                .Select(x => x.PurchaseOrderLineId).ToListAsync(token)).ToHashSet();
            var byLineNo = lines.ToDictionary(x => x.LineNo);
            if (awardLineIds.Count > 0 && (input.SupplierId != order.SupplierId || currencyCode != order.Currency))
                throw AwardLocked("Supplier and currency of an order converted from awards cannot change.");
            var locked = existing.Where(x => awardLineIds.Contains(x.Id)).ToDictionary(x => x.LineNo);
            foreach (var row in locked.Values)
            {
                if (!byLineNo.TryGetValue(row.LineNo, out var line) || line.ItemId != row.MaterialId ||
                    line.Quantity != row.Quantity || line.UnitPrice != row.UnitPrice ||
                    line.TaxRateId != row.TaxRateId || line.RequisitionLineId != row.RequisitionLineId)
                    throw AwardLocked($"Awarded line {row.LineNo} must keep its item, quantity, unit price, tax rate and source.");
            }
            var snapshot = locked.Count > 0 && order.BaseCurrencyCode is not null && order.ExchangeRate is not null
                ? order : null;
            return await items.RunAsync(lines.Select(x => x.ItemId).Distinct().ToArray(), async () =>
            {
                var pricing = await PriceAsync(input, lines, currencyCode, snapshot, locked, token);
                // Lines are matched by line number so awarded rows keep the ID referenced by pur_order_source.
                var kept = existing.ToDictionary(x => x.LineNo);
                db.RemoveRange(existing.Where(x => !byLineNo.ContainsKey(x.LineNo)));
                var savedLines = new PurchaseOrderSourceLine[lines.Length];
                decimal baseNet = 0m, baseTax = 0m, baseGross = 0m;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!kept.TryGetValue(lines[i].LineNo, out var row))
                    {
                        row = new PurchaseOrderSourceLine
                        {
                            CompanyId = companyId, PurchaseOrderId = id, LineNo = lines[i].LineNo
                        };
                        db.Add(row);
                    }
                    // Awarded rows keep their saved commercial snapshot, including amounts (BR-PUR-022).
                    if (!locked.ContainsKey(row.LineNo)) Apply(row, lines[i], i, pricing);
                    var computed = pricing.Amounts.Lines[i];
                    baseNet += row.BaseNet ?? computed.BaseNet;
                    baseTax += row.BaseTax ?? computed.BaseTax;
                    baseGross += row.BaseGross ?? computed.BaseGross;
                    savedLines[i] = row;
                }
                order.SupplierId = input.SupplierId;
                order.BusinessDate = input.BusinessDate;
                order.Currency = currencyCode;
                order.BaseCurrencyCode = pricing.BaseCurrencyCode;
                order.ExchangeRate = pricing.ExchangeRate;
                order.Remark = NormalizeRemark(input.Remark);
                order.BaseNet = baseNet;
                order.BaseTax = baseTax;
                order.BaseGross = baseGross;
                order.Status = "Draft";
                // A line-only revision must still advance the header version used for optimistic locking.
                db.Entry(order).Property(x => x.Status).IsModified = true;
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                return Detail(order, savedLines);
            }, token);
        }
        catch (Exception ex)
        {
            await RollbackAsync(transaction, ex);
            throw;
        }
    }

    private static (PurchaseOrderLineInput[] Lines, string CurrencyCode) SortedSingleCurrency(PurchaseOrderInput input)
    {
        var lines = input.Lines!.OrderBy(x => x.LineNo).ToArray();
        if (lines.Select(x => x.CurrencyCode).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new RequestValidationException("lines", "All order lines must use one currency.");
        return (lines, lines[0].CurrencyCode);
    }

    private static string? NormalizeRemark(string? remark) =>
        string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();

    private static BusinessRuleException AwardLocked(string message) =>
        new("PUR.ORDER.AWARD_LINE_LOCKED", message);

    private static void Apply(PurchaseOrderSourceLine row, PurchaseOrderLineInput line, int index, Pricing pricing)
    {
        var amount = pricing.Amounts.Lines[index];
        row.MaterialId = line.ItemId;
        row.RequisitionLineId = line.RequisitionLineId;
        row.Quantity = line.Quantity;
        row.UnitPrice = line.UnitPrice;
        row.TaxRateId = line.TaxRateId;
        row.TaxRatePercent = pricing.TaxRatePercents[index];
        row.ExchangeRate = pricing.ExchangeRate;
        row.OriginalNet = amount.OriginalNet;
        row.OriginalTax = amount.OriginalTax;
        row.OriginalGross = amount.OriginalGross;
        row.BaseNet = amount.BaseNet;
        row.BaseTax = amount.BaseTax;
        row.BaseGross = amount.BaseGross;
    }

    /// <summary>
    /// 重驗供應商、本位幣、匯率、請購來源與進項稅率，並計算整單原幣／本位幣價稅。
    /// <paramref name="snapshot"/> 為含決標行訂單的已保存匯率快照（不重估）；<paramref name="locked"/>
    /// 的決標行沿用保存稅率百分比，且不重驗其請購來源。
    /// </summary>
    private async Task<Pricing> PriceAsync(PurchaseOrderInput input, PurchaseOrderLineInput[] lines,
        string currencyCode, PurchaseOrderSource? snapshot,
        IReadOnlyDictionary<int, PurchaseOrderSourceLine> locked, CancellationToken token)
    {
        var (baseCurrencyCode, exchangeRate) = snapshot is null
            ? await CurrentRateAsync(input, currencyCode, token)
            : (snapshot.BaseCurrencyCode!, snapshot.ExchangeRate!.Value);
        var manual = lines.Where(x => !locked.ContainsKey(x.LineNo)).ToArray();
        var sourceIds = manual.Where(x => x.RequisitionLineId.HasValue)
            .Select(x => x.RequisitionLineId!.Value).Distinct().ToArray();
        var sources = await (from line in db.Set<RequisitionLine>().AsNoTracking()
            join parent in db.Set<Requisition>().AsNoTracking() on
                new { line.CompanyId, Id = line.RequisitionId } equals new { parent.CompanyId, parent.Id }
            where sourceIds.Contains(line.Id)
            select new { line.Id, line.ItemId, parent.Status, parent.IsDeleted })
            .ToDictionaryAsync(x => x.Id, token);
        foreach (var line in manual.Where(x => x.RequisitionLineId.HasValue))
        {
            if (!sources.TryGetValue(line.RequisitionLineId!.Value, out var source) ||
                source.IsDeleted || source.Status != RequisitionStatus.Approved || source.ItemId != line.ItemId)
                throw new BusinessRuleException("PUR.ORDER.INVALID_REQUISITION_SOURCE",
                    $"Line {line.LineNo} must reference an approved, same-company requisition line for the same item.");
        }
        var taxRates = new Dictionary<long, decimal>();
        foreach (var taxId in manual.Select(x => x.TaxRateId).Distinct())
            taxRates[taxId] = (await taxes.GetRateSnapshotAsync(taxId, TaxDirection.Input,
                input.BusinessDate, token)).RatePercent;
        var percents = lines.Select(x => locked.TryGetValue(x.LineNo, out var row)
            ? row.TaxRatePercent : taxRates[x.TaxRateId]).ToArray();
        var amounts = PurchaseOrderAmounts.Calculate(lines.Select((x, i) => new PurchaseOrderAmountInput(
            x.LineNo, x.Quantity, x.UnitPrice, currencyCode, percents[i], exchangeRate))
            .ToArray(), baseCurrencyCode);
        return new Pricing(baseCurrencyCode, exchangeRate, percents, amounts);
    }

    private async Task<(string BaseCurrencyCode, decimal ExchangeRate)> CurrentRateAsync(
        PurchaseOrderInput input, string currencyCode, CancellationToken token)
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
        return (baseCurrency.Code, exchange.Rate);
    }

    private async Task RollbackAsync(IDbContextTransaction transaction, Exception ex)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        db.ChangeTracker.Clear();
        if (ex is DbUpdateConcurrencyException || ex is DbUpdateException { InnerException: SqlException sql }
            && sql.Number is 2601 or 2627) throw new ConflictException();
    }

    private static PurchaseOrderDetail Detail(PurchaseOrderSource order, IEnumerable<PurchaseOrderSourceLine> lines) =>
        new(order.Id, order.DocumentNo!, order.Status, order.Version, order.SupplierId, order.BusinessDate!.Value,
            order.Currency, order.BaseCurrencyCode!, order.ExchangeRate!.Value, order.BaseNet!.Value,
            order.BaseTax!.Value, order.BaseGross!.Value, order.Remark,
            lines.OrderBy(x => x.LineNo).Select(x => new PurchaseOrderLineDetail(x.Id, x.LineNo, x.MaterialId,
                x.RequisitionLineId, x.Quantity, x.UnitPrice, x.TaxRateId!.Value,
                x.TaxRatePercent, x.OriginalNet, x.OriginalTax,
                x.OriginalGross, x.BaseNet!.Value, x.BaseTax!.Value,
                x.BaseGross!.Value)).ToArray());
}
