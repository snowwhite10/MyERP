using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Purchasing.Application;
using MyERP.Modules.Purchasing.Contracts;
using MyERP.Modules.Purchasing.Infrastructure;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Security;

namespace MyERP.Modules.Purchasing;

/// <summary>PUR 模組入口。請購資料模型與端點由 WU-PUR-002 加入。</summary>
public sealed class PurchasingModule : IErpModule
{
    public string Code => PurchasingModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPermissionDefinitionProvider, PurchasingPermissions>();
        services.AddScoped<RequisitionCompanyAccess>();
        services.AddScoped<RequisitionQueryService>();
        services.AddScoped<RfqQueryService>();
        services.AddScoped<RfqDraftService>();
        services.AddScoped<RequisitionCreateService>();
        services.AddScoped<RequisitionMutationService>();
        services.AddScoped<RequisitionSubmissionService>();
        services.AddScoped<IApprovalResultHandler, RequisitionApprovalResultHandler>();
        services.AddSingleton<IModuleDbModel, RequisitionDbModel>();
        services.AddSingleton<IModuleDbModel, RfqDbModel>();
        services.AddSingleton<IModuleDbModel, QuotationDbModel>();
    }
}

public sealed class PurchasingPermissions : IPermissionDefinitionProvider
{
    public const string RequisitionView = "PUR.REQUISITION.VIEW";
    public const string RequisitionCreate = "PUR.REQUISITION.CREATE";
    public const string RequisitionEdit = "PUR.REQUISITION.EDIT";
    public const string RequisitionDelete = "PUR.REQUISITION.DELETE";
    public const string RequisitionSubmit = "PUR.REQUISITION.SUBMIT";
    public const string RequisitionExport = "PUR.REQUISITION.EXPORT";
    public const string RfqView = "PUR.RFQ.VIEW";
    public const string RfqCreate = "PUR.RFQ.CREATE";
    public const string RfqEdit = "PUR.RFQ.EDIT";
    public const string RfqDelete = "PUR.RFQ.DELETE";
    public const string RfqSend = "PUR.RFQ.SEND";
    public const string RfqClose = "PUR.RFQ.CLOSE";

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(RequisitionView, PurchasingModuleContracts.ModuleCode),
        new(RequisitionCreate, PurchasingModuleContracts.ModuleCode),
        new(RequisitionEdit, PurchasingModuleContracts.ModuleCode),
        new(RequisitionDelete, PurchasingModuleContracts.ModuleCode),
        new(RequisitionSubmit, PurchasingModuleContracts.ModuleCode),
        new(RequisitionExport, PurchasingModuleContracts.ModuleCode),
        new(RfqView, PurchasingModuleContracts.ModuleCode),
        new(RfqCreate, PurchasingModuleContracts.ModuleCode),
        new(RfqEdit, PurchasingModuleContracts.ModuleCode),
        new(RfqDelete, PurchasingModuleContracts.ModuleCode),
        new(RfqSend, PurchasingModuleContracts.ModuleCode),
        new(RfqClose, PurchasingModuleContracts.ModuleCode),
    ];
}
