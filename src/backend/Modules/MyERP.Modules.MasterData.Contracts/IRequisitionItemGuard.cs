namespace MyERP.Modules.MasterData.Contracts;

/// <summary>在 PUR 保存交易內驗證同公司有效物料，持有 MD 共享鎖至交易提交。</summary>
public interface IRequisitionItemGuard
{
    Task<T> RunAsync<T>(IReadOnlyCollection<long> itemIds, Func<Task<T>> action, CancellationToken token = default);
}
