using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

/// <summary>REQ_F4114.6.3：明確選取同一供應商的決標行；來源驗證由交易服務處理。</summary>
public sealed record RfqConversionInput(int Version, long SupplierId, long[]? AwardIds)
{
    public void Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (Version < 1)
            errors.Add("version", ["Current RFQ version is required."]);
        if (SupplierId <= 0)
            errors.Add("supplierId", ["Supplier is required."]);
        if (AwardIds is null || AwardIds.Length is < 1 or > 1000 ||
            AwardIds.Any(id => id <= 0) || AwardIds.Distinct().Count() != AwardIds.Length)
            errors.Add("awardIds", ["Select 1 to 1000 different positive award IDs."]);
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
    }
}
