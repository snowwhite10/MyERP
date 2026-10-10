# SYS 組織主檔需求（WU-SYS-001）

> 版本 1.1。公司、分店與部門為後續權限及單據的組織基礎；BlueWhale 公司與部門資料表對應 REQ_DS43.20 / .22。資料模型及公司／分店／部門內部應用服務已建立；授權與介面待同一工作單元續作。

| Sub-function No. | Sub-function |
|---|---|
| 8.20 | Company Management |
| 8.21 | Branch Management |
| 8.22 | Department Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.20.1 | 1.1 | System should allow authorized users to view company details. | Developer | 僅有組織檢視權的用戶可見其獲授權且啟用、未刪除的公司；列表支援分頁、關鍵字及代碼／名稱排序；無權限回 403。 |
| REQ_F4118.20.2 | 1.1 | System should allow authorized users to create a company. | Developer | 代碼全系統唯一；代碼與名稱必填；代碼限 1–30 字元的 ASCII 英文、數字、`-`、`_`，保存為大寫；重複代碼回 409。 |
| REQ_F4118.20.3 | 1.0 | System should allow authorized users to edit a company. | Developer | 版本不符回 409；審計欄位記錄修改者。 |
| REQ_F4118.20.4 | 1.0 | System should allow authorized users to deactivate a company. | Developer | 停用後不能成為新操作的公司上下文；歷史資料保留。 |
| REQ_F4118.21.1 | 1.0 | System should allow authorized users to view branches in the selected company. | Developer | 公司 A 的查詢不返回公司 B 分店；未選公司結果為空。 |
| REQ_F4118.21.2 | 1.0 | System should allow authorized users to create a branch. | Developer | 公司內代碼唯一；所屬公司必須為授權公司。 |
| REQ_F4118.21.3 | 1.0 | System should allow authorized users to edit or deactivate a branch. | Developer | 版本不符回 409；被引用時只可停用，不得刪除。 |
| REQ_F4118.22.1 | 1.0 | System should allow authorized users to view departments in the selected company. | Developer | 公司隔離、軟刪除篩選生效。 |
| REQ_F4118.22.2 | 1.0 | System should allow authorized users to create a department under a branch or parent department. | Developer | 分店與上級部門必須屬於同一公司；不得循環參照。 |
| REQ_F4118.22.3 | 1.0 | System should allow authorized users to edit or deactivate a department. | Developer | 版本不符回 409；被引用時只可停用。 |

## 追溯與階段

變更記錄：2026-10-10，REQ_F4118.20.2 由 1.0 增至 1.1，明確規定代碼格式與大寫儲存，使服務檢查與資料庫唯一性一致。REQ_F4118.20.1 由 1.0 增至 1.1，明確規定公司列表只顯示授權且啟用的公司與查詢參數。

資料層與三種組織內部應用規則已實作；公司列表須由授權層傳入可見公司 ID，分店／部門內部列表已具分頁、關鍵字、排序及公司隔離的整合測試。REQ_F4118.20–22 的 API、授權、UI 驗收尚未通過，工作單元維持 IN_PROGRESS。`OrganizationService` 未註冊 DI，也沒有公開 API；WU-SYS-004 的公司授權機制完成前不得註冊或開放組織端點。服務內的公司上下文與活躍狀態檢查不能取代用戶授權。
