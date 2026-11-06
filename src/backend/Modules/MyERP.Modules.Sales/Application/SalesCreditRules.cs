using MyERP.Modules.Finance.Contracts;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>Stable SD credit error codes (BR-SD-031～034, ADR-104).</summary>
public static class SalesCreditErrors
{
    public const string Unavailable = "SAL.CREDIT.UNAVAILABLE";
    public const string LimitNotSet = "SAL.CREDIT.LIMIT_NOT_SET";
    public const string InvalidSnapshot = "SAL.CREDIT.INVALID_SNAPSHOT";
    public const string InvalidAmount = "SAL.CREDIT.INVALID_AMOUNT";
    public const string CustomerInactive = "SAL.CREDIT.CUSTOMER_INACTIVE";
    public const string LimitExceeded = "SAL.CREDIT.LIMIT_EXCEEDED";
    public const string OverrideForbidden = "SAL.CREDIT.OVERRIDE_FORBIDDEN";
}

/// <summary>Saved base-currency gross of another Approved order still uninvoiced into posted FI AR.</summary>
public sealed record SalesCreditCommitment(long OrderId, decimal BaseGross);

/// <summary>
/// Inputs the caller reads inside its transaction after acquiring the FI credit lock (WU-SD-006b).
/// <see cref="EvaluationDate"/> is <c>IClock.Today</c>, never the order business date; company comes from the
/// current-company context and <see cref="CompanyBaseCurrency"/> from MD. No client figure is accepted.
/// </summary>
public sealed record SalesCreditCheckInput(long CompanyId, long CustomerId, DateOnly EvaluationDate,
    bool CustomerActive, string? CompanyBaseCurrency, long CandidateOrderId, decimal CandidateBaseGross,
    IReadOnlyList<SalesCreditCommitment> Commitments);

public enum SalesCreditStatus { WithinLimit, OverLimit, Blocked }

/// <summary>
/// Credit components for preview, refusal and saved decision evidence. Figures are null when the check is
/// Blocked; Headroom = Limit − PostedOpenAr − OtherCommitments − Candidate and Excess = max(0, −Headroom).
/// </summary>
public sealed record SalesCreditAssessment(SalesCreditStatus Status, string? ErrorCode, DateOnly EvaluationDate,
    string? BaseCurrency, decimal? Limit, decimal? PostedOpenAr, decimal? OtherCommitments, decimal Candidate,
    decimal? Headroom, decimal? Excess, int? LimitVersion);

/// <summary>Outcome a submit/final-decision transaction saves; an override carries the trimmed reason.</summary>
public sealed record SalesCreditDecision(SalesCreditAssessment Assessment, bool IsOverride, string? OverrideReason);

/// <summary>
/// WU-SD-006b0 offline credit rules for REQ_F4112.6.1 v1.1. The caller acquires the FI credit lock, reads the FI
/// snapshot and SD commitments in the same transaction (WU-SD-006b); these functions never read the clock, the
/// database or the client's identity, and never substitute a missing figure with zero or unlimited credit.
/// </summary>
public static class SalesCreditRules
{
    public const int OverrideReasonMaxLength = 200;
    private const decimal MaxAmount = 999_999_999_999_999.9999m;

