using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Sales.Application;

/// <summary>Advisory credit position; the submit and the SYS final decision always recheck it.</summary>
public sealed record SalesCreditCheckResult(long CustomerId, long? OrderId, SalesCreditAssessment Assessment);

/// <summary>
/// WU-SD-006b3 (BR-SD-035, ADR-170): read-only credit check of one customer, optionally for one saved order as the
/// candidate. Access: a customer-level check needs <c>SAL.CREDIT.VIEW</c> in the current company; an order check
/// needs <c>SAL.CREDIT.VIEW</c> or <c>SAL.ORDER.CREATE/EDIT/SUBMIT</c> whose SYS data scope covers that order.
/// The FI snapshot can only be read under the FI credit key, so the check takes the lock in its own Read Committed
/// transaction and always rolls it back: no row (not even a first-time FI key) and no audit entry survive.
/// </summary>
public sealed class SalesCreditCheckService(MyErpDbContext db, SalesCompanyAccess company,
    ICurrentAuthorization authorization, ICustomerLookup customers, SalesOrderCreditCheck credit)
{
    public const string ViewPermission = "SAL.CREDIT.VIEW";

    /// <summary>Order actions whose holders may check their own order (BR-SD-035).</summary>
    public static IReadOnlyList<string> OrderPermissions { get; } =
        ["SAL.ORDER.CREATE", "SAL.ORDER.EDIT", "SAL.ORDER.SUBMIT"];

    public async Task<SalesCreditCheckResult> CheckAsync(long customerId, long? orderId,
        CancellationToken token = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (customerId <= 0) errors["customerId"] = ["Customer ID must be a positive number."];
        if (orderId is <= 0) errors["orderId"] = ["Order ID must be a positive number."];
        if (errors.Count > 0) throw new RequestValidationException(errors);

        var companyId = company.RequireCompanyId();
        var current = await authorization.GetAsync(token);
        if (current is null || current.CompanyId != companyId) throw Denied();

        var candidate = orderId is { } id
            ? await RequireOrderAsync(current, companyId, customerId, id, token)
            : await RequireCustomerAsync(current, customerId, token);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var dbTransaction = transaction.GetDbTransaction();
            await credit.LockAsync(companyId, customerId, dbTransaction, token);
            var assessment = await credit.AssessAsync(db, companyId, customerId, candidate.OrderId,
                candidate.BaseGross, candidate.BaseCurrency, dbTransaction, token);
            if (assessment.Status == SalesCreditStatus.Blocked)
                throw new BusinessRuleException(assessment.ErrorCode ?? SalesCreditErrors.Unavailable,
                    "Customer credit cannot be confirmed.");
            return new SalesCreditCheckResult(customerId, orderId, assessment);
        }
        finally
        {
            // Read-only by contract: the FI key insert and the lock are discarded with the transaction.
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }
    }

    private async Task<Candidate> RequireCustomerAsync(UserCompanyAuthorization current, long customerId,
        CancellationToken token)
    {
        if (!current.Has(ViewPermission)) throw Denied();
        try
        {
            await customers.RequireApprovedAsync(customerId, token);
        }
        catch (BusinessRuleException ex) when (ex.ErrorCode == "MD.CUSTOMER.NOT_APPROVED")
        {
            // Visible but not usable: the evaluation reports SAL.CREDIT.CUSTOMER_INACTIVE (422).
        }
        return new Candidate(0, 0m, null);
    }

    private async Task<Candidate> RequireOrderAsync(UserCompanyAuthorization current, long companyId,
        long customerId, long orderId, CancellationToken token)
    {
        var scopes = OrderPermissions.Prepend(ViewPermission)
            .Select(current.FilterFor).OfType<DataScopeFilter>().ToList();
        if (scopes.Count == 0) throw Denied();

        var order = await db.Set<SalesOrder>().AsNoTracking()
            .Where(x => x.Id == orderId && x.CompanyId == companyId && !x.IsDeleted)
            .Select(x => new ScopedOrder(x.BranchId, x.DepartmentId, x.OwnerUserId, x.CustomerId, x.BaseGross,
                x.BaseCurrencyCode))
            .SingleOrDefaultAsync(token);
        if (order is null || order.CustomerId != customerId || !scopes.Any(scope => scope.Allows(order)))
            throw new NotFoundException("SAL.ORDER.NOT_FOUND", "Sales order", orderId);
        return new Candidate(orderId, order.BaseGross, order.BaseCurrencyCode);
    }

    private static ForbiddenException Denied() =>
        new("COMMON.PERMISSION.DENIED", "Credit view or an order action permission is required.");

    private sealed record Candidate(long OrderId, decimal BaseGross, string? BaseCurrency);

    private sealed record ScopedOrder(long? BranchId, long? DepartmentId, long OwnerUserId, long CustomerId,
        decimal BaseGross, string BaseCurrencyCode) : IDataScopedEntity;
}
