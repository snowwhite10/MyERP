using MyERP.Shared.Paging;

namespace MyERP.Modules.MasterData.Contracts;

/// <summary>供交易單據選擇已核准供應商；已保存引用由呼叫端先驗單據範圍。</summary>
public interface ISupplierSelectionOptions
{
    Task<PagedResult<SupplierSelectionOption>> ApprovedAsync(PagedRequest request, CancellationToken token = default);
    Task<IReadOnlyList<SupplierSelectionOption>> SavedAsync(IReadOnlyCollection<long> ids, CancellationToken token = default);
}

public sealed record SupplierSelectionOption(long Id, string Code, string Name, bool IsActive, bool IsApproved);
