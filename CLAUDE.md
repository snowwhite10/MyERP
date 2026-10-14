# CLAUDE.md — MyERP（Claude Code 入口）

> 本檔只是入口。**唯一總指令是 `MASTER_PROMPT.md`**，兩者有衝突時以 MASTER_PROMPT 為準。
> Claude Code 讀 `CLAUDE.md`，Codex 讀本檔；兩者內容相同，共用同一份 `state.json` 與文件。

## 每輪開始

1. 讀 `MASTER_PROMPT.md` → `state.json` → `docs/00-management/progress.md` → `docs/00-management/backlog.md`。
2. 如 `git status` 有未提交變更，先處理（完成並提交，或還原）。
3. 只做一個工作單元（MASTER_PROMPT 第 14 節），更新狀態、寫日誌、提交，然後結束。

## 常用指令（Windows，repo 根目錄）

| 用途 | 指令 |
|---|---|
| 後端建置 | `dotnet build src/backend/MyERP.sln -c Debug` |
| 後端測試 | `dotnet test --solution src/backend/MyERP.sln --no-build`（Microsoft.Testing.Platform，ADR-004；單一專案用 `--project <csproj>`、篩選用 `--filter-method "*名稱*"`；主機記憶體不足時整合測試大量 SQL 連線逾時，加 `--max-threads 2`） |
| 後端覆蓋率 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Get-BackendCoverage.ps1 [-TestProject MyERP.Shared.Tests] [-Minimum 80]`（先 build；coverlet 經 dotnet vstest，ADR-005） |
| 重建 sln | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/New-BackendSolution.ps1`（新增專案後加入 sln） |
| 新增 migration | `dotnet ef migrations add <Module>_<Purpose> -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api` |
| 套用 migration | `dotnet ef database update -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api` |
| 前端安裝 / 建置 | PowerShell：`& ./tools/scripts/Invoke-Frontend.ps1 npm ci`、`& ./tools/scripts/Invoke-Frontend.ps1 npx ng build`（Node 24.21.0 可攜版，ADR-007；不要直接用系統 Node 22） |
| 前端測試 | `& ./tools/scripts/Invoke-Frontend.ps1 npx ng test --watch=false`（覆蓋率加 `--coverage`；輸出很長，可 `*> logs/fe-test.txt` 再搜尋 `Tests`） |
| E2E | PowerShell：首次 `& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npm ci`、`& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright install chromium`；執行 `& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright test`（自動 production 建置前端並啟動靜態伺服器；瀏覽器在 `.tools/ms-playwright`，ADR-009；見 docs/01-architecture/e2e.md） |
| 種子資料 | `dotnet run --project src/backend/MyERP.Api -- --seed`（只限 Development、冪等；帳號在 appsettings.Development.json 的 `SeedUsers`，ADR-023） |
| 真實 API E2E | PowerShell：`& ./tools/scripts/Invoke-RealApiE2E.ps1 [-NoBuild] *> logs/real-e2e.txt`（migration → 種子 → 啟動 API → `specs/real-api-*`；需 SQL Server 與 Redis） |
| 寫 AI 日誌 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/runner/Write-AiLog.ps1 -Module SD -Stage 測試 -Status OK -Message "..."` |
| 產生 SRS/SDS | 見 `tools/docgen/README.md`（Phase 0 建立） |

> 上表路徑在 Phase 0 建立骨架後，如有不同，AI 必須同步更新本檔與 `CLAUDE.md`（這兩個入口檔可由 AI 維護）。

## 硬性限制（摘要）

- 技術棧固定（MASTER_PROMPT 第 4 節）；不使用 Docker。
- 不得修改 `MASTER_PROMPT.md`、`tools/runner/Start-Runner.ps1`、`tools/runner/lib/`、`tools/runner/runner.config.json`。
- 不得提交 `docs/reference/`、`logs/`、密碼、金鑰、Token。
- 不得跨模組直接讀寫資料表；不得直接 UPDATE 庫存彙總或修改已過帳傳票。
- 全部測試通過才可 merge 到 `main`；不得刪除或削弱測試。
- 不使用 `DateTime.Now/UtcNow`（用 `IClock`）、不使用 `float/double` 處理業務數值、不使用 `System.Data.SqlClient`。
- 參考 `docs/reference/legacy-source/`（舊系統原始碼）只可理解業務規則，不得複製代碼。

## Claude Code 補充

- 以 `-p` 無人值守模式執行，沒有人會回覆問題；不確定時寫 ADR 後繼續。
- 可用的 Skills / Plugins 以 MASTER_PROMPT 第 16 節為準；不存在時用最接近的方法並記錄於 ADR。
- 大型探索（例如閱讀參考文件、舊系統原始碼）優先使用 subagent，避免主上下文被佔滿。

## 專案 Skills（Codex 亦須遵守）

`.claude/skills/` 內的專案 skills 適用於所有代理。Codex 不會自動載入，必須在以下情況先閱讀對應的 SKILL.md 並按其步驟執行：

- 撰寫 test-plan 或開始寫測試：`.claude/skills/myerp-test-scenarios/SKILL.md`
- 完成或修改前端畫面、模組 QA 工作單元：`.claude/skills/myerp-ux-review/SKILL.md`
