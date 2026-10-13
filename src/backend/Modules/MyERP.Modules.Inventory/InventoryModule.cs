using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Security;

namespace MyERP.Modules.Inventory;

/// <summary>INV 模組入口；異動帳與交易服務由後續工作單元加入。</summary>
public sealed class InventoryModule : IErpModule
{
    public string Code => InventoryModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPermissionDefinitionProvider, InventoryPermissions>();
        services.AddSingleton<IModuleDbModel, InventoryReferenceDbModel>();
        services.AddScoped<InventoryReferenceService>();
        services.AddScoped<IItemUsage>(sp => sp.GetRequiredService<InventoryReferenceService>());
        services.AddScoped<IWarehouseUsage>(sp => sp.GetRequiredService<InventoryReferenceService>());
        services.AddScoped<IStorageLocationUsage>(sp => sp.GetRequiredService<InventoryReferenceService>());
    }
}

public sealed class InventoryPermissions : IPermissionDefinitionProvider
{
    public const string InventoryView = "INV.INVENTORY.VIEW";
    public const string MovementView = "INV.MOVEMENT.VIEW";

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(InventoryView, InventoryModuleContracts.ModuleCode),
        new(MovementView, InventoryModuleContracts.ModuleCode),
    ];
}
