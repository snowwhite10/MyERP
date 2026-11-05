namespace MyERP.Modules.Finance.Domain;

/// <summary>Tax detail role (BR-FI-104): SD invoice output, billed SD return credit, or matched PUR invoice input.</summary>
public enum TaxRole
{
    Output = 1,
    OutputCredit = 2,
    Input = 3
}

/// <summary>Saved approved purchase tax treatment (BR-FI-106); one treatment per saved line, mixed treatment is split by line.</summary>
public enum TaxTreatment
{
    Recoverable = 1,
    NonRecoverable = 2
}

/// <summary>
/// What the original SD invoice line still allows after prior committed credits, read by the caller in the posting
/// transaction (BR-FI-105). Magnitudes, never negative.
/// </summary>
public sealed record TaxCreditRemaining(decimal Net, decimal Tax, decimal BaseNet, decimal BaseTax);

/// <summary>
/// One saved SD/PUR source line's tax components as exposed by the source module's public contract, plus the caller's
/// FI mapping snapshot (REQ_F4116.17.1–2). Amounts are non-negative magnitudes; the role decides the sign. This rule
/// queries nothing: MD tax-code eligibility, account postability, permanent identity and fingerprints stay with 013a.
/// </summary>
public sealed record TaxDetailLineInput(
    TaxRole Role,
    long SourceLineId,
    string? TaxCode,
    int CurrencyDecimals,
    decimal Net,
    decimal Tax,
    decimal Gross,
    int BaseCurrencyDecimals,
    decimal BaseNet,
    decimal BaseTax,
    decimal BaseGross,
    TaxTreatment? Treatment,
    long? TaxAccountId,
    long? CostAccountId,
    TaxCreditRemaining? Remaining);

public enum TaxDetailError
{
    Required,
    OutOfRange,
    InvalidScale,
    NotBalanced,
    ZeroMismatch,
    NotApplicable,
    ExceedsRemaining
}

/// <summary>One field error; <see cref="Field"/> is the camelCase contract field name.</summary>
public sealed record TaxDetailFieldError(string Field, TaxDetailError Error);

/// <summary>
/// Validated tax detail values ready to save with the source and GL journal. Signed amounts are positive for output and
/// input and negative for output credit. <see cref="ClaimableBaseTax"/> is what a return may deduct; it is never
/// inferred later from a current rate. A zero-tax line has no GL leg and no account.
/// </summary>
public sealed record TaxDetailLine(
    TaxRole Role,
    long SourceLineId,
    string TaxCode,
    decimal SignedNet,
    decimal SignedTax,
    decimal SignedBaseNet,
    decimal SignedBaseTax,
    TaxTreatment? Treatment,
    decimal ClaimableBaseTax,
    decimal NonRecoverableBaseTax,
    long? GlAccountId)
{
    public bool RequiresGlLeg => GlAccountId is not null;
}

public sealed record TaxDetailLineResult(TaxDetailLine? Detail, IReadOnlyList<TaxDetailFieldError> Errors)
{
    public bool IsValid => Detail is not null;
}

/// <summary>
/// Pure tax detail line validation (BR-FI-103～106, ADR-081). Every invalid field is reported once, in contract
/// field order; a detail is returned only when there is no error. Balance, zero-tax agreement and credit limits are
/// checked only between components that are themselves valid, so one mistake never blames another field.
/// </summary>
public static class TaxDetailLineRules
{
    public const int MaxCurrencyDecimals = 4;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    private static readonly string[] FieldOrder =
    [
        "role", "sourceLineId", "taxCode", "currencyDecimals", "net", "tax", "gross",
        "baseCurrencyDecimals", "baseNet", "baseTax", "baseGross", "treatment", "taxAccountId", "costAccountId",
        "remaining"
    ];

