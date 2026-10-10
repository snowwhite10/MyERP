using MyERP.Shared.Errors;

namespace MyERP.Shared.Paging;

/// <summary>列表查詢的分頁參數（api-conventions.md 第 5 節）。Controller 以 <c>[AsParameters]</c> / <c>[FromQuery]</c> 綁定。</summary>
public record PagedRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;

    /// <summary>頁碼，從 1 起。</summary>
    public int Page { get; init; } = 1;

    /// <summary>每頁筆數，1–200。</summary>
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>逗號分隔排序欄位，<c>-</c> 前綴為降冪，以 <see cref="SortSpec.Parse"/> 對白名單解析。</summary>
    public string? Sort { get; init; }

    /// <summary>模糊搜尋關鍵字（各資源指定欄位）。</summary>
    public string? Keyword { get; init; }

    public int Skip => (Page - 1) * PageSize;

    /// <summary>超出範圍時拋出 <see cref="RequestValidationException"/>（400）。</summary>
    public void EnsureValid()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        if (Page < 1)
        {
            errors["page"] = ["page must be greater than or equal to 1."];
        }
        else if (errors.Count == 0 && (long)(Page - 1) * PageSize > int.MaxValue)
        {
            errors["page"] = ["page is too large."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}
