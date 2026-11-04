using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderCreateSourceTests
{
    [Fact]
    public void AcceptsManualAndConvertedDrafts()
    {
        SalesOrderCreateSource.Validate(Order(null, null));
        SalesOrderCreateSource.Validate(Order(42, 101));
    }

    [Theory]
    [InlineData(null, 101L)]
    [InlineData(0L, 101L)]
    [InlineData(42L, null)]
    [InlineData(42L, 0L)]
    public void RejectsMixedOrMissingSourceIds(long? quotationId, long? lineId)
    {
        Action act = () => SalesOrderCreateSource.Validate(Order(quotationId, lineId));
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.INVALID_SOURCE");
    }

    [Fact]
    public void RejectsDuplicateSavedLineIds()
    {
        var order = Order(42, 101);
        order.Lines.Add(Line(2, 101));
        Action act = () => SalesOrderCreateSource.Validate(order);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.INVALID_SOURCE");
    }

    [Theory]
    [InlineData(SalesOrderStatus.Pending, false)]
    [InlineData(SalesOrderStatus.Draft, true)]
    public void RejectsNonDraftOrDeleted(SalesOrderStatus status, bool deleted)
    {
        var order = Order(42, 101);
        order.Status = status;
        order.IsDeleted = deleted;
        Action act = () => SalesOrderCreateSource.Validate(order);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode.Should().Be("SAL.ORDER.INVALID_STATUS");
    }

    private static SalesOrder Order(long? quotationId, long? lineId)
    {
        var order = new SalesOrder
        {
            CompanyId = 7, DocumentNo = "SO001", CustomerId = 9, CustomerCode = "C9",
            CustomerName = "Customer", CurrencyCode = "USD", BaseCurrencyCode = "TWD",
            SourceQuotationId = quotationId
        };
        order.Lines.Add(Line(1, lineId));
        return order;
    }

    private static SalesOrderLine Line(int lineNo, long? sourceId) => new()
    {
        CompanyId = 7, LineNo = lineNo, ItemId = 11, ItemCode = "I11", ItemName = "Item",
        SourceQuotationLineId = sourceId
    };
}
