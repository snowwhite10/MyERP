using MyERP.Modules.Finance.Application;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-002a3b（ADR-113）：GL 的 <c>SD/Return</c> 映射版本來源。銷退信用傳票沿用 SD 發票科目映射
/// （<c>gl_sales_invoice_account_map</c>），因此版本就是同一列的列版本；委派 <see cref="SalesInvoiceAccountMapStore"/>
/// 讀取，公司範圍與封閉失敗規則相同。
/// </summary>
public sealed class SalesReturnMappingVersionSource(MyErpDbContext db) : IGlPostingMappingVersionSource
{
    private readonly SalesInvoiceAccountMapStore store = new(db);

    public string SourceModule => SalesInvoicePostingService.SourceModule;

    public string SourceType => SalesCreditPostingService.ReturnType;

    public Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        store.ReadCurrentVersionAsync(companyId, cancellationToken);
}
