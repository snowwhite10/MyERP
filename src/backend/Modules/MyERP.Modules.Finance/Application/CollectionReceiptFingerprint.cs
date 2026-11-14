using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MyERP.Modules.Finance.Domain;

namespace MyERP.Modules.Finance.Application;

/// <summary>
/// Versioned canonical hash of the saved receipt document fields that the posting writes (WU-FI-005b3a, ADR-128):
/// company, document ID, customer, business date, currency, amount (4 decimals), fund account and bank reference.
/// Status, number, remark and audit fields are excluded, so the approval transition does not change it. Length-prefixed
/// fields keep text and numbers unambiguous. Result: <c>FI-COLLECTION-RECEIPT-v1:</c> + 64 lowercase hex characters.
/// </summary>
public static class CollectionReceiptFingerprint
{
    public const string Prefix = "FI-COLLECTION-RECEIPT-v1:";

    public static string Create(CollectionReceiptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var canonical = new StringBuilder(Prefix);
        Add(canonical, document.CompanyId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.Id.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.CustomerId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(canonical, document.Currency);
        Add(canonical, document.Amount.ToString("0.0000", CultureInfo.InvariantCulture));
        Add(canonical, document.BankAccountId.ToString(CultureInfo.InvariantCulture));
        Add(canonical, document.BankReference);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void Add(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
}
