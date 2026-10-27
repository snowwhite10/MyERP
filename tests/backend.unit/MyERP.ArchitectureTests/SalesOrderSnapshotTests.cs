using Moq;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Shared.Monetary;

namespace MyERP.ArchitectureTests;

public sealed class SalesOrderSnapshotTests
{
    private static readonly DateOnly LeapDay = new(2028, 2, 29);

    [Fact]
    public async Task BuildsManualSnapshotFromCurrentCompanyReferencesAndDatedRates()
    {
        var customers = new Mock<ICustomerLookup>();
        customers.Setup(x => x.RequireApprovedAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomerIdentity(7, "C7", "Customer Seven"));
        var items = new Mock<ISalesItemLookup>();
        items.Setup(x => x.RequireActiveAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesItemIdentity(11, "I11", "Item Eleven"));
        var currencies = new Mock<ICurrencyLookup>();
        currencies.Setup(x => x.GetBaseCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("TWD", "Taiwan dollar", null, 0, true, true));
        currencies.Setup(x => x.FindCurrencyAsync("USD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyInfo("USD", "US dollar", null, 2, false, true));
        currencies.Setup(x => x.GetRateSnapshotAsync("USD", LeapDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeRateSnapshot(31m, LeapDay, LeapDay, 91));
        currencies.Setup(x => x.GetPrecisionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CurrencyPrecision.Default);
        var taxes = new Mock<ITaxRateLookup>();
        taxes.Setup(x => x.GetRateSnapshotAsync(5, TaxDirection.Output, LeapDay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaxRateSnapshot(5, 5m, LeapDay, LeapDay));

        var builder = new SalesOrderSnapshotBuilder(customers.Object, items.Object,
            currencies.Object, taxes.Object);
        var input = new SalesOrderDraftInput(7, LeapDay, "USD", "  memo  ",
            [new(1, 11, 2m, 1.005m, 0m, 5)]);
        var order = await builder.BuildManualAsync(3, "SO-1", input, TestContext.Current.CancellationToken);

        order.CompanyId.Should().Be(3);
        order.DocumentNo.Should().Be("SO-1");
        order.CustomerCode.Should().Be("C7");
        order.CustomerName.Should().Be("Customer Seven");
        order.CurrencyCode.Should().Be("USD");
        order.BaseCurrencyCode.Should().Be("TWD");
        order.ExchangeRate.Should().Be(31m);
        order.Remark.Should().Be("memo");
        order.Lines.Should().ContainSingle();
        var line = order.Lines.Single();
        line.ItemCode.Should().Be("I11");
        line.TaxRatePercent.Should().Be(5m);
        line.OriginalNet.Should().Be(2.01m);
        line.OriginalTax.Should().Be(0.10m);
        line.BaseGross.Should().Be(65m);
        order.BaseGross.Should().Be(line.BaseGross);
        taxes.Verify(x => x.GetRateSnapshotAsync(5, TaxDirection.Output, LeapDay,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidInputStopsBeforeAnyReferenceLookup()
    {
        var customers = new Mock<ICustomerLookup>(MockBehavior.Strict);
        var builder = new SalesOrderSnapshotBuilder(customers.Object,
            Mock.Of<ISalesItemLookup>(), Mock.Of<ICurrencyLookup>(), Mock.Of<ITaxRateLookup>());
        var input = new SalesOrderDraftInput(0, LeapDay, "USD", null, []);
        await Assert.ThrowsAsync<MyERP.Shared.Errors.RequestValidationException>(
            () => builder.BuildManualAsync(3, "SO-1", input, TestContext.Current.CancellationToken));
        customers.VerifyNoOtherCalls();
    }

    [Fact]
    public void UsesCompanyCurrencyPrecisionForLineAndBaseRounding()
    {
        var precision = CurrencyPrecision.Default.With("USD", 3).With("TWD", 0);
        var result = SalesOrderAmounts.Calculate(
            [new SalesOrderAmountInput(1, 1m, 1.005m, 0m, "USD", 0m, 31m)],
            "TWD", precision);
        result.Lines.Single().OriginalNet.Should().Be(1.005m);
        result.BaseNet.Should().Be(31m);
    }
}
