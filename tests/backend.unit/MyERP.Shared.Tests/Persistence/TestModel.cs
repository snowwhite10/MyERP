using Microsoft.EntityFrameworkCore;
using MyERP.Shared.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Shared.Tests.Persistence;

public enum TestOrderStatus
{
    Draft,
    Approved,
}

/// <summary>單據表頭範例：具備第 6.4 節必備欄位。</summary>
public sealed class TestOrder : BusinessEntity
{
    public string DocNo { get; set; } = "";

    public DateOnly DocDate { get; set; }

    public TestOrderStatus Status { get; set; }

    public decimal Amount { get; set; }

    public decimal UnitPrice { get; set; }

    public DateTime? ApprovedAt { get; set; }
}

/// <summary>主檔範例：另有軟刪除。</summary>
public sealed class TestCustomer : MasterEntity
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";
}

/// <summary>沒有任何共用介面的表（例如全系統設定），不得套用公司篩選。</summary>
public sealed class TestSetting
{
    public long Id { get; set; }

    public string Key { get; set; } = "";
}

public sealed class TestModuleDbModel : IModuleDbModel
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestOrder>(b =>
        {
            b.ToTable("tst_order");
            b.Property(e => e.UnitPrice).HasPrecision(19, 6);
            b.Property(e => e.DocNo).HasMaxLength(30);
        });
        modelBuilder.Entity<TestCustomer>(b => b.ToTable("tst_customer"));
        modelBuilder.Entity<TestSetting>(b => b.ToTable("tst_setting"));
    }
}

public sealed class FixedCurrentCompany(long? companyId) : ICurrentCompany
{
    public long? CompanyId { get; set; } = companyId;
}

public sealed class FixedCurrentUser(long? userId) : ICurrentUser
{
    public long? UserId { get; set; } = userId;

    public bool IsAuthenticated => UserId is not null;
}

internal static class TestDb
{
    // 只建模型與產生 SQL，不連線；Server 名稱刻意無效，萬一誤連線會立即失敗
    private const string NoConnection = "Server=invalid.myerp.test;Database=myerp_unit;Integrated Security=true;Connect Timeout=1";

    public static MyErpDbContext Create(ICurrentCompany company, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<MyErpDbContext>()
            .UseMyErpSqlServer(NoConnection)
            .AddInterceptors(interceptors)
            .Options;
        return new MyErpDbContext(options, [new TestModuleDbModel()], company);
    }
}
