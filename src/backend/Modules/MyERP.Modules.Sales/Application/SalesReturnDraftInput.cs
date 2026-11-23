using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MyERP.Modules.Sales.Domain;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>Billed portion of a requested return line: quantity credited against one Posted invoice line.</summary>
public sealed record SalesReturnBilledInput(long InvoiceLineId, decimal Quantity);

/// <summary>
/// One requested return line (BR-SD-044): a Posted delivery line, the destination warehouse/location of the physical
/// receipt and an explicit partition of the returned quantity into an unbilled portion and billed portions. The line
/// quantity is derived as <c>UnbilledQuantity + Σ Billed.Quantity</c>; it is never sent by the client.
/// </summary>
public sealed record SalesReturnLineInput(int LineNo, long DeliveryLineId, long WarehouseId, long? LocationId,
    decimal UnbilledQuantity, IReadOnlyList<SalesReturnBilledInput>? Billed)
{
    public decimal Quantity => UnbilledQuantity + (Billed ?? []).Where(x => x is not null).Sum(x => x.Quantity);
}

/// <summary>
/// WU-SD-008a2a (ADR-185, BR-SD-043/044/048/049): client request to create a Draft sales return. It carries no
/// customer, order, currency, amount, cost, status or FI/INV reference — the server derives them from the saved
/// sources. <see cref="RequestKey"/> is the client idempotency key; <see cref="Fingerprint"/> hashes the canonical
/// payload compared on replay.
/// </summary>
public sealed partial record SalesReturnDraftInput(string? RequestKey, DateOnly BusinessDate, string? Remark,
    IReadOnlyList<SalesReturnLineInput>? Lines)
{
    public const int MaxLines = 1000;
    public const int MaxBilledPerLine = 100;
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public void Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);
        static bool ValidQuantity(decimal value, bool allowZero) =>
            (allowZero ? value >= 0m : value > 0m) && value < QuantityLimit && decimal.Round(value, 6) == value;

        if (RequestKey is null || !RequestKeyPattern().IsMatch(RequestKey))
            Fail("requestKey", "Request key must be 1 to 64 letters, digits, hyphens or underscores.");
        if (BusinessDate == default)
            Fail("businessDate", "Return date is required.");
        if (Remark is { Length: > SalesReturn.RemarkMaxLength })
            Fail("remark", $"Remark must be at most {SalesReturn.RemarkMaxLength} characters.");
        if (Lines is not { Count: > 0 and <= MaxLines })
        {
            Fail("lines", $"A return needs 1 to {MaxLines} lines.");
        }
        else
        {
            var lineNos = new HashSet<int>();
            var sources = new HashSet<long>();
            for (var i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                var path = $"lines[{i}]";
                if (line is null)
                {
                    Fail(path, "Line is required.");
                    continue;
                }
                if (line.LineNo <= 0)
                    Fail(path + ".lineNo", "Line number must be positive.");
                else if (!lineNos.Add(line.LineNo))
                    Fail(path + ".lineNo", "Line numbers must be unique.");
                if (line.DeliveryLineId <= 0)
                    Fail(path + ".deliveryLineId", "A delivery line is required.");
                else if (!sources.Add(line.DeliveryLineId))
                    Fail(path + ".deliveryLineId", "Each delivery line may appear only once per return.");
                if (line.WarehouseId <= 0)
                    Fail(path + ".warehouseId", "A destination warehouse is required.");
                if (line.LocationId is <= 0)
                    Fail(path + ".locationId", "Location must be empty or a valid location.");
                if (!ValidQuantity(line.UnbilledQuantity, allowZero: true))
                    Fail(path + ".unbilledQuantity",
                        "Unbilled quantity must be zero or positive, below 10^13 and have at most 6 decimals.");
                var billed = line.Billed ?? [];
                if (billed.Count > MaxBilledPerLine)
                    Fail(path + ".billed", $"A return line may credit at most {MaxBilledPerLine} invoice lines.");
                var invoiceLines = new HashSet<long>();
                for (var j = 0; j < billed.Count; j++)
                {
                    var portion = billed[j];
                    var portionPath = $"{path}.billed[{j}]";
                    if (portion is null)
                    {
                        Fail(portionPath, "Billed portion is required.");
                        continue;
                    }
                    if (portion.InvoiceLineId <= 0)
                        Fail(portionPath + ".invoiceLineId", "An invoice line is required.");
                    else if (!invoiceLines.Add(portion.InvoiceLineId))
                        Fail(portionPath + ".invoiceLineId", "Each invoice line may appear only once per return line.");
                    if (!ValidQuantity(portion.Quantity, allowZero: false))
                        Fail(portionPath + ".quantity",
                            "Billed quantity must be positive, below 10^13 and have at most 6 decimals.");
                }
                if (!errors.Keys.Any(k => k.StartsWith(path, StringComparison.Ordinal)) &&
                    (line.Quantity <= 0m || line.Quantity >= QuantityLimit))
                    Fail(path + ".quantity", "The returned quantity (unbilled plus billed) must be positive and below 10^13.");
            }
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
    }

    /// <summary>Trimmed remark; blank is stored as null.</summary>
    public string? NormalizedRemark => string.IsNullOrWhiteSpace(Remark) ? null : Remark.Trim();

    /// <summary>
    /// Lower-case hex SHA-256 of the canonical payload: date, trimmed remark and lines ordered by line number, each
    /// with source, destination, six-decimal unbilled quantity and billed portions ordered by invoice line. The request
    /// key itself is the lookup and is not part of the payload.
    /// </summary>
    public string Fingerprint()
    {
        static string Q(decimal value) => decimal.Round(value, 6).ToString("0.000000", CultureInfo.InvariantCulture);
        static string I(long value) => value.ToString(CultureInfo.InvariantCulture);
        var text = new StringBuilder("SD-RETURN-DRAFT-v1|")
            .Append(BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append('|').Append(NormalizedRemark?.Length.ToString(CultureInfo.InvariantCulture) ?? "-")
            .Append(':').Append(NormalizedRemark);
        foreach (var line in (Lines ?? []).Where(x => x is not null).OrderBy(x => x.LineNo))
        {
            text.Append('|').Append(line.LineNo.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(I(line.DeliveryLineId))
                .Append(':').Append(I(line.WarehouseId))
                .Append(':').Append(line.LocationId is { } location ? I(location) : "-")
                .Append(':').Append(Q(line.UnbilledQuantity));
            foreach (var portion in (line.Billed ?? []).Where(x => x is not null).OrderBy(x => x.InvoiceLineId))
                text.Append(':').Append(I(portion.InvoiceLineId)).Append('=').Append(Q(portion.Quantity));
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RequestKeyPattern();
}
