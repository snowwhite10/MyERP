using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Sales.Contracts;
using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Security;
using MyERP.Shared.Time;

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
        services.AddSingleton<IModuleDbModel, SalesQuotationDbModel>();
        services.AddSingleton<IModuleDbModel, SalesPriceRuleDbModel>();
        services.AddScoped<SalesPriceRuleService>();
        services.AddScoped<SalesCompanyAccess>();
        services.AddScoped<SalesActionAccess>();
        services.AddScoped<SalesDeliverySelectionStore>();
        services.AddScoped<SalesDeliveryDraftLines>();
        services.AddScoped<SalesDeliveryCreateService>();
        services.AddScoped<SalesDeliveryDraftCommandService>();
        services.AddScoped<SalesDeliveryQueryService>();
        services.AddScoped<SalesDeliveryPosting>();
        services.AddScoped<SalesDeliverySubmissionService>();
        services.AddScoped<SalesDeliveryTrackingOptionsService>();
        services.AddScoped<SalesDeliveryFormOptionsService>();
        // Deferred posting step: INV posting resolves MD services, which must not be built with the SYS workflow.
        services.AddScoped<IApprovalResultHandler>(sp => new SalesDeliveryApprovalResultHandler(
            sp.GetRequiredService<MyErpDbContext>(), sp.GetRequiredService<SalesDeliveryPosting>));
        services.AddScoped<SalesOrderDraftStore>();
        services.AddScoped<SalesQuotationDraftStore>();
        services.AddScoped<SalesQuotationSnapshotBuilder>();
        services.AddScoped<SalesQuotationCreateService>();
        services.AddScoped<SalesQuotationQueryService>();
        services.AddScoped<SalesQuotationDraftCommandService>();
        services.AddScoped<SalesQuotationSubmissionService>();
        services.AddScoped<SalesQuotationVoidService>();
        services.AddScoped<IApprovalResultHandler, SalesQuotationApprovalResultHandler>();
        services.AddScoped<SalesOrderSnapshotBuilder>();
        services.AddScoped<SalesOrderCreateService>();
        services.AddScoped<SalesOrderConversionService>();
        services.AddScoped<SalesOrderDraftCommandService>();
        services.AddScoped<SalesOrderQueryService>();
        services.AddScoped<SalesOrderCreditCheck>();
        services.AddScoped<SalesOrderSubmissionService>();
        services.AddScoped<SalesOrderCancellationService>();
        // Deferred credit gate: constructor injection would cycle through MD CustomerService → SYS workflow.
        services.AddScoped<IApprovalResultHandler>(sp => new SalesOrderApprovalResultHandler(
            sp.GetRequiredService<MyErpDbContext>(), sp.GetRequiredService<SalesOrderCreditCheck>,
            sp.GetRequiredService<IClock>()));
    }
}

public sealed class SalesPermissions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        .. Build("QUOTATION", "VIEW", "CREATE", "EDIT", "DELETE", "SUBMIT", "VOID"),
        .. Build("ORDER", "VIEW", "CREATE", "EDIT", "DELETE", "SUBMIT", "CANCEL", "EXPORT"),
        .. Build("DELIVERY", "VIEW", "CREATE", "EDIT", "DELETE", "SUBMIT"),
        .. Build("PRICING", "VIEW", "CREATE", "EDIT", "DEACTIVATE", "OVERRIDE"),
        .. Build("INVOICE", "VIEW", "ISSUE"),
        .. Build("CREDIT", "VIEW"),
        .. Build("RETURN", "VIEW", "CREATE")
    ];

    private static IEnumerable<PermissionDefinition> Build(string resource, params string[] actions) =>
        actions.Select(action => new PermissionDefinition($"SAL.{resource}.{action}", SalesModuleContracts.ModuleCode));
}
