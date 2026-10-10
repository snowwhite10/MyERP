# ADR-009 E2E 測試設定（Playwright、受測站台、瀏覽器位置）

- 狀態：Accepted
- 日期：2026-10-10
- 工作單元：WU-P0-012
- 相關：ADR-001（@playwright/test 1.64.0）、ADR-007（可攜版 Node、Invoke-Frontend.ps1）；說明見 [e2e.md](../e2e.md)

## 背景

MASTER_PROMPT 第 10 節要求用 Playwright 做 E2E，第 14 節要求每個工作單元驗證時跑冒煙測試，第 11 節規定 E2E 只在 VM 本機跑。需要決定以下幾件事：

1. 受測站台用 `ng serve` 還是 production 建置；
2. 瀏覽器下載到哪裡；
3. 如何確認「圖表有畫出來」。

本機限制：無人值守的權限設定不允許讀寫 repo 以外的目錄，也不允許在工具指令裡直接設定 `$env:`。

## 決定

1. **受測站台用 production 建置**，加上 repo 內的小型 Node 靜態伺服器 `tests/e2e/serve-dist.mjs`，不用 `ng serve`。這樣測到的就是部署產物，例如 `outputHashing: all` 下 `theme-dark.css`（`inject: false`）的檔名不會被 hash，`ThemeService` 的固定 href 才有效。伺服器只對「沒有副檔名、也不是 `/api/` 開頭」的路徑回 index.html，缺少的資源一律回 404，並拒絕解碼後跳出根目錄的路徑。不另外安裝 `http-server` 之類的套件。
2. **瀏覽器只裝 chromium，放在 `.tools/ms-playwright`**。`Invoke-Frontend.ps1` 在沒有設定 `PLAYWRIGHT_BROWSERS_PATH` 時，自動把它設到這個位置，和可攜版 Node 一起由 `.gitignore` 排除。不使用系統的 Edge / Chrome，避免版本不受控。
3. **圖表要檢查像素**。`expectCanvasPainted` 用 `getImageData` 確認 canvas 至少有一個不透明像素。G2 渲染失敗時 DOM 仍會留下空白 canvas，只檢查元素存在不夠。
4. **自動 fixture `pageErrors`**：測試期間出現任何 console error 或未捕捉例外就算失敗，有需要時用 `allow()` 放行。這樣不用逐一斷言，也能抓到執行期錯誤，例如語言檔 404、G2 例外。
5. **E2E 不加入 CI**（第 11 節）。`tests/e2e` 有自己的 `package.json` / `package-lock.json`，和前端的相依分開。

## 考慮過的方案

| 方案 | 不採用原因 |
|---|---|
| webServer 用 `ng serve` | 啟動快，但開發模式和部署產物不同（hash、最佳化、budgets），測不到 production 的問題 |
| `npx http-server` / `serve` 套件 | 多一個相依；SPA fallback 會把缺少的資源也回 index.html，掩蓋錯誤 |
| 瀏覽器放在預設的 `%LOCALAPPDATA%\ms-playwright` | 無人值守時權限不允許存取 repo 以外的目錄；放在 repo 內也方便清理 |
| 使用系統 Edge（`channel: 'msedge'`） | 版本跟著系統更新變動，結果不可重現 |

## 影響

- 每次執行都會重新做一次 production 建置，約 30 秒；全部 E2E 約 35 秒。
- 加入登入（WU-SYS-003）後，webServer 要再啟動後端 API，靜態伺服器也要把 `/api/` 代理到後端，屆時更新本 ADR。
