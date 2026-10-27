using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Sales.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales;

/// <summary>SD 組合根；單據模型及端點由後續工作單元加入。</summary>
public sealed class SalesModule : IErpModule
{
    public string Code => SalesModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPermissionDefinitionProvider, SalesPermissions>();
        services.AddSingleton<IModuleDbModel, SalesDeliveryDbModel>();
        services.AddSingleton<IModuleDbModel, SalesOrderDbModel>();
        services.AddScoped<SalesCompanyAccess>();
        services.AddScoped<SalesActionAccess>();
        services.AddScoped<SalesDeliverySelectionStore>();
        services.AddScoped<SalesOrderDraftStore>();
        services.AddScoped<SalesOrderSnapshotBuilder>();
        services.AddScoped<SalesOrderCreateService>();
    }
}

public sealed class SalesPermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        .. Build("QUOTATION", "VIEW", "CREATE", "EDIT", "DELETE", "SUBMIT"),
        .. Build("ORDER", "VIEW", "CREATE", "EDIT", "DELETE", "SUBMIT", "CANCEL", "EXPORT"),
        .. Build("DELIVERY", "VIEW", "CREATE", "EDIT", "DELETE", "SUBMIT"),
        .. Build("PRICING", "VIEW"),
        .. Build("INVOICE", "VIEW", "ISSUE"),
        .. Build("CREDIT", "VIEW"),
        .. Build("RETURN", "VIEW", "CREATE")
    ];

    private static IEnumerable<PermissionDefinition> Build(string resource, params string[] actions) =>
        actions.Select(action => new PermissionDefinition($"SAL.{resource}.{action}", SalesModuleContracts.ModuleCode));
}
