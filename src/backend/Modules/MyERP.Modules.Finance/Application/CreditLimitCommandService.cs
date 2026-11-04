using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MyERP.Modules.Finance.Contracts;
using MyERP.Modules.Finance.Domain;
using MyERP.Modules.MasterData.Contracts;
using MyERP.Shared.Errors;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Security;

namespace MyERP.Modules.Finance.Application;

/// <summary>Client-editable credit limit fields. Company, base currency, status and actor are server-derived.</summary>
public sealed record CreditLimitInput(long CustomerId, DateOnly EffectiveFrom, DateOnly EffectiveTo, decimal Amount);

public sealed record CreditLimitDetail(long Id, long CustomerId, string BaseCurrency, DateOnly EffectiveFrom,
    DateOnly EffectiveTo, decimal Amount, CreditLimitStatus Status, int Version, DateTime CreatedAt,
    long CreatedBy, DateTime? UpdatedAt, long? UpdatedBy)
{
    internal static CreditLimitDetail From(CreditLimit x) => new(x.Id, x.CustomerId, x.BaseCurrency, x.EffectiveFrom,
        x.EffectiveTo, x.Amount, x.Status, x.Version, x.CreatedAt, x.CreatedBy, x.UpdatedAt, x.UpdatedBy);
}

public static class CreditLimitErrors
{
    public const string NotFound = "FI.CREDIT.LIMIT_NOT_FOUND";
    /// <summary>409: another Active period of the same company/customer shares at least one inclusive date.</summary>
    public const string Overlap = "FI.CREDIT.LIMIT_OVERLAP";
    /// <summary>422: a Deactivated limit is history; it is never edited, re-deactivated or reactivated.</summary>
    public const string NotActive = "FI.CREDIT.LIMIT_NOT_ACTIVE";
    /// <summary>422: the current company has no MD base currency (BR-FI-002).</summary>
    public const string BaseCurrencyMissing = "FI.CREDIT.BASE_CURRENCY_MISSING";
    /// <summary>422: the saved limit currency differs from the current company base currency.</summary>
    public const string CurrencyMismatch = "FI.CREDIT.CURRENCY_MISMATCH";
}

