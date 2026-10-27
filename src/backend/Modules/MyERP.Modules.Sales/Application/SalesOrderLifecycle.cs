using MyERP.Shared.Errors;
using MyERP.Modules.Sales.Domain;

namespace MyERP.Modules.Sales.Application;

public enum SalesOrderAction { Edit, Delete, SubmitPending, SubmitApproved, Approve, Reject, Cancel }

/// <summary>REQ_F4112.2.3–6: pure status rules; caller owns authorisation, version, credit and transaction.</summary>
public static class SalesOrderLifecycle
{
    public static SalesOrderStatus Next(SalesOrderStatus status, SalesOrderAction action,
        bool hasActiveOrPostedDelivery)
    {
        var allowed = (status, action) switch
        {
            (SalesOrderStatus.Draft or SalesOrderStatus.Rejected, SalesOrderAction.Edit) => true,
            (SalesOrderStatus.Draft, SalesOrderAction.Delete or SalesOrderAction.SubmitPending or
                SalesOrderAction.SubmitApproved) => true,
            (SalesOrderStatus.Pending, SalesOrderAction.Approve or SalesOrderAction.Reject) => true,
            (SalesOrderStatus.Approved, SalesOrderAction.Cancel) => true,
            _ => false
        };
        if (!allowed)
            throw new BusinessRuleException("SAL.ORDER.INVALID_STATUS",
                "Order action is unavailable in its current status.");
        if (action == SalesOrderAction.Cancel && hasActiveOrPostedDelivery)
            throw new BusinessRuleException("SAL.ORDER.HAS_DELIVERY",
                "Order with an active or posted delivery cannot be cancelled.");

        return action switch
        {
            SalesOrderAction.Edit or SalesOrderAction.Delete => SalesOrderStatus.Draft,
            SalesOrderAction.SubmitPending => SalesOrderStatus.Pending,
            SalesOrderAction.SubmitApproved or SalesOrderAction.Approve => SalesOrderStatus.Approved,
            SalesOrderAction.Reject => SalesOrderStatus.Rejected,
            SalesOrderAction.Cancel => SalesOrderStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }
}
