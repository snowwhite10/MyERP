using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Modules.System.Contracts;
using MyERP.Shared.Infrastructure.Modules;

namespace MyERP.Modules.System;

/// <summary>SYS 系統管理模組進入點。</summary>
public sealed class SystemModule : IErpModule
{
    public string Code => SystemModuleContracts.ModuleCode;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // WU-SYS-* 於此註冊 Application 服務、Repository 與 Contracts 實作。
    }
}