    public static TaxDetailLineResult Validate(TaxDetailLineInput? input)
    {
        if (input is null)
            return new(null, [new("request", TaxDetailError.Required)]);

        var errors = new Dictionary<string, TaxDetailError>();
        void Fail(string field, TaxDetailError error) => errors.TryAdd(field, error);

        var roleValid = Enum.IsDefined(input.Role);
        if (!roleValid) Fail("role", TaxDetailError.OutOfRange);
        if (input.SourceLineId <= 0) Fail("sourceLineId", TaxDetailError.Required);
        var taxCode = input.TaxCode?.Trim().ToUpperInvariant() ?? "";
        if (taxCode.Length == 0) Fail("taxCode", TaxDetailError.Required);

        var original = CheckSide(input.CurrencyDecimals, input.Net, input.Tax, input.Gross,
            "currencyDecimals", "net", "tax", "gross", Fail);
        var baseSide = CheckSide(input.BaseCurrencyDecimals, input.BaseNet, input.BaseTax, input.BaseGross,
            "baseCurrencyDecimals", "baseNet", "baseTax", "baseGross", Fail);

        // A zero original tax with a non-zero base tax (or the reverse) would become an implicit rounding plug.
        if (original.TaxValid && baseSide.TaxValid && (input.Tax == 0) != (input.BaseTax == 0))
            Fail("baseTax", TaxDetailError.ZeroMismatch);

        var isInput = input.Role == TaxRole.Input;
        var isCredit = input.Role == TaxRole.OutputCredit;
        var treatmentValid = false;
        if (roleValid)
        {
            if (!isInput && input.Treatment is not null) Fail("treatment", TaxDetailError.NotApplicable);
            else if (isInput && input.Treatment is null) Fail("treatment", TaxDetailError.Required);
            else if (isInput && !Enum.IsDefined(input.Treatment!.Value)) Fail("treatment", TaxDetailError.OutOfRange);
            else treatmentValid = true;
        }

        var nonRecoverable = isInput && input.Treatment == TaxTreatment.NonRecoverable;
        var hasTax = input.Tax != 0 || input.BaseTax != 0;
        if (treatmentValid)
        {
            if (!nonRecoverable && input.CostAccountId is not null) Fail("costAccountId", TaxDetailError.NotApplicable);
            else if (hasTax && nonRecoverable && input.CostAccountId is not > 0) Fail("costAccountId", TaxDetailError.Required);
            if (hasTax && !nonRecoverable && input.TaxAccountId is not > 0) Fail("taxAccountId", TaxDetailError.Required);
        }

        if (roleValid)
        {
            if (!isCredit && input.Remaining is not null) Fail("remaining", TaxDetailError.NotApplicable);
            else if (isCredit) CheckRemaining(input, original, baseSide, Fail);
        }

        if (errors.Count > 0)
            return new(null, FieldOrder.Where(errors.ContainsKey).Select(f => new TaxDetailFieldError(f, errors[f])).ToList());

        var sign = isCredit ? -1m : 1m;
        long? account = !hasTax ? null : nonRecoverable ? input.CostAccountId : input.TaxAccountId;
        return new(new TaxDetailLine(
            input.Role, input.SourceLineId, taxCode,
            sign * input.Net, sign * input.Tax, sign * input.BaseNet, sign * input.BaseTax,
            input.Treatment,
            isInput && !nonRecoverable ? input.BaseTax : 0m,
            nonRecoverable ? input.BaseTax : 0m,
            account), []);
    }

    private sealed record SideValidity(bool NetValid, bool TaxValid);

    private static SideValidity CheckSide(int decimals, decimal net, decimal tax, decimal gross,
        string decimalsField, string netField, string taxField, string grossField, Action<string, TaxDetailError> fail)
    {
        int? scale = decimals is >= 0 and <= MaxCurrencyDecimals ? decimals : null;
        if (scale is null) fail(decimalsField, TaxDetailError.OutOfRange);

        bool Amount(string field, decimal value, bool positive)
        {
            if (value < 0 || (positive && value == 0) || value > SqlAmountMaximum) fail(field, TaxDetailError.OutOfRange);
            else if (scale is { } s && decimal.Round(value, s) != value) fail(field, TaxDetailError.InvalidScale);
            else return true;
            return false;
        }

        var netValid = Amount(netField, net, positive: false);
        var taxValid = Amount(taxField, tax, positive: false);
        var grossValid = Amount(grossField, gross, positive: true);
        if (netValid && taxValid && grossValid && net + tax != gross)
            fail(grossField, TaxDetailError.NotBalanced);
        return new(netValid, taxValid);
    }

    private static void CheckRemaining(TaxDetailLineInput input, SideValidity original, SideValidity baseSide,
        Action<string, TaxDetailError> fail)
    {
        if (input.Remaining is not { } rem)
        {
            fail("remaining", TaxDetailError.Required);
            return;
        }

        if (rem.Net < 0 || rem.Tax < 0 || rem.BaseNet < 0 || rem.BaseTax < 0)
        {
            fail("remaining", TaxDetailError.OutOfRange);
            return;
        }

        if (original.NetValid && input.Net > rem.Net) fail("net", TaxDetailError.ExceedsRemaining);
        if (original.TaxValid && input.Tax > rem.Tax) fail("tax", TaxDetailError.ExceedsRemaining);
        if (baseSide.NetValid && input.BaseNet > rem.BaseNet) fail("baseNet", TaxDetailError.ExceedsRemaining);
        if (baseSide.TaxValid && input.BaseTax > rem.BaseTax) fail("baseTax", TaxDetailError.ExceedsRemaining);
    }
}
