# 風險清單

| ID | 風險 | 可能性 | 影響 | 應對 | 負責 | 狀態 |
|---|---|---|---|---|---|---|
| R-001 | AI 額度在長時間任務中途用完，留下半完成代碼 | 高 | 中 | 每輪只做一個小工作單元；Runner 偵測未提交變更並要求下一輪先處理 | AI / Runner | 開放 |
| R-002 | Claude 與 Codex 交替執行時理解不一致 | 中 | 中 | 狀態只放 state.json 與文件；nextAction 必須具體；入口檔內容一致 | AI | 開放 |
| R-003 | 上下文過長令 AI 忽略規則 | 中 | 高 | 每輪重讀 MASTER_PROMPT；模組文件分檔；完成模組後不重讀其全部文件 | AI | 開放 |
| R-004 | 參考文件或機密被提交到公開 repo | 低 | 高 | .gitignore + Init 腳本檢查；每次 merge 前自我審查 diff | AI / 人手 | 開放 |
| R-005 | 規模過大，模組開發時間遠超預期 | 高 | 中 | 嚴格按 Phase 次序；BlueWhale 基線優先，擴充功能放後 | AI | 開放 |
| R-006 | 測試數量增長令每輪「全部測試」耗時過長 | 中 | 中 | 測試分層、並行執行；整合測試共用資料庫重建快照 | AI | 開放 |
| R-007 | 以 bypass 權限執行 AI 代理，可能誤刪 VM 檔案 | 低 | 高 | VM 只用於本項目；定期快照；不在 VM 存放個人憑證 | 人手 | 開放 |
| R-008 | 照抄 BlueWhale 不完整的需求 ID（漏模組編號，如 `REQ_F411.1.x`、`REQ_F411.19.x`）導致 ID 重號或追溯斷裂 | 中 | 高 | ADR-002 規定一律正規化為 `REQ_F411{模組}.{子功能}.{序號}`；docgen 一致性檢查偵測重複 / 不合格式 ID | AI | 開放 |
| R-009 | SRS 4.4 / 4.5 與 SDS 4.2 介面次序、名稱不一致（BlueWhale 原文 Sales Order / Outbound 次序不同、Supplier Contact 等名稱不同）被沿用 | 中 | 中 | 統一 View → Create → Edit、以 4.4 名稱為準（reference-notes 4、ADR-002）；三處清單由同一來源產生並逐項比對 | AI | 開放 |
| R-010 | BlueWhale 資料表描述錯誤（Account 實為資金帳戶、PayType 描述為工序、PurOrder / PageList 同名、Brand 重複）導致錯誤建模 | 中 | 中 | 以業務含義而非原文表名建模；對照表已更正（reference-notes 4、7）；建表時在 data-structure.md 註明 | AI | 開放 |
| R-011 | SRS 4.3（88 表）與 SDS 4.1（90 表）表清單不一致被帶入 MyERP | 中 | 中 | 兩處均由 docgen 從實際資料庫 `INFORMATION_SCHEMA` 產生；一致性檢查比對 | AI | 開放 |
| R-012 | SRS / SDS 版面偏離 BlueWhale（字型、標題級數、手打編號、表格欄寬與框線、圖說格式），或殘留追蹤修訂 / 空白標題 | 中 | 高 | reference-notes 2.3、3 已記錄實測數值；docgen（WU-P0-013）以參數化樣式產生，並自動檢查無追蹤修訂與空白標題；每模組完成即重產並比對標題結構 | AI | 開放 |
| R-013 | BlueWhale 原文 NDA、訪談、客戶名稱或金額被抄入公開 repo | 低 | 高 | reference-notes 只寫結構；SRS / SDS 用 `<Client>` 等佔位符；merge 前檢查 diff | AI | 開放 |
| R-014 | VM 缺少 Python，docx / xlsx 解析與產生工具受限 | 高 | 低 | ADR-001 決定 docgen 用 .NET 10 + DocumentFormat.OpenXml 3.5.1；Node docx / exceljs 為備選 | AI | 已處理（ADR-001） |
| R-015 | Angular 最新版與 ng-zorro-antd 尚未正式支援的版本組合，或 .NET 10 套件（EFCore.NamingConventions 等）未有對應版本 | 中 | 中 | 已查證：Angular 22.2 + ng-zorro-antd 22.1.1、EF Core 10.0.12 + EFCore.NamingConventions 10.0.1 相容，固定於 ADR-001；升級須另寫 ADR 並通過全部測試 | AI | 已處理（ADR-001） |
| R-016 | 本機 Node 22.16.0 不符合 Angular 22 `engines`（`^22.22.3 \|\| ^24.15.0 \|\| >=26`），`nvm use` 可能需要系統管理員權限 | 高 | 中 | WU-P0-010 先以 nvm-windows 安裝 Node 24.21.0；無法切換時退回 Angular 21.2 + ng-zorro-antd 21.2（ADR-001 決定 2.4）並另寫 ADR | AI / 人手 | 開放 |
| R-017 | 套件授權變更（FluentAssertions 8.x 起商業用途需付費授權）帶來授權風險 | 中 | 中 | ADR-001 固定 FluentAssertions 7.2.2（Apache-2.0）；升級任何套件前檢查 nuspec / package.json 授權 | AI | 開放 |
