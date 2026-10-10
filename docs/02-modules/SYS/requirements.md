# SYS 組織主檔需求（WU-SYS-001）

> 版本 1.0。公司、分店與部門為後續權限及單據的組織基礎；BlueWhale 公司與部門資料表對應 REQ_DS43.20 / .22。本輪建立資料模型，應用服務及介面待同一工作單元續作。

| Sub-function No. | Sub-function |
|---|---|
| 8.20 | Company Management |
| 8.21 | Branch Management |
| 8.22 | Department Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.20.1 | 1.0 | System should allow authorized users to view company details. | Developer | 僅有組織檢視權的用戶可見其獲授權公司；無權限回 403。 |
| REQ_F4118.20.2 | 1.0 | System should allow authorized users to create a company. | Developer | 代碼全系統唯一；代碼與名稱必填；重複代碼回 409。 |
| REQ_F4118.20.3 | 1.0 | System should allow authorized users to edit a company. | Developer | 版本不符回 409；審計欄位記錄修改者。 |
| REQ_F4118.20.4 | 1.0 | System should allow authorized users to deactivate a company. | Developer | 停用後不能成為新操作的公司上下文；歷史資料保留。 |
| REQ_F4118.21.1 | 1.0 | System should allow authorized users to view branches in the selected company. | Developer | 公司 A 的查詢不返回公司 B 分店；未選公司結果為空。 |
| REQ_F4118.21.2 | 1.0 | System should allow authorized users to create a branch. | Developer | 公司內代碼唯一；所屬公司必須為授權公司。 |
| REQ_F4118.21.3 | 1.0 | System should allow authorized users to edit or deactivate a branch. | Developer | 版本不符回 409；被引用時只可停用，不得刪除。 |
| REQ_F4118.22.1 | 1.0 | System should allow authorized users to view departments in the selected company. | Developer | 公司隔離、軟刪除篩選生效。 |
| REQ_F4118.22.2 | 1.0 | System should allow authorized users to create a department under a branch or parent department. | Developer | 分店與上級部門必須屬於同一公司；不得循環參照。 |
| REQ_F4118.22.3 | 1.0 | System should allow authorized users to edit or deactivate a department. | Developer | 版本不符回 409；被引用時只可停用。 |

## 追溯與階段

目前資料層完成；REQ_F4118.20–22 的 API、授權、UI 驗收尚未通過，工作單元維持 IN_PROGRESS。組織 API 應在 WU-SYS-002～004 的登入／權限完成前只供受保護的內部用例呼叫，不公開無授權的寫入端點。
