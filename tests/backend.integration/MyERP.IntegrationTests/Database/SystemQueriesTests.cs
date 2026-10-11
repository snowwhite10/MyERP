using MyERP.Modules.System.Application;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Paging;

namespace MyERP.IntegrationTests.Database;

/// <summary><see cref="SystemQueries.OrderByKeyOrName"/> 的排序規則（以 LINQ to Objects 驗證表達式）。</summary>
public sealed class SystemQueriesTests
{
    private static readonly SystemBranch[] Branches =
    [
        new() { Id = 3, Code = "B", Name = "Alpha" },
        new() { Id = 1, Code = "A", Name = "Beta" },
        new() { Id = 2, Code = "C", Name = "Alpha" },
    ];

    [Theory]
    [InlineData("code", false, new long[] { 1, 3, 2 })]
    [InlineData("code", true, new long[] { 2, 3, 1 })]
    [InlineData("name", false, new long[] { 2, 3, 1 })]
    [InlineData("name", true, new long[] { 1, 2, 3 })]
    public void Orders_by_key_or_name_then_by_id(string field, bool descending, long[] expected)
    {
        var ordered = Branches.AsQueryable().OrderByKeyOrName(new SortField(field, descending), x => x.Code, x => x.Name);

        Assert.Equal(expected, ordered.Select(x => x.Id).ToArray());
    }
}
