using System.Globalization;
using MyERP.Shared.Monetary;

namespace MyERP.Shared.Tests.Monetary;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("TWD", 0)]
    [InlineData("JPY", 0)]
    [InlineData("USD", 2)]
    [InlineData("HKD", 2)]
    [InlineData("CNY", 2)]
    [InlineData("EUR", 2)]
    public void Decimals_DefaultPrecisionPerCurrency(string currency, int expected)
    {
        Money.Decimals(currency).Should().Be(expected);
    }

    [Theory]
    [InlineData("100.5", "TWD", "101")]
    [InlineData("100.49", "TWD", "100")]
    [InlineData("-100.5", "TWD", "-101")]
    [InlineData("2.5", "JPY", "3")]
    [InlineData("1.005", "USD", "1.01")]
    [InlineData("1.004", "USD", "1.00")]
    [InlineData("-1.005", "USD", "-1.01")]
    [InlineData("0", "USD", "0")]
    public void Round_UsesAwayFromZeroAtCurrencyPrecision(string value, string currency, string expected)
    {
        Money.Round(decimal.Parse(value, CultureInfo.InvariantCulture), currency)
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Round_IsNotBankersRounding()
    {
        // 銀行家捨入會得到 2；ERP 金額規則是四捨五入（AwayFromZero）
        Money.Round(2.5m, "TWD").Should().Be(3m);
    }

    [Fact]
    public void Round_LowercaseCurrency_IsNormalized()
    {
        Money.Round(10.5m, "twd").Should().Be(11m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NT")]
    [InlineData("NTD$")]
    [InlineData("12A")]
    public void Round_InvalidCurrencyCode_Throws(string? currency)
    {
        var act = () => Money.Round(1m, currency!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Round_WithCustomPrecision_OverridesDefaults()
    {
        var precision = CurrencyPrecision.Default.With("TWD", 2).With("KWD", 3);

        Money.Round(1.005m, "TWD", precision).Should().Be(1.01m);
        Money.Round(1.0005m, "KWD", precision).Should().Be(1.001m);
        Money.Round(1.005m, "USD", precision).Should().Be(1.01m);
    }

    [Fact]
    public void Round_WithMidpointMode_IsConfigurable()
    {
        var precision = CurrencyPrecision.Default with { Rounding = MidpointRounding.ToEven };

        Money.Round(2.5m, "TWD", precision).Should().Be(2m);
    }

    [Fact]
    public void CurrencyPrecision_With_DoesNotMutateDefault()
    {
        _ = CurrencyPrecision.Default.With("TWD", 2);

        CurrencyPrecision.Default.DecimalsFor("TWD").Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void CurrencyPrecision_With_RejectsOutOfRangeDecimals(int decimals)
    {
        var act = () => CurrencyPrecision.Default.With("TWD", decimals);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Round_NullPrecision_Throws()
    {
        var act = () => Money.Round(1m, "TWD", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
