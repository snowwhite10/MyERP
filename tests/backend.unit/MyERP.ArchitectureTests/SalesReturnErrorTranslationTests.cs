using System.Reflection;
using System.Text.Json;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Sales.Application;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-SD-008a6 (SD-RETURN-U01, ADR-192): every SAL.RETURN.* error code raised by return create, edit, delete, submit,
/// posting, the SYS decision handler and the reads has a non-empty, non-English-copy translation under SAL.RETURN in
/// all three errors.json files, with no extra keys, so a refusal never shows only "unknown error".
/// </summary>
public sealed class SalesReturnErrorTranslationTests
{
    private const string Prefix = "SAL.RETURN.";

    private static List<string> BackendCodes() =>
        new[]
            {
                typeof(SalesReturnCreateService), typeof(SalesReturnDraftCommandService),
                typeof(SalesReturnSubmissionService), typeof(SalesReturnPosting), typeof(SalesReturnAmounts),
                typeof(SalesReturnApprovalResultHandler), typeof(SalesReturnQueryService),
                typeof(SalesReturnDraftBuilder), typeof(SalesReturnDraftInput)
            }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                && !f.Name.EndsWith("Permission", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c.StartsWith(Prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> Translations(string language)
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "public", "i18n", language, "errors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("SAL", out var sal) && sal.TryGetProperty("RETURN", out var item))
        {
            foreach (var property in item.EnumerateObject())
            {
                result[Prefix + property.Name] =
                    property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : string.Empty;
            }
        }
        return result;
    }

    [Fact]
    public void BackendCodes_AreTheTwentyTwoReturnCodes()
    {
        var codes = BackendCodes();
        codes.Should().HaveCount(22);
        codes.Should().Contain(["SAL.RETURN.NOT_FOUND", "SAL.RETURN.SNAPSHOT_STALE", "SAL.RETURN.FI_POSTING_FAILED",
            "SAL.RETURN.APPROVAL_STATE_CHANGED", "SAL.RETURN.REQUEST_KEY_IMMUTABLE"]);
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
