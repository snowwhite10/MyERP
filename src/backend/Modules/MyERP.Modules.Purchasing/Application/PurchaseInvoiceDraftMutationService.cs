using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceDraftSaved(long Id, int Version);

/// <summary>Authorized HTTP boundary for one atomic purchase invoice draft save.</summary>
public sealed class PurchaseInvoiceDraftMutationService(
    RequisitionCompanyAccess access, ICurrentAuthorization authorization,
    PurchaseInvoiceDraftSaveService drafts, MyErpDbContext db)
{
    public async Task<PurchaseInvoiceDraftSaved> CreateAsync(PurchaseInvoiceDraftInput? body,
        CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        await RequireCompanyScopeAsync(PurchasingPermissions.InvoiceCreate, companyId, token);
        if (body is null)
            throw new RequestValidationException("body", "Invoice body is required.");
        var command = PurchaseInvoiceDraftHttpIntent.Create(companyId, body);
        var id = await drafts.SaveAsync(command, token);
        return await ResultAsync(id, companyId, token);
    }

    public async Task<PurchaseInvoiceDraftSaved> EditAsync(long id, int version,
        PurchaseInvoiceDraftInput? body, CancellationToken token = default)
    {
        var companyId = access.RequireCompanyId();
        await RequireCompanyScopeAsync(PurchasingPermissions.InvoiceEdit, companyId, token);
        if (body is null)
            throw new RequestValidationException("body", "Invoice body is required.");
        var command = PurchaseInvoiceDraftHttpIntent.Edit(companyId, id, version, body);
        var savedId = await drafts.SaveAsync(command, token);
        return await ResultAsync(savedId, companyId, token);
    }

    private async Task RequireCompanyScopeAsync(string permission, long companyId, CancellationToken token)
    {
        var grant = await authorization.GetAsync(token);
        // Invoice source rows do not yet carry an owner, branch or department. Fail closed for narrower grants.
        if (grant is null || grant.CompanyId != companyId ||
            grant.FilterFor(permission)?.EffectiveScope != DataScope.Company)
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Company-wide invoice permission is required.");
    }

    private async Task<PurchaseInvoiceDraftSaved> ResultAsync(long id, long companyId, CancellationToken token)
    {
        var result = await db.Set<PurchaseInvoice>().AsNoTracking()
            .Where(x => x.Id == id && x.CompanyId == companyId)
            .Select(x => new PurchaseInvoiceDraftSaved(x.Id, x.Version))
            .SingleAsync(token);
        return result;
    }
}
