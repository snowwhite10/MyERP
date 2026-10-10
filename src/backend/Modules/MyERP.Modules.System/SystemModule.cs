using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using Microsoft.AspNetCore.Identity;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;
using StackExchange.Redis;

namespace MyERP.Modules.System;

/// <summary>SYS 系統管理模組進入點。</summary>
public sealed class SystemModule : IErpModule
{
    public const string RedisConfigurationKey = "Redis:Configuration";

    public string Code => SystemModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModuleDbModel, SystemOrganizationDbModel>();
        var policy = configuration.GetSection("Security:PasswordPolicy").Get<PasswordPolicy>() ?? new PasswordPolicy();
        policy.ValidateConfiguration();
        services.AddSingleton(policy);
        services.AddSingleton<IPasswordHasher<SystemUser>, PasswordHasher<SystemUser>>();
        services.AddScoped<SystemUserService>();
        AddAuthentication(services, configuration);
    }

    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        // Redis 連線延遲建立且不因暫時無法連線而中止（abortConnect=false）；可用性由 /health/ready 反映
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration[RedisConfigurationKey] ?? "localhost:6379");
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddSingleton<IOneTimeStore, RedisOneTimeStore>();
        services.AddHealthChecks().AddCheck<RedisHealthCheck>(RedisHealthCheck.Name, tags: ["ready"]);

        services.AddOptions<CaptchaOptions>().Bind(configuration.GetSection(CaptchaOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CaptchaOptions>, CaptchaOptionsValidator>();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IPostConfigureOptions<JwtOptions>, DevelopmentJwtKeySetup>();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddScoped<CaptchaService>();
        services.AddScoped<AuthService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var settings = jwt.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = AuthService.SigningKey(settings),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });
        services.AddAuthorization();
    }
}
