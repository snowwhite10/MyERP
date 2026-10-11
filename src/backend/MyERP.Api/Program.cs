using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MyERP.Modules.System;
using MyERP.Shared.Infrastructure;
using MyERP.Shared.Infrastructure.Logging;
using MyERP.Shared.Infrastructure.Modules;
using MyERP.Shared.Infrastructure.Persistence;
using MyERP.Shared.Infrastructure.Security;
using MyERP.Shared.Infrastructure.Seeding;
using MyERP.Shared.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMyErpLogging(builder.Configuration, builder.Environment)
    .AddMyErpCore(builder.Configuration)
    .AddMyErpPersistence(builder.Configuration)
    .AddMyErpWebSecurity(builder.Configuration)
    .AddControllers()
    .AddErpModules(builder.Configuration, new SystemModule());

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, tags: [ReadyTag]);

var app = builder.Build();

// dotnet run --project src/backend/MyERP.Api -- --seed：載入種子資料後結束（只限 Development，ADR-023）
if (DataSeedRunner.IsRequested(args))
{
    await app.Services.RunDataSeedAsync().ConfigureAwait(false);
    return;
}

// 只信任設定中列明的 Proxy 的 X-Forwarded-*；放最前面，之後的日誌與限流才看到真實客戶端 IP（WU-SYS-005）
app.UseMyErpForwardedHeaders();

// 安全標頭在回應開始時寫入，所有回應（含錯誤）都帶上
app.UseMyErpSecurityHeaders();

// 請求日誌在最外層：記錄最終狀態碼與總耗時，超過 App:SlowRequestMilliseconds 寫 Warning
app.UseMyErpRequestLogging();

// 全域例外處理與狀態碼頁放在最前面：其後所有中介層與端點的錯誤（含限流的 429）一律回 ProblemDetails
app.UseErpProblemDetails();

// 非 Development：HSTS + HTTP 轉 HTTPS
app.UseMyErpTransportSecurity(app.Environment);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// CORS 在驗證與限流之前：預檢請求直接回應，不計入限流
app.UseMyErpCors();
app.UseAuthentication();

// 限流在驗證之後（按用戶分區）、公司檢查之前（被拒絕的請求不查資料庫）
app.UseRateLimiter();

// IP 白名單：限流之後（白名單內的 IP 仍受限流）、公司檢查之前；未登入請求只套用全系統層級（WU-SYS-006、ADR-025）
app.UseIpWhitelist();

// 已登入請求的 X-Company-Id 必須是可存取的公司，之後的公司篩選與權限檢查才可信（WU-SYS-004）
app.UseCompanyAccessCheck();
app.UseAuthorization();

// 存活檢查：只確認程序可回應，不執行任何檢查；就緒檢查：資料庫與 Redis（SYS 模組註冊），失敗回 503。
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
