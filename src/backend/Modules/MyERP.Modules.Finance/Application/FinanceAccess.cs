using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Company and action-permission gate shared by FI services. Credit limits are company-wide configuration with no
/// branch, department or owner, so any granted data scope of the action sees the whole company (ADR-075).
/// </summary>
internal static class FinanceAccess
{
    public static async Task<long> RequireAsync(ICurrentCompany currentCompany, ICurrentAuthorization authorization,
        string permission, CancellationToken token)
    {
        var companyId = currentCompany.CompanyId is long id && id > 0 ? id
            : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");
        var owner = await authorization.GetAsync(token);
        if (owner is null || owner.CompanyId != companyId || !owner.Has(permission))
            throw new ForbiddenException("COMMON.PERMISSION.DENIED", "Credit limit permission is required.");
        return companyId;
    }
}
