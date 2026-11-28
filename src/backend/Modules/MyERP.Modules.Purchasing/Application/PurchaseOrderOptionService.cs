using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Paging;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>介面 4.2／4.3 請購來源選項：原量、已轉量與可用餘量都是保存值，送審時仍在鎖內重算。</summary>
public sealed record PurchaseOrderRequisitionOption(long Id, long RequisitionId, string DocumentNo, int LineNo,
    long ItemId, DateOnly RequiredDate, decimal Quantity, decimal OrderedQuantity, decimal RemainingQuantity);

/// <summary>
/// WU-PUR-006f2a：採購單表單的請購來源選項。只需 PUR.ORDER.CREATE 或 EDIT（不需 REQUISITION.VIEW），
/// 列出目前公司未刪除、已核准且原量大於已轉量的請購行；按單號、行號、ID 穩定分頁。
/// </summary>
public sealed class PurchaseOrderOptionService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization)
{
    public async Task<PagedResult<PurchaseOrderRequisitionOption>> RequisitionOptionsAsync(PagedRequest request,
        long? itemId, CancellationToken token = default)
    {
        access.RequireCompanyId();
        request.EnsureValid();
        if (itemId is <= 0) throw new RequestValidationException("itemId", "Item must be positive.");
        var grants = await authorization.GetAsync(token);
        if (grants?.FilterFor(PurchasingPermissions.OrderCreate) is null &&
            grants?.FilterFor(PurchasingPermissions.OrderEdit) is null)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Purchase order create or edit permission is required.");
        var query = from requisition in db.Set<Requisition>().AsNoTracking()
                    join line in db.Set<RequisitionLine>().AsNoTracking() on requisition.Id equals line.RequisitionId
                    where requisition.Status == RequisitionStatus.Approved && line.Quantity > line.OrderedQuantity
                    select new { line.Id, line.RequisitionId, requisition.DocumentNo, line.LineNo, line.ItemId,
                        line.RequiredDate, line.Quantity, line.OrderedQuantity };
        if (itemId is { } selectedItem) query = query.Where(x => x.ItemId == selectedItem);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x => x.DocumentNo.Contains(keyword));
        }
        var count = await query.LongCountAsync(token);
        var rows = await query.OrderBy(x => x.DocumentNo).ThenBy(x => x.LineNo).ThenBy(x => x.Id)
            .Skip(request.Skip).Take(request.PageSize)
            .Select(x => new PurchaseOrderRequisitionOption(x.Id, x.RequisitionId, x.DocumentNo, x.LineNo, x.ItemId,
                x.RequiredDate, x.Quantity, x.OrderedQuantity, x.Quantity - x.OrderedQuantity))
            .ToListAsync(token);
        return PagedResult.Create(rows, request, count);
    }
}
