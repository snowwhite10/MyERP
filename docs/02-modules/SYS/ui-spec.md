# SYS 組織介面規格

> 公司／分店／部門列表與表單的本地元件骨架已建立，三語與明暗主題沿用共用設定。授權、API、路由和正式畫面仍待 WU-SYS-004 後接線；目前不在選單或路由公開。

骨架位於 `src/frontend/src/app/modules/sys/organization/`：列表支援代碼／名稱搜尋、穩定代碼排序及本地分頁；分店／部門在未選公司時為空，並依公司 ID 限定輸入資料。表單校驗組織代碼、名稱，輸出大寫代碼；部門可選分店與上級。元件只收資料與發出草稿，不自行呼叫 API，後續接線須改用服務端分頁與授權公司清單。

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.58 | Company View Interface | REQ_F4118.20.1, REQ_F4118.20.2, REQ_F4118.20.3, REQ_F4118.20.4 |
| 8.59 | Branch View Interface | REQ_F4118.21.1, REQ_F4118.21.2, REQ_F4118.21.3 |
| 8.60 | Department View Interface | REQ_F4118.22.1, REQ_F4118.22.2, REQ_F4118.22.3 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.58.1 | Company Code (Input) | 公司代碼 | 唯一識別公司 | VARCHAR(30) | 1–30 字元；ASCII 英文、數字、`-`、`_` | None | Developer | REQ_F4118.20.2 |
| REQ_IO_A8.58.2 | Company Name (Input) | 公司名稱 | 顯示公司 | NVARCHAR(200) | 1–200 字元 | None | Developer | REQ_F4118.20.2 |
| REQ_IO_A8.59.1 | Branch Code (Input) | 分店代碼 | 公司內唯一 | VARCHAR(30) | 1–30 字元；ASCII 英文、數字、`-`、`_` | None | Developer | REQ_F4118.21.2 |
| REQ_IO_A8.60.1 | Department Code (Input) | 部門代碼 | 公司內唯一 | VARCHAR(30) | 1–30 字元；ASCII 英文、數字、`-`、`_` | None | Developer | REQ_F4118.22.2 |
| REQ_IO_A8.60.2 | Parent Department (Dropdown) | 上級部門 | 建立層級 | bigint ID | 同公司啟用部門 | None | Developer | REQ_F4118.22.2 |

## User Identity Interface（WU-SYS-002，待授權後實作）

| Interface No. | Interface Name | Related REQ_F |
|---|---|---|
| 8.61 | User Identity Create Interface | REQ_F4118.23.1, REQ_F4118.23.2 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.61.1 | Username (Input) | 帳號 | 建立登入身分 | ASCII text | 3–100 字元，字母／數字／點／底線／連字號 | None | Developer | REQ_F4118.23.1 |
| REQ_IO_A8.61.2 | Password (Input) | 初始密碼 | 依目前政策驗證，提交後清空 | Password text | 預設 12–128 字元 | None | Developer | REQ_F4118.23.2 |

前端表單不得顯示 `password_hash`。正式路由及操作要等 WU-SYS-004 權限檢查後接入，並提供 zh-Hant、zh-Hans、en 與 dark mode。
