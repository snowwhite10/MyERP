using System.Reflection;
using System.Text.Json;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Sales.Application;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-SD-006b0 (SD-CREDIT-U15)：每個 <see cref="SalesCreditErrors"/> 代碼在三種語言 errors.json 的 SAL.CREDIT
/// 都有非空、非英文複本的翻譯，且不多不少，避免信用拒絕只顯示「未知錯誤」。
/// </summary>
public sealed class SalesCreditErrorTranslationTests
{
    private const string Prefix = "SAL.CREDIT.";

    private static List<string> BackendCodes() =>
        typeof(SalesCreditErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> Translations(string language)
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "public", "i18n", language, "errors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("SAL", out var sal) && sal.TryGetProperty("CREDIT", out var credit))
        {
            foreach (var property in credit.EnumerateObject())
            {
                result[Prefix + property.Name] =
                    property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : string.Empty;
            }
        }
        return result;
    }

    [Fact]
    public void BackendCodes_UseCreditPrefixAndAreUnique()
    {
        var codes = BackendCodes();
        codes.Should().HaveCount(7);
        codes.Should().OnlyContain(c => c.StartsWith(Prefix, StringComparison.Ordinal));
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
