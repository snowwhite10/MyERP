using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Domain;
using MyERP.Shared.Security;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 全系統唯一的 DbContext（ADR-003）。同一請求內所有模組共用同一個 scoped 實例，因此在同一交易中。
/// 統一套用：型別對應（db-conventions.md）、company_id / is_deleted 篩選、version concurrency token。
/// 模型依模組組合建立並快取（<see cref="ErpModelCacheKeyFactory"/>），同一程序內可有不同組合（例如整合測試）。
/// </summary>
public class MyErpDbContext : DbContext
{
    private static readonly MethodInfo ConfigureSharedMembersMethod =
        typeof(MyErpDbContext).GetMethod(nameof(ConfigureSharedMembers), BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// 資料庫定序：臺灣筆畫排序、不分大小寫、支援補充字元；由 Core_Baseline migration 建庫時設定。
    /// MASTER_PROMPT 6.1 的 Chinese_Taiwan_Stroke_100_CI_AS_SC 不存在，改用其 100 版正式名稱（ADR-006）。
    /// </summary>
    public const string DatabaseCollation = "Chinese_Traditional_Stroke_Count_100_CI_AS_SC";

    private readonly IReadOnlyList<IModuleDbModel> _modules;
    private readonly ICurrentCompany _currentCompany;

    public MyErpDbContext(DbContextOptions<MyErpDbContext> options, IEnumerable<IModuleDbModel> modules, ICurrentCompany currentCompany)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(currentCompany);
        _modules = [.. modules];
        _currentCompany = currentCompany;
        ModuleSetKey = string.Join(';', _modules.Select(m => m.GetType().FullName));
    }

    /// <summary>模組組合（依註冊順序的型別全名），作為模型快取鍵的一部分（<see cref="ErpModelCacheKeyFactory"/>）。</summary>
    public string ModuleSetKey { get; }

    /// <summary>公司篩選的參數；每次查詢讀取本實例的值（EF 將 DbContext 成員參數化）。</summary>
    public long? CurrentCompanyId => _currentCompany.CompanyId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // 金額 / 數量預設 DECIMAL(19,4)；單價 (19,6)、匯率 (19,8) 由模組個別設定
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(6)");
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30).AreUnicode(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.UseCollation(DatabaseCollation);

        foreach (var module in _modules)
        {
            module.Configure(modelBuilder);
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType is null && !e.IsOwned()).ToList())
        {
            ConfigureSharedMembersMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ConfigureSharedMembers<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        var entity = modelBuilder.Entity<TEntity>();

        if (typeof(IVersioned).IsAssignableFrom(typeof(TEntity)))
        {
            entity.Property(nameof(IVersioned.Version)).IsConcurrencyToken();
        }

        if (typeof(ICompanyOwned).IsAssignableFrom(typeof(TEntity)))
        {
            // e => (long?)e.CompanyId == this.CurrentCompanyId；以 DbContext 成員作參數，每個實例讀自己的公司
            entity.HasQueryFilter(ErpQueryFilters.Company, BuildFilter<TEntity>(e =>
                Expression.Equal(
                    Expression.Convert(Expression.Property(e, nameof(ICompanyOwned.CompanyId)), typeof(long?)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentCompanyId)))));
        }

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            // e => !e.IsDeleted
            entity.HasQueryFilter(ErpQueryFilters.SoftDelete, BuildFilter<TEntity>(e =>
                Expression.Not(Expression.Property(e, nameof(ISoftDeletable.IsDeleted)))));
        }
    }

    private static Expression<Func<TEntity, bool>> BuildFilter<TEntity>(Func<Expression, Expression> body)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        return Expression.Lambda<Func<TEntity, bool>>(body(parameter), parameter);
    }
}
