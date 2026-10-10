using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Auditing;
using MyERP.Shared.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.IntegrationTests.Database;

/// <summary>測試用單據（tst_order）：具備全部必備欄位，用於驗證 interceptor、樂觀鎖與公司篩選對真實資料庫生效。</summary>
public sealed class TestOrder : BusinessEntity
{
    public string DocNo { get; set; } = "";

    public decimal Amount { get; set; }
}

/// <summary>測試用主檔（tst_customer）：另有軟刪除。</summary>
public sealed class TestCustomer : MasterEntity
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>敏感欄位：稽核日誌只記錄有變更，值遮蔽。</summary>
    [SensitiveData]
    public string? Secret { get; set; }
}

/// <summary>測試模組模型；資料表由 <see cref="TestDatabase"/> 建立，不屬於 migration。</summary>
public sealed class TestModuleDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestOrder>(b =>
        {
            b.ToTable("tst_order");
            b.Property(e => e.DocNo).HasMaxLength(30);
        });
        modelBuilder.Entity<TestCustomer>(b =>
        {
            b.ToTable("tst_customer");
            b.Property(e => e.Code).HasMaxLength(30);
            b.Property(e => e.Name).HasMaxLength(100);
            b.Property(e => e.Secret).HasMaxLength(100);
        });
    }
}

public sealed class FixedCompany(long? companyId) : ICurrentCompany
{
    public long? CompanyId { get; set; } = companyId;
}

public sealed class FixedUser(long? userId) : ICurrentUser
{
    public long? UserId { get; } = userId;

    public bool IsAuthenticated => UserId is not null;
}

public sealed class FixedClock(DateTime localNow) : IClock
{
    public DateTimeOffset Now => new(LocalNow, Offset);

    public DateTime LocalNow { get; } = localNow;

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public TimeSpan Offset => TimeSpan.FromHours(8);
}

/// <summary>每個測試用不同的公司 Id，令共用的 tst_* 表內資料互不干擾。</summary>
public static class TestCompanies
{
    private static long _next = 1000;

    public static long Next() => Interlocked.Increment(ref _next);
}
