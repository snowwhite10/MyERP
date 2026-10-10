# SYS 組織測試計畫

| Test Case ID | Related REQ_F | Description | Status |
|---|---|---|---|
| SYS-ORG-001 | REQ_F4118.20.2, REQ_F4118.21.2, REQ_F4118.22.2 | Migration 建立三張表、索引與 JSON 約束 | 部分通過（表與模型） |
| SYS-ORG-002 | REQ_F4118.21.1, REQ_F4118.22.1 | 不同公司資料與軟刪除篩選 | 待測 |
| SYS-ORG-003 | REQ_F4118.22.2 | 跨公司分店／上級部門與循環參照拒絕 | 待測 |
| SYS-ORG-004 | REQ_F4118.20.1, REQ_F4118.21.1, REQ_F4118.22.1 | 無權限返回 403 | 待測 |
| SYS-ORG-005 | REQ_F4118.20.3, REQ_F4118.21.3, REQ_F4118.22.3 | 並發版本衝突返回 409 | 待測 |
| SYS-ORG-006 | REQ_F4118.20.4 | 停用公司後不得成為新操作上下文，歷史資料保留 | 待測 |
