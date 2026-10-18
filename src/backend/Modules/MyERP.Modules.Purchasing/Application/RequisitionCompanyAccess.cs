using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>請購 Application 層的公司上下文守衛；資料查詢另由 EF 公司篩選限制。</summary>
public sealed class RequisitionCompanyAccess(ICurrentCompany currentCompany)
{
    public long RequireCompanyId() => currentCompany.CompanyId is long id && id > 0 ? id :
        throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    public void EnsureVisible(long documentCompanyId, long documentId)
    {
        if (documentCompanyId != RequireCompanyId())
            throw new NotFoundException("PUR.REQUISITION.NOT_FOUND", "Purchase requisition", documentId);
    }
}
