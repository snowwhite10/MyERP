using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyERP.Shared.Infrastructure.Persistence;

namespace MyERP.IntegrationTests.Database;

/// <summary>連到 myerp_test 的 API 主機：正式模組 + 測試模組（tst_*）+ <see cref="TestOrdersController"/>。</summary>
public sealed class DatabaseApiFactory(TestDatabase database) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        database.UseTestDatabase(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IModuleDbModel, TestModuleDbModel>();
            services.AddControllers().AddApplicationPart(typeof(TestOrdersController).Assembly);
        });
    }
}
