# INV 測試計畫

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-BASE-001 | REQ_F4115.21.1 | 模組、Contracts 與權限目錄 | 組合根載入兩個組件，模組代碼 INV，所有權限只屬 INV | 通過（架構測試） |
| INV-BASE-002 | REQ_F4115.21.1, REQ_F4115.21.2 | 跨公司及非法單據狀態轉換 | 跨公司、重複核准拒絕；合法草稿送審及核准成功 | 通過（架構測試） |
| INV-BASE-003 | REQ_F4115.21.1 | 首張資料表的 SQL 公司隔離 | SQL 查詢／寫入與外鍵隔離 | 待 WU-INV-003；目前無資料表 |
