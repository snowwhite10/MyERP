using Microsoft.EntityFrameworkCore;
using Moq;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Shared.Tests.Persistence;

/// <summary>以 ChangeTracker 驗證 interceptor 的套用邏輯（不連線；實際寫入 SQL Server 的整合測試在 WU-P0-008）。</summary>
public sealed class AuditSaveChangesInterceptorTests
{
    private static readonly DateTime LocalNow = new(2026, 10, 10, 11, 27, 0);

    private static AuditSaveChangesInterceptor Interceptor(long? userId = 5, long? companyId = 3)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.LocalNow).Returns(LocalNow);
        return new AuditSaveChangesInterceptor(new AuditStamper(clock.Object, new FixedCurrentUser(userId), new FixedCurrentCompany(companyId)));
    }

    [Fact]
    public void Apply_AddedEntity_IsStamped()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(3));
        var order = new TestOrder { DocNo = "SO-1" };
        db.Add(order);

        Interceptor().Apply(db);

        order.CreatedBy.Should().Be(5);
        order.CompanyId.Should().Be(3);
        order.Version.Should().Be(1);
    }

    [Fact]
    public void Apply_ModifiedEntity_IncrementsVersionFromOriginalValueAndProtectsCreatedFields()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(3));
        var created = LocalNow.AddDays(-3);
        var order = new TestOrder { Id = 10, CompanyId = 3, CreatedAt = created, CreatedBy = 2, Version = 4, DocNo = "SO-1" };
        db.Attach(order);

        // 用戶端嘗試竄改不可變欄位
        order.DocNo = "SO-1A";
        order.CreatedBy = 999;
        order.CreatedAt = LocalNow;
        order.CompanyId = 8;

        Interceptor(userId: 6).Apply(db);

        var entry = db.Entry(order);
        order.Version.Should().Be(5);
        entry.Property(e => e.Version).OriginalValue.Should().Be(4, "the original value is the optimistic concurrency check");
        order.UpdatedBy.Should().Be(6);
        order.UpdatedAt.Should().Be(LocalNow);
        order.CreatedBy.Should().Be(2);
        order.CreatedAt.Should().Be(created);
        order.CompanyId.Should().Be(3);
        entry.Property(e => e.CreatedBy).IsModified.Should().BeFalse();
        entry.Property(e => e.CompanyId).IsModified.Should().BeFalse();
    }

    [Fact]
    public void Apply_ClientSuppliedVersion_IsUsedAsConcurrencyCheck()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(3));
        var order = new TestOrder { Id = 10, CompanyId = 3, Version = 7 };
        db.Attach(order);

        // Application 層把請求的 version 設為 OriginalValue（API 規範：PUT 帶 version）
        db.Entry(order).Property(e => e.Version).OriginalValue = 6;
        order.DocNo = "changed";

        Interceptor().Apply(db);

        order.Version.Should().Be(7);
        db.Entry(order).Property(e => e.Version).OriginalValue.Should().Be(6);
    }

    [Fact]
    public void Apply_UnchangedAndDeletedEntities_AreNotStamped()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(3));
        var unchanged = new TestOrder { Id = 1, CompanyId = 3, Version = 2 };
        var deleted = new TestOrder { Id = 2, CompanyId = 3, Version = 2 };
        db.Attach(unchanged);
        db.Attach(deleted);
        db.Remove(deleted);

        Interceptor().Apply(db);

        unchanged.Version.Should().Be(2);
        unchanged.UpdatedBy.Should().BeNull();
        deleted.UpdatedBy.Should().BeNull();
    }

    [Fact]
    public void Apply_SoftDelete_IsTreatedAsModification()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(3));
        var customer = new TestCustomer { Id = 1, CompanyId = 3, Version = 1 };
        db.Attach(customer);

        customer.IsDeleted = true;
        Interceptor().Apply(db);

        customer.Version.Should().Be(2);
        customer.UpdatedBy.Should().Be(5);
    }

    [Fact]
    public void Apply_ModifiedEntityWithoutSharedInterfaces_IsLeftAlone()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(3));
        var setting = new TestSetting { Id = 1, Key = "a" };
        db.Attach(setting);

        setting.Key = "b";
        var act = () => Interceptor().Apply(db);

        act.Should().NotThrow();
        setting.Key.Should().Be("b");
    }

    [Fact]
    public void Apply_AddedWithoutCompanyContext_Throws()
    {
        using var db = TestDb.Create(new FixedCurrentCompany(null));
        db.Add(new TestOrder());

        var act = () => Interceptor(companyId: null).Apply(db);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Apply_NullContext_IsIgnored()
    {
        var act = () => Interceptor().Apply(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NullStamper_Throws()
    {
        var act = () => new AuditSaveChangesInterceptor(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
