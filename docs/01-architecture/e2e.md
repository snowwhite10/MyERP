# E2E 測試（Playwright）

> WU-P0-012。依據 MASTER_PROMPT 第 10、11、14 節。決策見 [ADR-009](adr/ADR-009-E2E測試設定.md)。

## 1. 範圍

- 只在 VM 本機執行，**不加入 CI**（第 11 節）。
- 每個工作單元的「驗證」步驟（第 14 節第 5 步）要跑一次冒煙測試。
- 目前只有前端，沒有後端。主框架需要登入：fixture `signedIn`（預設 true）在分頁第一次載入前寫入假 Session；登入流程測試用 `test.use({ signedIn: false })`，並以 `support/mock-auth-api.ts` 的 `page.route` 模擬 `/api/v1/sys/auth/*`（ADR-013）。模擬 API 須與 SYS `api.md` 一致。CORS（WU-SYS-005）與種子帳號（WU-SYS-013）完成後，再加連真實 API 的登入 E2E。

## 2. 結構

```
tests/e2e/
├─ package.json            # @playwright/test 1.64.0（ADR-001）、typescript、@types/node
├─ playwright.config.ts    # chromium；locale zh-TW；webServer
├─ serve-dist.mjs          # 靜態伺服器（production 建置結果）
├─ tsconfig.json           # npx tsc --noEmit 型別檢查
├─ support/fixtures.ts     # 共用 fixture 與輔助函式
└─ specs/*.spec.ts
```

## 3. 執行

```powershell
# 首次（或 package-lock 變更後）
& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npm ci
& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright install chromium

# 執行（輸出很長時重導到 logs/）
& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright test *> logs/e2e-run.txt
# 型別檢查
& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx tsc --noEmit
```

- `Invoke-Frontend.ps1` 用可攜版 Node 24，並把 `PLAYWRIGHT_BROWSERS_PATH` 設為 `.tools/ms-playwright`（已列入 .gitignore）。
- webServer 先跑 `npm --prefix ../../src/frontend run build`（production），再用 `node serve-dist.mjs --port 4300` 啟動。本機已有伺服器在跑時直接沿用（`reuseExistingServer`）。
- 環境變數：`E2E_PORT`（預設 4300）；`E2E_BASE_URL` 指向已啟動的站台時，不會啟動 webServer，也會略過靜態伺服器的檢查。
- 失敗時在 `tests/e2e/test-results/` 保留 trace 與截圖，報告在 `playwright-report/`，兩者都不提交。

### 3.1 連真實 API（ADR-023）

```powershell
& ./tools/scripts/Invoke-RealApiE2E.ps1 [-NoBuild] [-ApiUrl http://127.0.0.1:5180] *> logs/real-e2e.txt
```

- 腳本依序建置後端、對 `myerp` 套用 migration、`--seed` 載入種子、啟動 API（等待 `/health/ready`），再以 `E2E_API_URL` 執行 `specs/real-api-*`，最後停止 API。API 輸出在 `logs/real-api-e2e-api.txt`。需要本機 SQL Server 與 Redis。
- 設定 `E2E_API_URL` 時，`serve-dist.mjs` 把 `/api/` 轉送到後端，預設埠改為 4301。
- `real-api-*.spec.ts` 直接用 `@playwright/test`（不寫假 Session、不模擬 API），自己收集 console error；沒有 `E2E_API_URL` 時略過，所以一般執行會顯示 3 skipped。

## 4. serve-dist.mjs

| 請求 | 回應 |
|---|---|
| 存在的檔案 | 200，依副檔名設定 Content-Type，`Cache-Control: no-store` |
| 有副檔名但不存在、`/api/` 開頭 | 404。不用 index.html 掩蓋缺少的 js、css 或語言檔 |
| 其他路徑（Angular 路由） | 200 index.html |
| 解碼後跳出根目錄（`..%2F`、`%2e%2e`、`..%5C`） | 404 |
| GET / HEAD 以外的方法 | 405 |
| 指定 `--api`／`E2E_API_URL` 時的 `/api/` 請求（任何方法） | 轉送到後端（後端無法連線時 502） |

## 5. 撰寫規範

