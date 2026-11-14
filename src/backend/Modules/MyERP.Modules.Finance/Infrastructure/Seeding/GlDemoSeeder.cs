using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Seeding;
using MyERP.Shared.Time;

namespace MyERP.Modules.Finance.Infrastructure.Seeding;

/// <summary>
/// 示範傳票的映射版本（ADR-105）。來源固定為 <c>FI／DemoSeed</c>；只有 Development 環境回傳 1，其他環境回傳 0，
/// 過帳決策因此拒絕（封閉失敗），正式環境不可能出現示範傳票。沒有主機環境的組合（例如只組 FI 模組的測試）同樣回傳 0，
/// 不影響其他來源的映射解析。
/// </summary>
public sealed class GlDemoSeedMapping(IHostEnvironment? environment = null) : IGlPostingMappingVersionSource
{
    public const string Module = "FI";
    public const string Type = "DemoSeed";
    public const int Version = 1;

    public string SourceModule => Module;

    public string SourceType => Type;

    public Task<int> ReadCurrentVersionAsync(long companyId, CancellationToken cancellationToken) =>
        Task.FromResult(environment?.IsDevelopment() == true ? Version : 0);
}

/// <summary>新增的筆數；重複執行應全部為 0。<paramref name="SalesInvoiceMaps"/>／<paramref name="PurchaseInvoiceMaps"/>／
/// <paramref name="SalesCollectionMaps"/> 為新建的
/// SD 發票／PUR 採購發票／收款科目映射列。</summary>
public sealed record GlDemoSeedResult(int Periods, int Journals, int ClosedPeriods, int SalesInvoiceMaps,
    int PurchaseInvoiceMaps, int SalesCollectionMaps);

