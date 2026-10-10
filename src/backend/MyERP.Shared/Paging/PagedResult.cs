namespace MyERP.Shared.Paging;

/// <summary>列表回應：<c>{ items, page, pageSize, totalCount }</c>（api-conventions.md 第 5 節）。</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public long TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
}

public static class PagedResult
{
    /// <summary>以請求的分頁參數建立結果。</summary>
    public static PagedResult<T> Create<T>(IReadOnlyList<T> items, PagedRequest request, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PagedResult<T>(items, request.Page, request.PageSize, totalCount);
    }
}
