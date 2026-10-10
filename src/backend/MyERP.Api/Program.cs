using MyERP.Modules.System;
using MyERP.Shared.Infrastructure.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddErpModules(builder.Configuration, new SystemModule());

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 存活檢查：只確認程序可回應；資料庫 / Redis 的就緒檢查（/health/ready）於 WU-P0-008、WU-SYS-003 加入。
app.MapHealthChecks("/health/live");
app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

/// <summary>供 WebApplicationFactory 整合測試使用。</summary>
public partial class Program;
