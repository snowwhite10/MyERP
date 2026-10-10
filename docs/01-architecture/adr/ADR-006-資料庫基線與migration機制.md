# ADR-006 資料庫基線與 migration 機制（定序、設計期、SP 部署、測試資料庫）

- 狀態：Accepted
- 日期：2026-10-10
- 工作單元：WU-P0-008
- 相關：ADR-001（套件清單）、ADR-003（單一 MyErpDbContext、單一 migration 組件）、ADR-005；規範見 [db-conventions.md](../db-conventions.md)

## 背景

WU-P0-008 建立第一個 migration、SP 部署與整合測試資料庫時，遇到以下需決定的事項：

- MASTER_PROMPT 6.1 指定的定序 `Chinese_Taiwan_Stroke_100_CI_AS_SC` 在 SQL Server 2022 不存在（`sys.fn_helpcollations()` 查無；`CREATE DATABASE ... COLLATE` 回「定序無效」）。
- `dotnet ef` 需要建立 `MyErpDbContext` 的方法，且模組組合必須與執行期一致，否則 snapshot 會漏掉模組的表。
- EF 模型快取預設只以 DbContext 型別為鍵；整合測試在同一程序內同時需要「正式模型」（套用 migration）與「正式 + 測試模組模型」。

## 決定

1. **定序 = `Chinese_Traditional_Stroke_Count_100_CI_AS_SC`**。SQL Server 2008 起 `Chinese_Taiwan_Stroke` 的 100 版正式名稱即 `Chinese_Traditional_Stroke_Count_100`（臺灣筆畫數排序）；`_CI_AS_SC` 保留 MASTER_PROMPT 要求的「不分大小寫、分重音、支援補充字元」。（`Chinese_Traditional_Stroke_Order_100` 對應香港 / 澳門筆順，不採用。）常數 `MyErpDbContext.DatabaseCollation`，模型以 `UseCollation` 宣告。建議修正 MASTER_PROMPT 的寫法（CR-007）。
2. **建庫方式**：`Migrate()` / `dotnet ef database update` 在資料庫不存在時由 EF 執行 `CREATE DATABASE ... COLLATE <定序>`。`Core_Baseline` 另以 `suppressTransaction: true` 檢查定序，若資料庫由他人預先建立且定序不同則 `ALTER DATABASE ... COLLATE`（需無其他連線、無相依物件；失敗時應刪庫重建）。`Down` 不還原定序。
3. **設計期**：不寫 `IDesignTimeDbContextFactory`；`dotnet ef ... -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api` 以 Api 主機的 DI 建立 DbContext（`Microsoft.EntityFrameworkCore.Design` 只加在 Api，`PrivateAssets=all`）。模組組合與執行期完全一致，不必在兩處維護。Api 引用 `MyERP.Migrations`，令執行期可依名稱載入 migration 組件。
4. **模型快取鍵** = DbContext 型別 + 模組組合（`ModuleSetKey`，依註冊順序的 `IModuleDbModel` 型別全名）+ 是否設計期（`ErpModelCacheKeyFactory`，由 `UseMyErpSqlServer` 套用）。
5. **SP 部署**：原始碼 `database/procedures/{前綴}/usp_{前綴}_{用途}.sql` 以 `EmbeddedResource` 嵌入 `MyERP.Migrations`（資源名稱 `procedures/{前綴}/{檔名}`）；migration 呼叫 `migrationBuilder.CreateOrAlterProcedure("{前綴}/usp_xxx")`，`Down` 用 `DropProcedure` 或還原上一版。修改 SP = 新增 migration 再呼叫一次 `CreateOrAlterProcedure`（.sql 檔是唯一來源；修改 SP 的 migration 其 `Down` 以 `migrationBuilder.Sql` 內嵌上一版內容還原，因為屆時 .sql 檔已是新版）。測試檢查每個嵌入的 SP 都已部署、命名與 `SET NOCOUNT ON` / `@company_id` 慣例。
6. **整合測試資料庫**：xunit v3 assembly fixture `TestDatabase` 每次測試執行重建 `myerp_test` 一次：`EnsureDeleted` → 以正式主機（`WebApplicationFactory<Program>`，不加測試模組）`Migrate` → 以只含測試模組的 DbContext `CreateTables` 建立 `tst_*` 表（不屬於 migration）。連線字串可用環境變數 `MYERP_TEST_CONNECTION` 覆寫，但資料庫名稱必須是 `myerp_test`，否則 fixture 拋出（測試不得連到 `myerp`）。各測試以不同公司 Id 隔離資料，不需逐測試清表。
7. **就緒檢查** `/health/ready`：自寫 `DatabaseHealthCheck`（`CanConnectAsync`），DbContext 於檢查內解析，連線字串缺少時回 503 而非 500；`/health/live` 不執行任何檢查。不引入 `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`（不在 ADR-001）。
8. **測試守門**：`ProductionModel_HasNoChangesMissingFromMigrations`（`HasPendingModelChanges()`）——模組改了實體卻忘記新增 migration 時測試失敗。

## 考慮過的方案

| 方案 | 不採用原因 |
|---|---|
| 照字面使用 `Chinese_Taiwan_Stroke_100_CI_AS_SC` | 不存在，建庫失敗 |
| `Chinese_Taiwan_Stroke_90_CI_AS_SC` | 舊版排序權重；100 版較新且支援更多字元 |
| `IDesignTimeDbContextFactory` 自行列出模組 | 模組清單需在 Program.cs 與 factory 兩處維護，漏改會產生錯誤 snapshot |
| 測試模組的表也放入 migration | 正式資料庫會出現測試表 |
| 每個測試類別各自重建資料庫 | 慢；重建一次 + 公司 Id 隔離已足夠 |
| SP 原始碼直接寫在 migration 字串 | SP 無法單獨閱讀 / 比對差異；違反 db-conventions.md 第 7 節 |
