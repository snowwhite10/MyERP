using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyERP.Shared.Infrastructure.Modules;

/// <summary>
/// 模組進入點。每個模組實作專案提供一個實作，由組合根（MyERP.Api）註冊。
/// </summary>
public interface IErpModule
{
    /// <summary>模組代碼，必須存在於 <see cref="MyERP.Shared.Modules.ErpModuleCatalog"/>。</summary>
    string Code { get; }

    /// <summary>註冊模組自己的服務（Application 服務、Repository、驗證器、Contracts 實作）。</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);
}
