using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Finance.Application;
using MyERP.Modules.Finance.Contracts;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-FI-002a4c1 (FI-SALES-MAP-UI-I01～I03), WU-FI-003a2d (FI-PUR-MAP-UI-I01～I03 for <see cref="PurchaseInvoiceAccountMapErrors"/>
/// and <c>purchase-map.service.ts</c>), WU-FI-005a2f (FI-COLL-MAP-UI-I01～I03 for <see cref="SalesCollectionAccountMapErrors"/>
/// and <c>collection-map.service.ts</c>): every <see cref="SalesPostingErrors"/> and <see cref="SalesInvoiceAccountMapErrors"/>
/// code has a non-empty translation in all three errors.json files with no extra keys, and the frontend whitelists
/// (<c>sales-posting-errors.ts</c>, <c>sales-map.service.ts</c>) equal the backend constants, so screens never fall back to
/// "unknown error" for a known refusal.
/// </summary>
public sealed partial class SalesPostingErrorTranslationTests
{
    private static readonly string[] Languages = ["en", "zh-Hans", "zh-Hant"];

    public static TheoryData<string, string> Groups() => new()
    {
        { "SALES_POSTING", "en" }, { "SALES_POSTING", "zh-Hans" }, { "SALES_POSTING", "zh-Hant" },
        { "SALES_MAP", "en" }, { "SALES_MAP", "zh-Hans" }, { "SALES_MAP", "zh-Hant" },
        { "PURCHASE_MAP", "en" }, { "PURCHASE_MAP", "zh-Hans" }, { "PURCHASE_MAP", "zh-Hant" },
        { "COLLECTION_MAP", "en" }, { "COLLECTION_MAP", "zh-Hans" }, { "COLLECTION_MAP", "zh-Hant" },
    };

    private static List<string> BackendCodes(string group) =>
        (group switch
        {
            "SALES_POSTING" => typeof(SalesPostingErrors),
            "SALES_MAP" => typeof(SalesInvoiceAccountMapErrors),
            "COLLECTION_MAP" => typeof(SalesCollectionAccountMapErrors),
            _ => typeof(PurchaseInvoiceAccountMapErrors),
        })
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> Translations(string group, string language)
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "public", "i18n", language, "errors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("FI", out var fi) && fi.TryGetProperty(group, out var codes))
        {
            foreach (var property in codes.EnumerateObject())
            {
                result[$"FI.{group}.{property.Name}"] =
                    property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : string.Empty;
            }
        }
        return result;
    }

    [Fact]
    public void BackendCodes_UseTheirPrefix()
    {
        BackendCodes("SALES_POSTING").Should().HaveCount(11).And.OnlyContain(c => c.StartsWith("FI.SALES_POSTING.", StringComparison.Ordinal));
        BackendCodes("SALES_MAP").Should().HaveCount(2).And.OnlyContain(c => c.StartsWith("FI.SALES_MAP.", StringComparison.Ordinal));
        BackendCodes("PURCHASE_MAP").Should().HaveCount(2).And.OnlyContain(c => c.StartsWith("FI.PURCHASE_MAP.", StringComparison.Ordinal));
        BackendCodes("COLLECTION_MAP").Should().HaveCount(4).And.OnlyContain(c => c.StartsWith("FI.COLLECTION_MAP.", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Groups))]
    public void EveryBackendCode_HasNonEmptyTranslation_AndNoExtraKeys(string group, string language)
    {
        var translations = Translations(group, language);

        translations.Keys.Order(StringComparer.Ordinal).Should().Equal(BackendCodes(group),
            $"errors.json ({language}) FI.{group} must match the backend constants exactly");
        translations.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Value) && !t.Value.Contains("FI.", StringComparison.Ordinal),
            "messages are user-facing text, not raw codes");
    }

    [Theory]
    [InlineData("SALES_POSTING")]
    [InlineData("SALES_MAP")]
    [InlineData("PURCHASE_MAP")]
    [InlineData("COLLECTION_MAP")]
    public void ChineseTranslations_AreNotEnglishCopies(string group)
    {
        var en = Translations(group, "en");
        foreach (var language in Languages.Where(l => l != "en"))
        {
            Translations(group, language).Should().OnlyContain(t => t.Value != en.GetValueOrDefault(t.Key), $"{language} must be translated");
        }
    }

    [Theory]
    [InlineData("SALES_POSTING", "sales-posting-errors.ts")]
    [InlineData("SALES_MAP", "sales-map.service.ts")]
    [InlineData("PURCHASE_MAP", "purchase-map.service.ts")]
    [InlineData("COLLECTION_MAP", "collection-map.service.ts")]
    public void FrontendWhitelist_MatchesBackendCodes(string group, string file)
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "src", "app", "modules", "fi", file);
        File.Exists(path).Should().BeTrue("the frontend must whitelist the codes for apiErrorKey");

        var frontend = FrontendCodeRegex().Matches(File.ReadAllText(path)).Select(m => m.Groups["code"].Value)
            .Where(c => c.StartsWith($"FI.{group}.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        frontend.Should().Equal(BackendCodes(group));
    }

    [GeneratedRegex(@"'(?<code>FI\.(?:SALES_POSTING|SALES_MAP|PURCHASE_MAP|COLLECTION_MAP)\.[A-Z_]+)'")]
    private static partial Regex FrontendCodeRegex();
}
