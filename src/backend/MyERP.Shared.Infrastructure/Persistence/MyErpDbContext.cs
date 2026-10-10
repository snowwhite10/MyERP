using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Domain;
using MyERP.Shared.Security;

namespace MyERP.Shared.Infrastructure.Persistence;

/// <summary>
/// 全系統唯一的 DbContext（ADR-003）。同一請求內所有模組共用同一個 scoped 實例，因此在同一交易中。
/// 統一套用：型別對應（db-conventions.md）、company_id / is_deleted 篩選、version concurrency token。
/// 模型依程序內固定的模組組合建立一次（EF 模型快取以 DbContext 型別為鍵）。
/// </summary>
public class MyErpDbContext : DbContext
{
    private static readonly MethodInfo ConfigureSharedMembersMethod =
        typeof(MyErpDbContext).GetMethod(nameof(ConfigureSharedMembers), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly IReadOnlyList<IModuleDbModel> _modules;
    private readonly ICurrentCompany _currentCompany;

    public MyErpDbContext(DbContextOptions<MyErpDbContext> options, IEnumerable<IModuleDbModel> modules, ICurrentCompany currentCompany)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(currentCompany);
        _modules = [.. modules];
        _currentCompany = currentCompany;
    }

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
