using MyERP.Shared.Errors;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

public enum SalesQuotationStatus { Draft, Pending, Rejected, Approved, Voided }

public enum SalesQuotationAction { Edit, SubmitPending, SubmitApproved, Approve, Reject, Convert, Void }

/// <summary>REQ_F4112.1.2–3: pure transition rules; callers own version, SYS decision and transaction.</summary>
public static class SalesQuotationLifecycle
{
    public static bool IsExpired(DateOnly expiryDate, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return expiryDate < clock.Today;
    }

    public static SalesQuotationStatus Next(SalesQuotationStatus status, SalesQuotationAction action,
        DateOnly expiryDate, bool hasSourceOrder, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var allowed = (status, action) switch
        {
            (SalesQuotationStatus.Draft or SalesQuotationStatus.Rejected, SalesQuotationAction.Edit) => true,
            (SalesQuotationStatus.Draft, SalesQuotationAction.SubmitPending or SalesQuotationAction.SubmitApproved) => true,
            (SalesQuotationStatus.Pending, SalesQuotationAction.Approve or SalesQuotationAction.Reject) => true,
            (SalesQuotationStatus.Approved, SalesQuotationAction.Convert or SalesQuotationAction.Void) => true,
            _ => false
        };
        if (!allowed)
            throw new BusinessRuleException("SAL.QUOTATION.INVALID_STATUS",
                "Quotation action is unavailable in its current status.");
        if (hasSourceOrder && action is SalesQuotationAction.Convert or SalesQuotationAction.Void)
            throw new BusinessRuleException("SAL.QUOTATION.ALREADY_CONVERTED",
                "Quotation already has a source order.");
        if (action is SalesQuotationAction.SubmitPending or SalesQuotationAction.SubmitApproved or
            SalesQuotationAction.Convert && IsExpired(expiryDate, clock))
            throw new BusinessRuleException("SAL.QUOTATION.EXPIRED", "Quotation has expired.");

        return action switch
        {
            SalesQuotationAction.Edit => SalesQuotationStatus.Draft,
            SalesQuotationAction.SubmitPending => SalesQuotationStatus.Pending,
            SalesQuotationAction.SubmitApproved or SalesQuotationAction.Approve => SalesQuotationStatus.Approved,
            SalesQuotationAction.Reject => SalesQuotationStatus.Rejected,
            SalesQuotationAction.Void => SalesQuotationStatus.Voided,
            _ => status
        };
    }
}
