using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.MasterData.Application;
using MyERP.Modules.MasterData.Infrastructure;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.MasterData;

/// <summary>MD 主檔模組入口；資料模型與業務服務由各主檔工作單元加入。</summary>
public sealed class MasterDataModule : IErpModule
{
    public string Code => MasterDataModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModuleDbModel, CustomerCategoryDbModel>();
        services.AddSingleton<IModuleDbModel, CustomerDbModel>();
        services.AddScoped<CustomerCategoryService>();
        services.AddScoped<CustomerService>();
        services.AddSingleton<IModuleDbModel, SupplierCategoryDbModel>();
        services.AddScoped<SupplierCategoryService>();
        services.AddSingleton<IPermissionDefinitionProvider, MasterDataPermissions>();
    }
}

/// <summary>模組入口檢視權限；各主檔 CRUD 權限由對應工作單元追加。</summary>
public sealed class MasterDataPermissions : IPermissionDefinitionProvider
{
    public const string MasterView = "MD.MASTER.VIEW";
    public const string CustomerCategoryView = "MD.CUSTOMER_CATEGORY.VIEW";
    public const string CustomerCategoryCreate = "MD.CUSTOMER_CATEGORY.CREATE";
    public const string CustomerCategoryEdit = "MD.CUSTOMER_CATEGORY.EDIT";
    public const string CustomerCategoryDelete = "MD.CUSTOMER_CATEGORY.DELETE";
    public const string CustomerView = "MD.CUSTOMER.VIEW";
    public const string CustomerCreate = "MD.CUSTOMER.CREATE";
    public const string CustomerEdit = "MD.CUSTOMER.EDIT";
    public const string SupplierCategoryView = "MD.SUPPLIER_CATEGORY.VIEW";
    public const string SupplierCategoryCreate = "MD.SUPPLIER_CATEGORY.CREATE";
    public const string SupplierCategoryEdit = "MD.SUPPLIER_CATEGORY.EDIT";
    public const string SupplierCategoryDelete = "MD.SUPPLIER_CATEGORY.DELETE";

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
        [
            new PermissionDefinition(MasterView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerCategoryView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerCategoryCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerCategoryEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerCategoryDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CustomerEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SupplierCategoryView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SupplierCategoryCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SupplierCategoryEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SupplierCategoryDelete, MasterDataModuleContracts.ModuleCode),
        ];
}
