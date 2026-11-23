using System.Reflection;
using System.Text.Json;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Sales.Application;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-SD-007a3b (SD-INVOICE-U11, ADR-179): every SAL.INVOICE.* error code raised by invoice ISSUE and the invoice
/// reads has a non-empty, non-English-copy translation under SAL.INVOICE in all three errors.json files, with no
/// extra keys, so an issue refusal never shows only "unknown error".
/// </summary>
public sealed class SalesInvoiceErrorTranslationTests
{
    private const string Prefix = "SAL.INVOICE.";

    private static List<string> BackendCodes() =>
        new[] { typeof(SalesInvoiceIssuing), typeof(SalesInvoiceAllocation), typeof(SalesInvoiceIssueService),
                typeof(SalesInvoiceQueryService) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                && !f.Name.EndsWith("Permission", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c.StartsWith(Prefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> Translations(string language)
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "public", "i18n", language, "errors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("SAL", out var sal) && sal.TryGetProperty("INVOICE", out var invoice))
        {
            foreach (var property in invoice.EnumerateObject())
            {
                result[Prefix + property.Name] =
                    property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : string.Empty;
            }
        }
        return result;
    }

    [Fact]
    public void BackendCodes_AreTheElevenInvoiceCodesAndUnique()
    {
        var codes = BackendCodes();
        codes.Should().HaveCount(11);
        codes.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void EveryBackendCode_HasTranslation_AndNoExtraKeys(string language)
    {
        var translations = Translations(language);
        translations.Keys.Order(StringComparer.Ordinal).Should().Equal(BackendCodes());
        translations.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Value) &&
            !t.Value.Contains(Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void ChineseTranslations_AreNotEnglishCopies()
    {
        var en = Translations("en");
        foreach (var language in new[] { "zh-Hans", "zh-Hant" })
            Translations(language).Should().OnlyContain(t => t.Value != en.GetValueOrDefault(t.Key));
    }
}
