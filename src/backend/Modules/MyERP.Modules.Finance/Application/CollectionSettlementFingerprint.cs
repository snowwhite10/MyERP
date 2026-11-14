using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Versioned canonical hash of the saved settlement document fields the write-off posting uses (WU-FI-005b3b, ADR-128):
/// company, document ID, customer, business date, currency, receipt ID, total (4 decimals), line count and each line's
/// receivable ID and amount in line number order. Status, number, remark and audit fields are excluded, so the approval
/// transition does not change it. Length-prefixed fields keep values unambiguous. Result:
/// <c>FI-COLLECTION-SETTLEMENT-v1:</c> + 64 lowercase hex characters.
/// </summary>
public static class CollectionSettlementFingerprint
{
    public const string Prefix = "FI-COLLECTION-SETTLEMENT-v1:";

    public static string Create(CollectionSettlementDocument document,
        IReadOnlyList<CollectionSettlementDocumentLine> lines)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(lines);
        var canonical = new StringBuilder(Prefix);
        Add(canonical, document.CompanyId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.Id.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.CustomerId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(canonical, document.Currency);
        Add(canonical, document.ReceiptId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.TotalAmount.ToString("0.0000", CultureInfo.InvariantCulture));
        Add(canonical, lines.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var line in lines.OrderBy(x => x.LineNo))
        {
            Add(canonical, line.ReceivableId.ToString(CultureInfo.InvariantCulture));
            Add(canonical, line.Amount.ToString("0.0000", CultureInfo.InvariantCulture));
        }
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Add(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
}
