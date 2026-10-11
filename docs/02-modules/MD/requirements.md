# MD 主檔需求

## WU-MD-001 共用入口

| Sub-function No. | Sub-function |
|---|---|
| 8.34 | Master Data Portal |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.34.1 | 1.0 | System should allow authorized users to view the master data portal. | Developer | `/md` 僅向已登入且在目前公司有 `MD.MASTER.VIEW` 的用戶開放；無權限導向 403；三語標題與說明一致。 |

後續客戶、供應商與物料等主檔沿用 BlueWhale 的功能編號，各工作單元再加入驗收條件。MD 主檔均以目前公司為隔離邊界，跨模組只透過 Contracts 或 Application 介面存取。

## WU-MD-002 客戶分類（Client Category）

| Sub-function No. | Sub-function |
|---|---|
| 8.8 | Client Category |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.8.1 | 1.0 | System should enable user to view records of client categories. | Developer | 具 `MD.CUSTOMER_CATEGORY.VIEW` 可依目前公司分頁、搜尋代碼／名稱、排序；已軟刪除及別公司資料不出現；可看停用分類。 |
| REQ_F4118.8.2 | 1.0 | System should enable user to create records of client categories. | Developer | 具 CREATE 權限可建立公司內唯一的代碼（1–30 英數、`-`、`_`，轉大寫）及非空白名稱（最多 200 字）；重複代碼回 409，軟刪除資料的代碼也不可重用。 |
| REQ_F4118.8.3 | 1.0 | System should enable user to edit records of client categories. | Developer | 具 EDIT 權限可以版本號修改名稱及啟用狀態；代碼不可修改，版本衝突回 409，別公司或已刪除資料回 404。 |
| REQ_F4118.8.4 | 1.0 | System should enable user to delete records of client categories. | Developer | 具 DELETE 權限可用版本號軟刪除未被引用的分類；再次刪除或別公司資料回 404；已有客戶引用後僅能停用。 |
