using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderLifecycleTests
{
    [Theory]
    [InlineData(SalesOrderStatus.Draft, SalesOrderAction.Edit, SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Rejected, SalesOrderAction.Edit, SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Draft, SalesOrderAction.Delete, SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Draft, SalesOrderAction.SubmitPending, SalesOrderStatus.Pending)]
    [InlineData(SalesOrderStatus.Draft, SalesOrderAction.SubmitApproved, SalesOrderStatus.Approved)]
    [InlineData(SalesOrderStatus.Pending, SalesOrderAction.Approve, SalesOrderStatus.Approved)]
    [InlineData(SalesOrderStatus.Pending, SalesOrderAction.Reject, SalesOrderStatus.Rejected)]
    [InlineData(SalesOrderStatus.Approved, SalesOrderAction.Cancel, SalesOrderStatus.Cancelled)]
    public void AllowsSpecifiedTransitions(SalesOrderStatus current, SalesOrderAction action,
        SalesOrderStatus expected)
    {
        SalesOrderLifecycle.Next(current, action, false).Should().Be(expected);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Rejected, SalesOrderAction.SubmitPending)]
    [InlineData(SalesOrderStatus.Pending, SalesOrderAction.Edit)]
    [InlineData(SalesOrderStatus.Pending, SalesOrderAction.Delete)]
    [InlineData(SalesOrderStatus.Draft, SalesOrderAction.Approve)]
    [InlineData(SalesOrderStatus.Approved, SalesOrderAction.Reject)]
    [InlineData(SalesOrderStatus.Approved, SalesOrderAction.Edit)]
    [InlineData(SalesOrderStatus.Cancelled, SalesOrderAction.Cancel)]
    [InlineData(SalesOrderStatus.Cancelled, SalesOrderAction.Edit)]
    public void RejectsInvalidTransitions(SalesOrderStatus current, SalesOrderAction action)
    {
        Action act = () => SalesOrderLifecycle.Next(current, action, false);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.INVALID_STATUS");
    }

    [Fact]
    public void ActiveOrPostedDeliveryPreventsCancellation()
    {
        Action act = () => SalesOrderLifecycle.Next(SalesOrderStatus.Approved, SalesOrderAction.Cancel, true);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.HAS_DELIVERY");
    }
}
