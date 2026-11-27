using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Infrastructure.Seeding;

namespace MyERP.Modules.Finance.Infrastructure.Seeding;

/// <summary>
/// WU-FI-005c3a（ADR-135）：FI 單據的示範單號規則，經 SYS 單號規則種子契約建立（FI 不寫 SYS 資料表）。
/// 銷售收款 <c>RC</c>＋年月＋4 位（例如 RC2026110001）、收款核銷 <c>RS</c>、其他付款 <c>OP</c>（WU-FI-008b1）＋年月＋4 位，每月重設。
/// </summary>
public sealed class FinanceNumberRuleSeeds : ISeedNumberRuleProvider
{
    public string Module => FinanceModuleContracts.ModuleCode;

    public IReadOnlyList<SeedNumberRuleDefinition> Rules { get; } =
    [
        new(CollectionReceiptDocumentService.DocumentType, "銷售收款", "RC"),
        new(CollectionSettlementDocumentService.DocumentType, "收款核銷", "RS"),
        new(OtherPaymentDocumentService.DocumentType, "其他付款", "OP"),
    ];
}
