using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

public enum SalesDeliveryStatus { Draft, Pending, Rejected, Posted }

public enum SalesDeliveryAction { Edit, Delete, SubmitPending, SubmitPosted, Approve, Reject }

/// <summary>REQ_F4112.3.3–6: pure transition rules; caller owns saved order, version, SYS and INV transaction.</summary>
public static class SalesDeliveryLifecycle
{
    public static SalesDeliveryStatus Next(SalesDeliveryStatus status, SalesDeliveryAction action,
        bool orderIsApproved)
    {
        var allowed = (status, action) switch
        {
            (SalesDeliveryStatus.Draft or SalesDeliveryStatus.Rejected, SalesDeliveryAction.Edit) => true,
            (SalesDeliveryStatus.Draft, SalesDeliveryAction.Delete or SalesDeliveryAction.SubmitPending or
                SalesDeliveryAction.SubmitPosted) => true,
            (SalesDeliveryStatus.Pending, SalesDeliveryAction.Approve or SalesDeliveryAction.Reject) => true,
            _ => false
        };
        if (!allowed)
            throw new BusinessRuleException("SAL.DELIVERY.INVALID_STATUS",
                "Delivery action is unavailable in its current status.");

        if (!orderIsApproved && action is SalesDeliveryAction.SubmitPending or
            SalesDeliveryAction.SubmitPosted or SalesDeliveryAction.Approve)
            throw new BusinessRuleException("SAL.DELIVERY.ORDER_NOT_APPROVED",
                "The source order must be approved before delivery submission or posting.");

        return action switch
        {
            SalesDeliveryAction.Edit or SalesDeliveryAction.Delete => SalesDeliveryStatus.Draft,
            SalesDeliveryAction.SubmitPending => SalesDeliveryStatus.Pending,
            SalesDeliveryAction.SubmitPosted or SalesDeliveryAction.Approve => SalesDeliveryStatus.Posted,
            SalesDeliveryAction.Reject => SalesDeliveryStatus.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }
}
