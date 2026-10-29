using System.Data.Common;

namespace MyERP.Modules.Finance.Contracts;

public enum CreditUnavailableReason
{
    MissingLimit,
    AmbiguousLimit,
    MissingBaseCurrency,
    CustomerUnavailable,
    ExposureUnavailable
}

/// <summary>FI-owned, company/customer base-currency result. A missing limit is never represented as zero.</summary>
public sealed record CreditSnapshot
{
    private CreditSnapshot(long companyId, long customerId, DateOnly evaluationDate)
    {
        CompanyId = companyId;
        CustomerId = customerId;
        EvaluationDate = evaluationDate;
    }

    public long CompanyId { get; }
    public long CustomerId { get; }
    public DateOnly EvaluationDate { get; }
    public bool IsAvailable => UnavailableReason is null;
    public string? BaseCurrency { get; private init; }
    public decimal? Limit { get; private init; }
    public decimal? PostedOpenAr { get; private init; }
    public int? LimitVersion { get; private init; }
    public DateTime? ObservedAt { get; private init; }
    public CreditUnavailableReason? UnavailableReason { get; private init; }

    public static CreditSnapshot Available(long companyId, long customerId, string currency, decimal limit,
        decimal postedOpenAr, DateOnly evaluationDate, int limitVersion, DateTime observedAt)
    {
        ValidateIdentity(companyId, customerId);
        if (currency is not { Length: 3 } || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("Base currency must be an ISO 4217 uppercase code.", nameof(currency));
        ValidateAmount(limit, nameof(limit));
        ValidateAmount(postedOpenAr, nameof(postedOpenAr));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitVersion);
        return new CreditSnapshot(companyId, customerId, evaluationDate)
        {
            BaseCurrency = currency,
            Limit = limit,
            PostedOpenAr = postedOpenAr,
            LimitVersion = limitVersion,
            ObservedAt = observedAt
        };
    }

    public static CreditSnapshot Unavailable(long companyId, long customerId, DateOnly evaluationDate,
        CreditUnavailableReason reason)
    {
        ValidateIdentity(companyId, customerId);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        return new CreditSnapshot(companyId, customerId, evaluationDate) { UnavailableReason = reason };
    }

    private static void ValidateIdentity(long companyId, long customerId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(companyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
    }

    private static void ValidateAmount(decimal amount, string name)
    {
        if (amount < 0m || amount > 999_999_999_999_999.9999m || decimal.Round(amount, 4) != amount)
            throw new ArgumentOutOfRangeException(name, "Amount must fit nonnegative decimal(19,4).");
    }
}

/// <summary>
/// FI credit owner. The caller supplies its open SQL transaction; the lock remains held until that transaction ends.
/// The caller must acquire the key before reading FI exposure or SD commitments. Implemented by WU-FI-001a.
/// </summary>
public interface ICustomerCreditService
{
    Task AcquireCreditLockAsync(long companyId, long customerId, DbTransaction transaction,
        CancellationToken cancellationToken = default);

    Task<CreditSnapshot> GetSnapshotAsync(long companyId, long customerId, DateOnly evaluationDate,
        DbTransaction transaction, CancellationToken cancellationToken = default);
}
