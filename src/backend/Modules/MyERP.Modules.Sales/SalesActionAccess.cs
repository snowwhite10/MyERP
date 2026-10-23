using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales;

/// <summary>Resolves the SYS data scope for the specific SD action in the current company.</summary>
public sealed class SalesActionAccess(SalesCompanyAccess company, ICurrentAuthorization authorization)
{
    public async Task<DataScopeFilter> RequireScopeAsync(string permission, CancellationToken token = default)
    {
        var companyId = company.RequireCompanyId();
        var current = await authorization.GetAsync(token);
        if (current is null || current.CompanyId != companyId || !current.Has(permission))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Sales action permission is required.");
        return current.FilterFor(permission)
            ?? throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Sales action scope is required.");
    }
}
