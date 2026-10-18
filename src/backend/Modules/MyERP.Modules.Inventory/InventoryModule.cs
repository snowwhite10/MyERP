using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.Inventory.Contracts;
using MyERP.Modules.Inventory.Application;
using MyERP.Modules.Inventory.Infrastructure;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Approvals;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Security;

namespace MyERP.Modules.Inventory;

/// <summary>INV 模組入口：引用安全、異動帳過帳與結存查詢；業務單據由後續工作單元加入。</summary>
public sealed class InventoryModule : IErpModule
{
    public string Code => InventoryModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPermissionDefinitionProvider, InventoryPermissions>();
        services.AddSingleton<IModuleDbModel, InventoryReferenceDbModel>();
        services.AddSingleton<IModuleDbModel, StockMovementDbModel>();
        services.AddSingleton<IModuleDbModel, SafetyStockDbModel>();
        services.AddScoped<SafetyStockService>();
        services.AddSingleton<IModuleDbModel, StockTrackingDbModel>();
        services.AddScoped<InventoryReferenceService>();
        services.AddScoped<IItemUsage>(sp => sp.GetRequiredService<InventoryReferenceService>());
        services.AddScoped<IWarehouseUsage>(sp => sp.GetRequiredService<InventoryReferenceService>());
        services.AddScoped<IStorageLocationUsage>(sp => sp.GetRequiredService<InventoryReferenceService>());
        services.AddScoped<IInventoryPostingService, StockPostingService>();
        services.AddScoped<IExternalInventoryPostingService, ExternalInventoryPostingService>();
        services.AddScoped<IStockBalanceQuery, StockBalanceQuery>();
        services.AddScoped<IInventoryValuationQuery, InventoryValuationQuery>();
        services.AddScoped<AssemblyCostSnapshot>();
        services.AddScoped<StockQueryService>();
        services.AddScoped<StockFlowReportService>();
        services.AddSingleton<IModuleDbModel, OtherReceiptDbModel>();
        services.AddScoped<OtherReceiptService>();
        services.AddScoped<IApprovalResultHandler, OtherReceiptApprovalResultHandler>();
        services.AddSingleton<IModuleDbModel, OtherIssueDbModel>();
        services.AddScoped<OtherIssueService>();
        services.AddScoped<IApprovalResultHandler, OtherIssueApprovalResultHandler>();
        services.AddSingleton<IModuleDbModel, OpeningStockDbModel>();
        services.AddScoped<OpeningStockService>();
        services.AddScoped<TrackingQueryService>();
        services.AddSingleton<IModuleDbModel, TransferDbModel>();
        services.AddScoped<TransferService>();
        services.AddScoped<IApprovalResultHandler, TransferApprovalResultHandler>();
        services.AddSingleton<IModuleDbModel, AssemblyDbModel>();
        services.AddScoped<AssemblyService>();
        services.AddScoped<IApprovalResultHandler, AssemblyApprovalResultHandler>();
        services.AddSingleton<IModuleDbModel, StocktakeDbModel>();
        services.AddScoped<StocktakeService>();
        services.AddScoped<IApprovalResultHandler, StocktakeApprovalResultHandler>();
    }
}

