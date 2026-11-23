using MyERP.Modules.Sales.Application;
using MyERP.Modules.Sales.Contracts;
using MyERP.Shared.Infrastructure.Seeding;

namespace MyERP.Modules.Sales.Infrastructure.Seeding;

/// <summary>
/// WU-SD-005b3a (ADR-166): demo number rules for sales documents through the SYS number-rule seed contract (ADR-135;
/// SD writes no SYS table). Quotation <c>SQ</c>, order <c>SO</c>, outbound delivery <c>DN</c>, each + year-month +
/// 4 digits, reset monthly. Without them a Development database rejects every quotation/order save with 422
/// <c>SYS.NUMBER_RULE.NOT_CONFIGURED</c>. Existing (user-edited or deactivated) rules are never changed.
/// </summary>
public sealed class SalesNumberRuleSeeds : ISeedNumberRuleProvider
{
    public string Module => SalesModuleContracts.ModuleCode;

    public IReadOnlyList<SeedNumberRuleDefinition> Rules { get; } =
    [
        new(SalesQuotationCreateService.DocumentType, "銷售報價", "SQ"),
        new(SalesOrderCreateService.DocumentType, "銷售訂單", "SO"),
        new(SalesDeliveryCreateService.DocumentType, "銷售出庫", "DN"),
        new(SalesInvoiceIssuing.DocumentType, "銷售發票", "SI"),
    ];
}
