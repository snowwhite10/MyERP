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
/// gross of active Approved orders of the customer minus the base gross of their Posted invoices, never below zero
/// (BR-SD-041, FI-SALES-003, ADR-178): invoice ISSUE moves that amount to FI open AR under the same credit lock.
/// Submit (WU-SD-006b1) and the SYS final decision (WU-SD-006b2, ADR-171) accept a reasoned override through
/// <see cref="DecideAsync"/>; each caller verifies its own actor's <c>SAL.CREDIT.OVERRIDE</c> grant.
/// </summary>
public sealed class SalesOrderCreditCheck(ICustomerCreditService credit, ICustomerLookup customers,
    ICurrencyLookup currencies, IClock clock)
{
    public Task LockAsync(long companyId, long customerId, DbTransaction transaction, CancellationToken token) =>
        credit.AcquireCreditLockAsync(companyId, customerId, transaction, token);

    /// <summary>
    /// Evaluates the locked, saved candidate order and applies <see cref="SalesCreditRules.Decide"/> (WU-SD-006b1):
    /// within limit passes, a blocked position always throws, and an over-limit result passes only with a reason
    /// and the caller-verified <c>SAL.CREDIT.OVERRIDE</c> grant. Nothing is written here.
    /// </summary>
    public async Task<SalesCreditDecision> DecideAsync(MyErpDbContext db, SalesOrder order,
        DbTransaction transaction, string? overrideReason, bool hasOverrideGrant, CancellationToken token,
        string reasonField = "creditOverrideReason")
    {
        ArgumentNullException.ThrowIfNull(order);
        var assessment = await AssessAsync(db, order.CompanyId, order.CustomerId, order.Id, order.BaseGross,
            order.BaseCurrencyCode, transaction, token);
        return SalesCreditRules.Decide(assessment, overrideReason, hasOverrideGrant, reasonField);
    }

    /// <summary>
    /// Reads the FI snapshot and the customer's Approved SD commitments under the FI credit lock the caller already
    /// holds, and evaluates them. Shared by the commands and the read-only check (WU-SD-006b3).
    /// <paramref name="candidateOrderId"/> 0 is a customer-level check without a candidate order. A
    /// <paramref name="savedBaseCurrency"/> that no longer matches the company base currency is 422
    /// SAL.ORDER.BASE_CURRENCY_CHANGED. Nothing is written here.
    /// </summary>
    public async Task<SalesCreditAssessment> AssessAsync(MyErpDbContext db, long companyId, long customerId,
        long candidateOrderId, decimal candidateBaseGross, string? savedBaseCurrency, DbTransaction transaction,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(db);
        var customerActive = await IsCustomerActiveAsync(customerId, token);
        var baseCurrency = (await currencies.GetBaseCurrencyAsync(token))?.Code;
        if (customerActive && baseCurrency is not null && savedBaseCurrency is not null &&
            baseCurrency != savedBaseCurrency)
            throw new BusinessRuleException("SAL.ORDER.BASE_CURRENCY_CHANGED",
                "The company base currency changed after the order was saved.");

        var today = clock.Today;
        var snapshot = await credit.GetSnapshotAsync(companyId, customerId, today, transaction, token);
        var commitments = await db.Database.SqlQuery<SalesCreditCommitment>($"""
            SELECT o.id AS order_id,
                   CASE WHEN o.base_gross > ISNULL(i.billed, 0) THEN o.base_gross - ISNULL(i.billed, 0) ELSE 0 END
                       AS base_gross
            FROM dbo.sal_order o WITH (INDEX(ix_sal_order_company_id_customer_id_business_date), FORCESEEK)
            OUTER APPLY (
                SELECT SUM(v.base_gross) AS billed
                FROM dbo.sal_invoice v WITH (INDEX(ix_sal_invoice_company_id_order_id_id), FORCESEEK)
                WHERE v.company_id = o.company_id AND v.order_id = o.id AND v.status = 'Posted') i
            WHERE o.company_id = {companyId} AND o.customer_id = {customerId}
              AND o.status = 'Approved' AND o.is_deleted = 0
            """).ToListAsync(token);

        return SalesCreditRules.Evaluate(new SalesCreditCheckInput(companyId, customerId, today, customerActive,
            baseCurrency, candidateOrderId, candidateBaseGross, commitments), snapshot);
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
