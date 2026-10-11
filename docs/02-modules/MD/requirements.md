# MD 主檔需求

## WU-MD-001 共用入口

| Sub-function No. | Sub-function |
|---|---|
| 8.34 | Master Data Portal |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.34.1 | 1.0 | System should allow authorized users to view the master data portal. | Developer | `/md` 僅向已登入且在目前公司有 `MD.MASTER.VIEW` 的用戶開放；無權限導向 403；三語標題與說明一致。 |

後續客戶、供應商與物料等主檔沿用 BlueWhale 的功能編號，各工作單元再加入驗收條件。MD 主檔均以目前公司為隔離邊界，跨模組只透過 Contracts 或 Application 介面存取。
