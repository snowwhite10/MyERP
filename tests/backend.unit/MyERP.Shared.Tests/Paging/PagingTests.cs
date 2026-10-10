using MyERP.Shared.Errors;
using MyERP.Shared.Paging;

namespace MyERP.Shared.Tests.Paging;

public sealed class PagingTests
{
    [Fact]
    public void PagedRequest_Defaults_Page1Size20()
    {
        var request = new PagedRequest();

        request.Page.Should().Be(1);
        request.PageSize.Should().Be(20);
        request.Skip.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(3, 50, 100)]
    [InlineData(10, 200, 1800)]
    public void Skip_IsPageMinusOneTimesSize(int page, int size, int expected)
    {
        new PagedRequest { Page = page, PageSize = size }.Skip.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 200)]
    [InlineData(99, 20)]
    public void EnsureValid_AcceptsInRangeValues(int page, int size)
    {
        var act = () => new PagedRequest { Page = page, PageSize = size }.EnsureValid();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 201, "pageSize")]
    public void EnsureValid_OutOfRange_ThrowsValidationErrorForField(int page, int size, string field)
    {
        var act = () => new PagedRequest { Page = page, PageSize = size }.EnsureValid();

        act.Should().Throw<RequestValidationException>()
            .Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public void EnsureValid_PageBeyondIntRangeOfSkip_Throws()
    {
        var act = () => new PagedRequest { Page = int.MaxValue, PageSize = 200 }.EnsureValid();

        act.Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("page");
    }

    [Fact]
    public void PagedResult_ExposesItemsAndTotals()
    {
        var result = new PagedResult<string>(["a", "b"], Page: 2, PageSize: 2, TotalCount: 5);

        result.Items.Should().Equal("a", "b");
        result.TotalPages.Should().Be(3);
    }

    [Fact]
    public void PagedResult_EmptyTotal_HasZeroPages()
    {
        new PagedResult<int>([], 1, 20, 0).TotalPages.Should().Be(0);
    }

    [Fact]
    public void PagedResult_Create_UsesRequestPaging()
    {
        var result = PagedResult.Create<int>([1], new PagedRequest { Page = 4, PageSize = 10 }, 31);

        result.Page.Should().Be(4);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(31);
        result.TotalPages.Should().Be(4);
    }

    [Fact]
    public void SortSpec_Parse_ReadsDirectionAndOrder()
    {
        var spec = SortSpec.Parse("-docDate, docNo", ["docDate", "docNo", "id"]);

        spec.Should().Equal(new SortField("docDate", Descending: true), new SortField("docNo", Descending: false));
    }

    [Fact]
    public void SortSpec_Parse_MatchesWhitelistCaseInsensitivelyAndReturnsCanonicalName()
    {
        SortSpec.Parse("DOCDATE", ["docDate"]).Should().Equal(new SortField("docDate", false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SortSpec_Parse_EmptyUsesDefault(string? sort)
    {
        SortSpec.Parse(sort, ["docDate", "id"], "-docDate,-id")
            .Should().Equal(new SortField("docDate", true), new SortField("id", true));
    }

    [Fact]
    public void SortSpec_Parse_EmptyWithoutDefault_ReturnsEmpty()
    {
        SortSpec.Parse(null, ["id"]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("password")]
    [InlineData("docDate; DROP TABLE x")]
    [InlineData("-")]
    [InlineData("docDate,,id")]
    [InlineData("docDate,docDate")]
    [InlineData("docDate,-DOCDATE")]
    [InlineData("--docDate")]
    public void SortSpec_Parse_RejectsFieldsOutsideWhitelistOrMalformed(string sort)
    {
        var act = () => SortSpec.Parse(sort, ["docDate", "id"]);

        act.Should().Throw<RequestValidationException>().Which.Errors.Should().ContainKey("sort");
    }
}
