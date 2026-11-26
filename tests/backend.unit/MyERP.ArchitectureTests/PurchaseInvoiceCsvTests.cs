using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>PUR-INV-EXPORT-U01–U05: invoice CSV text follows the request language and is spreadsheet-safe.</summary>
public sealed class PurchaseInvoiceCsvTests
{
    private static PurchaseInvoiceCsvRow Row(string number = "INV, \"A\"",
        PurchaseInvoiceStatus status = PurchaseInvoiceStatus.Disputed) => new(
        number, new DateOnly(2028, 2, 29), status, 7, 31, "USD", 1, 501, 1000.5m, 12.345678m, 5m,
        12351.000001m, 617.55m, 12968.550001m);

    [Theory]
    [InlineData("zh-Hant", "供應商發票號", "收貨行 ID")]
    [InlineData("zh-Hans", "供应商发票号", "收货行 ID")]
    [InlineData("en", "Supplier invoice no.", "Receipt line ID")]
    public void HeaderLineFollowsLanguage(string language, string first, string contained)
    {
        var header = PurchaseInvoiceCsv.HeaderLine(language);
        header.Split(',')[0].Should().Be(first);
        header.Should().Contain(contained);
        header.Split(',').Should().HaveCount(PurchaseInvoiceCsv.Headers("en").Count);
    }

    [Fact]
    public void HeadersHaveSameColumnCountInEveryLanguageAndUnknownFallsBackToTraditional()
    {
        var en = PurchaseInvoiceCsv.Headers("en");
        PurchaseInvoiceCsv.Headers("zh-Hant").Should().HaveCount(en.Count);
        PurchaseInvoiceCsv.Headers("zh-Hans").Should().HaveCount(en.Count);
        PurchaseInvoiceCsv.Headers("fr").Should().Equal(PurchaseInvoiceCsv.Headers("zh-Hant"));
        en.Should().OnlyHaveUniqueItems();
        PurchaseInvoiceCsv.Headers("zh-Hant").Should().OnlyHaveUniqueItems();
        PurchaseInvoiceCsv.Headers("zh-Hans").Should().OnlyHaveUniqueItems();
    }

    /// <summary>Same wording as the 介面 4.19 status tags (pur.invoice.state.*).</summary>
    [Theory]
    [InlineData(PurchaseInvoiceStatus.Draft, "zh-Hant", "草稿")]
    [InlineData(PurchaseInvoiceStatus.Matched, "zh-Hant", "已比對")]
    [InlineData(PurchaseInvoiceStatus.Disputed, "zh-Hant", "有差異")]
    [InlineData(PurchaseInvoiceStatus.Rejected, "zh-Hant", "已拒絕")]
    [InlineData(PurchaseInvoiceStatus.Posted, "zh-Hant", "已過帳")]
    [InlineData(PurchaseInvoiceStatus.Draft, "zh-Hans", "草稿")]
    [InlineData(PurchaseInvoiceStatus.Matched, "zh-Hans", "已比对")]
    [InlineData(PurchaseInvoiceStatus.Disputed, "zh-Hans", "有差异")]
    [InlineData(PurchaseInvoiceStatus.Rejected, "zh-Hans", "已拒绝")]
    [InlineData(PurchaseInvoiceStatus.Posted, "zh-Hans", "已过账")]
    [InlineData(PurchaseInvoiceStatus.Draft, "en", "Draft")]
    [InlineData(PurchaseInvoiceStatus.Matched, "en", "Matched")]
    [InlineData(PurchaseInvoiceStatus.Disputed, "en", "Disputed")]
    [InlineData(PurchaseInvoiceStatus.Rejected, "en", "Rejected")]
    [InlineData(PurchaseInvoiceStatus.Posted, "en", "Posted")]
    public void StatusTextFollowsLanguageAndMatchesScreenWording(PurchaseInvoiceStatus status, string language,
        string expected) => PurchaseInvoiceCsv.StatusText(status, language).Should().Be(expected);

    [Fact]
    public void UndefinedStatusIsRejectedInsteadOfPrintingNumber()
    {
        var act = () => PurchaseInvoiceCsv.StatusText((PurchaseInvoiceStatus)99, "en");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LineUsesInvariantNumbersIsoDateAndQuotesText() =>
        PurchaseInvoiceCsv.Line(Row(), "en").Should().Be(
            "\"INV, \"\"A\"\"\",2028-02-29,Disputed,7,31,USD,1,501,1000.5,12.345678,5,12351.000001,617.55,12968.550001");

    [Fact]
    public void LineTextFollowsLanguageButNumbersStayInvariant() =>
        PurchaseInvoiceCsv.Line(Row(number: "發票"), "zh-Hans").Should()
            .Be("發票,2028-02-29,有差异,7,31,USD,1,501,1000.5,12.345678,5,12351.000001,617.55,12968.550001");

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("  =x", "'  =x")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TextCellsAreNeutralisedAgainstFormulaInjection(string? value, string expected) =>
        PurchaseInvoiceCsv.Cell(value).Should().Be(expected);

    [Fact]
    public void QuotedFormulaKeepsGuardInsideQuotes() =>
        PurchaseInvoiceCsv.Cell("=1,2").Should().Be("\"'=1,2\"");

    [Fact]
    public void OverLimitIsNamedBusinessRuleAndLimitItselfIsAllowed()
    {
        PurchaseInvoiceCsv.MaxRows.Should().Be(10000);
        PurchaseInvoiceCsv.EnsureWithinLimit(10000);
        PurchaseInvoiceCsv.EnsureWithinLimit(0);
        var act = () => PurchaseInvoiceCsv.EnsureWithinLimit(10001);
        act.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>()
            .Which.ErrorCode.Should().Be("PUR.INVOICE.EXPORT_LIMIT");
    }
}