- 從 `support/fixtures.ts` 匯入 `test` / `expect`。自動 fixture `pageErrors` 會收集 console error、未捕捉例外和 `/api/` 回應 5xx，並在測試結束時檢查當前畫面可見文字及 placeholder/title/aria-label 的未翻譯 key。預期 console/pageerror 用 `pageErrors.allow(/pattern/)`，預期 API 5xx 須另用 `pageErrors.allowApi5xx(/路徑/, 狀態碼)`；兩者均須明確登記。守衛只檢查目前仍開啟的頁面，已離開頁面的短暫翻譯缺漏需由各頁 E2E 斷言覆蓋。
- 選取元素一律用 `data-testid`（ui-conventions.md），不用 CSS class 或文字。只有驗證翻譯時才比對文字。
- 下拉選單用 `chooseTheme` / `chooseLanguage`，會等選單關閉後才繼續。ng-zorro 下拉有開關動畫，直接連點會點到正在移除的舊選單。
- 圖表用 `expectCanvasPainted`：canvas 尺寸大於 0，且至少有一個不透明像素。只檢查 DOM 有沒有 canvas 不夠。
- 測試之間不共用狀態。每個測試都是新的 browser context，localStorage 是空的。

### 5.1 共用守衛情境（WU-P0-017）

| ID | 類別／層級 | 情境與預期 | 結果 |
|---|---|---|---|
| P0-GUARD-001 | 錯誤／E | 即使預期的瀏覽器資源錯誤已放行，未登記的 API 503 仍使測試失敗 | 自測預期失敗 |
| P0-GUARD-002 | 正常／E | 只放行指定 URL 與狀態碼的預期 500，其他錯誤仍受守衛檢查 | 通過 |
| P0-GUARD-003 | 錯誤／E | 未處理 Promise rejection 使測試失敗 | 自測預期失敗 |
| P0-GUARD-004 | 錯誤／E | 可見的 `inv.receipt.title` 原樣顯示使測試失敗 | 自測預期失敗 |
| P0-GUARD-005 | 邊界／E | 隱藏 key 及一般版本號 `1.2.3` 不誤判 | 通過 |

重複／衝突、部分結果、權限、狀態、關聯與檔案情境不適用：此工作單元只檢查 Playwright 回應與畫面，不執行業務資料操作。既有組裝 500 案例另斷言畫面 traceId 與草稿狀態一致。

代碼檢查：原本只靠 console error 判斷 5xx，若測試放行瀏覽器資源錯誤，會連同意外的 5xx 一併漏報。現按回應 URL／狀態獨立比對；i18n 掃描只讀可見元素，避免把隱藏模板當作畫面問題。守衛自測分別覆蓋失敗與放行，未發現需再優化的產品程式。

## 6. 冒煙測試清單

| 檔案 | 驗證內容 |
|---|---|
| home.spec.ts | 主框架載入、預設繁體與 `<html lang>`；三張 G2 圖表加 KPI 迷你趨勢圖實際畫出 canvas；KPI 數值；側欄收合 / 展開 |
| theme.spec.ts | 亮色預設不載入暗色樣式；切換暗色時 theme-dark.css 回 200、CSSOM 有規則、`html.erp-dark`、body 背景 / 文字色符合設計 token、圖表仍正常；重新整理後保留；切回亮色時移除 `<link>`；跟隨系統（emulateMedia） |
| language.spec.ts | 切換英文 / 簡體時 `<html lang>` 與文字同步，重新整理後保留；沒有偏好時依瀏覽器語言（en-US）決定 |
| routing.spec.ts | 404 頁可返回首頁、403 頁；serve-dist.mjs 的 404、SPA fallback、路徑跳出根目錄、語言檔 |

E2E 第一次執行就發現暗色模式下 body 背景是 `#000`，不是設計 token `#141414`：暗色 ng-zorro bundle 在執行期插入，排在全域樣式之後，它的 `body { background-color }` 蓋掉了全域樣式。已把 `styles.scss` 的選擇器改成 `html body`，提高權重。

WU-MD-022d 的純 API 規格可用 `& ./tools/scripts/Invoke-RealApiE2E.ps1 -NoBuild -ApiOnly -Specs 'specs/real-api-partner-approval.spec.ts'` 執行。`-ApiOnly` 仍執行 migration、種子與 API 啟動，將 `E2E_BASE_URL` 指向 API，省去不使用的 Angular 靜態站台建置。此規格以真實 HTTP、SQL Server 與 Redis 驗證客戶及供應商的 SYS 多級審批、審批中保護與付款預設值；每次使用唯一主檔代碼，並在結束後還原原有審批流程（新流程則停用）。
