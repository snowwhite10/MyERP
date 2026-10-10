# 安全中介層設計（WU-SYS-005）

> 來源：MASTER_PROMPT 第 7 節；決策見 [ADR-018](adr/ADR-018-安全中介層與限流.md)。SDS 3.2 Security Architecture 的來源之一（總覽見 [architecture.md](architecture.md) 第 6 節）。
> 實作：`src/backend/MyERP.Shared.Infrastructure/Security/WebSecurityExtensions.cs`、`WebSecurityOptions.cs`；測試：`tests/backend.integration/MyERP.IntegrationTests/Security/WebSecurityTests.cs`（SYS-SEC-001～014）。

## 1. 中介層順序（Program.cs）

| 順序 | 中介層 | 理由 |
|---|---|---|
| 1 | `UseMyErpForwardedHeaders` | 最先執行，之後的日誌、限流、登入記錄才看到真實客戶端 IP；未設定 Proxy 時不註冊 |
| 2 | `UseMyErpSecurityHeaders` | 以 `Response.OnStarting` 寫入，例外處理清除標頭後產生的錯誤回應也帶上 |
| 3 | `UseMyErpRequestLogging` | 記錄最終狀態碼（含 429） |
| 4 | `UseErpProblemDetails` | 例外與狀態碼頁；限流拒絕只設狀態碼，由此產生 429 ProblemDetails |
| 5 | `UseMyErpTransportSecurity` | 非 Development：HSTS + HTTP→HTTPS（307） |
| 6 | `UseMyErpCors` | 預檢在此直接回應，不計入限流、不需要驗證 |
| 7 | `UseAuthentication` | 限流需要用戶 Id |
| 8 | `UseRateLimiter` | 公司檢查之前：被拒絕的請求不查資料庫 |
| 9 | `UseCompanyAccessCheck`、`UseAuthorization` | WU-SYS-004 |

路由由 WebApplication 自動放在管線最前面，故限流與 CORS 可讀到端點的 `[EnableRateLimiting]`。

## 2. 設定（`Security` 區段）

| 鍵 | 預設 | 說明 |
|---|---|---|
| `Security:Cors:AllowedOrigins` | `[]`（Development：`http://localhost:4200`、`http://localhost:4300`、`http://127.0.0.1:4300`） | 只接受 `scheme://host[:port]`；`*`、含路徑、非 http(s) 拒絕啟動 |
| `Security:ForwardedHeaders:KnownProxies` | `[]` | 反向代理 IP |
| `Security:ForwardedHeaders:KnownNetworks` | `[]` | 反向代理 CIDR |
| `Security:ForwardedHeaders:ForwardLimit` | 1 | X-Forwarded-For 最多處理幾層 |
| `Security:RateLimiting:Auth` | 5 次／60 秒 | login、refresh；每 IP 每端點 |
| `Security:RateLimiting:Captcha` | 10 次／60 秒 | 每 IP |
| `Security:RateLimiting:User` | 300 次／60 秒 | 已登入請求；每用戶（全域限流） |
| `Security:RateLimiting:Export` | 10 次／60 秒 | 匯出端點以 `[EnableRateLimiting(RateLimitPolicies.Export)]` 套用；每用戶 |
| `Security:Hsts:MaxAgeDays` | 365 | HSTS `max-age`，含 `includeSubDomains` |

每個規則有 `PermitLimit` 與 `WindowSeconds`，必須是正整數；任何設定錯誤在啟動時拋出例外（訊息含設定鍵），不會以不安全的預設值繼續。

## 3. Rate limiting

- 固定視窗、`QueueLimit = 0`（不排隊，直接拒絕）。
- 匿名端點必須套用 `RateLimitPolicies.Auth` 或 `Captcha`（ArchitectureTests `EveryAnonymousActionDeclaresRateLimitPolicy`）；匿名請求不受全域限流。
- 已登入請求的全域分區鍵為 `user:{UserId}`；端點政策與全域限流同時生效。
- 拒絕時：狀態 429、`Retry-After`（秒，至少 1）、Warning 日誌（方法、路徑、IP、用戶），Body 由 StatusCodePages 產生 ProblemDetails（`errorCode` `COMMON.REQUEST.RATE_LIMITED`）。
- 限流狀態在程序記憶體內；多台 API 主機時每台各自計數（上限等於每台上限 × 主機數）。屆時改用 Redis 分散式限流，見 ADR-018。
- 整合測試主機由 `TestDatabase.UseTestDatabase` 放寬登入類限流（TestServer 沒有連線 IP，所有請求同一分區），限流本身由 `WebSecurityTests` 驗證。

## 4. ForwardedHeaders

- 只處理 `X-Forwarded-For` 與 `X-Forwarded-Proto`；`KnownProxies`／`KnownNetworks` 先清空框架預設的 loopback，再加入設定值。
- 兩者皆空時不呼叫 `UseForwardedHeaders`：框架在兩個清單都空時會信任任何來源。
- 經 IIS（ASP.NET Core Module in-process）部署時，連線 IP 已是客戶端 IP，不需設定 Proxy；前面再有負載平衡器時才設定。

## 5. 安全標頭與前端 CSP

API 回應（全部，含錯誤）：

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'
```

前端靜態站台（IIS 或其他伺服器）必須送出下列標頭；E2E 靜態伺服器 `tests/e2e/serve-dist.mjs` 使用同一組設定，全部 E2E 在此 CSP 下通過：

```
Content-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
```

- `script-src 'self'`：不允許任何內嵌腳本。因此 production 建置關閉 critical CSS 內嵌（`angular.json` `optimization.styles.inlineCritical: false`），否則 Angular 產生的內嵌腳本會被擋，樣式表停在 `media="print"`。
- `style-src 'unsafe-inline'`：ng-zorro 與 Angular 元件在執行期插入 `<style>`；樣式注入的風險遠低於腳本。
- `img-src data:`：Captcha 圖片為 SVG data URI。
- API 與前端不同源時，`connect-src` 須加入 API 來源，並把前端來源加入 `Security:Cors:AllowedOrigins`。

## 6. 部署檢查清單

1. 設定 `Security:Jwt:SigningKey`（環境變數或 user-secrets）；不得設定 `Captcha:MasterCode`。
2. 前端與 API 不同源時設定 `Security:Cors:AllowedOrigins`。
3. 有反向代理或負載平衡器時設定 `KnownProxies`／`KnownNetworks`。
4. 前端站台送出第 5 節的安全標頭。
5. 確認 HTTPS 憑證與 `https_port`（或 `ASPNETCORE_HTTPS_PORT`），讓 HTTP 轉址到正確埠。
