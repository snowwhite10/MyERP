using MyERP.Modules.Sales.Application;
using MyERP.Shared.Errors;
using MyERP.Shared.Time;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderQuotationSourceTests
{
    private static readonly FixedClock Clock = new(new DateOnly(2024, 3, 1));

    [Fact]
    public void ApprovedSameCompanyQuotationIsEligibleThroughExpiryDay()
    {
        SalesOrderQuotationSource.Validate(7, 7, SalesQuotationStatus.Approved,
            Clock.Today, false, Clock);
    }

    [Theory]
    [InlineData(SalesQuotationStatus.Draft)]
    [InlineData(SalesQuotationStatus.Pending)]
    [InlineData(SalesQuotationStatus.Rejected)]
    [InlineData(SalesQuotationStatus.Voided)]
    public void OtherQuotationStatesAreRejected(SalesQuotationStatus status)
    {
        Action act = () => SalesOrderQuotationSource.Validate(7, 7, status,
            Clock.Today, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.QUOTATION.INVALID_STATUS");
    }

    [Fact]
    public void ExpiredQuotationIsRejected()
    {
        Action act = () => SalesOrderQuotationSource.Validate(7, 7,
            SalesQuotationStatus.Approved, Clock.Today.AddDays(-1), false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.QUOTATION.EXPIRED");
    }

    [Fact]
    public void AlreadyConvertedQuotationIsRejected()
    {
        Action act = () => SalesOrderQuotationSource.Validate(7, 7,
            SalesQuotationStatus.Approved, Clock.Today, true, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.QUOTATION.ALREADY_CONVERTED");
    }

    [Fact]
    public void OtherCompanyQuotationIsHidden()
    {
        Action act = () => SalesOrderQuotationSource.Validate(7, 8,
            SalesQuotationStatus.Approved, Clock.Today, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.DOCUMENT.NOT_FOUND");
    }

    [Fact]
    public void InvalidCompanyIsRejected()
    {
        Action act = () => SalesOrderQuotationSource.Validate(0, 0,
            SalesQuotationStatus.Approved, Clock.Today, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("SAL.DOCUMENT.NOT_FOUND");
    }

    private sealed record FixedClock(DateOnly Today) : IClock
    {
        public DateTimeOffset Now => new(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
        public DateTime LocalNow => Today.ToDateTime(TimeOnly.MinValue);
        public TimeSpan Offset => TimeSpan.FromHours(8);
    }
}
