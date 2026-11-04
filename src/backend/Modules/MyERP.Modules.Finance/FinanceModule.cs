using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance;

/// <summary>FI composition root: credit key/limit/exposure foundation (WU-FI-001a/b1) and credit limit API (001b2).</summary>
public sealed class FinanceModule : IErpModule
{
    public string Code => FinanceModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPermissionDefinitionProvider, FinancePermissions>();
        services.AddSingleton<IModuleDbModel, CustomerCreditDbModel>();
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
        services.AddScoped<CreditLimitCommandService>();
        services.AddScoped<CreditLimitQueryService>();
    }
}

/// <summary>FI permission catalogue. Seed FINANCE receives every FI code; MANAGER and VIEWER receive VIEW only.</summary>
public sealed class FinancePermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        .. new[] { "VIEW", "CREATE", "EDIT", "DEACTIVATE" }
            .Select(action => new PermissionDefinition($"AR.CREDIT.{action}", FinanceModuleContracts.ModuleCode))
    ];
}
