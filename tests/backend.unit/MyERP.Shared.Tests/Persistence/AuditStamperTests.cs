using Moq;
using MyERP.Shared.Domain;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

namespace MyERP.Shared.Tests.Persistence;

public sealed class AuditStamperTests
{
    private static readonly DateTime LocalNow = new(2026, 10, 10, 11, 27, 0);

    private static AuditStamper Stamper(long? userId = 5, long? companyId = 3)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.LocalNow).Returns(LocalNow);
        return new AuditStamper(clock.Object, new FixedCurrentUser(userId), new FixedCurrentCompany(companyId));
    }

    [Fact]
    public void StampAdded_SetsCreatedFieldsVersionAndCompany()
    {
        var order = new TestOrder();

        Stamper().StampAdded(order);

        order.CreatedAt.Should().Be(LocalNow);
        order.CreatedBy.Should().Be(5);
        order.UpdatedAt.Should().BeNull();
        order.UpdatedBy.Should().BeNull();
        order.Version.Should().Be(1);
        order.CompanyId.Should().Be(3);
    }

    [Fact]
    public void StampAdded_ExplicitCompanyId_IsKept()
    {
        // 例如種子程序為第二間公司建資料
        var order = new TestOrder { CompanyId = 9 };

        Stamper(companyId: 3).StampAdded(order);

        order.CompanyId.Should().Be(9);
    }

    [Fact]
    public void StampAdded_NoCompanyContextAndNoExplicitCompany_Throws()
    {
        var act = () => Stamper(companyId: null).StampAdded(new TestOrder());

        act.Should().Throw<InvalidOperationException>().WithMessage("*company*");
    }

    [Fact]
    public void StampAdded_NoUser_UsesSystemUser()
    {
        var order = new TestOrder();

        Stamper(userId: null).StampAdded(order);

        order.CreatedBy.Should().Be(SystemUser.Id);
    }

    [Fact]
    public void StampAdded_EntityWithoutSharedInterfaces_IsUntouched()
    {
        var setting = new TestSetting { Id = 1, Key = "k" };

        Stamper().StampAdded(setting);

        setting.Key.Should().Be("k");
    }

    [Fact]
    public void StampModified_SetsUpdatedFieldsAndIncrementsOriginalVersion()
    {
        var order = new TestOrder { CreatedAt = LocalNow.AddDays(-1), CreatedBy = 2, Version = 99 };

        Stamper(userId: 6).StampModified(order, originalVersion: 4);

        order.UpdatedAt.Should().Be(LocalNow);
        order.UpdatedBy.Should().Be(6);
        order.Version.Should().Be(5, "the new version is derived from the version the client loaded, not from the current value");
        order.CreatedBy.Should().Be(2);
    }

    [Fact]
    public void StampModified_NoUser_UsesSystemUser()
    {
        var order = new TestOrder();

        Stamper(userId: null).StampModified(order, 1);

        order.UpdatedBy.Should().Be(SystemUser.Id);
    }

    [Fact]
    public void StampModified_VersionOverflow_Throws()
    {
        var act = () => Stamper().StampModified(new TestOrder(), int.MaxValue);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Stamp_NullEntity_Throws()
    {
        var added = () => Stamper().StampAdded(null!);
        var modified = () => Stamper().StampModified(null!, 1);

        added.Should().Throw<ArgumentNullException>();
        modified.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MasterEntity_DefaultsToActiveAndNotDeleted()
    {
        var customer = new TestCustomer();

        customer.IsActive.Should().BeTrue();
        customer.IsDeleted.Should().BeFalse();
        customer.Should().BeAssignableTo<ISoftDeletable>();
    }
}