/// <summary>
/// WU-FI-004c4a（ADR-105）：每間種子公司當年 1–12 月的會計期間，以及至今每月兩張示範傳票（銷貨、租金），
/// HK01 本月另有一張 17 位有效數字的精度示範傳票。期間經 <see cref="GlPeriodWriteService"/>（重疊、日期規則），
/// 傳票經 <see cref="IGlPostingService"/>（鎖、MD 科目快照、期間、借貸平衡、來源鍵）寫入，不直接寫 gl_ 資料表。
/// 本次新建、且早於上月的期間在過帳後關閉。冪等：同起訖的期間與已存在的來源鍵略過；不修改用戶既有的期間。
/// </summary>
public sealed class GlDemoSeeder(IServiceScopeFactory scopes, ISeedCompanyDirectory companies, IClock clock)
    : IDataSeeder
{
    public int Order => 700;

    public string Name => "FI";

    internal const decimal PrecisionAmount = 123456789012345.67m;

    Task IDataSeeder.SeedAsync(CancellationToken cancellationToken) => SeedAsync(cancellationToken);

    public async Task<GlDemoSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        var total = new GlDemoSeedResult(0, 0, 0, 0, 0, 0);
        foreach (var company in await companies.ListAsync(cancellationToken))
        {
            using var context = SeedCompanyContext.Enter(company.Id);
            var result = await SeedCompanyAsync(company, cancellationToken);
            total = new(total.Periods + result.Periods, total.Journals + result.Journals,
                total.ClosedPeriods + result.ClosedPeriods, total.SalesInvoiceMaps + result.SalesInvoiceMaps,
                total.PurchaseInvoiceMaps + result.PurchaseInvoiceMaps, total.SalesCollectionMaps + result.SalesCollectionMaps);
        }
        return total;
    }

    /// <summary>示範傳票：來源 ID（yyyyMM×10＋種類）、業務日期、借方科目、貸方科目、金額。</summary>
    internal static IReadOnlyList<(long SourceId, DateOnly Date, string Debit, string Credit, decimal Amount)> Journals(
        SeedCompanyRef company, DateOnly today)
    {
        var twd = string.Equals(company.BaseCurrency, "TWD", StringComparison.Ordinal);
        var list = new List<(long, DateOnly, string, string, decimal)>();
        for (var month = 1; month <= today.Month; month++)
        {
            var date = new DateOnly(today.Year, month, month == today.Month ? Math.Min(15, today.Day) : 15);
            var key = (today.Year * 100L + month) * 10;
            list.Add((key + 1, date, "1131", "4101", twd ? 100000m + month * 12345m : 25000.50m + month * 1234.25m));
            list.Add((key + 2, date, "6102", "1102", twd ? 45000m : 18000.75m));
            if (!twd && month == today.Month)
                list.Add((key + 9, date, "1102", "3101", PrecisionAmount));
        }
        return list;
    }

    private async Task<GlDemoSeedResult> SeedCompanyAsync(SeedCompanyRef company, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var created = new List<long>();
        Dictionary<DateOnly, (long Id, GlPeriodStatus Status)> periods;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<GlPeriodWriteService>();
            var saved = await db.Set<GlPeriod>().AsNoTracking().Where(p => p.CompanyId == company.Id)
                .ToListAsync(cancellationToken);
            for (var month = 1; month <= 12; month++)
            {
                var start = new DateOnly(today.Year, month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                if (saved.Any(p => p.StartDate <= end && p.EndDate >= start)) continue;
                var period = await writer.CreateAsync(company.Id, start, end, true, cancellationToken);
                saved.Add(period);
                created.Add(period.Id);
            }
            periods = saved.Where(p => p.StartDate.Day == 1 && p.EndDate == p.StartDate.AddMonths(1).AddDays(-1))
                .ToDictionary(p => p.StartDate, p => (p.Id, p.Status));
        }

        var journals = 0;
        foreach (var (sourceId, date, debit, credit, amount) in Journals(company, today))
        {
            var month = new DateOnly(date.Year, date.Month, 1);
            if (!periods.TryGetValue(month, out var period) || period.Status != GlPeriodStatus.Open) continue;
            if (await PostAsync(company, sourceId, date, debit, credit, amount, cancellationToken)) journals++;
        }

        var closeBefore = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var closed = 0;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyErpDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<GlPeriodWriteService>();
            var toClose = await db.Set<GlPeriod>().AsNoTracking()
                .Where(p => created.Contains(p.Id) && p.EndDate < closeBefore && p.Status == GlPeriodStatus.Open)
                .ToListAsync(cancellationToken);
            foreach (var period in toClose)
            {
                await writer.UpdateAsync(company.Id, period.Id, period.Version, null, null, false, cancellationToken);
                closed++;
            }
        }
        var maps = await EnsureMapAsync(company, "sales invoice",
            [DemoSalesInvoiceMap.Receivable, DemoSalesInvoiceMap.Revenue, DemoSalesInvoiceMap.OutputTax],
            () => new SalesInvoiceAccountMapping
            {
                CompanyId = company.Id,
                ReceivableAccountCode = DemoSalesInvoiceMap.Receivable,
                RevenueAccountCode = DemoSalesInvoiceMap.Revenue,
                OutputTaxAccountCode = DemoSalesInvoiceMap.OutputTax,
            }, cancellationToken) ? 1 : 0;
        var purchaseMaps = await EnsureMapAsync(company, "purchase invoice",
            [DemoPurchaseInvoiceMap.Payable, DemoPurchaseInvoiceMap.Purchase, DemoPurchaseInvoiceMap.InputTax],
            () => new PurchaseInvoiceAccountMapping
            {
                CompanyId = company.Id,
                PayableAccountCode = DemoPurchaseInvoiceMap.Payable,
                PurchaseAccountCode = DemoPurchaseInvoiceMap.Purchase,
                InputTaxAccountCode = DemoPurchaseInvoiceMap.InputTax,
            }, cancellationToken) ? 1 : 0;
        var collectionMaps = await EnsureMapAsync(company, "sales collection",
            [DemoSalesCollectionMap.UnappliedCash, DemoSalesCollectionMap.Receivable],
            () => new SalesCollectionAccountMapping
            {
                CompanyId = company.Id,
                UnappliedCashAccountCode = DemoSalesCollectionMap.UnappliedCash,
                ReceivableAccountCode = DemoSalesCollectionMap.Receivable,
            }, cancellationToken) ? 1 : 0;
        return new(created.Count, journals, closed, maps, purchaseMaps, collectionMaps);
    }

    /// <summary>示範 SD 發票科目映射（WU-FI-002a4c2、ADR-112）：應收、收入、銷項稅。</summary>
    internal static readonly (string Receivable, string Revenue, string OutputTax) DemoSalesInvoiceMap = ("1131", "4101", "2141");

    /// <summary>示範 PUR 採購發票科目映射（WU-FI-003a2c、ADR-118）：應付、進貨、進項稅。</summary>
    internal static readonly (string Payable, string Purchase, string InputTax) DemoPurchaseInvoiceMap = ("2101", "5111", "1151");

    /// <summary>示範收款科目映射（WU-FI-005a2d、ADR-123）：暫收款、應收控制（與 SD 發票映射應收相同，不出警告）。</summary>
    internal static readonly (string UnappliedCash, string Receivable) DemoSalesCollectionMap = ("2181", "1131");

    /// <summary>
    /// 公司尚無該映射列時建立示範列（版本 1）；已有列（不論用戶改成甚麼）一律不動，保留用戶設定（ADR-112／118／123）。
    /// 每個科目先經 MD 契約驗證可過帳（與維護 API 同一規則），否則中止種子，避免留下過帳時才失敗的映射。
    /// </summary>
    private async Task<bool> EnsureMapAsync<TMap>(SeedCompanyRef company, string kind, string[] codes, Func<TMap> create,
        CancellationToken cancellationToken) where TMap : BusinessEntity
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MyErpDbContext>();
        if (await db.Set<TMap>().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(m => m.CompanyId == company.Id, cancellationToken))
            return false;

        var chart = services.GetRequiredService<IChartOfAccounts>();
        foreach (var code in codes)
        {
            var reason = SalesInvoiceAccountMapRules.AccountReason(await chart.FindAsync(code, cancellationToken));
            if (reason is not null)
                throw new InvalidOperationException($"Seed {kind} map account {code} of {company.Code} is not postable: {reason}.");
        }
        db.Add(create());
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> PostAsync(SeedCompanyRef company, long sourceId, DateOnly date, string debitCode,
        string creditCode, decimal amount, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MyErpDbContext>();
        if (await db.Set<GlSourceKey>().AsNoTracking().AnyAsync(k => k.CompanyId == company.Id
                && k.SourceModule == GlDemoSeedMapping.Module && k.SourceType == GlDemoSeedMapping.Type
                && k.SourceId == sourceId, cancellationToken))
            return false;

        var chart = services.GetRequiredService<IChartOfAccounts>();
        var debit = await chart.FindAsync(debitCode, cancellationToken)
            ?? throw new InvalidOperationException($"Seed account {debitCode} is missing in company {company.Code}.");
        var credit = await chart.FindAsync(creditCode, cancellationToken)
            ?? throw new InvalidOperationException($"Seed account {creditCode} is missing in company {company.Code}.");
        var baseCurrency = (await services.GetRequiredService<ICurrencyLookup>().GetBaseCurrencyAsync(cancellationToken))?.Code;
        const int version = GlDemoSeedMapping.Version;
        var draft = GlJournalLineDraftRules.Build(company.Id, version,
        [
            new(GlAccountSnapshotRules.Validate(company.Id, debit.Id, debit.Code, version, version, debit), amount, 0m),
            new(GlAccountSnapshotRules.Validate(company.Id, credit.Id, credit.Code, version, version, credit), 0m, amount),
        ]);
        var fingerprint = "seed-v1:" + amount.ToString(CultureInfo.InvariantCulture) + ":" + debitCode + ":" + creditCode;
        var outcome = await services.GetRequiredService<IGlPostingService>().PostAsync(new GlPostingRequest(company.Id,
            new GlPostingSource(company.Id, GlDemoSeedMapping.Module, GlDemoSeedMapping.Type, sourceId, 1, fingerprint),
            date, baseCurrency, draft, version), cancellationToken);
        return outcome.Kind switch
        {
            GlPostingOutcomeKind.Posted => true,
            GlPostingOutcomeKind.Unchanged => false,
            _ => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"Seed journal {sourceId} for {company.Code} was not posted: {outcome.Kind} {outcome.HttpStatus} {outcome.ErrorCode} line {outcome.InvalidLine}.")),
        };
    }
}
