using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyERP.Modules.System.Application.Auth;

/// <summary>
/// JWT 設定（<c>Security:Jwt</c>）。簽章金鑰不得提交到 Git：正式環境以環境變數或 user-secrets 提供；
/// Development 未設定時於程序啟動時產生臨時金鑰（重啟後舊 Token 失效，ADR-012）。
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Security:Jwt";
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; set; } = "MyERP";
    public string Audience { get; set; } = "MyERP";
    public string? SigningKey { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
}

/// <summary>
/// Development 未設定金鑰時補上臨時金鑰；單例只產生一次，確保簽發與驗證使用同一把金鑰。
/// 首次產生於啟動檢查（ValidateOnStart），只記一筆 Information，屬預期的開發行為。
/// </summary>
public sealed class DevelopmentJwtKeySetup : IPostConfigureOptions<JwtOptions>
{
    private readonly IHostEnvironment environment;
    private readonly Lazy<string> ephemeralKey;

    public DevelopmentJwtKeySetup(IHostEnvironment environment, ILogger<DevelopmentJwtKeySetup> logger)
    {
        this.environment = environment;
        ephemeralKey = new(() =>
        {
            AuthLog.EphemeralJwtKey(logger);
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        });
    }

    public void PostConfigure(string? name, JwtOptions options)
    {
        if (string.IsNullOrEmpty(options.SigningKey) && environment.IsDevelopment())
        {
            options.SigningKey = ephemeralKey.Value;
        }
    }
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrEmpty(options.SigningKey) || Encoding.UTF8.GetByteCount(options.SigningKey) < JwtOptions.MinimumKeyBytes)
        {
            failures.Add($"Security:Jwt:SigningKey must be configured (user-secrets or environment variable) with at least {JwtOptions.MinimumKeyBytes} bytes.");
        }
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Security:Jwt:Issuer and Security:Jwt:Audience are required.");
        }
        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            failures.Add("Security:Jwt:AccessTokenMinutes must be between 1 and 60.");
        }
        if (options.RefreshTokenDays is < 1 or > 30)
        {
            failures.Add("Security:Jwt:RefreshTokenDays must be between 1 and 30.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
