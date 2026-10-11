using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Application.Auditing;
using MyERP.Modules.System.Application.Auth;
using MyERP.Modules.System.Application.CompanyImages;
using MyERP.Modules.System.Application.Authorization;
using MyERP.Modules.System.Application.Approvals;
using MyERP.Modules.System.Application.Numbering;
using MyERP.Modules.System.Application.Parameters;
using MyERP.Shared.Settings;
using MyERP.Modules.System.Application.Seeding;
using MyERP.Modules.System.Application.Security;
using MyERP.Shared.Infrastructure.Seeding;
using MyERP.Shared.Approvals;
using MyERP.Shared.Numbering;
using IIpAccessPolicy = MyERP.Shared.Security.IIpAccessPolicy;
using IPermissionDefinitionProvider = MyERP.Shared.Security.IPermissionDefinitionProvider;
using IUserAuthorizationStore = MyERP.Shared.Security.IUserAuthorizationStore;
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
        services.AddScoped<OrganizationService>();
        AddAuthentication(services, configuration);

        // 角色、權限代碼與資料範圍（WU-SYS-004、ADR-014）
        services.AddSingleton<IPermissionDefinitionProvider, SystemPermissions>();
        services.AddScoped<UserAuthorizationStore>();
        services.AddScoped<IUserAuthorizationStore>(sp => sp.GetRequiredService<UserAuthorizationStore>());
        services.AddScoped<RoleService>();
        services.AddScoped<AuditLogService>();

        // IP 白名單（WU-SYS-006）：設定由 WebSecurityOptions 管理，規則快照跨請求共用。
        services.AddSingleton<IpWhitelistCache, IpWhitelistCache>();
        services.AddScoped<IpWhitelistPolicy>();
        services.AddScoped<IIpAccessPolicy>(sp => sp.GetRequiredService<IpWhitelistPolicy>());
        services.AddScoped<IpWhitelistService>();

        // 單號規則（WU-SYS-008、ADR-021）
        services.AddScoped<NumberRuleService>();
        services.AddScoped<INumberGenerator, NumberGenerator>();

        // 審批流程引擎（WU-SYS-009、ADR-022）
        services.AddScoped<ApprovalFlowService>();
        services.AddScoped<ApprovalDelegationService>();
        services.AddScoped<ApprovalWorkflowService>();
        services.AddScoped<IApprovalWorkflowService>(sp => sp.GetRequiredService<ApprovalWorkflowService>());

        // 系統參數（WU-SYS-010a、ADR-026）：各模組以 IParameterDefinitionProvider 宣告參數
        services.AddSingleton<IParameterDefinitionProvider, SystemParameterDefinitions>();
        services.AddSingleton<ParameterCatalog>();
        services.AddScoped<SystemParameterService>();
        services.AddScoped<SystemParameterReader>();
        services.AddScoped<ISystemParameters>(sp => sp.GetRequiredService<SystemParameterReader>());
        // 列印設定（WU-SYS-010c、ADR-027）：print 分組參數，其他模組經 Contracts 讀取
        services.AddScoped<IPrintSettingsReader, PrintSettingsReader>();
        // Logo 與印章（WU-SYS-010d、ADR-028）：圖片存在資料庫，其他模組經 Contracts 讀取
        services.AddScoped<CompanyImageService>();
        services.AddScoped<ICompanyImageReader, CompanyImageReader>();

        // 種子資料（WU-SYS-013、ADR-023）：只在 Development 以 --seed 執行
        services.AddScoped<SystemDataSeeder>();
        services.AddScoped<IDataSeeder>(sp => sp.GetRequiredService<SystemDataSeeder>());
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
