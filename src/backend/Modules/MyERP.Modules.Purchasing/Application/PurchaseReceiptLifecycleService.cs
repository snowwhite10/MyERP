using System.Data;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>
/// REQ_F4114.2.6（WU-PUR-007a10，ADR-222）：以版本軟刪 DELETE 範圍內的 Draft 收貨單。Draft 不佔訂單量也未寫 INV，
/// 因此只設軟刪欄位；單號、保存行、批序號擷取與 SYS 歷史原樣保留，單號不重用。Rejected 須先修訂回 Draft。
/// </summary>
public sealed class PurchaseReceiptLifecycleService(MyErpDbContext db, RequisitionCompanyAccess access,
    ICurrentAuthorization authorization, IClock clock)
{
    /// <summary>版本不符 409；非 Draft 422 <c>PUR.RECEIPT.NOT_DELETABLE</c>；範圍外、他公司、舊來源列或已刪 404。</summary>
    public async Task DeleteAsync(long id, int version, CancellationToken token = default)
    {
        if (id <= 0) throw new RequestValidationException("id", "Receipt ID must be positive.");
        var companyId = access.RequireCompanyId();
        var grants = await authorization.GetAsync(token);
        var scope = (grants?.CompanyId == companyId ? grants.FilterFor(PurchasingPermissions.ReceiptDelete) : null)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Receipt delete permission is required.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            // Same lock as EDIT／SUBMIT on the receipt row, so a concurrent submit either wins or sees the deletion.
            await PurchaseReceiptLocks.LockReceiptAsync(db, id, companyId, token);
            var receipt = await PurchaseReceiptQueryService.ApplyScope(db.Set<PurchaseReceiptSource>()
                    .Where(x => x.CompanyId == companyId && x.DocumentNo != null && x.BusinessDate != null), scope)
                .SingleOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("PUR.RECEIPT.NOT_FOUND", "Purchase receipt", id);
            if (version < 1 || receipt.Version != version) throw new ConflictException();
            if (receipt.Status != "Draft")
                throw new BusinessRuleException("PUR.RECEIPT.NOT_DELETABLE",
                    "Only draft receipts can be deleted; revise a rejected receipt back to draft first.");
            receipt.IsDeleted = true;
            receipt.DeletedAt = clock.LocalNow;
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }
}
