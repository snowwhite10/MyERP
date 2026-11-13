using Microsoft.EntityFrameworkCore;
using MyERP.Modules.System.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Seeding;

namespace MyERP.Modules.System.Application.Seeding;

/// <summary>
/// WU-FI-004c4a（ADR-105）：其他模組的種子程序以此取得已建立的種子公司。只回傳 <see cref="SystemSeedData.Companies"/>
/// 內、未刪除的公司；本位幣取自種子定義（公司主檔沒有本位幣欄位，本位幣由 MD 幣別維護）。
/// </summary>
public sealed class SeedCompanyDirectory(MyErpDbContext db) : ISeedCompanyDirectory
{
    public async Task<IReadOnlyList<SeedCompanyRef>> ListAsync(CancellationToken cancellationToken)
    {
        var codes = SystemSeedData.Companies.Select(c => c.Code).ToList();
        var ids = await db.Set<SystemCompany>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => codes.Contains(c.Code) && !c.IsDeleted)
            .Select(c => new { c.Code, c.Id })
            .ToListAsync(cancellationToken);
        return SystemSeedData.Companies
            .Select(seed => (seed, id: ids.FirstOrDefault(x => string.Equals(x.Code, seed.Code, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.id is not null)
            .OrderBy(x => x.seed.Code, StringComparer.Ordinal)
            .Select(x => new SeedCompanyRef(x.id!.Id, x.seed.Code, x.seed.BaseCurrency))
            .ToList();
    }
}
