# ADR-025：IP 白名單判斷與規則快取

- 日期：2026-10-11
- 狀態：採用（WU-SYS-006 後端）

## 背景

MASTER_PROMPT 第 7 節要求全系統、角色、用戶三層 IP 白名單，含 CIDR、有效期限、緊急解鎖、可信代理與拒絕稽核。登入前無身分，TestServer 等環境可能沒有連線 IP。

## 決定

1. `sys_ip_whitelist` 是全系統設定，不隸屬公司；每條規則只屬一個層級。三個篩選唯一索引避免 SQL Server 對 NULL 欄位的索引篩選漏掉系統規則。停用代替刪除，修改帶 `version`。
2. 同層多條規則為 OR；全系統、用戶、用戶擁有的每個啟用角色之間為 AND。登入前只看全系統。有效期限含頭尾，今天由 `IClock` 取得。沒有有效規則即放行；有規則而無連線 IP 則拒絕；loopback 永遠放行。
3. 只讀 `HttpContext.Connection.RemoteIpAddress`；可信反向代理先由 `UseForwardedHeaders` 處理，不能直接讀任意 `X-Forwarded-For`。中介層在驗證與限流後、公司檢查前。健康檢查例外。白名單總開關只由 `appsettings` 控制，Development 預設關閉，管理 API 不提供修改總開關。
4. 啟用規則快取 30 秒；本機修改立即失效，多主機最遲 30 秒看見。拒絕每個 IP／用戶每分鐘最多記錄一次 Warning 與 `IP_DENIED` 稽核，避免日誌被大量請求灌爆。
5. CIDR 輸入必須是網段基址（拒絕 `10.1.2.3/8`），存入正規化文字；IPv4 對映 IPv6 位址比對 IPv4 規則。

## 目前範圍

後端資料表、API、規則判斷與基礎測試已完成。三語管理頁與端到端驗收留在同一工作單元的後續段落；完成前 WU-SYS-006 仍標記 IN_PROGRESS。
