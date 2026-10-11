# ADR-023：種子資料的執行方式與連真實 API 的 E2E

- 日期：2026-10-11
- 狀態：採用
- 工作單元：WU-SYS-013

## 背景

MASTER_PROMPT 第 12 節要求：Development 環境有 9 個測試帳號（admin／finance／sales／…／viewer），帳號設定放在 `appsettings.Development.json` 的 `SeedUsers` 區段，種子程序把密碼雜湊後寫入資料庫，且只在 Development 執行；示範資料有 2 間公司（本位幣 TWD、HKD）、每間 2–3 間分店。之前的 E2E 全部以 `page.route` 模擬 API（ADR-013），需要一組連真實後端的登入測試（SYS-AUTH-017）。

需要決定：

1. 種子何時執行，怎樣避免整合測試（`WebApplicationFactory` 預設 Development 環境）意外載入種子；
2. 冪等規則；
3. 角色權限怎樣涵蓋尚未開發的模組；
4. 密碼政策與帳號格式的例外；
5. 真實 API E2E 的架設。

## 決定

1. **以命令列明確執行**：`dotnet run --project src/backend/MyERP.Api -- --seed`，執行全部種子後即結束，不啟動 Web 伺服器。不在每次啟動時自動執行，整合測試與一般開發啟動都不受影響。
   - 共用介面 `IDataSeeder`（`Order`、`Name`、`SeedAsync`）與 `DataSeedRunner` 放在 `MyERP.Shared.Infrastructure/Seeding`。各模組註冊自己的種子，依 `Order` 執行（SYS = 100；之後 MD、INV… 依依賴關係往後排，例如 MD 主檔與 12 個月交易資料）。
   - `DataSeedRunner` 在非 Development 環境寫 Error 日誌並拋出例外，不執行任何種子。
2. **冪等**：以代碼／帳號比對，只新增缺少的資料；不改既有資料（名稱、資料範圍、已修改的密碼、帳號狀態）。整個 SYS 種子在同一交易內。第二次執行新增 0 筆（日誌列出每類筆數）。
3. **角色權限以規則展開**：種子角色以規則表示（例如 FINANCE =「FI、CO 模組全部 + 所有 `*.VIEW`」），執行時對照權限目錄（`PermissionCatalog`）展開成 `sys_role_permission`。之後新模組宣告的權限，重新執行種子即補上。種子會補回符合規則但缺少的權限（包括管理員曾移除的），但不移除額外加入的權限。
   - ADMIN 用 `IsFullAccess`（不逐一列權限）。全部種子角色資料範圍為全公司。
   - 模組沒有 WMS：warehouse 對應 INV。manager 的「各模組審批權限 + 全部報表」= `SYS.APPROVAL.*`、任何 `APPROVE`／`REJECT` 動作、BI 全部，另加全部 `VIEW`（審批時要能查看單據）。
   - 除 viewer 外，業務角色都有 `SYS.APPROVAL.VIEW/PROCESS`，可被指定為審批人並處理自己的待辦。
4. **示範密碼不套用密碼政策**：`az5911`、`Demo@2026` 是 MASTER_PROMPT 公開的示範值，不符合正式密碼政策（至少 12 字元等）。種子直接以 `PasswordHasher` 雜湊，不呼叫 `PasswordPolicy`。帳號 `hr` 只有 2 個字元，低於管理 API 的 3 字元下限，同樣是刻意例外；登入 API 本來就不檢查帳號格式。這些值只存在 `appsettings.Development.json`，種子只在 Development 執行，正式環境沒有種子帳號。
5. **組織**：TW01（3 間分店 TPE／TXG／KHH）、HK01（2 間分店 HKG／KLN），每間公司 7 個部門（ADM、FIN、SAL、PUR、WH、PRD、HR）。每個種子帳號可存取兩間公司，預設 TW01，歸屬第一間分店及角色對應的部門。`sys_company` 目前沒有本位幣欄位，TWD／HKD 記在種子定義，待 MD 幣別主檔與 FI 公司財務設定建立後寫入。示範審批流程與單號規則要有單據類型才有意義，留給各業務模組的種子。
6. **真實 API E2E**：
   - `serve-dist.mjs` 指定 `--api`（或環境變數 `E2E_API_URL`）時，把 `/api/` 請求轉送到後端。瀏覽器看到的仍是同源請求，前端 CSP 的 `connect-src 'self'` 不變，也不需要 CORS。
   - `playwright.config.ts` 在設定 `E2E_API_URL` 時改用 4301 埠，避免重用不轉送的既有伺服器。
   - `specs/real-api-login.spec.ts` 沒有 `E2E_API_URL` 時略過，所以一般 E2E 執行不需要後端。
   - `tools/scripts/Invoke-RealApiE2E.ps1` 依序建置、對 Development 資料庫 `myerp` 套用 migration、`--seed`、在 `http://127.0.0.1:5180` 啟動 API（等待 `/health/ready`），然後執行 `specs/real-api-*`，結束時停止 API。
   - 錯誤登入的測試用不存在的帳號，不會累積種子帳號的失敗次數。

## 影響

- 新增指令：`dotnet run --project src/backend/MyERP.Api -- --seed`、`powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Invoke-RealApiE2E.ps1 [-NoBuild]`（CLAUDE.md、AGENTS.md 已更新）。
- 之後各模組的種子實作 `IDataSeeder`，並遵守同樣的冪等與只在 Development 執行的規則。
- 真實 API E2E 需要本機 SQL Server 與 Redis，和整合測試一樣不加入 CI。