    /// <summary>
    /// Fail-closed order (ADR-104): inactive MD customer → missing FI snapshot → snapshot identity/date →
    /// FI unavailable reason → base currency → SD amounts → arithmetic. The candidate order is removed from
    /// the commitments by ID so it is counted exactly once.
    /// </summary>
    public static SalesCreditAssessment Evaluate(SalesCreditCheckInput input, CreditSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Commitments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.CompanyId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.CustomerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(input.CandidateOrderId);
        if (input.EvaluationDate == default)
            throw new ArgumentException("Evaluation date is required.", nameof(input));

        if (!input.CustomerActive) return Blocked(input, SalesCreditErrors.CustomerInactive);
        if (snapshot is null) return Blocked(input, SalesCreditErrors.Unavailable);
        if (snapshot.CompanyId != input.CompanyId || snapshot.CustomerId != input.CustomerId ||
            snapshot.EvaluationDate != input.EvaluationDate)
            return Blocked(input, SalesCreditErrors.InvalidSnapshot);
        if (snapshot.UnavailableReason is { } reason)
        {
            return Blocked(input, reason switch
            {
                CreditUnavailableReason.MissingLimit => SalesCreditErrors.LimitNotSet,
                CreditUnavailableReason.AmbiguousLimit => SalesCreditErrors.InvalidSnapshot,
                _ => SalesCreditErrors.Unavailable,
            });
        }

        if (!ValidCurrency(input.CompanyBaseCurrency)) return Blocked(input, SalesCreditErrors.Unavailable);
        if (snapshot.Limit is not { } limit || snapshot.PostedOpenAr is not { } openAr ||
            !string.Equals(snapshot.BaseCurrency, input.CompanyBaseCurrency, StringComparison.Ordinal))
            return Blocked(input, SalesCreditErrors.InvalidSnapshot);

        if (!ValidAmount(input.CandidateBaseGross)) return Blocked(input, SalesCreditErrors.InvalidAmount);
        var orderIds = new HashSet<long>();
        var others = 0m;
        foreach (var commitment in input.Commitments)
        {
            if (commitment is null || commitment.OrderId <= 0 || !orderIds.Add(commitment.OrderId) ||
                !ValidAmount(commitment.BaseGross))
                return Blocked(input, SalesCreditErrors.InvalidAmount);
            if (commitment.OrderId == input.CandidateOrderId) continue;
            others += commitment.BaseGross;
            if (others > MaxAmount) return Blocked(input, SalesCreditErrors.InvalidAmount);
        }

        var exposure = openAr + others + input.CandidateBaseGross;
        if (exposure > MaxAmount) return Blocked(input, SalesCreditErrors.InvalidAmount);

        var headroom = limit - exposure;
        var over = headroom < 0m;
        return new SalesCreditAssessment(over ? SalesCreditStatus.OverLimit : SalesCreditStatus.WithinLimit,
            over ? SalesCreditErrors.LimitExceeded : null, input.EvaluationDate, snapshot.BaseCurrency, limit,
            openAr, others, input.CandidateBaseGross, headroom, over ? -headroom : 0m, snapshot.LimitVersion);
    }

    /// <summary>
    /// Command decision (BR-SD-034). Order: Blocked 422 (never overridable) → within limit (any reason ignored,
    /// no override recorded) → over limit without reason 422 with excess → missing grant 403 → reason 1～200
    /// characters 400. The grant is <c>SAL.CREDIT.OVERRIDE</c>; the caller has already checked SUBMIT/SYS action.
    /// </summary>
    public static SalesCreditDecision Decide(SalesCreditAssessment assessment, string? overrideReason,
        bool hasOverrideGrant)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        switch (assessment.Status)
        {
            case SalesCreditStatus.Blocked:
                throw new BusinessRuleException(assessment.ErrorCode ?? SalesCreditErrors.Unavailable,
                    "Customer credit cannot be confirmed, so the order cannot proceed.");
            case SalesCreditStatus.WithinLimit:
                return new SalesCreditDecision(assessment, false, null);
            case SalesCreditStatus.OverLimit:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(assessment));
        }

        if (overrideReason is null)
        {
            throw new BusinessRuleException(SalesCreditErrors.LimitExceeded,
                "The order exceeds the customer's available credit.")
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["excess"] = assessment.Excess,
                    ["currency"] = assessment.BaseCurrency,
                },
            };
        }

        if (!hasOverrideGrant)
            throw new ForbiddenException(SalesCreditErrors.OverrideForbidden,
                "Overriding the credit limit needs credit override permission.");

        var reason = overrideReason.Trim();
        if (reason.Length == 0 || reason.Length > OverrideReasonMaxLength)
            throw new RequestValidationException("creditOverrideReason",
                $"An override reason of 1 to {OverrideReasonMaxLength} characters is required.");

        return new SalesCreditDecision(assessment, true, reason);
    }

    private static SalesCreditAssessment Blocked(SalesCreditCheckInput input, string code) =>
        new(SalesCreditStatus.Blocked, code, input.EvaluationDate, null, null, null, null,
            input.CandidateBaseGross, null, null, null);

    private static bool ValidCurrency(string? code) =>
        code is { Length: 3 } && code.All(ch => ch is >= 'A' and <= 'Z');

    private static bool ValidAmount(decimal value) =>
        value is >= 0m and <= MaxAmount && decimal.Round(value, 4) == value;
}