public sealed class InventoryPermissions : IPermissionDefinitionProvider
{
    public const string InventoryView = "INV.INVENTORY.VIEW";
    public const string MovementView = "INV.MOVEMENT.VIEW";
    public const string OtherReceiptView = "INV.OTHER_RECEIPT.VIEW";
    public const string OtherReceiptCreate = "INV.OTHER_RECEIPT.CREATE";
    public const string OtherReceiptEdit = "INV.OTHER_RECEIPT.EDIT";
    public const string OtherReceiptDelete = "INV.OTHER_RECEIPT.DELETE";
    public const string OtherReceiptSubmit = "INV.OTHER_RECEIPT.SUBMIT";
    public const string OtherIssueView = "INV.OTHER_ISSUE.VIEW";
    public const string OtherIssueCreate = "INV.OTHER_ISSUE.CREATE";
    public const string OtherIssueEdit = "INV.OTHER_ISSUE.EDIT";
    public const string OtherIssueDelete = "INV.OTHER_ISSUE.DELETE";
    public const string OtherIssueSubmit = "INV.OTHER_ISSUE.SUBMIT";
    public const string OpeningStockView = "INV.OPENING_STOCK.VIEW";
    public const string OpeningStockCreate = "INV.OPENING_STOCK.CREATE";
    public const string TrackingView = "INV.TRACKING.VIEW";
    public const string TransferView = "INV.TRANSFER.VIEW";
    public const string TransferCreate = "INV.TRANSFER.CREATE";
    public const string TransferEdit = "INV.TRANSFER.EDIT";
    public const string TransferDelete = "INV.TRANSFER.DELETE";
    public const string TransferSubmit = "INV.TRANSFER.SUBMIT";
    public const string StocktakeView = "INV.STOCKTAKE.VIEW";
    public const string StocktakeCreate = "INV.STOCKTAKE.CREATE";
    public const string StocktakeEdit = "INV.STOCKTAKE.EDIT";
    public const string StocktakeDelete = "INV.STOCKTAKE.DELETE";
    public const string StocktakeSubmit = "INV.STOCKTAKE.SUBMIT";
    public const string ValuationView = "INV.VALUATION.VIEW";
    public const string AssemblyView = "INV.ASSEMBLY.VIEW";
    public const string AssemblyCreate = "INV.ASSEMBLY.CREATE";
    public const string AssemblyEdit = "INV.ASSEMBLY.EDIT";
    public const string AssemblyDelete = "INV.ASSEMBLY.DELETE";
    public const string AssemblySubmit = "INV.ASSEMBLY.SUBMIT";
    public const string SafetyStockView = "INV.SAFETY_STOCK.VIEW";
    public const string SafetyStockEdit = "INV.SAFETY_STOCK.EDIT";
    public const string ReportView = "INV.REPORT.VIEW";
    public const string ReportExport = "INV.REPORT.EXPORT";

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(InventoryView, InventoryModuleContracts.ModuleCode),
        new(MovementView, InventoryModuleContracts.ModuleCode),
        new(OtherReceiptView, InventoryModuleContracts.ModuleCode),
        new(OtherReceiptCreate, InventoryModuleContracts.ModuleCode),
        new(OtherReceiptEdit, InventoryModuleContracts.ModuleCode),
        new(OtherReceiptDelete, InventoryModuleContracts.ModuleCode),
        new(OtherReceiptSubmit, InventoryModuleContracts.ModuleCode),
        new(OtherIssueView, InventoryModuleContracts.ModuleCode),
        new(OtherIssueCreate, InventoryModuleContracts.ModuleCode),
        new(OtherIssueEdit, InventoryModuleContracts.ModuleCode),
        new(OtherIssueDelete, InventoryModuleContracts.ModuleCode),
        new(OtherIssueSubmit, InventoryModuleContracts.ModuleCode),
        new(OpeningStockView, InventoryModuleContracts.ModuleCode),
        new(OpeningStockCreate, InventoryModuleContracts.ModuleCode),
        new(TrackingView, InventoryModuleContracts.ModuleCode),
        new(TransferView, InventoryModuleContracts.ModuleCode),
        new(TransferCreate, InventoryModuleContracts.ModuleCode),
        new(TransferEdit, InventoryModuleContracts.ModuleCode),
        new(TransferDelete, InventoryModuleContracts.ModuleCode),
        new(TransferSubmit, InventoryModuleContracts.ModuleCode),
        new(StocktakeView, InventoryModuleContracts.ModuleCode),
        new(StocktakeCreate, InventoryModuleContracts.ModuleCode),
        new(StocktakeEdit, InventoryModuleContracts.ModuleCode),
        new(StocktakeDelete, InventoryModuleContracts.ModuleCode),
        new(StocktakeSubmit, InventoryModuleContracts.ModuleCode),
        new(ValuationView, InventoryModuleContracts.ModuleCode),
        new(AssemblyView, InventoryModuleContracts.ModuleCode),
        new(AssemblyCreate, InventoryModuleContracts.ModuleCode),
        new(AssemblyEdit, InventoryModuleContracts.ModuleCode),
        new(AssemblyDelete, InventoryModuleContracts.ModuleCode),
        new(AssemblySubmit, InventoryModuleContracts.ModuleCode),
        new(SafetyStockView, InventoryModuleContracts.ModuleCode),
        new(SafetyStockEdit, InventoryModuleContracts.ModuleCode),
        new(ReportView, InventoryModuleContracts.ModuleCode),
        new(ReportExport, InventoryModuleContracts.ModuleCode),
    ];
}
