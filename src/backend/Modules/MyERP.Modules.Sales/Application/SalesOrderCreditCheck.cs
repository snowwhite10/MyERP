using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Time;

namespace MyERP.Modules.Sales.Application;

/// <summary>
/// WU-SD-003b1 (BR-SD-018/031～033, ADR-142): the transactional credit gate shared by order submit and the SYS
/// final decision. The caller first takes <see cref="LockAsync"/> (FI credit key) and only then locks the order row,
/// so every order approval for one company/customer serializes on the FI lock. Commitments are the saved base
/// gross of active Approved orders of the customer (no SD invoice exists yet, so nothing is uninvoiced-released).
/// Submit accepts a reasoned override through <see cref="DecideAsync"/> (WU-SD-006b1); the SYS final decision still
/// refuses any over-limit result until WU-SD-006b2.
/// </summary>
public sealed class SalesOrderCreditCheck(ICustomerCreditService credit, ICustomerLookup customers,
    ICurrencyLookup currencies, IClock clock)
{
    public Task LockAsync(long companyId, long customerId, DbTransaction transaction, CancellationToken token) =>
        credit.AcquireCreditLockAsync(companyId, customerId, transaction, token);

    /// <summary>Evaluates the locked, saved candidate order; throws the named credit error unless within limit.</summary>
    public async Task<SalesCreditAssessment> RequireWithinLimitAsync(MyErpDbContext db, SalesOrder order,
        DbTransaction transaction, CancellationToken token) =>
        (await DecideAsync(db, order, transaction, null, false, token)).Assessment;

    /// <summary>
    /// Evaluates the locked, saved candidate order and applies <see cref="SalesCreditRules.Decide"/> (WU-SD-006b1):
    /// within limit passes, a blocked position always throws, and an over-limit result passes only with a reason
    /// and the caller-verified <c>SAL.CREDIT.OVERRIDE</c> grant. Nothing is written here.
    /// </summary>
    public async Task<SalesCreditDecision> DecideAsync(MyErpDbContext db, SalesOrder order,
        DbTransaction transaction, string? overrideReason, bool hasOverrideGrant, CancellationToken token)
    {
        var customerActive = await IsCustomerActiveAsync(order.CustomerId, token);
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(token))?.Code;
        if (customerActive && baseCurrency is not null && baseCurrency != order.BaseCurrencyCode)
            throw new BusinessRuleException("SAL.ORDER.BASE_CURRENCY_CHANGED",
                "The company base currency changed after the order was saved.");

        var today = clock.Today;
        var snapshot = await credit.GetSnapshotAsync(order.CompanyId, order.CustomerId, today, transaction, token);
        var commitments = await db.Database.SqlQuery<SalesCreditCommitment>($"""
            SELECT id AS order_id, base_gross
            FROM dbo.sal_order WITH (INDEX(ix_sal_order_company_id_customer_id_business_date), FORCESEEK)
            WHERE company_id = {order.CompanyId} AND customer_id = {order.CustomerId}
              AND status = 'Approved' AND is_deleted = 0
            """).ToListAsync(token);

        var assessment = SalesCreditRules.Evaluate(new SalesCreditCheckInput(order.CompanyId, order.CustomerId,
            today, customerActive, baseCurrency, order.Id, order.BaseGross, commitments), snapshot);
        return SalesCreditRules.Decide(assessment, overrideReason, hasOverrideGrant);
    }

    private async Task<bool> IsCustomerActiveAsync(long customerId, CancellationToken token)
    {
        try
        {
            await customers.RequireApprovedAsync(customerId, token);
            return true;
        }
        catch (NotFoundException)
        {
            return false;
        }
        catch (BusinessRuleException ex) when (ex.ErrorCode == "MD.CUSTOMER.NOT_APPROVED")
        {
            return false;
        }
    }
}
