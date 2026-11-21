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

    /// <summary>True when the current-company user holds <paramref name="permission"/> with a scope covering the entity.</summary>
    public async Task<bool> AllowsAsync(string permission, IDataScopedEntity entity, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var companyId = company.RequireCompanyId();
        var current = await authorization.GetAsync(token);
        return current is not null && current.CompanyId == companyId &&
            current.FilterFor(permission) is { } scope && scope.Allows(entity);
    }
}
