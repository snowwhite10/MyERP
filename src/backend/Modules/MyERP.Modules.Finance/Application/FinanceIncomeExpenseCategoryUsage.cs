using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// WU-FI-008b1（ADR-212）：向 MD 回報目前公司的費用類別被其他付款引用，MD 因此拒絕刪除（停用仍允許）。
/// FI 保存穩定的 MD ID，所以依 ID 判斷、忽略代碼。引用來源：未軟刪除的其他付款單據（Draft／Pending／Rejected／Posted）、
/// 永久過帳來源 <c>ap_other_payment_source</c>（歷史快照）、費用類別對照 <c>gl_other_payment_account_map</c>。
/// 只讀 FI 自己的表，公司查詢篩選限定範圍。其他收款（WU-FI-007）的收入類別待其 SQL 驗證後另行加入。
/// </summary>
public sealed class FinanceIncomeExpenseCategoryUsage(MyErpDbContext db) : IIncomeExpenseCategoryUsage
{
    public async Task<bool> IsInUseAsync(long categoryId, string categoryCode, CancellationToken token = default) =>
        categoryId > 0
        && (await db.Set<OtherPaymentDocument>().AnyAsync(x => x.ExpenseCategoryId == categoryId, token)
            || await db.Set<OtherPaymentSource>().AnyAsync(x => x.ExpenseCategoryId == categoryId, token)
            || await db.Set<OtherPaymentAccountMapping>().AnyAsync(x => x.ExpenseCategoryId == categoryId, token));
}
