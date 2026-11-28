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
        services.AddScoped<QuotationQueryService>();
        services.AddScoped<QuotationComparisonService>();
        services.AddScoped<RfqAwardService>();
        services.AddScoped<RfqConversionService>();
        services.AddScoped<PurchaseOrderDraftService>();
        services.AddScoped<PurchaseOrderQueryService>();
        services.AddScoped<PurchaseOrderOptionService>();
        services.AddScoped<PurchaseOrderSubmissionService>();
        services.AddScoped<PurchaseOrderLifecycleService>();
        services.AddScoped<PurchaseReceiptDraftService>();
        services.AddScoped<PurchaseReceiptSubmissionService>();
        services.AddScoped<PurchaseReceiptQueryService>();
        services.AddScoped<PurchaseReceiptLifecycleService>();
        services.AddScoped<PurchaseReturnDraftService>();
        services.AddScoped<PurchaseReturnSubmissionService>();
        services.AddScoped<PurchaseReturnQueryService>();
        services.AddScoped<PurchaseReturnPosting>();
        services.AddScoped<QuotationMutationService>();
        services.AddScoped<RfqDraftService>();
        services.AddScoped<RequisitionCreateService>();
        services.AddScoped<RequisitionMutationService>();
        services.AddScoped<RequisitionSubmissionService>();
        services.AddScoped<IApprovalResultHandler, RequisitionApprovalResultHandler>();
        services.AddScoped<IApprovalResultHandler, PurchaseOrderApprovalResultHandler>();
        services.AddScoped<PurchaseReceiptPosting>();
        services.AddScoped<PurchaseReceiptTracking>();
        // Deferred posting step (ADR-220): INV posting resolves MD services, which must not be built with the SYS workflow.
        services.AddScoped<IApprovalResultHandler>(sp => new PurchaseReceiptApprovalResultHandler(
            sp.GetRequiredService<MyErpDbContext>(), sp.GetRequiredService<PurchaseReceiptPosting>));
        services.AddScoped<IApprovalResultHandler>(sp => new PurchaseReturnApprovalResultHandler(
            sp.GetRequiredService<MyErpDbContext>(), sp.GetRequiredService<PurchaseReturnPosting>));
        services.AddSingleton<IModuleDbModel, RequisitionDbModel>();
        services.AddSingleton<IModuleDbModel, RfqDbModel>();
        services.AddSingleton<IModuleDbModel, QuotationDbModel>();
        services.AddSingleton<IModuleDbModel, RfqAwardDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseInvoiceNumberDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseInvoiceDraftDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseInvoiceSourceDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseReturnDbModel>();
        services.AddSingleton<IModuleDbModel, PurchaseInvoiceResolutionDbModel>();
        services.AddScoped<IPurchaseInvoiceDraftSourceReader, PurchaseInvoiceDraftSourceReader>();
        services.AddScoped<IPurchaseInvoiceExchangeSource, PurchaseInvoiceExchangeSource>();
        // FI 應付過帳來源埠（WU-PUR-008b3、ADR-207）：FI 在 PUR POST 交易內讀取保存發票。
        services.AddScoped<MyERP.Modules.Finance.Contracts.IFinancePurchaseInvoiceSource, PurchaseInvoiceFinanceSource>();
        services.AddScoped<IPurchaseInvoiceDraftSessionFactory, PurchaseInvoiceDraftSessionFactory>();
        services.AddScoped<PurchaseInvoiceDraftSaveService>();
        services.AddScoped<PurchaseInvoiceDraftMutationService>();
        services.AddScoped<PurchaseInvoiceMatchService>();
        services.AddScoped<PurchaseInvoiceResolveService>();
        services.AddScoped<PurchaseInvoicePostService>();
        services.AddScoped<PurchaseInvoiceQueryService>();
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
    public const string RfqExport = "PUR.RFQ.EXPORT";
    public const string RfqCreate = "PUR.RFQ.CREATE";
    public const string RfqEdit = "PUR.RFQ.EDIT";
    public const string RfqDelete = "PUR.RFQ.DELETE";
    public const string RfqSend = "PUR.RFQ.SEND";
    public const string RfqClose = "PUR.RFQ.CLOSE";
    public const string QuotationView = "PUR.QUOTATION.VIEW";
    public const string QuotationQuote = "PUR.QUOTATION.QUOTE";
    public const string RfqCompare = "PUR.RFQ.COMPARE";
    public const string RfqAward = "PUR.RFQ.AWARD";
    public const string RfqConvert = "PUR.RFQ.CONVERT";
    public const string OrderCreate = "PUR.ORDER.CREATE";
    public const string OrderView = "PUR.ORDER.VIEW";
    public const string OrderExport = "PUR.ORDER.EXPORT";
    public const string OrderEdit = "PUR.ORDER.EDIT";
    public const string OrderSubmit = "PUR.ORDER.SUBMIT";
    public const string OrderDelete = "PUR.ORDER.DELETE";
    public const string OrderCancel = "PUR.ORDER.CANCEL";
    public const string ReceiptCreate = "PUR.RECEIPT.CREATE";
    public const string ReceiptEdit = "PUR.RECEIPT.EDIT";
    public const string ReceiptView = "PUR.RECEIPT.VIEW";
    public const string ReceiptSubmit = "PUR.RECEIPT.SUBMIT";
    public const string ReceiptDelete = "PUR.RECEIPT.DELETE";
    public const string ReceiptExport = "PUR.RECEIPT.EXPORT";
    public const string ReturnView = "PUR.RETURN.VIEW";
    public const string ReturnCreate = "PUR.RETURN.CREATE";
    public const string ReturnEdit = "PUR.RETURN.EDIT";
    public const string ReturnDelete = "PUR.RETURN.DELETE";
    public const string ReturnSubmit = "PUR.RETURN.SUBMIT";
    public const string ReturnExport = "PUR.RETURN.EXPORT";
    public const string InvoiceCreate = "PUR.INVOICE.CREATE";
    public const string InvoiceEdit = "PUR.INVOICE.EDIT";
    public const string InvoiceMatch = "PUR.INVOICE.MATCH";
    public const string InvoiceResolve = "PUR.INVOICE.RESOLVE";
    public const string InvoicePost = "PUR.INVOICE.POST";
    public const string InvoiceView = "PUR.INVOICE.VIEW";
    public const string InvoiceExport = "PUR.INVOICE.EXPORT";

    public IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        new(RequisitionView, PurchasingModuleContracts.ModuleCode),
        new(RequisitionCreate, PurchasingModuleContracts.ModuleCode),
        new(RequisitionEdit, PurchasingModuleContracts.ModuleCode),
        new(RequisitionDelete, PurchasingModuleContracts.ModuleCode),
        new(RequisitionSubmit, PurchasingModuleContracts.ModuleCode),
        new(RequisitionExport, PurchasingModuleContracts.ModuleCode),
        new(RfqView, PurchasingModuleContracts.ModuleCode),
        new(RfqExport, PurchasingModuleContracts.ModuleCode),
        new(RfqCreate, PurchasingModuleContracts.ModuleCode),
        new(RfqEdit, PurchasingModuleContracts.ModuleCode),
        new(RfqDelete, PurchasingModuleContracts.ModuleCode),
        new(RfqSend, PurchasingModuleContracts.ModuleCode),
        new(RfqClose, PurchasingModuleContracts.ModuleCode),
        new(QuotationView, PurchasingModuleContracts.ModuleCode),
        new(QuotationQuote, PurchasingModuleContracts.ModuleCode),
        new(RfqCompare, PurchasingModuleContracts.ModuleCode),
        new(RfqAward, PurchasingModuleContracts.ModuleCode),
        new(RfqConvert, PurchasingModuleContracts.ModuleCode),
        new(OrderCreate, PurchasingModuleContracts.ModuleCode),
        new(OrderView, PurchasingModuleContracts.ModuleCode),
        new(OrderExport, PurchasingModuleContracts.ModuleCode),
        new(OrderEdit, PurchasingModuleContracts.ModuleCode),
        new(OrderSubmit, PurchasingModuleContracts.ModuleCode),
        new(OrderDelete, PurchasingModuleContracts.ModuleCode),
        new(OrderCancel, PurchasingModuleContracts.ModuleCode),
        new(ReceiptCreate, PurchasingModuleContracts.ModuleCode),
        new(ReceiptEdit, PurchasingModuleContracts.ModuleCode),
        new(ReceiptView, PurchasingModuleContracts.ModuleCode),
        new(ReceiptSubmit, PurchasingModuleContracts.ModuleCode),
        new(ReceiptDelete, PurchasingModuleContracts.ModuleCode),
        new(ReceiptExport, PurchasingModuleContracts.ModuleCode),
        new(ReturnView, PurchasingModuleContracts.ModuleCode),
        new(ReturnCreate, PurchasingModuleContracts.ModuleCode),
        new(ReturnEdit, PurchasingModuleContracts.ModuleCode),
        new(ReturnDelete, PurchasingModuleContracts.ModuleCode),
        new(ReturnSubmit, PurchasingModuleContracts.ModuleCode),
        new(ReturnExport, PurchasingModuleContracts.ModuleCode),
        new(InvoiceCreate, PurchasingModuleContracts.ModuleCode),
        new(InvoiceEdit, PurchasingModuleContracts.ModuleCode),
        new(InvoiceMatch, PurchasingModuleContracts.ModuleCode),
        new(InvoiceResolve, PurchasingModuleContracts.ModuleCode),
        new(InvoicePost, PurchasingModuleContracts.ModuleCode),
        new(InvoiceView, PurchasingModuleContracts.ModuleCode),
        new(InvoiceExport, PurchasingModuleContracts.ModuleCode),
    ];
}
