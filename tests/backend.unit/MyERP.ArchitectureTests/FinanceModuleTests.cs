using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.System.Application.Seeding;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Modules;
using MyERP.Shared.Security;

namespace MyERP.ArchitectureTests;

public sealed class FinanceModuleTests
{
    private static readonly string[] CreditPermissions =
        ["AR.CREDIT.VIEW", "AR.CREDIT.CREATE", "AR.CREDIT.EDIT", "AR.CREDIT.DEACTIVATE"];

    [Fact]
    public void ModuleOwnsCreditContractAndArTables()
    {
        var module = new FinanceModule();
        module.Code.Should().Be(FinanceModuleContracts.ModuleCode);
        ErpModuleCatalog.All.Should().Contain(x => x.Code == module.Code);

        var services = new ServiceCollection();
        module.AddServices(services, new ConfigurationBuilder().Build());
        services.Should().ContainSingle(x => x.ServiceType == typeof(ICustomerCreditService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Where(x => x.ServiceType == typeof(IModuleDbModel))
            .Select(x => x.ImplementationType)
            .Should().BeEquivalentTo([typeof(CustomerCreditDbModel), typeof(GlPeriodDbModel), typeof(GlJournalDbModel)]);
        services.Should().ContainSingle(x => x.ServiceType == typeof(CreditLimitQueryService)
            && x.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(x => x.ServiceType == typeof(CreditLimitCommandService)
            && x.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void ModuleRegistersSeparateCreditPermissionsUnderTheArPrefix()
    {
        // FI-CREDIT-012: VIEW/CREATE/EDIT/DEACTIVATE are separate catalogue entries owned by FI.
        var services = new ServiceCollection();
        new FinanceModule().AddServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var permissions = provider.GetServices<IPermissionDefinitionProvider>().Single().Permissions;

        permissions.Should().OnlyContain(x => x.Module == "FI");
        permissions.Select(x => x.Code).Should().BeEquivalentTo(CreditPermissions);
        permissions.Should().OnlyContain(x => PermissionCode.IsValid(x.Code));
    }

    [Fact]
    public void FinanceSeedRoleMaintainsLimitsAndManagerOnlyViewsThem()
    {
        var permissions = new FinancePermissions().Permissions;
        string[] Granted(string role) =>
            [.. permissions.Where(SystemSeedData.Roles.Single(r => r.Code == role).Grants).Select(p => p.Code)];

        Granted("FINANCE").Should().BeEquivalentTo(CreditPermissions);
        Granted("MANAGER").Should().Equal("AR.CREDIT.VIEW");
        Granted("VIEWER").Should().Equal("AR.CREDIT.VIEW");
        Granted("SALES").Should().BeEmpty();
    }
}
