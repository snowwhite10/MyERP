# SYS 組織測試計畫

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| SYS-ORG-001 | REQ_F4118.20.2, REQ_F4118.21.2, REQ_F4118.22.2 | Migration 建立三張表、索引與 JSON 約束 | 通過（三表、三種代碼唯一性、三表 JSON 約束） |
| SYS-ORG-002 | REQ_F4118.21.1, REQ_F4118.22.1 | 不同公司資料與軟刪除篩選 | 通過（分店及部門公司隔離、軟刪除篩選、無公司時空結果） |
| SYS-ORG-003 | REQ_F4118.22.2 | 跨公司分店／上級部門與循環參照拒絕 | 通過（複合外鍵寫入、上級循環） |
| SYS-ORG-004 | REQ_F4118.20.1, REQ_F4118.21.1, REQ_F4118.22.1 | 無權限返回 403 | 待測 |
| SYS-ORG-005 | REQ_F4118.20.3, REQ_F4118.21.3, REQ_F4118.22.3 | 並發版本衝突返回 409 | 部分通過（公司／分店／部門服務版本衝突及部門真實雙 DbContext 競爭；API 待測） |
| SYS-ORG-006 | REQ_F4118.20.4 | 停用公司後不得成為新操作上下文，歷史資料保留 | 通過（拒絕新分店／部門，原部門與軟刪除歷史保留） |
| SYS-ORG-007 | REQ_F4118.22.3 | 停用部門後不可當作新上級 | 通過（內部服務） |
| SYS-ORG-008 | REQ_F4118.20.2 | 代碼格式與資料庫唯一性 | 通過（公司服務格式及唯一索引） |
| SYS-ORG-009 | REQ_F4118.20.3, REQ_F4118.22.3 | 修改審計與版本遞增 | 通過（公司 UpdatedBy／UpdatedAt、公司與部門版本遞增） |
| SYS-ORG-010 | REQ_F4118.21.1, REQ_F4118.22.1 | 內部列表分頁、關鍵字、排序、公司隔離、軟刪除與無公司拒絕 | 通過（SQL Server 整合測試；HTTP 授權待測） |
| SYS-ORG-011 | REQ_F4118.20.1 | 公司列表僅返回授權且啟用的公司，支援分頁、關鍵字與排序 | 通過（SQL Server 整合測試；HTTP 授權待測） |
| SYS-ORG-012 | REQ_F4118.20.1, REQ_F4118.21.1, REQ_F4118.22.1 | 三語組織列表骨架搜尋、公司篩選及未授權前路由隔離 | 通過（前端元件測試；HTTP 列表待測） |
| SYS-ORG-013 | REQ_F4118.20.2, REQ_F4118.21.2, REQ_F4118.22.2 | 三語組織表單骨架代碼／名稱校驗、部門參照選項與儲存草稿 | 通過（前端元件測試；API 寫入待測） |

## 用戶身分

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| SYS-USER-001 | REQ_F4118.23.1, REQ_F4118.23.2 | 弱密碼拒絕，合格密碼只保存 Identity 雜湊並可驗證 | 通過（SQL Server 整合測試） |
| SYS-USER-002 | REQ_F4118.23.3 | 五次失敗鎖 15 分鐘，鎖定期間正確密碼拒絕，成功清零 | 通過（SQL Server 整合測試） |
| SYS-USER-003 | REQ_F4118.23.3, REQ_F4118.23.4 | 鎖定到期重開計數窗口；密碼到期及 0 天豁免 | 通過（SQL Server 整合測試） |
| SYS-USER-004 | REQ_F4118.23.1 | 帳號大小寫唯一與軟刪除後不可重用 | 通過（SQL Server 整合測試） |
| SYS-USER-005 | REQ_F4118.23.3 | 不同 DbContext 同時登入失敗不得遺失計數 | 通過（SQL Server 整合測試） |
| SYS-USER-007 | REQ_F4118.23.2 | 不合法密碼政策設定（長度、上限、到期天數）於模組註冊時拒絕 | 通過（整合測試專案） |
| SYS-USER-006 | REQ_F4118.23.1–4 | 管理 API 權限、三語用戶頁及登入 E2E | 待 WU-SYS-003/004 |

## 登入與 Token

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| SYS-AUTH-001 | REQ_F4118.24.1, REQ_F4118.24.2 | Captcha 登入成功，JWT `sub` 為用戶 Id、有效 15 分鐘，可呼叫 `/me` | 通過（整合測試） |
| SYS-AUTH-002 | REQ_F4118.24.1 | Captcha 端點只回 SVG、`no-store`；錯誤一次即作廢、成功後不可重用 | 通過（整合測試） |
| SYS-AUTH-003 | REQ_F4118.24.4 | Development 萬用碼可登入並寫 Warning（帳號、IP），日誌無密碼 | 通過（整合測試） |
| SYS-AUTH-004 | REQ_F4118.24.4, REQ_F4118.24.2 | 非 Development 設定萬用碼（寫 Error）、缺簽章金鑰、金鑰過短、萬用碼格式錯誤皆拒絕啟動 | 通過（整合測試，4 例） |
| SYS-AUTH-005 | REQ_F4118.24.1 | 密碼錯誤與帳號不存在同一 401 代碼；鎖定後正確密碼亦 401 | 通過（整合測試） |
| SYS-AUTH-006 | REQ_F4118.24.1 | 密碼到期回 403，不發 Token | 通過（整合測試） |
| SYS-AUTH-007 | REQ_F4118.24.2 | Refresh 輪替；5 個並發重放只有 1 個成功，舊 Token 重放 401 | 通過（整合測試） |
| SYS-AUTH-008 | REQ_F4118.24.3 | 登出需 Access Token；不能撤銷他人 Token；撤銷後刷新 401 | 通過（整合測試） |
| SYS-AUTH-009 | REQ_F4118.24.1, REQ_F4118.24.2 | 空欄位 400、無效 Refresh 401、竄改 JWT 401 | 通過（整合測試） |
| SYS-AUTH-010 | REQ_F4118.24.2 | `/health/ready` 含 Redis 檢查 | 通過（整合測試） |
| SYS-AUTH-011 | REQ_F4118.24.1–3 | 三語登入頁、Captcha 元件、Token 自動刷新與登入 E2E | 待續 |
