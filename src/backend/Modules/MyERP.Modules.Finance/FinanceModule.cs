using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance;

/// <summary>FI composition root: credit key/limit/exposure foundation (WU-FI-001a/b1), credit limit API (001b2) and GL posting service over its SQL port (004a3x1b2b1).</summary>
public sealed class FinanceModule : IErpModule
{
    public string Code => FinanceModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPermissionDefinitionProvider, FinancePermissions>();
        services.AddSingleton<IModuleDbModel, CustomerCreditDbModel>();
        services.AddSingleton<IModuleDbModel, GlPeriodDbModel>();
        services.AddSingleton<IModuleDbModel, GlJournalDbModel>();
        services.AddSingleton<IModuleDbModel, SalesSourceDbModel>();
        services.AddSingleton<IModuleDbModel, SalesInvoiceAccountMapDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseSourceDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseInvoiceAccountMapDbModel>();
        services.AddSingleton<IModuleDbModel, CollectionDbModel>();
        services.AddSingleton<IModuleDbModel, SalesCollectionAccountMapDbModel>();
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
        services.AddScoped<GlPeriodWriteService>();
        services.AddScoped<GlPostingWriter>();
        services.AddScoped<IGlPostingTransactionPort, GlPostingTransactionPort>();
        services.AddScoped<IGlPostingService, GlPostingService>();
        services.AddScoped<IGlJoinedPostingService, GlJoinedPostingService>();
        services.AddScoped<ISalesInvoicePostingService, SalesInvoicePostingService>();
        // SD 發票科目映射正式來源（WU-FI-002a4a、ADR-109）：兩個介面同一型別、同一列、同一版本
        services.AddScoped<ISalesInvoiceAccountMapSource, SalesInvoiceAccountMapStore>();
        services.AddScoped<IGlPostingMappingVersionSource, SalesInvoiceAccountMapStore>();
        // 銷退信用過帳（WU-FI-002a3b、ADR-113）：沿用發票映射列，SD/Return 版本＝同一列版本
        services.AddScoped<ISalesCreditPostingService, SalesCreditPostingService>();
        services.AddScoped<IGlPostingMappingVersionSource, SalesReturnMappingVersionSource>();
        // PUR 採購發票科目映射正式來源（WU-FI-003a2a、ADR-116）：兩個介面同一型別、同一列、同一版本
        services.AddScoped<IPurchaseInvoiceAccountMapSource, PurchaseInvoiceAccountMapStore>();
        services.AddScoped<IGlPostingMappingVersionSource, PurchaseInvoiceAccountMapStore>();
        // PUR 採購發票應付過帳（WU-FI-003a2b、ADR-117）：PUR 來源埠 IFinancePurchaseInvoiceSource 由 PUR-008a 註冊
        services.AddScoped<IPurchaseInvoicePostingService, PurchaseInvoicePostingService>();
        // 收款／核銷科目映射正式來源（WU-FI-005a2a、ADR-120）：FI/SalesCollection 與 FI/CollectionWriteOff 共用同一列、同一版本
        services.AddScoped<ISalesCollectionAccountMapSource, SalesCollectionAccountMapStore>();
        services.AddScoped<IGlPostingMappingVersionSource, SalesCollectionAccountMapStore>();
        services.AddScoped<IGlPostingMappingVersionSource, CollectionWriteOffMappingVersionSource>();
        // 銷售收款過帳（WU-FI-005a2b、ADR-121）：收款單據來源埠 IFinanceCollectionReceiptSource 由 005b 註冊
        services.AddScoped<ICollectionReceiptPostingService, CollectionReceiptPostingService>();
        // 收款核銷過帳（WU-FI-005a2c、ADR-122）：核銷單據來源埠 IFinanceCollectionWriteOffSource 由 005b 註冊
        services.AddScoped<ICollectionWriteOffPostingService, CollectionWriteOffPostingService>();
        services.AddScoped<IAccountUsage, FinanceAccountUsage>();
        services.AddScoped<CreditLimitCommandService>();
        services.AddScoped<CreditLimitQueryService>();
        services.AddScoped<GlReadService>();
        services.AddScoped<GlPeriodCommandService>();
        services.AddScoped<SalesInvoiceAccountMapService>();
        services.AddScoped<PurchaseInvoiceAccountMapService>();
        // 示範期間與傳票種子（WU-FI-004c4a、ADR-105）：只在 Development 以 --seed 執行；映射在非 Development 回傳 0（拒絕過帳）
        services.AddScoped<IGlPostingMappingVersionSource, Infrastructure.Seeding.GlDemoSeedMapping>();
        services.AddScoped<Infrastructure.Seeding.GlDemoSeeder>();
        services.AddScoped<MyERP.Shared.Infrastructure.Seeding.IDataSeeder>(sp =>
            sp.GetRequiredService<Infrastructure.Seeding.GlDemoSeeder>());
    }
}

/// <summary>FI permission catalogue. Seed FINANCE receives every FI code; MANAGER and VIEWER receive VIEW only.</summary>
public sealed class FinancePermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        .. new[] { "VIEW", "CREATE", "EDIT", "DEACTIVATE" }
            .Select(action => new PermissionDefinition($"AR.CREDIT.{action}", FinanceModuleContracts.ModuleCode)),
        new PermissionDefinition(GlReadService.PeriodViewPermission, FinanceModuleContracts.ModuleCode),
        .. new[] { GlPeriodCommandService.CreatePermission, GlPeriodCommandService.EditPermission,
            GlPeriodCommandService.ClosePermission, GlPeriodCommandService.ReopenPermission,
            GlPeriodCommandService.DeletePermission }
            .Select(code => new PermissionDefinition(code, FinanceModuleContracts.ModuleCode)),
        new PermissionDefinition(GlReadService.JournalViewPermission, FinanceModuleContracts.ModuleCode),
        new PermissionDefinition(SalesInvoiceAccountMapService.ViewPermission, FinanceModuleContracts.ModuleCode),
        new PermissionDefinition(SalesInvoiceAccountMapService.EditPermission, FinanceModuleContracts.ModuleCode),
        new PermissionDefinition(PurchaseInvoiceAccountMapService.ViewPermission, FinanceModuleContracts.ModuleCode),
        new PermissionDefinition(PurchaseInvoiceAccountMapService.EditPermission, FinanceModuleContracts.ModuleCode)
    ];
}
