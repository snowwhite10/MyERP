using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance;

/// <summary>FI composition root. WU-FI-001a adds the credit key/limit/exposure foundation and the credit contract.</summary>
public sealed class FinanceModule : IErpModule
{
    public string Code => FinanceModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModuleDbModel, CustomerCreditDbModel>();
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
    }
}
