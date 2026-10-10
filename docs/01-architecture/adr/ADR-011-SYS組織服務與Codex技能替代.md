# ADR-011：SYS 組織服務授權邊界與 Codex 技能替代

- 日期：2026-10-10
- 狀態：採用
- 工作單元：WU-SYS-001

## 背景

組織模型與 migration 已存在，但 SYS 用戶登入、公司授權與權限代碼尚未實作。HTTP 的 `X-Company-Id` 只代表請求指定的公司，並不證明用戶有權存取。MASTER_PROMPT 第 16 節所列 TDD、Playwright、診斷與代碼審查等 Claude Skills 在本輪 Codex 環境沒有對應技能。

## 決定

1. 先建立可直接測試的 `OrganizationService`；它驗證目前公司是否啟用、分店／部門代碼、同公司且啟用的參照、上級循環與版本。服務暫不註冊 DI，也不建立公開 API；WU-SYS-004 實作公司授權後，才可接入端點。內部服務的公司檢查不作為授權替代。
2. 用原生 xUnit 整合測試、`dotnet build` / `dotnet test`、手動代碼檢查及既有 Playwright 指令，替代缺席的 Claude Skills。遵守測試先行與結果記錄。

## 後果

目前分店／部門應用規則已有可執行測試，但組織主檔的 HTTP 驗收及前端仍未完成。後續接入授權時須測試未授權 403、公司切換與資料範圍；不能直接把服務註冊並開放無權限檢查的路由。
