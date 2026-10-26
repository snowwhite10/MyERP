using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesDeliveryLifecycleTests
{
    [Theory]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.Edit, SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Rejected, SalesDeliveryAction.Edit, SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.Delete, SalesDeliveryStatus.Draft)]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.SubmitPending, SalesDeliveryStatus.Pending)]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.SubmitPosted, SalesDeliveryStatus.Posted)]
    [InlineData(SalesDeliveryStatus.Pending, SalesDeliveryAction.Approve, SalesDeliveryStatus.Posted)]
    [InlineData(SalesDeliveryStatus.Pending, SalesDeliveryAction.Reject, SalesDeliveryStatus.Rejected)]
    public void AllowsOnlySpecifiedTransition(SalesDeliveryStatus current, SalesDeliveryAction action,
        SalesDeliveryStatus expected)
    {
        SalesDeliveryLifecycle.Next(current, action, true).Should().Be(expected);
    }

    [Theory]
    [InlineData(SalesDeliveryStatus.Rejected, SalesDeliveryAction.SubmitPending)]
    [InlineData(SalesDeliveryStatus.Rejected, SalesDeliveryAction.Delete)]
    [InlineData(SalesDeliveryStatus.Pending, SalesDeliveryAction.Edit)]
    [InlineData(SalesDeliveryStatus.Pending, SalesDeliveryAction.Delete)]
    [InlineData(SalesDeliveryStatus.Pending, SalesDeliveryAction.SubmitPosted)]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.Approve)]
    [InlineData(SalesDeliveryStatus.Posted, SalesDeliveryAction.Edit)]
    [InlineData(SalesDeliveryStatus.Posted, SalesDeliveryAction.Delete)]
    [InlineData(SalesDeliveryStatus.Posted, SalesDeliveryAction.Approve)]
    [InlineData(SalesDeliveryStatus.Rejected, SalesDeliveryAction.Reject)]
    public void RefusesInvalidOrRepeatedDecision(SalesDeliveryStatus current, SalesDeliveryAction action)
    {
        Action act = () => SalesDeliveryLifecycle.Next(current, action, true);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.DELIVERY.INVALID_STATUS");
    }

    [Theory]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.SubmitPending)]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.SubmitPosted)]
    [InlineData(SalesDeliveryStatus.Pending, SalesDeliveryAction.Approve)]
    public void RefusesSubmitOrFinalApprovalWhenOrderNoLongerApproved(
        SalesDeliveryStatus current, SalesDeliveryAction action)
    {
        Action act = () => SalesDeliveryLifecycle.Next(current, action, false);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.DELIVERY.ORDER_NOT_APPROVED");
    }

    [Fact]
    public void RejectionMayReleaseReservationWhenOrderNoLongerApproved()
    {
        SalesDeliveryLifecycle.Next(SalesDeliveryStatus.Pending, SalesDeliveryAction.Reject, false)
            .Should().Be(SalesDeliveryStatus.Rejected);
    }

    [Theory]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.Edit)]
    [InlineData(SalesDeliveryStatus.Draft, SalesDeliveryAction.Delete)]
    [InlineData(SalesDeliveryStatus.Rejected, SalesDeliveryAction.Edit)]
    public void LocalDraftChangesDoNotClaimOrderApproval(
        SalesDeliveryStatus current, SalesDeliveryAction action)
    {
        SalesDeliveryLifecycle.Next(current, action, false).Should().Be(SalesDeliveryStatus.Draft);
    }
}
