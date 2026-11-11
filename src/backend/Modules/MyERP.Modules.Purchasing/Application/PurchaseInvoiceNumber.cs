using System.Globalization;
using System.Text;
using MyERP.Shared.Errors;

namespace MyERP.Modules.Purchasing.Application;

public sealed record PurchaseInvoiceNumberValue(string Display, string Key);

/// <summary>BR-PUR-036a: deterministic supplier invoice number key for the future unique index.</summary>
public static class PurchaseInvoiceNumber
{
    public static PurchaseInvoiceNumberValue Normalize(string? number)
    {
        if (string.IsNullOrWhiteSpace(number) || HasInvisibleCharacters(number) ||
            !IsWellFormedUtf16(number))
            throw InvalidNumber();

        var display = number.Trim();
        var key = display.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
        if (display.Length > 80 || key.Length is < 1 or > 80 || HasInvisibleCharacters(key))
            throw InvalidNumber();
        return new(display, key);
    }

    private static bool HasInvisibleCharacters(string value) => value.Any(character =>
        char.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format);

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index])) return false;
            if (!char.IsHighSurrogate(value[index])) continue;
            if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false;
        }
        return true;
    }

    private static BusinessRuleException InvalidNumber() =>
        new("PUR.INVOICE.INVALID_NUMBER", "Supplier invoice number is empty or invalid.");
}
