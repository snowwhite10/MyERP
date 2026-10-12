namespace MyERP.Modules.MasterData.Contracts;

/// <summary>使用客戶的單據模組實作；MD 不查詢其他模組資料表。</summary>
public interface ICustomerUsage
{
    Task<bool> IsUsedAsync(long customerId, CancellationToken cancellationToken = default);
}
