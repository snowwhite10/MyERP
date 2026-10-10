using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MyERP.Modules.System;
using MyERP.Shared.Infrastructure;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMyErpCore(builder.Configuration)
    .AddMyErpPersistence(builder.Configuration)
    .AddControllers()
    .AddErpModules(builder.Configuration, new SystemModule());

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, tags: [ReadyTag]);

var app = builder.Build();

// 全域例外處理與狀態碼頁放在最前面：其後所有中介層與端點的錯誤一律回 ProblemDetails
app.UseErpProblemDetails();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 存活檢查：只確認程序可回應，不執行任何檢查；就緒檢查：資料庫（Redis 於 WU-SYS-003 加入），失敗回 503。
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
app.MapControllers();

await app.RunAsync().ConfigureAwait(false);

/// <summary>供 WebApplicationFactory 整合測試使用。</summary>
public partial class Program
{
    /// <summary>就緒檢查的標籤。</summary>
    public const string ReadyTag = "ready";
}
