using System.Text.Json.Serialization;

namespace MyERP.Modules.Sales.Domain;

/// <summary>
/// How a quotation/manual-order line price was decided (BR-SD-029, ADR-160). <see cref="Legacy"/> marks lines saved
/// before WU-SD-005a11a and is never produced by a new save. Serialized as its name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesLinePricingMode>))]
public enum SalesLinePricingMode { Legacy, Rule, Override }
