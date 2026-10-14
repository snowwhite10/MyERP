namespace MyERP.Modules.MasterData.Contracts;

using MyERP.Shared.Paging;

public sealed record ValuationItemOption(long Id, string Code, string Name);

/// <summary>唯讀驗證估值物料屬於目前公司；停用物料仍可查看歷史估值。</summary>
public interface IValuationItemScope
{
    Task RequireItemAsync(long itemId, CancellationToken token = default);
    Task<PagedResult<ValuationItemOption>> ListItemsAsync(PagedRequest request, CancellationToken token = default);
}
