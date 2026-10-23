using MyERP.Shared.Errors;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>REQ_F4112.2.2: pure quote-source eligibility check before transactional conversion.</summary>
public static class SalesOrderQuotationSource
{
    public static void Validate(long orderCompanyId, long quotationCompanyId,
        SalesQuotationStatus quotationStatus, DateOnly expiryDate,
        bool hasSourceOrder, IClock clock)
    {
        if (orderCompanyId <= 0 || quotationCompanyId != orderCompanyId)
            throw new BusinessRuleException("SAL.DOCUMENT.NOT_FOUND", "Quotation was not found.");

        SalesQuotationLifecycle.Next(quotationStatus, SalesQuotationAction.Convert,
            expiryDate, hasSourceOrder, clock);
    }
}
