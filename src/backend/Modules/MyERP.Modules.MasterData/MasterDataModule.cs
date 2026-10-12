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
        services.AddSingleton<IModuleDbModel, CustomerContactDbModel>();
        services.AddScoped<CustomerContactService>();
        services.AddScoped<CustomerAddressService>();
        services.AddSingleton<IModuleDbModel, SupplierCategoryDbModel>();
        services.AddScoped<SupplierCategoryService>();
        services.AddSingleton<IModuleDbModel, SupplierDbModel>();
        services.AddScoped<SupplierService>();
        services.AddSingleton<IModuleDbModel, UomDbModel>();
        services.AddScoped<UomService>();
        services.AddScoped<UomConversionService>();
        services.AddScoped<IUomConverter>(sp => sp.GetRequiredService<UomConversionService>());
        services.AddSingleton<IModuleDbModel, ItemBrandDbModel>();
        services.AddScoped<ItemBrandService>();
        services.AddSingleton<IModuleDbModel, ItemCategoryDbModel>();
        services.AddScoped<ItemCategoryService>();
        services.AddSingleton<IModuleDbModel, ItemDbModel>();
        services.AddScoped<ItemService>();
        services.AddScoped<ItemUomConversionService>();
        services.AddSingleton<IModuleDbModel, WarehouseDbModel>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<StorageLocationService>();
        services.AddSingleton<IModuleDbModel, CurrencyDbModel>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<ExchangeRateService>();
        services.AddScoped<ICurrencyLookup, CurrencyLookup>();
        services.AddSingleton<IModuleDbModel, TaxDbModel>();
        services.AddScoped<TaxCodeService>();
        services.AddScoped<TaxRateService>();
        services.AddScoped<ITaxRateLookup, TaxRateLookup>();
        services.AddSingleton<IModuleDbModel, AccountDbModel>();
        services.AddScoped<AccountService>();
        services.AddScoped<IChartOfAccounts, ChartOfAccounts>();
        services.AddSingleton<IModuleDbModel, PaymentSetupDbModel>();
        services.AddScoped<SettlementMethodService>();
        services.AddScoped<ISettlementMethodLookup, SettlementMethodLookup>();
        services.AddScoped<PaymentTermService>();
        services.AddScoped<IPaymentTermLookup, PaymentTermLookup>();
        services.AddSingleton<IModuleDbModel, FundSetupDbModel>();
        services.AddScoped<FundAccountService>();
        services.AddScoped<IFundAccountLookup, FundAccountLookup>();
        services.AddScoped<IncomeExpenseCategoryService>();
        services.AddScoped<IIncomeExpenseCategoryLookup, IncomeExpenseCategoryLookup>();
        services.AddScoped<IAccountUsage, FundSetupAccountUsage>();
        services.AddSingleton<IModuleDbModel, ProcessDbModel>();
        services.AddScoped<ProcessCategoryService>();
        services.AddScoped<ProcessService>();
        services.AddScoped<IProcessLookup, ProcessLookup>();
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
    public const string SupplierView = "MD.SUPPLIER.VIEW";
    public const string SupplierCreate = "MD.SUPPLIER.CREATE";
    public const string SupplierEdit = "MD.SUPPLIER.EDIT";
    public const string UomView = "MD.UOM.VIEW";
    public const string UomCreate = "MD.UOM.CREATE";
    public const string UomEdit = "MD.UOM.EDIT";
    public const string UomDelete = "MD.UOM.DELETE";
    public const string ItemBrandView = "MD.ITEM_BRAND.VIEW";
    public const string ItemBrandCreate = "MD.ITEM_BRAND.CREATE";
    public const string ItemBrandEdit = "MD.ITEM_BRAND.EDIT";
    public const string ItemBrandDelete = "MD.ITEM_BRAND.DELETE";
    public const string ItemCategoryView = "MD.ITEM_CATEGORY.VIEW";
    public const string ItemCategoryCreate = "MD.ITEM_CATEGORY.CREATE";
    public const string ItemCategoryEdit = "MD.ITEM_CATEGORY.EDIT";
    public const string ItemCategoryDelete = "MD.ITEM_CATEGORY.DELETE";
    public const string ItemView = "MD.ITEM.VIEW";
    public const string ItemCreate = "MD.ITEM.CREATE";
    public const string ItemEdit = "MD.ITEM.EDIT";
    public const string WarehouseView = "MD.WAREHOUSE.VIEW";
    public const string WarehouseCreate = "MD.WAREHOUSE.CREATE";
    public const string WarehouseEdit = "MD.WAREHOUSE.EDIT";
    public const string WarehouseDelete = "MD.WAREHOUSE.DELETE";
    public const string CurrencyView = "MD.CURRENCY.VIEW";
    public const string CurrencyCreate = "MD.CURRENCY.CREATE";
    public const string CurrencyEdit = "MD.CURRENCY.EDIT";
    public const string CurrencyDelete = "MD.CURRENCY.DELETE";
    public const string TaxCodeView = "MD.TAX_CODE.VIEW";
    public const string TaxCodeCreate = "MD.TAX_CODE.CREATE";
    public const string TaxCodeEdit = "MD.TAX_CODE.EDIT";
    public const string TaxCodeDelete = "MD.TAX_CODE.DELETE";
    public const string AccountView = "MD.ACCOUNT.VIEW";
    public const string AccountCreate = "MD.ACCOUNT.CREATE";
    public const string AccountEdit = "MD.ACCOUNT.EDIT";
    public const string AccountDelete = "MD.ACCOUNT.DELETE";
    public const string SettlementMethodView = "MD.SETTLEMENT_METHOD.VIEW";
    public const string SettlementMethodCreate = "MD.SETTLEMENT_METHOD.CREATE";
    public const string SettlementMethodEdit = "MD.SETTLEMENT_METHOD.EDIT";
    public const string SettlementMethodDelete = "MD.SETTLEMENT_METHOD.DELETE";
    public const string PaymentTermView = "MD.PAYMENT_TERM.VIEW";
    public const string PaymentTermCreate = "MD.PAYMENT_TERM.CREATE";
    public const string PaymentTermEdit = "MD.PAYMENT_TERM.EDIT";
    public const string PaymentTermDelete = "MD.PAYMENT_TERM.DELETE";
    public const string FundAccountView = "MD.FUND_ACCOUNT.VIEW";
    public const string FundAccountCreate = "MD.FUND_ACCOUNT.CREATE";
    public const string FundAccountEdit = "MD.FUND_ACCOUNT.EDIT";
    public const string FundAccountDelete = "MD.FUND_ACCOUNT.DELETE";
    public const string IncomeExpenseCategoryView = "MD.INCOME_EXPENSE_CATEGORY.VIEW";
    public const string IncomeExpenseCategoryCreate = "MD.INCOME_EXPENSE_CATEGORY.CREATE";
    public const string IncomeExpenseCategoryEdit = "MD.INCOME_EXPENSE_CATEGORY.EDIT";
    public const string IncomeExpenseCategoryDelete = "MD.INCOME_EXPENSE_CATEGORY.DELETE";
    public const string ProcessCategoryView = "MD.PROCESS_CATEGORY.VIEW";
    public const string ProcessCategoryCreate = "MD.PROCESS_CATEGORY.CREATE";
    public const string ProcessCategoryEdit = "MD.PROCESS_CATEGORY.EDIT";
    public const string ProcessCategoryDelete = "MD.PROCESS_CATEGORY.DELETE";
    public const string ProcessView = "MD.PROCESS.VIEW";
    public const string ProcessCreate = "MD.PROCESS.CREATE";
    public const string ProcessEdit = "MD.PROCESS.EDIT";
    public const string ProcessDelete = "MD.PROCESS.DELETE";

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
            new PermissionDefinition(SupplierView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SupplierCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SupplierEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(UomView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(UomCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(UomEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(UomDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemBrandView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemBrandCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemBrandEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemBrandDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemCategoryView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemCategoryCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemCategoryEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemCategoryDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ItemEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(WarehouseView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(WarehouseCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(WarehouseEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(WarehouseDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CurrencyView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CurrencyCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CurrencyEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(CurrencyDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(TaxCodeView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(TaxCodeCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(TaxCodeEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(TaxCodeDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(AccountView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(AccountCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(AccountEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(AccountDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SettlementMethodView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SettlementMethodCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SettlementMethodEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(SettlementMethodDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(PaymentTermView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(PaymentTermCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(PaymentTermEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(PaymentTermDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(FundAccountView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(FundAccountCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(FundAccountEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(FundAccountDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(IncomeExpenseCategoryView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(IncomeExpenseCategoryCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(IncomeExpenseCategoryEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(IncomeExpenseCategoryDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessCategoryView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessCategoryCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessCategoryEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessCategoryDelete, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessView, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessCreate, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessEdit, MasterDataModuleContracts.ModuleCode),
            new PermissionDefinition(ProcessDelete, MasterDataModuleContracts.ModuleCode),
        ];
}
