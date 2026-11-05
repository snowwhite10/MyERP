namespace MyERP.Modules.Finance.Domain;

/// <summary>MD asset class as read by the caller through the MD public contract; <c>null</c> means not found or out of scope.</summary>
public sealed record FixedAssetClassSnapshot(long Id, bool Active);

/// <summary>Company base currency as read by the caller through the MD public contract (ISO code, GL scale 0–4).</summary>
public sealed record FixedAssetCurrencySnapshot(string? Code, int Decimals);

/// <summary>
/// Raw acquisition Draft fields from a create/edit request plus the caller's MD snapshots (REQ_F4116.14.1). This rule
/// queries nothing: code uniqueness (including soft-deleted history), company scope and version stay with 012a.
/// </summary>
public sealed record FixedAssetAcquisitionDraftInput(
    string? AssetCode,
    string? Description,
    long AssetClassId,
    FixedAssetClassSnapshot? AssetClass,
    DateOnly? AcquisitionDate,
    DateOnly? InServiceDate,
    decimal Cost,
    decimal Residual,
    int? UsefulLifeMonths,
    FixedAssetCurrencySnapshot? BaseCurrency,
    string? SourceReference);

public enum FixedAssetDraftError
{
    Required,
    TooLong,
    InvalidFormat,
    OutOfRange,
    InvalidScale,
    NotFound,
    Inactive,
    Mismatch,
    BeforeAcquisition,
    NotLessThanCost
}

/// <summary>One field error; <see cref="Field"/> is the camelCase request field name used by the API and form.</summary>
public sealed record FixedAssetDraftFieldError(string Field, FixedAssetDraftError Error);

/// <summary>Normalised Draft values ready to save: trimmed text, upper-case code, empty source reference as <c>null</c>.</summary>
public sealed record FixedAssetAcquisitionDraft(
    string AssetCode,
    string Description,
    long AssetClassId,
    DateOnly AcquisitionDate,
    DateOnly InServiceDate,
    decimal Cost,
    decimal Residual,
    int UsefulLifeMonths,
    string CurrencyCode,
    int CurrencyDecimals,
    string? SourceReference)
{
    public FixedAssetDepreciationInput ToDepreciationInput() =>
        new(Cost, Residual, UsefulLifeMonths, InServiceDate, CurrencyDecimals);
}

public sealed record FixedAssetAcquisitionDraftResult(
    FixedAssetAcquisitionDraft? Draft, IReadOnlyList<FixedAssetDraftFieldError> Errors)
{
    public bool IsValid => Draft is not null;
}

/// <summary>
/// Pure acquisition Draft validation (BR-FI-093/095, ADR-080). Every invalid field is reported once, in form order;
/// a Draft is returned only when there is no error. Amount scale is checked only against a valid base currency, and
/// residual is compared only with a valid cost, so one mistake never blames another field.
/// </summary>
public static class FixedAssetAcquisitionDraftRules
{
    public const int AssetCodeMaxLength = 30;
    public const int DescriptionMaxLength = 200;
    public const int SourceReferenceMaxLength = 100;
    private const decimal SqlAmountMaximum = 999999999999999.9999m;

    public static FixedAssetAcquisitionDraftResult Validate(FixedAssetAcquisitionDraftInput? input)
    {
        if (input is null)
            return new(null, [new("request", FixedAssetDraftError.Required)]);

        var errors = new List<FixedAssetDraftFieldError>();
        void Fail(string field, FixedAssetDraftError error) => errors.Add(new(field, error));

        var code = input.AssetCode?.Trim().ToUpperInvariant() ?? "";
        if (code.Length == 0) Fail("assetCode", FixedAssetDraftError.Required);
        else if (code.Length > AssetCodeMaxLength) Fail("assetCode", FixedAssetDraftError.TooLong);
        else if (code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not '_' and not '-'))
            Fail("assetCode", FixedAssetDraftError.InvalidFormat);

        var description = input.Description?.Trim() ?? "";
        if (description.Length == 0) Fail("description", FixedAssetDraftError.Required);
        else if (description.Length > DescriptionMaxLength) Fail("description", FixedAssetDraftError.TooLong);
        else if (description.Any(char.IsControl)) Fail("description", FixedAssetDraftError.InvalidFormat);

        if (input.AssetClassId <= 0) Fail("assetClassId", FixedAssetDraftError.Required);
        else if (input.AssetClass is null) Fail("assetClassId", FixedAssetDraftError.NotFound);
        else if (input.AssetClass.Id != input.AssetClassId) Fail("assetClassId", FixedAssetDraftError.Mismatch);
        else if (!input.AssetClass.Active) Fail("assetClassId", FixedAssetDraftError.Inactive);

        if (input.AcquisitionDate is null) Fail("acquisitionDate", FixedAssetDraftError.Required);
        if (input.InServiceDate is null) Fail("inServiceDate", FixedAssetDraftError.Required);
        else if (input.AcquisitionDate is { } acquired && input.InServiceDate < acquired)
            Fail("inServiceDate", FixedAssetDraftError.BeforeAcquisition);

        var currency = input.BaseCurrency;
        FixedAssetDraftError? currencyError =
            currency is null ? FixedAssetDraftError.Required
            : !IsCurrencyCode(currency.Code) ? FixedAssetDraftError.InvalidFormat
            : currency.Decimals is < 0 or > FixedAssetDepreciationRules.MaxCurrencyDecimals ? FixedAssetDraftError.OutOfRange
            : null;
        int? scale = currencyError is null ? currency!.Decimals : null;

        var costValid = false;
        if (input.Cost <= 0 || input.Cost > SqlAmountMaximum) Fail("cost", FixedAssetDraftError.OutOfRange);
        else if (scale is { } s1 && !HasScale(input.Cost, s1)) Fail("cost", FixedAssetDraftError.InvalidScale);
        else costValid = true;

        if (input.Residual < 0) Fail("residual", FixedAssetDraftError.OutOfRange);
        else if (costValid && input.Residual >= input.Cost) Fail("residual", FixedAssetDraftError.NotLessThanCost);
        else if (input.Residual > SqlAmountMaximum) Fail("residual", FixedAssetDraftError.OutOfRange);
        else if (scale is { } s2 && !HasScale(input.Residual, s2)) Fail("residual", FixedAssetDraftError.InvalidScale);

        if (input.UsefulLifeMonths is null) Fail("usefulLifeMonths", FixedAssetDraftError.Required);
        else if (input.UsefulLifeMonths is < 1 or > FixedAssetDepreciationRules.MaxUsefulLifeMonths)
            Fail("usefulLifeMonths", FixedAssetDraftError.OutOfRange);

        if (currencyError is { } ce) Fail("baseCurrency", ce);

        var source = input.SourceReference?.Trim();
        if (source?.Length > SourceReferenceMaxLength) Fail("sourceReference", FixedAssetDraftError.TooLong);
        else if (source is not null && source.Any(char.IsControl)) Fail("sourceReference", FixedAssetDraftError.InvalidFormat);

        if (errors.Count > 0)
            return new(null, errors);

        return new(new FixedAssetAcquisitionDraft(
            code, description, input.AssetClassId, input.AcquisitionDate!.Value, input.InServiceDate!.Value,
            input.Cost, input.Residual, input.UsefulLifeMonths!.Value, currency!.Code!, currency.Decimals,
            string.IsNullOrEmpty(source) ? null : source), errors);
    }

    private static bool IsCurrencyCode(string? code) =>
        code is { Length: 3 } && code.All(char.IsAsciiLetterUpper);

    private static bool HasScale(decimal value, int decimals) =>
        decimal.Round(value, decimals) == value;
}
