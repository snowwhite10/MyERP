using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;
using MyERP.Shared.Time;

namespace MyERP.ArchitectureTests;

public sealed class SalesQuotationLifecycleTests
{
    private static readonly FixedClock Clock = new(new DateOnly(2024, 3, 1));

    [Theory]
    [InlineData(SalesQuotationStatus.Draft, SalesQuotationAction.Edit, SalesQuotationStatus.Draft)]
    [InlineData(SalesQuotationStatus.Rejected, SalesQuotationAction.Edit, SalesQuotationStatus.Draft)]
    [InlineData(SalesQuotationStatus.Draft, SalesQuotationAction.SubmitPending, SalesQuotationStatus.Pending)]
    [InlineData(SalesQuotationStatus.Draft, SalesQuotationAction.SubmitApproved, SalesQuotationStatus.Approved)]
    [InlineData(SalesQuotationStatus.Pending, SalesQuotationAction.Approve, SalesQuotationStatus.Approved)]
    [InlineData(SalesQuotationStatus.Pending, SalesQuotationAction.Reject, SalesQuotationStatus.Rejected)]
    [InlineData(SalesQuotationStatus.Approved, SalesQuotationAction.Void, SalesQuotationStatus.Voided)]
    [InlineData(SalesQuotationStatus.Approved, SalesQuotationAction.Convert, SalesQuotationStatus.Approved)]
    public void AllowsSpecifiedTransitions(SalesQuotationStatus status, SalesQuotationAction action,
        SalesQuotationStatus expected)
    {
        SalesQuotationLifecycle.Next(status, action, new DateOnly(2024, 3, 1), false, Clock)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(SalesQuotationStatus.Draft, SalesQuotationAction.Approve)]
    [InlineData(SalesQuotationStatus.Rejected, SalesQuotationAction.SubmitPending)]
    [InlineData(SalesQuotationStatus.Pending, SalesQuotationAction.Edit)]
    [InlineData(SalesQuotationStatus.Pending, SalesQuotationAction.SubmitApproved)]
    [InlineData(SalesQuotationStatus.Approved, SalesQuotationAction.Edit)]
    [InlineData(SalesQuotationStatus.Voided, SalesQuotationAction.Convert)]
    [InlineData(SalesQuotationStatus.Voided, SalesQuotationAction.Void)]
    public void RejectsInvalidTransitions(SalesQuotationStatus status, SalesQuotationAction action)
    {
        Action act = () => SalesQuotationLifecycle.Next(status, action, new DateOnly(2024, 3, 1), false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.QUOTATION.INVALID_STATUS");
    }

    [Theory]
    [InlineData(SalesQuotationAction.SubmitPending)]
    [InlineData(SalesQuotationAction.SubmitApproved)]
    [InlineData(SalesQuotationAction.Convert)]
    public void ExpiryIsInclusiveAndOnlyCheckedForSubmitOrConvert(SalesQuotationAction action)
    {
        var status = action == SalesQuotationAction.Convert ? SalesQuotationStatus.Approved : SalesQuotationStatus.Draft;
        SalesQuotationLifecycle.Next(status, action, Clock.Today, false, Clock).Should().NotBe(SalesQuotationStatus.Voided);
        Action expired = () => SalesQuotationLifecycle.Next(status, action, Clock.Today.AddDays(-1), false, Clock);
        expired.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.QUOTATION.EXPIRED");
        SalesQuotationLifecycle.Next(SalesQuotationStatus.Approved, SalesQuotationAction.Void,
            Clock.Today.AddDays(-1), false, Clock).Should().Be(SalesQuotationStatus.Voided);
    }

    [Theory]
    [InlineData(SalesQuotationAction.Convert)]
    [InlineData(SalesQuotationAction.Void)]
    public void ConvertedQuoteCannotConvertOrVoid(SalesQuotationAction action)
    {
        Action act = () => SalesQuotationLifecycle.Next(SalesQuotationStatus.Approved, action,
            Clock.Today, true, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.QUOTATION.ALREADY_CONVERTED");
    }

    [Fact]
    public void StoredApprovalDoesNotChangeWhenExpired()
    {
        SalesQuotationLifecycle.IsExpired(new DateOnly(2024, 2, 29), Clock).Should().BeTrue();
        SalesQuotationLifecycle.IsExpired(Clock.Today, Clock).Should().BeFalse();
    }

    private sealed record FixedClock(DateOnly Today) : IClock
    {
        public DateTimeOffset Now => new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
        public DateTime LocalNow => Today.ToDateTime(TimeOnly.MinValue);
        public TimeSpan Offset => TimeSpan.FromHours(8);
    }
}
