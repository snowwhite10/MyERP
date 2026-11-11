using MyERP.Modules.Purchasing.Application;
using MyERP.Shared.Errors;

namespace MyERP.ArchitectureTests;

public sealed class PurchaseInvoiceNumberTests
{
    [Theory]
    [InlineData("  ab-１２３  ", "ab-１２３", "AB-123")]
    [InlineData(" Ab 12 ", "Ab 12", "AB 12")]
    public void EquivalentNumbersHaveStableKeyAndKeepTrimmedDisplay(
        string input, string display, string key)
    {
        var result = PurchaseInvoiceNumber.Normalize(input);
        result.Display.Should().Be(display);
        result.Key.Should().Be(key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("A\tB")]
    [InlineData("\nA")]
    [InlineData("A\u200BB")]
    public void EmptyOrInvisibleCharactersReject(string? input)
    {
        Action act = () => PurchaseInvoiceNumber.Normalize(input);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER");
    }

    [Fact]
    public void UnpairedSurrogateRejectsWhenConstructedAtRuntime()
    {
        var input = string.Concat("A", (char)0xD800, "B");
        Action act = () => PurchaseInvoiceNumber.Normalize(input);
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER");
    }

    [Fact]
    public void LengthIsCheckedAfterCompatibilityNormalization()
    {
        PurchaseInvoiceNumber.Normalize(new string('Ａ', 80)).Key.Should().HaveLength(80);
        Action act = () => PurchaseInvoiceNumber.Normalize(new string('Ａ', 81));
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER");
    }

    [Fact]
    public void DisplayValueMustAlsoFitItsColumn()
    {
        Action act = () => PurchaseInvoiceNumber.Normalize(new string('A', 79) + "e\u0301");
        act.Should().Throw<BusinessRuleException>().Which.ErrorCode
            .Should().Be("PUR.INVOICE.INVALID_NUMBER");
    }
}
