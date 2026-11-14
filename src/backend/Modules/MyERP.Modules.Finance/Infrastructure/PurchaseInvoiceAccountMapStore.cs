using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Infrastructure;

/// <summary>
/// WU-FI-003a2a（ADR-116）：PUR 採購發票科目映射的正式來源。同一型別實作 <see cref="IPurchaseInvoiceAccountMapSource"/>
/// 與 GL 的 <c>PUR/PurchaseInvoice</c> <see cref="IGlPostingMappingVersionSource"/>，兩者都讀同一列，版本就是列版本。
/// 經 scoped DbContext 讀取，因此自動加入呼叫者或過帳嘗試已開啟的交易；查詢目前公司以外的 ID 一律視為沒有映射
/// （null／0，封閉失敗）。不快取：每次都讀已提交的最新列。
/// </summary>
public sealed class PurchaseInvoiceAccountMapStore(MyErpDbContext db)
    : IPurchaseInvoiceAccountMapSource, IGlPostingMappingVersionSource
{
    /// <summary>GL 來源模組代碼（PUR 採購）。</summary>
    public const string PurchaseSourceModule = "PUR";

    /// <summary>GL 來源類型（與 <see cref="PurchaseSourceType.PurchaseInvoice"/> 同名）。</summary>
    public const string PurchaseInvoiceType = nameof(PurchaseSourceType.PurchaseInvoice);

    public string SourceModule => PurchaseSourceModule;

    public string SourceType => PurchaseInvoiceType;

    public async Task<PurchaseInvoiceAccountMap?> ReadAsync(long companyId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(companyId, cancellationToken);
        return row is null
            ? null
            : new PurchaseInvoiceAccountMap(row.Version, row.PayableAccountCode, row.PurchaseAccountCode,
                row.InputTaxAccountCode);
    }

    public async Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        (await FindAsync(companyId, cancellationToken))?.Version ?? 0;

    private Task<PurchaseInvoiceAccountMapping?> FindAsync(long companyId, CancellationToken cancellationToken) =>
        companyId <= 0 || db.CurrentCompanyId != companyId
            ? Task.FromResult<PurchaseInvoiceAccountMapping?>(null)
            : db.Set<PurchaseInvoiceAccountMapping>().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .SingleOrDefaultAsync(cancellationToken);
}
