using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Sales.Application;

/// <summary>One requested invoice line: a Posted delivery line and the base-unit quantity to bill.</summary>
public sealed record SalesInvoiceLineInput(int LineNo, long DeliveryLineId, decimal Quantity);

/// <summary>
/// WU-SD-007a2 (BR-SD-037/038/040, ADR-177): client ISSUE request. It carries no amount, tax, customer, currency,
/// state or FI reference — the server derives all of them from the saved sources. <see cref="RequestKey"/> is an
/// opaque client idempotency key; <see cref="Fingerprint"/> is the canonical payload hash compared on replay.
/// </summary>
public sealed partial record SalesInvoiceIssueInput(string? RequestKey, DateOnly BusinessDate,
    IReadOnlyList<SalesInvoiceLineInput>? Lines)
{
    public const int MaxLines = 1000;
    private const decimal QuantityLimit = 10_000_000_000_000m;

    public void Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Fail(string field, string message) => errors.TryAdd(field, [message]);

        if (RequestKey is null || !RequestKeyPattern().IsMatch(RequestKey))
            Fail("requestKey", "Request key must be 1 to 64 letters, digits, hyphens or underscores.");
        if (BusinessDate == default)
            Fail("businessDate", "Invoice date is required.");
        if (Lines is not { Count: > 0 and <= MaxLines })
        {
            Fail("lines", $"An invoice needs 1 to {MaxLines} lines.");
        }
        else
        {
            var lineNos = new HashSet<int>();
            var sources = new HashSet<long>();
            for (var i = 0; i < Lines.Count; i++)
            {
                var line = Lines[i];
                if (line is null)
                {
                    Fail($"lines[{i}]", "Line is required.");
                    continue;
                }
                if (line.LineNo <= 0)
                    Fail($"lines[{i}].lineNo", "Line number must be positive.");
                else if (!lineNos.Add(line.LineNo))
                    Fail($"lines[{i}].lineNo", "Line numbers must be unique.");
                if (line.DeliveryLineId <= 0)
                    Fail($"lines[{i}].deliveryLineId", "A delivery line is required.");
                else if (!sources.Add(line.DeliveryLineId))
                    Fail($"lines[{i}].deliveryLineId", "Each delivery line may appear only once per invoice.");
                if (line.Quantity <= 0m || line.Quantity >= QuantityLimit || decimal.Round(line.Quantity, 6) != line.Quantity)
                    Fail($"lines[{i}].quantity", "Quantity must be positive, below 10^13 and have at most 6 decimals.");
            }
        }
        if (errors.Count > 0)
            throw new RequestValidationException(errors);
    }

    /// <summary>
    /// Lower-case hex SHA-256 of the canonical payload: invoice date and lines ordered by line number with quantities
    /// normalised to six decimals. The request key itself is the lookup and is not part of the payload.
    /// </summary>
    public string Fingerprint()
    {
        var text = new StringBuilder("SD-INVOICE-ISSUE-v1|")
            .Append(BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var line in (Lines ?? []).Where(x => x is not null).OrderBy(x => x.LineNo))
            text.Append('|').Append(line.LineNo.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(line.DeliveryLineId.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(decimal.Round(line.Quantity, 6).ToString("0.000000", CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RequestKeyPattern();
}
