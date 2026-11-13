using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Seeding;

namespace MyERP.Modules.MasterData.Application.Seeding;

/// <summary>新增的筆數；重複執行應全部為 0。</summary>
public sealed record MasterDataSeedResult(int Currencies, int Accounts);

/// <summary>
/// WU-FI-004c4a（MASTER_PROMPT 第 12 節、ADR-105）：每間種子公司的幣別（本位幣 + USD／CNY／JPY 與另一公司本位幣）
/// 與基本會計科目表。經 <see cref="CurrencyService"/>／<see cref="AccountService"/> 的正常規則寫入（代碼、上層、類別檢查），
/// 每間公司在自己的 DI scope 與 <see cref="SeedCompanyContext"/> 內執行。冪等：以代碼比對（含已軟刪除），只新增缺少的資料，
/// 不改既有資料（含用戶改過的本位幣）。
/// </summary>
public sealed class MasterDataDemoSeeder(IServiceScopeFactory scopes, ISeedCompanyDirectory companies) : IDataSeeder
{
    public int Order => 200;

    public string Name => "MD";

    /// <summary>幣別代碼、名稱、符號、小數位（TWD／JPY 0 位，見 Money.Round）。</summary>
    internal static readonly (string Code, string Name, string Symbol, int Decimals)[] Currencies =
    [
        ("TWD", "新台幣", "NT$", 0),
        ("HKD", "港幣", "HK$", 2),
        ("USD", "美元", "US$", 2),
        ("CNY", "人民幣", "¥", 2),
        ("JPY", "日圓", "JP¥", 0),
    ];

    /// <summary>科目代碼、名稱、上層代碼；第一層帶類別，下層承繼上層類別。</summary>
    internal static readonly (string Code, string Name, string? Parent, AccountType? Type)[] Accounts =
    [
        ("1", "資產", null, AccountType.Asset),
        ("1101", "現金", "1", null),
        ("1102", "銀行存款", "1", null),
        ("1131", "應收帳款", "1", null),
        ("1141", "存貨", "1", null),
        ("2", "負債", null, AccountType.Liability),
        ("2101", "應付帳款", "2", null),
        ("2171", "應付費用", "2", null),
        ("3", "權益", null, AccountType.Equity),
        ("3101", "股本", "3", null),
        ("4", "收入", null, AccountType.Revenue),
        ("4101", "銷貨收入", "4", null),
        ("5", "成本", null, AccountType.Cost),
        ("5101", "銷貨成本", "5", null),
        ("6", "費用", null, AccountType.Expense),
        ("6101", "薪資支出", "6", null),
        ("6102", "租金支出", "6", null),
    ];

    Task IDataSeeder.SeedAsync(CancellationToken cancellationToken) => SeedAsync(cancellationToken);

    public async Task<MasterDataSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var currencies = 0;
        var accounts = 0;
        foreach (var company in await companies.ListAsync(cancellationToken))
        {
            using var context = SeedCompanyContext.Enter(company.Id);
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            currencies += await EnsureCurrenciesAsync(services, company, cancellationToken);
            accounts += await EnsureAccountsAsync(services, company, cancellationToken);
        }
        return new(currencies, accounts);
    }

    private static async Task<int> EnsureCurrenciesAsync(IServiceProvider services, SeedCompanyRef company,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<MyErpDbContext>();
        var existing = await db.Set<Currency>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company.Id).Select(x => x.Code).ToListAsync(cancellationToken);
        var hasBase = await db.Set<Currency>().AsNoTracking().AnyAsync(x => x.IsBase, cancellationToken);
        var service = services.GetRequiredService<CurrencyService>();
        var created = 0;
        foreach (var (code, name, symbol, decimals) in Currencies)
        {
            if (existing.Contains(code, StringComparer.OrdinalIgnoreCase)) continue;
            var isBase = !hasBase && string.Equals(code, company.BaseCurrency, StringComparison.Ordinal);
            await service.CreateAsync(code, name, symbol, decimals, isBase, cancellationToken);
            created++;
        }
        return created;
    }

    private static async Task<int> EnsureAccountsAsync(IServiceProvider services, SeedCompanyRef company,
        CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<MyErpDbContext>();
        var ids = await db.Set<Account>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company.Id && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Code, x => x.Id, cancellationToken);
        var deleted = await db.Set<Account>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company.Id && x.IsDeleted).Select(x => x.Code).ToListAsync(cancellationToken);
        var service = services.GetRequiredService<AccountService>();
        var created = 0;
        foreach (var (code, name, parent, type) in Accounts)
        {
            if (ids.ContainsKey(code) || deleted.Contains(code)) continue;
            long? parentId = null;
            if (parent is not null)
            {
                // 上層被用戶刪除時不重建下層，保留用戶的決定。
                if (!ids.TryGetValue(parent, out var id)) continue;
                parentId = id;
            }
            var account = await service.CreateAsync(code, name, parentId, type, null, cancellationToken);
            ids[code] = account.Id;
            created++;
        }
        return created;
    }
}
