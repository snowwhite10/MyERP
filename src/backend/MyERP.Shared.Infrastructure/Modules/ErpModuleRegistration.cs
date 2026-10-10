using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Modules;

namespace MyERP.Shared.Infrastructure.Modules;

public static class ErpModuleRegistration
{
    /// <summary>
    /// 註冊模組：驗證代碼存在且不重複、呼叫各模組的 <see cref="IErpModule.AddServices"/>、
    /// 把模組組件加入 MVC ApplicationParts（令模組 Api 資料夾內的 Controller 可被發現）。
    /// </summary>
    public static IMvcBuilder AddErpModules(this IMvcBuilder mvc, IConfiguration configuration, params IErpModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(mvc);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            var info = ErpModuleCatalog.Get(module.Code);
            if (!seen.Add(info.Code))
            {
                throw new InvalidOperationException($"ERP module '{info.Code}' is registered more than once.");
            }
        }

        foreach (var module in modules)
        {
            module.AddServices(mvc.Services, configuration);
            mvc.Services.AddSingleton(module);

            var assembly = module.GetType().Assembly;
            if (!mvc.PartManager.ApplicationParts.OfType<AssemblyPart>().Any(p => p.Assembly == assembly))
            {
                mvc.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
            }
        }

        return mvc;
    }
}
