using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-005a2a（ADR-120）：收款／核銷科目映射的正式來源。同一型別實作 <see cref="ISalesCollectionAccountMapSource"/>
/// 與 GL 的 <c>FI/SalesCollection</c> <see cref="IGlPostingMappingVersionSource"/>；<c>FI/CollectionWriteOff</c> 由
/// <see cref="CollectionWriteOffMappingVersionSource"/> 委派本類讀同一列。經 scoped DbContext 讀取，自動加入呼叫者交易；
/// 目前公司以外的 ID 一律視為沒有映射（null／0，封閉失敗）。不快取。
/// </summary>
public sealed class SalesCollectionAccountMapStore(MyErpDbContext db)
    : ISalesCollectionAccountMapSource, IGlPostingMappingVersionSource
{
    /// <summary>GL 來源模組代碼：收款與核銷是 FI 自有單據（BR-FI-033、BR-FI-037）。</summary>
    public const string CollectionSourceModule = FinanceModuleContracts.ModuleCode;

    /// <summary>GL 來源類型（與 <see cref="CollectionReceipt.SalesCollectionType"/> 同名）。</summary>
    public const string ReceiptType = CollectionReceipt.SalesCollectionType;

    public string SourceModule => CollectionSourceModule;

    public string SourceType => ReceiptType;

    public async Task<SalesCollectionAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(companyId, cancellationToken);
        return row is null
            ? null
            : new SalesCollectionAccountMap(row.Version, row.UnappliedCashAccountCode, row.ReceivableAccountCode);
    }

    public async Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        (await FindAsync(companyId, cancellationToken))?.Version ?? 0;

    private Task<SalesCollectionAccountMapping?> FindAsync(long companyId, CancellationToken cancellationToken) =>
        companyId <= 0 || db.CurrentCompanyId != companyId
            ? Task.FromResult<SalesCollectionAccountMapping?>(null)
            : db.Set<SalesCollectionAccountMapping>().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .SingleOrDefaultAsync(cancellationToken);
}

/// <summary>
/// WU-FI-005a2a（ADR-120）：GL 的 <c>FI/CollectionWriteOff</c> 映射版本來源。核銷傳票（借暫收／貸應收）與收款共用
/// 同一映射列，因此版本就是同一列的列版本；委派 <see cref="SalesCollectionAccountMapStore"/>，公司範圍與封閉失敗規則相同。
/// </summary>
public sealed class CollectionWriteOffMappingVersionSource(MyErpDbContext db) : IGlPostingMappingVersionSource
{
    private readonly SalesCollectionAccountMapStore store = new(db);

    public string SourceModule => SalesCollectionAccountMapStore.CollectionSourceModule;

    public string SourceType => CollectionSettlement.CollectionWriteOffType;

    public Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        store.ReadCurrentVersionAsync(companyId, cancellationToken);
}
