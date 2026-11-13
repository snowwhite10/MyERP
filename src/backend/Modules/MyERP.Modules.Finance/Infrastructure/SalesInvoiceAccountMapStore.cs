using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-002a4a（ADR-109）：SD 發票科目映射的正式來源。同一型別實作 <see cref="ISalesInvoiceAccountMapSource"/>
/// 與 GL 的 <c>SD/Invoice</c> <see cref="IGlPostingMappingVersionSource"/>，兩者都讀同一列，版本就是列版本。
/// 經 scoped DbContext 讀取，因此自動加入呼叫者或過帳嘗試已開啟的交易；公司篩選器只回傳目前公司的列，
/// 查詢其他公司 ID 一律視為沒有映射（null／0，封閉失敗）。不快取：每次都讀已提交的最新列。
/// </summary>
public sealed class SalesInvoiceAccountMapStore(MyErpDbContext db)
    : ISalesInvoiceAccountMapSource, IGlPostingMappingVersionSource
{
    public string SourceModule => SalesInvoicePostingService.SourceModule;

    public string SourceType => SalesInvoicePostingService.InvoiceType;

    public async Task<SalesInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(companyId, cancellationToken);
        return row is null
            ? null
            : new SalesInvoiceAccountMap(row.Version, row.ReceivableAccountCode, row.RevenueAccountCode,
                row.OutputTaxAccountCode);
    }

    public async Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        (await FindAsync(companyId, cancellationToken))?.Version ?? 0;

    private Task<SalesInvoiceAccountMapping?> FindAsync(long companyId, CancellationToken cancellationToken) =>
        companyId <= 0 || db.CurrentCompanyId != companyId
            ? Task.FromResult<SalesInvoiceAccountMapping?>(null)
            : db.Set<SalesInvoiceAccountMapping>().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .SingleOrDefaultAsync(cancellationToken);
}
