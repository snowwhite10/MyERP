using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Time;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderQuotationSnapshotTests
{
    private static readonly IClock Clock = new FixedClock();

    [Fact]
    public void CopiesEverySavedCommercialFieldWithoutRepricing()
    {
        var quote = Quote();
        var order = SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);

        order.SourceQuotationId.Should().Be(42);
        order.DocumentNo.Should().Be("SO001");
        order.Status.Should().Be(SalesOrderStatus.Draft);
        order.CompanyId.Should().Be(7);
        order.CustomerId.Should().Be(9);
        order.CustomerCode.Should().Be("C9");
        order.CustomerName.Should().Be("Saved customer");
        order.BusinessDate.Should().Be(new DateOnly(2024, 3, 1));
        order.CurrencyCode.Should().Be("USD");
        order.BaseCurrencyCode.Should().Be("TWD");
        order.ExchangeRate.Should().Be(31.12345678m);
        order.OriginalNet.Should().Be(17.11m);
        order.OriginalTax.Should().Be(1.20m);
        order.OriginalGross.Should().Be(18.31m);
        order.BaseNet.Should().Be(532.55m);
        order.BaseTax.Should().Be(37.35m);
        order.BaseGross.Should().Be(569.90m);
        order.Remark.Should().Be("saved remark");
        order.Lines.Select(x => x.SourceQuotationLineId).Should().Equal(101, 102);
        order.Lines.Select(x => x.LineNo).Should().Equal(1, 2);
        var source = quote.Lines[0];
        var copied = order.Lines[0];
        copied.Should().BeEquivalentTo(source, options => options
            .Excluding(x => x.Id).Excluding(x => x.QuotationId)
            .Excluding(x => x.CreatedAt).Excluding(x => x.CreatedBy)
            .Excluding(x => x.UpdatedAt).Excluding(x => x.UpdatedBy));
        copied.Should().NotBeSameAs(source);
        copied.Id.Should().Be(0);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(2, true)]
    public void RejectsIncompleteOrDuplicatedSavedLines(int expectedCount, bool duplicateId)
    {
        var quote = Quote();
        if (duplicateId) quote.Lines[1].Id = quote.Lines[0].Id;
        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, expectedCount, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
    }

    [Fact]
    public void RejectsLineFromAnotherQuotationOrCompany()
    {
        var quote = Quote();
        quote.Lines[1].QuotationId = 99;
        Action wrongParent = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        wrongParent.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
        quote.Lines[1].QuotationId = quote.Id;
        quote.Lines[1].CompanyId = 8;
        Action wrongCompany = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        wrongCompany.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
    }

    [Fact]
    public void RejectsDuplicateLineNumberAndLeavesSourceUntouched()
    {
        var quote = Quote();
        quote.Lines[1].LineNo = 1;
        Action duplicate = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        duplicate.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
        quote.Lines.Select(line => line.Id).Should().Equal(101, 102);
        quote.Status.Should().Be(SalesQuotationStatus.Approved);
    }

    [Fact]
    public void UsesSavedLineNumbersWhenLoadOrderIsReversed()
    {
        var quote = Quote();
        quote.Lines.Reverse();
        var order = SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        order.Lines.Select(line => line.SourceQuotationLineId).Should().Equal(101, 102);
        quote.Lines.Select(line => line.Id).Should().Equal(102, 101);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RejectsEachInconsistentSavedHeaderTotal(int component)
    {
        var quote = Quote();
        var line = quote.Lines[1];
        switch (component)
        {
            case 0: line.OriginalNet += .01m; break;
            case 1: line.OriginalTax += .01m; break;
            case 2: line.OriginalGross += .01m; break;
            case 3: line.BaseNet += .01m; break;
            case 4: line.BaseTax += .01m; break;
            case 5: line.BaseGross += .01m; break;
        }

        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
    }

    [Fact]
    public void RejectsOverflowingSavedLineSum()
    {
        var quote = Quote();
        quote.Lines[0].OriginalNet = decimal.MaxValue;
        quote.Lines[1].OriginalNet = 1m;
        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsInternallyInconsistentSavedLineEvenWhenHeaderSumsMatch(bool baseCurrency)
    {
        var quote = Quote();
        var line = quote.Lines[0];
        if (baseCurrency)
        {
            line.BaseGross += .01m;
            quote.BaseGross += .01m;
        }
        else
        {
            line.OriginalGross += .01m;
            quote.OriginalGross += .01m;
        }

        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void RejectsInvalidSavedCommercialIdentityBeforeCopying(int component)
    {
        var quote = Quote();
        switch (component)
        {
            case 0: quote.CustomerId = 0; break;
            case 1: quote.BusinessDate = default; break;
            case 2: quote.CurrencyCode = ""; break;
            case 3: quote.ExchangeRate = 0; break;
            case 4: quote.Lines[0].ItemId = 0; break;
            case 5: quote.Lines[0].Quantity = 0; break;
            case 6: quote.Lines[0].UnitPrice = -1; break;
            case 7: quote.Lines[0].DiscountRate = 101; break;
            case 8: quote.Lines[0].TaxRateId = 0; break;
            case 9: quote.Lines[0].TaxRatePercent = -1; break;
            case 10: quote.Lines[0].Quantity = 1.0000001m; break;
        }

        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
        quote.Status.Should().Be(SalesQuotationStatus.Approved);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)]
    public void RejectsSavedTextOutsideOrderColumnsOrMissingExpiryDate(int component)
    {
        var quote = Quote();
        switch (component)
        {
            case 0: quote.CustomerCode = new string('C', 51); break;
            case 1: quote.CustomerName = new string('N', 201); break;
            case 2: quote.Remark = new string('R', 501); break;
            case 3: quote.Lines[0].ItemCode = new string('I', 51); break;
            case 4: quote.Lines[0].ItemName = new string('N', 201); break;
            case 5: quote.Lines[0].ItemCode = " "; break;
            case 6: quote.ExpiryDate = default; break;
        }

        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
        quote.Status.Should().Be(SalesQuotationStatus.Approved);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(2, 0)]
    [InlineData(3, 0)] [InlineData(4, 0)] [InlineData(5, 0)]
    [InlineData(0, 1)] [InlineData(1, 1)] [InlineData(2, 1)]
    [InlineData(3, 1)] [InlineData(4, 1)] [InlineData(5, 1)]
    [InlineData(0, 2)] [InlineData(1, 2)] [InlineData(2, 2)]
    [InlineData(3, 2)] [InlineData(4, 2)] [InlineData(5, 2)]
    public void RejectsSavedAmountOutsideSqlMoneyDomainEvenWhenTotalsBalance(int component, int defect)
    {
        var quote = Quote();
        var line = quote.Lines[0];
        var invalid = defect switch
        {
            0 => -2m,
            1 => 1_000_000_000_000_000m,
            _ => 2.00001m
        };
        var baseCurrency = component >= 3;
        var kind = component % 3;
        if (baseCurrency)
        {
            if (kind == 0) { line.BaseNet = invalid; line.BaseGross = invalid + line.BaseTax; }
            if (kind == 1) { line.BaseTax = invalid; line.BaseGross = line.BaseNet + invalid; }
            if (kind == 2) { line.BaseGross = invalid; line.BaseNet = invalid - line.BaseTax; }
        }
        else
        {
            if (kind == 0) { line.OriginalNet = invalid; line.OriginalGross = invalid + line.OriginalTax; }
            if (kind == 1) { line.OriginalTax = invalid; line.OriginalGross = line.OriginalNet + invalid; }
            if (kind == 2) { line.OriginalGross = invalid; line.OriginalNet = invalid - line.OriginalTax; }
        }
        quote.OriginalNet = quote.Lines.Sum(x => x.OriginalNet);
        quote.OriginalTax = quote.Lines.Sum(x => x.OriginalTax);
        quote.OriginalGross = quote.Lines.Sum(x => x.OriginalGross);
        quote.BaseNet = quote.Lines.Sum(x => x.BaseNet);
        quote.BaseTax = quote.Lines.Sum(x => x.BaseTax);
        quote.BaseGross = quote.Lines.Sum(x => x.BaseGross);

        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
        quote.Status.Should().Be(SalesQuotationStatus.Approved);
    }

    [Fact]
    public void RejectsConvertedExpiredDeletedOrForeignSource()
    {
        var quote = Quote();
        Action converted = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, true, Clock);
        converted.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.QUOTATION.ALREADY_CONVERTED");
        quote.ExpiryDate = new DateOnly(2024, 2, 29);
        Action expired = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        expired.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.QUOTATION.EXPIRED");
        quote.ExpiryDate = new DateOnly(2024, 3, 1);
        quote.IsDeleted = true;
        Action deleted = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        deleted.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.DOCUMENT.NOT_FOUND");
        quote.IsDeleted = false;
        Action foreign = () => SalesOrderQuotationSnapshot.Build(8, "SO001", quote, 2, false, Clock);
        foreign.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.DOCUMENT.NOT_FOUND");
        quote.Status = SalesQuotationStatus.Voided;
        Action voided = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);
        voided.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.QUOTATION.INVALID_STATUS");
    }

    [Fact]
    public void RejectsSavedQuotationWhoseExpiryPrecedesItsBusinessDate()
    {
        var quote = Quote();
        quote.BusinessDate = new DateOnly(2024, 3, 2);

        Action act = () => SalesOrderQuotationSnapshot.Build(7, "SO001", quote, 2, false, Clock);

        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.SOURCE_INCOMPLETE");
        quote.BusinessDate.Should().Be(new DateOnly(2024, 3, 2));
        quote.Status.Should().Be(SalesQuotationStatus.Approved);
    }

    private static SalesQuotation Quote()
    {
        var quote = new SalesQuotation
        {
            Id = 42, CompanyId = 7, DocumentNo = "SQ001", CustomerId = 9,
            CustomerCode = "C9", CustomerName = "Saved customer",
            BusinessDate = new DateOnly(2024, 3, 1), ExpiryDate = new DateOnly(2024, 3, 1),
            CurrencyCode = "USD", BaseCurrencyCode = "TWD", ExchangeRate = 31.12345678m,
            Status = SalesQuotationStatus.Approved, OriginalNet = 17.11m,
            OriginalTax = 1.20m, OriginalGross = 18.31m, BaseNet = 532.55m,
            BaseTax = 37.35m, BaseGross = 569.90m, Remark = "saved remark"
        };
        quote.Lines.Add(new SalesQuotationLine
        {
            Id = 101, CompanyId = 7, QuotationId = 42, LineNo = 1,
            ItemId = 11, ItemCode = "I11", ItemName = "Old item",
            Quantity = 1.234567m, UnitPrice = 10.123456m, DiscountRate = 2.5000m,
            TaxRateId = 5, TaxRatePercent = 7.0000m, OriginalNet = 12.18m,
            OriginalTax = 0.85m, OriginalGross = 13.03m, BaseNet = 379.05m,
            BaseTax = 26.45m, BaseGross = 405.50m
        });
        quote.Lines.Add(new SalesQuotationLine
        {
            Id = 102, CompanyId = 7, QuotationId = 42, LineNo = 2,
            ItemId = 12, ItemCode = "I12", ItemName = "Saved second",
            Quantity = 1m, UnitPrice = 4.93m, DiscountRate = 0m,
            TaxRateId = 5, TaxRatePercent = 7m, OriginalNet = 4.93m,
            OriginalTax = 0.35m, OriginalGross = 5.28m, BaseNet = 153.50m,
            BaseTax = 10.90m, BaseGross = 164.40m
        });
        return quote;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => new(2024, 3, 1, 0, 0, 0, TimeSpan.FromHours(8));
        public DateTime LocalNow => Now.DateTime;
        public DateOnly Today => new(2024, 3, 1);
        public TimeSpan Offset => TimeSpan.FromHours(8);
    }
}
