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
        services.AddScoped<ICustomerCreditService, CustomerCreditService>();
        services.AddScoped<GlPeriodWriteService>();
        services.AddScoped<GlPostingWriter>();
        services.AddScoped<IGlPostingTransactionPort, GlPostingTransactionPort>();
        services.AddScoped<IGlPostingService, GlPostingService>();
        services.AddScoped<IGlJoinedPostingService, GlJoinedPostingService>();
        services.AddScoped<ISalesInvoicePostingService, SalesInvoicePostingService>();
        services.AddScoped<IAccountUsage, FinanceAccountUsage>();
        services.AddScoped<CreditLimitCommandService>();
        services.AddScoped<CreditLimitQueryService>();
        services.AddScoped<GlReadService>();
        services.AddScoped<GlPeriodCommandService>();
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
        new PermissionDefinition(GlReadService.JournalViewPermission, FinanceModuleContracts.ModuleCode)
    ];
}
