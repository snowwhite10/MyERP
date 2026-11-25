# 持續整合（CI）

> WU-P0-011。依據 MASTER_PROMPT 第 10、11 節。工作流程檔：`.github/workflows/ci.yml`。

## 1. 範圍

| 項目 | CI（GitHub Actions） | VM 本機 |
|---|---|---|
| 後端建置（Release） | ✔ | ✔（Debug） |
| 後端單元測試（`tests/backend.unit/*`） | ✔ | ✔ |
| 後端整合測試（`MyERP.IntegrationTests`，需 SQL Server 2022 `myerp_test`） | ✘ | ✔ |
| 前端建置 + 單元測試（Vitest + jsdom，不需瀏覽器） | ✔ | ✔ |
| E2E（Playwright，見 [e2e.md](e2e.md)） | ✘ | ✔ |

提交 / merge 條件仍以**本機全部測試**為準（第 10 節）；CI 只是額外防線。

## 2. 工作流程

- 觸發：push 到 `main`、`feature/**`；PR 到 `main`；手動 `workflow_dispatch`。同一分支新 push 取消舊的執行（`concurrency`）。
- 權限：`permissions: contents: read`；不引用任何 `secrets.*`、連線字串或密碼。不使用 `pull_request_target`。
- `backend` job（`windows-latest`）：`actions/setup-dotnet` 讀 `global.json` → `dotnet restore` → `dotnet build src/backend/MyERP.sln -c Release` → 每個單元測試專案 `dotnet test --project <csproj> -c Release --no-build`。
  不用 `dotnet test --solution`，因為 solution 包含整合測試。
- `frontend` job（`windows-latest`，工作目錄 `src/frontend`）：`actions/setup-node` 讀 repo 根目錄 `.nvmrc`（與本機 `Invoke-Frontend.ps1` 相同版本，ADR-007），npm 快取以 `package-lock.json` 為鍵 → `npm ci` → `npx ng build` → `npx ng test --watch=false`。

## 3. 結構檢查（本機可跑）

`tests/backend.unit/MyERP.ArchitectureTests/CiWorkflowTests.cs` 以文字規則檢查 `ci.yml`（不引入 YAML 套件），隨一般單元測試執行：

- 所有 job `runs-on: windows-latest`；
- 無 `secrets.`、`ConnectionStrings`、`Password`、`pull_request_target`，權限只有 `contents: read`；
- 後端以 Release 建置、SDK 取自 `global.json`；
- `tests/backend.unit` 下**每一個** csproj 都有對應的 `dotnet test --project ... --no-build`（新增單元測試專案時必須同步加入 CI，否則測試失敗）；
- 不得執行 `tests/backend.integration` 的專案，且 `dotnet test` 一律指定 `--project`；
- 前端 Node 版本取自 `.nvmrc`，並執行 `npm ci`、`npx ng build`、`npx ng test --watch=false`。

本機沒有 actionlint；YAML 語法以 push 後的實際執行結果確認（`gh run list --workflow CI`）。

## 4. 維護

- 新增單元測試專案：放在 `tests/backend.unit/`，在 `ci.yml` 的 `backend` job 加一個 `dotnet test --project` 步驟。
- 升級 Node：只改 `.nvmrc`（CI 與本機同時生效）。升級 SDK：只改 `global.json`。

## 5. 前端「未處理錯誤」（WU-P0-FE-TIMEOUT-LEAK）

- 全部測試通過但 Vitest 報 `Unhandled Errors`／`TimeoutError: Timeout has occurred` 時，`ng test` 仍 exit 1，等同測試失敗。
- 常見原因：spec 以 `expectOne` 取得請求後**沒有 `flush`**，又以沒有 error 回呼的 `subscribe` 訂閱帶 rxjs `timeout(6000)` 的服務。`HttpTestingController.verify()` 只檢查未被比對的請求，查不出這種情況；計時器在檔案結束後才觸發，錯誤被歸到之後執行的檔案（歸屬每次不同）。
- 規則：每個被 `expectOne`／`match` 取得的請求都要 `flush`／`error`，或測試本身要以 `takeUntil`／`unsubscribe` 結束訂閱。
- 找出真正來源：暫時建立 setup 檔，在每個檔案 `afterAll` 等 7 秒（大於最長 `timeout`），令洩漏的計時器在原檔案內觸發，再執行
  `& ./tools/scripts/Invoke-Frontend.ps1 npx ng test --watch=false --include "src/app/modules/<模組>/**/*.spec.ts" --setup-files <setup 檔>`，
  看 `This error originated in` 的檔案。全量加等待約 12 分鐘，建議按模組分批；用完刪除該 setup 檔。
