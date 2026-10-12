using MyERP.Shared.Monetary;

namespace MyERP.Shared.Tests.Monetary;

public sealed class TaxTests
{
    [Theory]
    [InlineData(100, 5, false, "TWD", 100, 5, 105)]
    [InlineData(105, 5, true, "TWD", 100, 5, 105)]
    [InlineData(-105, 5, true, "TWD", -100, -5, -105)]
    [InlineData(10.01, 5, false, "USD", 10.01, 0.50, 10.51)]
    [InlineData(10.51, 5, true, "USD", 10.01, 0.50, 10.51)]
    [InlineData(100, 0, false, "USD", 100, 0, 100)]
    public void Calculate_UsesLineRoundingAndPreservesGrossIdentity(
        decimal amount, decimal rate, bool includesTax, string currency,
        decimal net, decimal tax, decimal gross)
    {
        var result = Tax.Calculate(amount, rate, includesTax, currency);

        result.Should().Be(new TaxAmounts(net, tax, gross));
        (result.Net + result.Tax).Should().Be(result.Gross);
    }

    [Fact]
    public void Calculate_UsesCurrencyMasterPrecision()
    {
        var precision = CurrencyPrecision.Default.With("TWD", 2);

        Tax.Calculate(10.05m, 5m, false, "TWD", precision)
            .Should().Be(new TaxAmounts(10.05m, 0.50m, 10.55m));
    }

    [Theory]
    [InlineData(-0.0001)]
    [InlineData(100.0001)]
    [InlineData(5.12345)]
    public void Calculate_RejectsOutOfRangeOrOverPrecisionRate(decimal rate)
    {
        var act = () => Tax.Calculate(100m, rate, false, "USD");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Calculate_RejectsNullPrecision()
    {
        var act = () => Tax.Calculate(100m, 5m, false, "USD", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
