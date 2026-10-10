# E2E 測試（Playwright）

> WU-P0-012。依據 MASTER_PROMPT 第 10、11、14 節。決策見 [ADR-009](adr/ADR-009-E2E測試設定.md)。

## 1. 範圍

- 只在 VM 本機執行，**不加入 CI**（第 11 節）。
- 每個工作單元的「驗證」步驟（第 14 節第 5 步）要跑一次冒煙測試。
- 目前只有前端，沒有後端，也不需要登入。WU-SYS-003 完成登入後，要補上登入流程的 E2E，並在 webServer 啟動後端（第 6 節）。

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

## 4. serve-dist.mjs

| 請求 | 回應 |
|---|---|
| 存在的檔案 | 200，依副檔名設定 Content-Type，`Cache-Control: no-store` |
| 有副檔名但不存在、`/api/` 開頭 | 404。不用 index.html 掩蓋缺少的 js、css 或語言檔 |
| 其他路徑（Angular 路由） | 200 index.html |
| 解碼後跳出根目錄（`..%2F`、`%2e%2e`、`..%5C`） | 404 |
| GET / HEAD 以外的方法 | 405 |

## 5. 撰寫規範

- 從 `support/fixtures.ts` 匯入 `test` / `expect`。自動 fixture `pageErrors` 會收集 console error 和未捕捉的例外，測試結束時有任何一筆就算失敗；預期會出錯的測試用 `pageErrors.allow(/pattern/)` 放行。
- 選取元素一律用 `data-testid`（ui-conventions.md），不用 CSS class 或文字。只有驗證翻譯時才比對文字。
- 下拉選單用 `chooseTheme` / `chooseLanguage`，會等選單關閉後才繼續。ng-zorro 下拉有開關動畫，直接連點會點到正在移除的舊選單。
- 圖表用 `expectCanvasPainted`：canvas 尺寸大於 0，且至少有一個不透明像素。只檢查 DOM 有沒有 canvas 不夠。
- 測試之間不共用狀態。每個測試都是新的 browser context，localStorage 是空的。

## 6. 冒煙測試清單

| 檔案 | 驗證內容 |
|---|---|
| home.spec.ts | 主框架載入、預設繁體與 `<html lang>`；三張 G2 圖表加 KPI 迷你趨勢圖實際畫出 canvas；KPI 數值；側欄收合 / 展開 |
| theme.spec.ts | 亮色預設不載入暗色樣式；切換暗色時 theme-dark.css 回 200、CSSOM 有規則、`html.erp-dark`、body 背景 / 文字色符合設計 token、圖表仍正常；重新整理後保留；切回亮色時移除 `<link>`；跟隨系統（emulateMedia） |
| language.spec.ts | 切換英文 / 簡體時 `<html lang>` 與文字同步，重新整理後保留；沒有偏好時依瀏覽器語言（en-US）決定 |
| routing.spec.ts | 404 頁可返回首頁、403 頁；serve-dist.mjs 的 404、SPA fallback、路徑跳出根目錄、語言檔 |

E2E 第一次執行就發現暗色模式下 body 背景是 `#000`，不是設計 token `#141414`：暗色 ng-zorro bundle 在執行期插入，排在全域樣式之後，它的 `body { background-color }` 蓋掉了全域樣式。已把 `styles.scss` 的選擇器改成 `html body`，提高權重。
