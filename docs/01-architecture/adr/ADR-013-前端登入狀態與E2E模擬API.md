# ADR-013：前端登入狀態保存與登入 E2E 的 API 模擬

- 日期：2026-10-10
- 狀態：採用
- 工作單元：WU-SYS-003（第二段）

## 背景

後端 `/api/v1/sys/auth` 已完成（ADR-012）。前端需要保存 Token、401 時自動刷新、守衛未登入路由。E2E 目前只有靜態前端（ADR-009），沒有後端；MASTER_PROMPT 第 10 節要求 Playwright 覆蓋登入。

## 決定

1. **Token 存放**：`AuthService` 把 `AuthTokens` 加上帳號，存 `localStorage` 的 `myerp.auth.session`（經 `PreferenceStorage`，不可用時退回記憶體），讓多分頁共用登入、重新整理不需重登。代價是 XSS 時可被讀取；以 Angular 預設跳脫、WU-SYS-005 的 CSP 與 15 分鐘 Access Token 降低影響。改用 HttpOnly Cookie 須同時改後端與 CSRF 防護，留待 WU-SYS-005 評估。
2. **登入判斷**：只看本機 Session 的 `refreshTokenExpiresAt`（前端時間），不在每次路由呼叫 `/me`。Access Token 過期交給攔截器處理。
3. **刷新**：攔截器對 API 請求（匿名端點 captcha / login / refresh 除外）加 `Authorization: Bearer`；收到 401 時呼叫 `AuthService.refresh()` 一次再重送。並發的 401 共用同一個 refresh 請求，因為後端輪替後舊 Refresh Token 立即失效，重複刷新會讓第二個請求失敗。重送仍 401 直接回錯，不再刷新。刷新失敗即清除 Session，導向 `/login?returnUrl=…`。
4. **returnUrl**：只接受 `/` 開頭、非 `//`、非 `/\`、非 `/login` 的站內路徑，防止開放重新導向。
5. **登出**：呼叫 `/logout` 撤銷 Refresh Token；伺服器失敗（例如網路中斷、Token 已過期）也清除本機 Session。
6. **登入頁語言檔**：登入頁在主框架外，用 `i18nScopeGuard('sys')` 載入 `sys.json`；Captcha 元件文字在 `common`，錯誤代碼在 `errors`（`errors.SYS.AUTH.*`）。
7. **E2E**：
   - 既有冒煙測試以 fixture `signedIn`（預設 true）在分頁第一次載入前寫入假 Session；登出後不會再寫入（用 `sessionStorage` 標記）。
   - 登入 E2E 以 `page.route` 模擬 `/api/v1/sys/auth/*`（`support/mock-auth-api.ts`：Captcha 一次即作廢、萬用碼 123456、帳號不分大小寫、錯誤回 ProblemDetails + errorCode）。
   - 不在 webServer 另啟 API：需要 SQL Server、Redis、種子帳號（WU-SYS-013）與 CORS（WU-SYS-005）。後端真實行為已由 13 個登入整合測試覆蓋。種子資料與 CORS 完成後，再加一組連真實 API 的 E2E（`E2E_API_URL`）。

## 後果

前端登入、刷新、登出可在單元測試及 E2E 完整驗證；E2E 與後端契約之間的一致性靠 api.md 與整合測試維持，模擬 API 必須跟著 api.md 更新。
