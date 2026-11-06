using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyERP.ArchitectureTests.Infrastructure;
using MyERP.Modules.Finance.Application;

namespace MyERP.ArchitectureTests;

/// <summary>
/// WU-FI-004a3v：每個 <see cref="GlPostingErrors"/> 代碼在三種語言的 errors.json 都有非空翻譯，
/// 且前端 <c>gl-posting-errors.ts</c> 白名單與後端常數完全一致（不多不少），避免畫面只顯示「未知錯誤」。
/// </summary>
public sealed partial class GlPostingErrorTranslationTests
{
    private const string Prefix = "FI.GL_POSTING.";
    private static readonly string[] Languages = ["en", "zh-Hans", "zh-Hant"];

    private static List<string> BackendCodes() =>
        typeof(GlPostingErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, string> GlPostingTranslations(string language)
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "public", "i18n", language, "errors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("FI", out var fi) && fi.TryGetProperty("GL_POSTING", out var gl))
        {
            foreach (var property in gl.EnumerateObject())
            {
                result[Prefix + property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : string.Empty;
            }
        }
        return result;
    }

    [Fact]
    public void BackendCodes_AllUseGlPostingPrefix()
    {
        var codes = BackendCodes();
        codes.Should().HaveCountGreaterThan(20);
        codes.Should().OnlyContain(c => c.StartsWith(Prefix, StringComparison.Ordinal));
        codes.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void EveryBackendCode_HasNonEmptyTranslation_AndNoExtraKeys(string language)
    {
        var translations = GlPostingTranslations(language);

        translations.Keys.Order(StringComparer.Ordinal).Should().Equal(BackendCodes(), $"errors.json ({language}) FI.GL_POSTING must match GlPostingErrors exactly");
        translations.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Value) && !t.Value.Contains("FI.GL_POSTING", StringComparison.Ordinal),
            "messages are user-facing text, not raw codes");
    }

    [Fact]
    public void ChineseTranslations_AreNotEnglishCopies()
    {
        var en = GlPostingTranslations("en");
        foreach (var language in Languages.Where(l => l != "en"))
        {
            var zh = GlPostingTranslations(language);
            zh.Should().OnlyContain(t => t.Value != en.GetValueOrDefault(t.Key), $"{language} must be translated");
        }
    }

    [Fact]
    public void FrontendWhitelist_MatchesBackendCodes()
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "frontend", "src", "app", "modules", "fi", "gl-posting-errors.ts");
        File.Exists(path).Should().BeTrue("the frontend must whitelist GL posting codes for apiErrorKey");

        var frontend = FrontendCodeRegex().Matches(File.ReadAllText(path)).Select(m => m.Groups["code"].Value)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        frontend.Should().Equal(BackendCodes());
    }

    [GeneratedRegex(@"'(?<code>FI\.GL_POSTING\.[A-Z_]+)'")]
    private static partial Regex FrontendCodeRegex();
}
