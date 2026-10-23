using MyERP.Shared.Errors;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales;

/// <summary>SD 單據讀寫的公司邊界；資料範圍檢查由後續單據查詢套用 SYS 契約。</summary>
public sealed class SalesCompanyAccess(ICurrentCompany company)
{
    public long RequireCompanyId() => company.CompanyId is long id && id > 0
        ? id : throw new ForbiddenException("COMMON.COMPANY.ACCESS_DENIED", "Current company is required.");

    public void EnsureVisible(long documentCompanyId, long documentId)
    {
        if (documentCompanyId != RequireCompanyId())
            throw new NotFoundException("SAL.DOCUMENT.NOT_FOUND", "Sales document", documentId);
    }
}
