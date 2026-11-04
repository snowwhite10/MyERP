using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance;
using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Modules;

namespace MyERP.ArchitectureTests;

public sealed class FinanceModuleTests
{
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
        services.Should().ContainSingle(x => x.ServiceType == typeof(IModuleDbModel));
    }
}
