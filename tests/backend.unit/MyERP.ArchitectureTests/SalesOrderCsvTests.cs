using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Domain;

namespace MyERP.ArchitectureTests;

/// <summary>SD-ORDER-EXPORT-001–008: order CSV text follows the request language and is spreadsheet-safe.</summary>
public sealed class SalesOrderCsvTests
{
    private static SalesOrderCsvRow Row(string customerName = "Acme, \"Ltd\"", string? remark = null,
        SalesOrderStatus status = SalesOrderStatus.Approved) => new(
        "SO-001", new DateOnly(2028, 2, 29), status, "C21", customerName, "USD", 31.123456m,
        1, "I9", "Widget", 1000.5m, 12.345678m, 0.1250m, 5m, 10.000001m, 0.5m, 10.500001m,
        311.23m, 15.56m, 326.79m, remark);

    [Theory]
    [InlineData("zh-Hant", "單號", "備註")]
    [InlineData("zh-Hans", "单号", "备注")]
    [InlineData("en", "Order no.", "Remark")]
    public void HeaderLineFollowsLanguage(string language, string first, string contained)
    {
        var header = SalesOrderCsv.HeaderLine(language);
        header.Split(',')[0].Should().Be(first);
        header.Should().Contain(contained);
        header.Split(',').Should().HaveCount(SalesOrderCsv.Headers("en").Count);
    }

    [Fact]
    public void HeadersHaveSameColumnCountInEveryLanguageAndUnknownFallsBackToTraditional()
    {
        var en = SalesOrderCsv.Headers("en");
        SalesOrderCsv.Headers("zh-Hant").Should().HaveCount(en.Count);
        SalesOrderCsv.Headers("zh-Hans").Should().HaveCount(en.Count);
        SalesOrderCsv.Headers("fr").Should().Equal(SalesOrderCsv.Headers("zh-Hant"));
        en.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft, "zh-Hant", "草稿")]
    [InlineData(SalesOrderStatus.Pending, "zh-Hant", "審批中")]
    [InlineData(SalesOrderStatus.Rejected, "zh-Hant", "已駁回")]
    [InlineData(SalesOrderStatus.Approved, "zh-Hant", "已核准")]
    [InlineData(SalesOrderStatus.Cancelled, "zh-Hant", "已取消")]
    [InlineData(SalesOrderStatus.Pending, "zh-Hans", "审批中")]
    [InlineData(SalesOrderStatus.Rejected, "zh-Hans", "已驳回")]
    [InlineData(SalesOrderStatus.Approved, "zh-Hans", "已批准")]
    [InlineData(SalesOrderStatus.Draft, "en", "Draft")]
    [InlineData(SalesOrderStatus.Pending, "en", "Pending approval")]
    [InlineData(SalesOrderStatus.Cancelled, "en", "Cancelled")]
    public void StatusTextFollowsLanguageAndMatchesScreenWording(SalesOrderStatus status, string language,
        string expected) => SalesOrderCsv.StatusText(status, language).Should().Be(expected);

    [Fact]
    public void UndefinedStatusIsRejectedInsteadOfPrintingNumber()
    {
        var act = () => SalesOrderCsv.StatusText((SalesOrderStatus)99, "en");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LineUsesInvariantNumbersIsoDateAndQuotesText()
    {
        var line = SalesOrderCsv.Line(Row(remark: "two\nlines"), "en");
        line.Should().Be("SO-001,2028-02-29,Approved,C21,\"Acme, \"\"Ltd\"\"\",USD,31.123456,1,I9,Widget,"
            + "1000.5,12.345678,0.1250,5,10.000001,0.5,10.500001,311.23,15.56,326.79,\"two\nlines\"");
    }

    [Fact]
    public void LineTextFollowsLanguageButNumbersStayInvariant()
    {
        var line = SalesOrderCsv.Line(Row(customerName: "客戶"), "zh-Hans");
        line.Should().Contain(",已批准,").And.Contain(",1000.5,").And.NotContain("1,000.5");
    }

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
        SalesOrderCsv.Cell(value).Should().Be(expected);

    [Fact]
    public void QuotedFormulaKeepsGuardInsideQuotes() =>
        SalesOrderCsv.Cell("=1,2").Should().Be("\"'=1,2\"");

    [Fact]
    public void MissingRemarkIsEmptyLastCell() =>
        SalesOrderCsv.Line(Row(), "en").Should().EndWith(",326.79,");

    [Fact]
    public void RowLimitIsTenThousand() => SalesOrderCsv.MaxRows.Should().Be(10000);

    [Fact]
    public void OverLimitIsNamedBusinessRuleAndLimitItselfIsAllowed()
    {
        SalesOrderCsv.EnsureWithinLimit(10000);
        SalesOrderCsv.EnsureWithinLimit(0);
        var act = () => SalesOrderCsv.EnsureWithinLimit(10001);
        act.Should().Throw<MyERP.Shared.Errors.BusinessRuleException>()
            .Which.ErrorCode.Should().Be("SAL.ORDER.EXPORT_LIMIT");
    }
}
