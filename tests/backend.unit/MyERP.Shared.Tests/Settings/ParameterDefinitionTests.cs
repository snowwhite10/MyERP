using MyERP.Shared.Errors;
using MyERP.Shared.Settings;

namespace MyERP.Shared.Tests.Settings;

/// <summary>SYS-PARAM-001：系統參數定義的鍵格式、預設值與輸入正規化（REQ_F4118.17.3）。</summary>
public sealed class ParameterDefinitionTests
{
    [Theory]
    [InlineData("SYS.QUANTITY_DECIMALS")]
    [InlineData("INV.ALLOW_NEGATIVE_STOCK")]
    [InlineData("MD.FIELD1_LABEL")]
    public void Key_AcceptsPrefixDotName(string key)
    {
        ParameterKey.IsValid(key).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sys.quantity")]
    [InlineData("SYS")]
    [InlineData("SYS.A.B")]
    [InlineData("SYS.1ABC")]
    [InlineData("SYS.HAS SPACE")]
    public void Key_RejectsOtherFormats(string? key)
    {
        ParameterKey.IsValid(key).Should().BeFalse();
    }

    [Fact]
    public void Key_RejectsOver80Characters()
    {
        ParameterKey.IsValid("SYS." + new string('A', 77)).Should().BeFalse();
        ParameterKey.IsValid("SYS." + new string('A', 76)).Should().BeTrue();
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData(" TRUE ", "true")]
    [InlineData("False", "false")]
    public void Boolean_NormalizesToLowerCase(string input, string expected)
    {
        ParameterDefinition.Boolean("SYS.FLAG", "SYS", "general", false).Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void Boolean_RejectsOtherText(string? input)
    {
        var act = () => ParameterDefinition.Boolean("SYS.FLAG", "SYS", "general", false).Normalize(input);
        act.Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("value");
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData(" 4 ", "4")]
    [InlineData("+3", "3")]
    public void Integer_AcceptsValuesInsideRange(string input, string expected)
    {
        ParameterDefinition.WholeNumber("SYS.DIGITS", "SYS", "general", 2, 0, 4).Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("5")]
    [InlineData("2.5")]
    [InlineData("abc")]
    [InlineData("")]
    public void Integer_RejectsOutOfRangeOrNonInteger(string input)
    {
        var act = () => ParameterDefinition.WholeNumber("SYS.DIGITS", "SYS", "general", 2, 0, 4).Normalize(input);
        act.Should().Throw<RequestValidationException>();
    }

    [Theory]
    [InlineData("0.5", "0.5")]
    [InlineData("10", "10")]
    [InlineData("1.2500", "1.25")]
    public void Decimal_UsesInvariantCultureAndTrimsTrailingZeros(string input, string expected)
    {
        ParameterDefinition.Number("SYS.RATE", "SYS", "general", 1m, 0m, 10m).Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("10.01")]
    [InlineData("-0.1")]
    [InlineData("1,5")]
    [InlineData("1e3")]
    public void Decimal_RejectsOutOfRangeOrLocalisedText(string input)
    {
        var act = () => ParameterDefinition.Number("SYS.RATE", "SYS", "general", 1m, 0m, 10m).Normalize(input);
        act.Should().Throw<RequestValidationException>();
    }

    [Fact]
    public void Text_TrimsAndChecksLength()
    {
        var definition = ParameterDefinition.Text("SYS.TITLE", "SYS", "general", "", 5);
        definition.Normalize("  繁體  ").Should().Be("繁體");
        definition.Normalize(null).Should().Be("");
        var act = () => definition.Normalize("123456");
        act.Should().Throw<RequestValidationException>();
    }

    [Fact]
    public void Option_AcceptsDeclaredOptionsIgnoringCase()
    {
        var definition = ParameterDefinition.Option("SYS.MODE", "SYS", "general", "Average", ["Average", "Fifo"]);
        definition.Normalize("fifo").Should().Be("Fifo");
        var act = () => definition.Normalize("Lifo");
        act.Should().Throw<RequestValidationException>();
    }

    [Fact]
    public void Factories_RejectInvalidDeclarations()
    {
        FluentActions.Invoking(() => ParameterDefinition.WholeNumber("bad key", "SYS", "general", 1, 0, 4)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ParameterDefinition.WholeNumber("SYS.DIGITS", "SYS", "general", 9, 0, 4)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ParameterDefinition.WholeNumber("SYS.DIGITS", "SYS", "general", 1, 5, 4)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ParameterDefinition.Option("SYS.MODE", "SYS", "general", "X", ["A", "B"])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ParameterDefinition.Option("SYS.MODE", "SYS", "general", "A", [])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ParameterDefinition.Text("SYS.TITLE", "SYS", "general", "", ParameterDefinition.MaxValueLength + 1)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => ParameterDefinition.Boolean("SYS.FLAG", "SYS", "", false)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultValue_IsStoredInNormalizedForm()
    {
        ParameterDefinition.Boolean("SYS.FLAG", "SYS", "general", true).DefaultValue.Should().Be("true");
        ParameterDefinition.Number("SYS.RATE", "SYS", "general", 1.50m, 0m, 10m).DefaultValue.Should().Be("1.5");
        ParameterDefinition.WholeNumber("SYS.DIGITS", "SYS", "general", 2, 0, 4).DefaultValue.Should().Be("2");
    }
}