/// <summary>
/// REQ_F4116.7.1, BR-FI-004 (WU-FI-001b1). Every write takes the FI company/customer credit key lock first
/// (<see cref="ICustomerCreditService.AcquireCreditLockAsync"/>), then re-reads and checks version and inclusive
/// overlap among Active rows inside the same READ COMMITTED transaction, so concurrent writers and SD credit
/// decisions serialize on one key. Any failure rolls back the key, the limit and the audit rows together.
/// </summary>
public sealed class CreditLimitCommandService(MyErpDbContext db, ICurrentCompany currentCompany,
    ICurrentAuthorization authorization, ICustomerCreditService credit, ICustomerLookup customers,
    ICurrencyLookup currencies)
{
    public const string CreatePermission = "AR.CREDIT.CREATE";
    public const string EditPermission = "AR.CREDIT.EDIT";
    public const string DeactivatePermission = "AR.CREDIT.DEACTIVATE";
    internal const decimal MaxAmount = 999_999_999_999_999.9999m;

    public async Task<CreditLimitDetail> CreateAsync(CreditLimitInput input, CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(CreatePermission, token);
        Validate(input);
        await customers.RequireApprovedAsync(input.CustomerId, token);
        var baseCurrency = await RequireBaseCurrencyAsync(token);
        return await InLockedTransactionAsync(companyId, input.CustomerId, async () =>
        {
            await EnsureNoOverlapAsync(input, null, token);
            var limit = new CreditLimit
            {
                CompanyId = companyId,
                CustomerId = input.CustomerId,
                BaseCurrency = baseCurrency,
                EffectiveFrom = input.EffectiveFrom,
                EffectiveTo = input.EffectiveTo,
                Amount = input.Amount,
                Status = CreditLimitStatus.Active
            };
            db.Add(limit);
            await db.SaveChangesAsync(token);
            return CreditLimitDetail.From(limit);
        }, token);
    }

    public async Task<CreditLimitDetail> UpdateAsync(long id, int version, CreditLimitInput input,
        CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(EditPermission, token);
        Validate(input);
        var customerId = await FindCustomerAsync(id, token);
        if (input.CustomerId != customerId)
            throw new RequestValidationException("customerId", "The customer of a credit limit cannot be changed.");
        await customers.RequireApprovedAsync(customerId, token);
        var baseCurrency = await RequireBaseCurrencyAsync(token);
        return await InLockedTransactionAsync(companyId, customerId, async () =>
        {
            var limit = await LoadActiveAsync(id, version, token);
            if (limit.BaseCurrency != baseCurrency)
                throw new BusinessRuleException(CreditLimitErrors.CurrencyMismatch,
                    "The credit limit currency differs from the company base currency; deactivate it and create a new limit.");
            await EnsureNoOverlapAsync(input, id, token);
            limit.EffectiveFrom = input.EffectiveFrom;
            limit.EffectiveTo = input.EffectiveTo;
            limit.Amount = input.Amount;
            await db.SaveChangesAsync(token);
            return CreditLimitDetail.From(limit);
        }, token);
    }

    /// <summary>Retires a limit without deleting it; MD customer and currency state are not required.</summary>
    public async Task<CreditLimitDetail> DeactivateAsync(long id, int version, CancellationToken token = default)
    {
        var companyId = await AuthorizeAsync(DeactivatePermission, token);
        var customerId = await FindCustomerAsync(id, token);
        return await InLockedTransactionAsync(companyId, customerId, async () =>
        {
            var limit = await LoadActiveAsync(id, version, token);
            limit.Status = CreditLimitStatus.Deactivated;
            await db.SaveChangesAsync(token);
            return CreditLimitDetail.From(limit);
        }, token);
    }

    internal static void Validate(CreditLimitInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        if (input.CustomerId <= 0) Fail("customerId", "Customer is required.");
        if (input.EffectiveFrom == default) Fail("effectiveFrom", "Effective from date is required.");
        if (input.EffectiveTo == default) Fail("effectiveTo", "Effective to date is required.");
        else if (input.EffectiveFrom != default && input.EffectiveTo < input.EffectiveFrom)
            Fail("effectiveTo", "Effective to date must not precede effective from date.");
        if (input.Amount < 0m || input.Amount > MaxAmount || decimal.Round(input.Amount, 4) != input.Amount)
            Fail("amount", "Amount must be between 0 and 999,999,999,999,999.9999 with at most 4 decimals.");
        if (errors.Count > 0) throw new RequestValidationException(errors);
    }

    private Task<long> AuthorizeAsync(string permission, CancellationToken token) =>
        FinanceAccess.RequireAsync(currentCompany, authorization, permission, token);

    private async Task<string> RequireBaseCurrencyAsync(CancellationToken token) =>
        (await currencies.GetBaseCurrencyAsync(token))?.Code
        ?? throw new BusinessRuleException(CreditLimitErrors.BaseCurrencyMissing, "The company base currency is not set.");

    /// <summary>Unlocked lookup of the immutable customer, only to choose the lock; the row is re-read under lock.</summary>
    private async Task<long> FindCustomerAsync(long id, CancellationToken token) =>
        await db.Set<CreditLimit>().AsNoTracking().Where(x => x.Id == id).Select(x => (long?)x.CustomerId)
            .SingleOrDefaultAsync(token)
        ?? throw new NotFoundException(CreditLimitErrors.NotFound, "Credit limit", id);

    private async Task<CreditLimit> LoadActiveAsync(long id, int version, CancellationToken token)
    {
        var limit = await db.Set<CreditLimit>().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException(CreditLimitErrors.NotFound, "Credit limit", id);
        if (version < 1 || limit.Version != version) throw new ConflictException();
        if (limit.Status != CreditLimitStatus.Active)
            throw new BusinessRuleException(CreditLimitErrors.NotActive, "A deactivated credit limit cannot be changed.");
        return limit;
    }

    private async Task EnsureNoOverlapAsync(CreditLimitInput input, long? excludeId, CancellationToken token)
    {
        var overlaps = await db.Set<CreditLimit>().AsNoTracking().AnyAsync(x =>
            x.CustomerId == input.CustomerId && x.Status == CreditLimitStatus.Active
            && (excludeId == null || x.Id != excludeId)
            && x.EffectiveFrom <= input.EffectiveTo && x.EffectiveTo >= input.EffectiveFrom, token);
        if (overlaps)
            throw new ConflictException(CreditLimitErrors.Overlap,
                "Another active credit limit of this customer covers at least one of these dates.");
    }

    private async Task<T> InLockedTransactionAsync<T>(long companyId, long customerId, Func<Task<T>> action,
        CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            await credit.AcquireCreditLockAsync(companyId, customerId, transaction.GetDbTransaction(), token);
            var result = await action();
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (ex is DbUpdateConcurrencyException) throw new ConflictException();
            throw;
        }
    }
}
