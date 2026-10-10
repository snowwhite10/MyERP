using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.System.Contracts;
using MyERP.Modules.System.Application;
using MyERP.Modules.System.Domain;
using MyERP.Modules.System.Infrastructure;
using Microsoft.AspNetCore.Identity;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.Modules.System;

/// <summary>SYS 系統管理模組進入點。</summary>
public sealed class SystemModule : IErpModule
{
    public string Code => SystemModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IModuleDbModel, SystemOrganizationDbModel>();
        var policy = configuration.GetSection("Security:PasswordPolicy").Get<PasswordPolicy>() ?? new PasswordPolicy();
        policy.ValidateConfiguration();
        services.AddSingleton(policy);
        services.AddSingleton<IPasswordHasher<SystemUser>, PasswordHasher<SystemUser>>();
        services.AddScoped<SystemUserService>();
    }
}
