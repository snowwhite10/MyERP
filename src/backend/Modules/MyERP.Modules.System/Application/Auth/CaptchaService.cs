using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyERP.Modules.System.Application.Auth;

/// <summary>Captcha 設定（<c>Captcha</c> 區段）。萬用碼只可在 Development 設定（MASTER_PROMPT 第 7 節）。</summary>
public sealed class CaptchaOptions
{
    public const string SectionName = "Captcha";

    public int Length { get; set; } = 5;
    public int ExpirySeconds { get; set; } = 120;
    public string? MasterCode { get; set; }
}

/// <summary>啟動檢查：非 Development 設定了萬用碼即寫 Error 日誌並拒絕啟動。</summary>
public sealed class CaptchaOptionsValidator(IHostEnvironment environment, ILogger<CaptchaOptionsValidator> logger) : IValidateOptions<CaptchaOptions>
{
    public ValidateOptionsResult Validate(string? name, CaptchaOptions options)
    {
        var failures = new List<string>();
        if (options.Length is < 4 or > 6)
        {
            failures.Add("Captcha:Length must be between 4 and 6.");
        }
        if (options.ExpirySeconds is < 30 or > 600)
        {
            failures.Add("Captcha:ExpirySeconds must be between 30 and 600.");
        }
        if (!string.IsNullOrEmpty(options.MasterCode))
        {
            if (!environment.IsDevelopment())
            {
                AuthLog.MasterCodeOutsideDevelopment(logger, environment.EnvironmentName);
                failures.Add("Captcha:MasterCode is only allowed in the Development environment.");
            }
            else if (options.MasterCode.Length is < 4 or > 6 || !options.MasterCode.All(char.IsAsciiDigit))
            {
                failures.Add("Captcha:MasterCode must be 4-6 digits.");
            }
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>發出的 Captcha 題目。<see cref="Code"/> 只在伺服器端使用，API 只回傳圖片。</summary>
public sealed record CaptchaChallenge(string Id, string Code, int ExpiresInSeconds);

public enum CaptchaVerification
{
    Invalid,
    Valid,
    MasterCode,
}

/// <summary>數字 Captcha：答案存 Redis，驗證一次即作廢（不論對錯）。</summary>
public sealed class CaptchaService(
    IOneTimeStore store,
    IOptions<CaptchaOptions> options,
    IHostEnvironment environment,
    ILogger<CaptchaService> logger)
{
    private const string KeyPrefix = "sys:captcha:";

    public async Task<CaptchaChallenge> CreateAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var max = (int)Math.Pow(10, settings.Length);
        var code = RandomNumberGenerator.GetInt32(0, max).ToString("D" + settings.Length.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        await store.SetAsync(KeyPrefix + id, code, TimeSpan.FromSeconds(settings.ExpirySeconds), cancellationToken);
        return new CaptchaChallenge(id, code, settings.ExpirySeconds);
    }

    /// <summary>驗證並作廢題目。萬用碼只在 Development 生效，每次使用寫 Warning（帳號、IP）。</summary>
    public async Task<CaptchaVerification> VerifyAsync(string? id, string? code, string username, string? remoteIp, CancellationToken cancellationToken = default)
    {
        var master = options.Value.MasterCode;
        if (environment.IsDevelopment() && !string.IsNullOrEmpty(master) && FixedEquals(code, master))
        {
            // 稽核日誌（sys_audit_log）於 WU-SYS-007 建立後補寫同一事件
            AuthLog.MasterCodeUsed(logger, username, remoteIp ?? "unknown");
            return CaptchaVerification.MasterCode;
        }
        if (string.IsNullOrEmpty(id) || id.Length > 64 || string.IsNullOrEmpty(code))
        {
            return CaptchaVerification.Invalid;
        }
        var expected = await store.TakeAsync(KeyPrefix + id, cancellationToken);
        return expected is not null && FixedEquals(code, expected) ? CaptchaVerification.Valid : CaptchaVerification.Invalid;
    }

    private static bool FixedEquals(string? actual, string expected) =>
        actual is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}

/// <summary>以 SVG 繪製數字（隨機位移、旋轉、干擾線），回傳 data URI。</summary>
public static class CaptchaImage
{
    private const int CharWidth = 24;
    private const int Height = 40;

    public static string ToDataUri(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var width = (code.Length * CharWidth) + 16;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{Height}\" viewBox=\"0 0 {width} {Height}\">");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#f4f1fb\"/>");
        for (var i = 0; i < 4; i++)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<line x1=\"{Next(width)}\" y1=\"{Next(Height)}\" x2=\"{Next(width)}\" y2=\"{Next(Height)}\" stroke=\"#b9a7e0\" stroke-width=\"1\"/>");
        }
        for (var i = 0; i < code.Length; i++)
        {
            var x = 10 + (i * CharWidth) + Next(5);
            var y = 28 + Next(6);
            var angle = Next(31) - 15;
            svg.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{x}\" y=\"{y}\" transform=\"rotate({angle} {x} {y})\" font-family=\"monospace\" font-size=\"24\" font-weight=\"bold\" fill=\"#5b4b8a\">{code[i]}</text>");
        }
        svg.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }

    private static int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}
