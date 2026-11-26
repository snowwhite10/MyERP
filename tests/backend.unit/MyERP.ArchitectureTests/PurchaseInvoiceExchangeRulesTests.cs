using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

/// <summary>WU-PUR-008b2 (ADR-206): saved purchase-order rate snapshot + company base currency → FI exchange snapshot.</summary>
public sealed class PurchaseInvoiceExchangeRulesTests
{
    private static PurchaseInvoiceOrderRateSnapshot Order(string currency = "USD", string? baseCurrency = "TWD",
        decimal? rate = 31.5m) => new(currency, baseCurrency, rate);

    private static string Code(Action act) => act.Should().Throw<BusinessRuleException>().Which.ErrorCode;

    [Fact]
    public void PUR_FXSRC_001_ForeignOrderSnapshotIsUsedAndFeedsTheFinanceMapping()
    {
        var exchange = PurchaseInvoiceExchangeRules.Resolve("USD", Order(), "TWD");

        exchange.Should().Be(new PurchaseInvoiceExchangeSnapshot("TWD", 31.5m));
        var saved = new PurchaseInvoiceFinanceSaved(1, 7, 2, 3, PurchaseInvoiceStatus.Matched, false,
            new DateOnly(2026, 11, 27), "USD", 10m, 0.5m, 10.5m, [new(1, 10m, 0.5m, 10.5m)]);
        var snapshot = PurchaseInvoiceFinanceSnapshot.Build(saved, "PUR-INVOICE-POST-v1:x", exchange);
        snapshot.BaseCurrency.Should().Be("TWD");
        snapshot.BaseNetAmount.Should().Be(315m);
        snapshot.ExchangeRate.Should().Be(31.5m);
    }

    [Fact]
    public void PUR_FXSRC_002_BaseCurrencyOrderUsesRateOne() =>
        PurchaseInvoiceExchangeRules.Resolve("TWD", Order("TWD", "TWD", 1m), "TWD")
            .Should().Be(new PurchaseInvoiceExchangeSnapshot("TWD", 1m));

    [Fact]
    public void PUR_FXSRC_003_OrderWithoutRateSnapshotIsRejectedNotFilledWithTodaysRate() =>
        Code(() => PurchaseInvoiceExchangeRules.Resolve("USD", Order(baseCurrency: null, rate: null), "TWD"))
            .Should().Be("PUR.INVOICE.RATE_SNAPSHOT_MISSING");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void PUR_FXSRC_004_CompanyWithoutBaseCurrencyIsRejected(string? companyBase) =>
        Code(() => PurchaseInvoiceExchangeRules.Resolve("USD", Order(), companyBase))
            .Should().Be("PUR.INVOICE.BASE_CURRENCY_NOT_SET");

    [Fact]
    public void PUR_FXSRC_005_SnapshotBaseDifferentFromCompanyBaseIsRejected() =>
        Code(() => PurchaseInvoiceExchangeRules.Resolve("USD", Order(), "HKD"))
            .Should().Be("PUR.INVOICE.BASE_CURRENCY_CHANGED");

    public static TheoryData<string, string, string?, decimal?> InvalidData => new()
    {
        { "EUR", "USD", "TWD", 31.5m },      // invoice currency differs from the order
        { "USD", "USD", "TWD", null },       // half snapshot
        { "USD", "USD", null, 31.5m },       // half snapshot
        { "USD", "USD", "TWD", 0m },
        { "USD", "USD", "TWD", -1m },
        { "USD", "USD", "TWD", 31.123456789m },
        { "TWD", "TWD", "TWD", 1.5m },       // same currency must be 1
        { "usd", "usd", "TWD", 31.5m },
        { "USD", "USD", "twd", 31.5m },
        { "", "", "TWD", 31.5m },
    };

    [Theory]
    [MemberData(nameof(InvalidData))]
    public void PUR_FXSRC_006_InconsistentDataIsAProgrammingErrorNotAPartialSnapshot(string invoiceCurrency,
        string orderCurrency, string? snapshotBase, decimal? rate) =>
        Code(() => PurchaseInvoiceExchangeRules.Resolve(invoiceCurrency, Order(orderCurrency, snapshotBase, rate), "TWD"))
            .Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");

    [Fact]
    public void PUR_FXSRC_006_NullOrderIsRejected() =>
        Code(() => PurchaseInvoiceExchangeRules.Resolve("USD", null!, "TWD"))
            .Should().Be("PUR.INVOICE.INVALID_POSTING_SNAPSHOT");

    [Fact]
    public void PUR_FXSRC_007_EightDecimalRateIsKeptAndCompanyBaseCaseIsNormalised() =>
        PurchaseInvoiceExchangeRules.Resolve("USD", Order(rate: 31.12345678m), "twd")
            .Should().Be(new PurchaseInvoiceExchangeSnapshot("TWD", 31.12345678m));
}
