# MyERP 文件產生器

`MyERP.DocGen` 依 `MASTER_PROMPT.md` 第 15 節，從 Markdown 單一來源建立 SRS、SDS、需求試算表、HTML 介面原型和一致性報告。實作使用 .NET 10 與 OpenXML；不需 Python。

## 執行

在 repo 根目錄執行：

```powershell
dotnet build src/backend/MyERP.sln -c Debug
dotnet tools/docgen/MyERP.DocGen/bin/Debug/net10.0/MyERP.DocGen.dll --root .
```

若正式 DOCX 正在 Word 中開啟，可將同一批來源產出到獨立目錄，仍執行完整一致性檢查：

```powershell
dotnet tools/docgen/MyERP.DocGen/bin/Debug/net10.0/MyERP.DocGen.dll --root . --output .tools/docgen-isolated
```

`--output` 可用絕對或相對路徑；相對路徑從 repo 根目錄計算。所有產出都放進指定目錄，不改正式 `docs/99-output/`。隔離目錄 `.tools/` 已被 Git 忽略。檢查有 Error 時仍會建立報告，並以非零退出碼表示未通過。

加入 `--connection '<SQL Server 連線字串>'` 後，SDS 4.1 欄位表會直接從資料庫 `INFORMATION_SCHEMA` 產生，一致性檢查也會比對 SRS 4.3 與實際資料表。不提供連線時仍產出文件空殼，但報告會有 `SCHEMA_NOT_CHECKED` 警告。SQL Server 連線錯誤或任何一致性 `Error` 會使程式以非零代碼結束；`Warning` 不阻擋骨架產出。不要把含機密的連線字串寫入 repo。

輸出在 `docs/99-output/`：`MyERP_SRS.docx`、`MyERP_SDS.docx`、`MyERP_Requirements.xlsx`、`consistency-report.md`、`prototypes/index.html` 及各介面的 HTML 原型。這些檔案由程式重建，不要手動修改。Word 封面保留人員與合約佔位符；敘述來源尚未建立時會在章節內標 `[Pending]`，並於報告列出缺少的檔案。

## SRS 4.5 HTML 原型（WU-P0-033 首個切片）

`prototypes/index.html` 和 `4.5.1.<模組>.<介面>.html` 完全依 SRS 4.4 的 `OrderedInterfaces` 產生；每頁的規格項目按 `REQ_IO_A` 順序排列。檔名只用固定編號，來源文字經 HTML 轉義。無項目的介面保留明確空狀態；重新產出時清理已移除介面的舊 HTML。這是靜態線框與次序契約，SRS 4.5 的截圖與 Word 嵌圖仍待下一切片。

| 類別 | 情境與驗證 | 層級 |
|---|---|---|
| 正常 | 亂序輸入時仍按 SRS 4.4 的模組／介面編號產生索引與頁面；頁內項目按編號排列 | U、隔離產出 |
| 邊界 | 介面沒有項目時顯示明確說明；標題與項目含 HTML 字元時安全轉義 | U |
| 重複與衝突 | 重複介面編號由既有 `DUPLICATE_ID` 一致性檢查報錯；重新產出時舊頁面清除 | U、既有檢查 |
| 部分結果 | 個別介面沒有項目時其餘介面仍產生；資料批次變更不適用 | U |
| 權限、狀態、關聯 | 離線唯讀文件，沒有登入、公司權限、單據狀態或資料關聯 | 不適用 |
| 錯誤、檔案 | 輸出目錄無法寫入時 DocGen 以非零碼失敗；來源缺項的畫面明示缺項，不假稱有實際 UI | U、執行時檢查 |

## Markdown 來源

- 模組資料：`docs/02-modules/<module>/requirements.md`、`data-structure.md`、`ui-spec.md`、`test-plan.md`。解析器依表頭辨認資料，額外欄位可保留。
- 敘述章節：`docs/05-doc-sources/srs/` 與 `sds/`。預期檔名由產出後的 `MISSING_SOURCE` 清單指出。這些檔案的 Markdown 標題當作普通段落，Word 章節編號由產生器控制。
- 非功能指標：`docs/01-architecture/architecture.md` 或 `docs/05-doc-sources/srs/ch4-2-*.md` 中含 `REQ_NF` 的表。
- BlueWhale 對照：`docs/00-management/reference-notes.md` 第 5–7 節；檔案只保留公開功能名稱與結構。

主要表頭：

| 用途 | 必要表頭 | 常用附加欄位 |
|---|---|---|
| 子功能 | `Sub-function No.`、`Sub-function` | |
| 功能需求 | `Requirement ID`、`Description` | `Version`、`Author`、`Acceptance Criteria` |
| 資料結構 | `Requirement ID`、`Table Name` | `Description`、`Related REQ_F` |
| 介面 | `Interface No.`、`Interface` | `Related REQ_F` |
| 介面項目 | `Requirement ID`、`Item` | `Description`、`Purpose`、`Format`、`Valid Range`、`Related I/O`、`Related REQ_F` |
| 測試案例 | `Test Case ID`、`Related REQ_F` | `Description`、`Status` |

所有 ID 使用 `MASTER_PROMPT.md` 第 15.2 節規則。正式來源逐步加入時，追溯矩陣會連結需求、資料表、介面、測試；一致性報告檢查重複 ID、未定義引用、介面次序、資料表差異及 BlueWhale 對照進度。

測試：`dotnet test --project tests/backend.unit/MyERP.DocGen.Tests/MyERP.DocGen.Tests.csproj`。

## Mermaid 圖

敘述來源的 `mermaid` 程式碼區塊會轉成 PNG 內嵌至 SRS／SDS；圖後一段以 `Figure ` 開頭的文字會套用圖說樣式。產圖使用 `tests/e2e/package-lock.json` 固定的 Mermaid CLI、repo `.nvmrc` 指定的可攜 Node，以及 `.tools/ms-playwright` 的 Chromium。首次使用先執行：

```powershell
& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npm ci
& ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright install chromium
```

圖語法錯誤、空圖或渲染逾時使 DocGen 回非零碼；不會把佔位文字當成成功。圖形來源和圖說均保留在 `docs/05-doc-sources/`，可在隔離輸出目錄重新生成。SRS 4.6.2 的依賴圖只顯示里程碑順序；正式甘特圖仍需核准日期與工期。

SDS 4.1 的 ER 圖來自 `docs/02-modules/<module>/data-structure.md` 的 `mermaid`／`erDiagram` 程式碼區塊，按 SRS 固定模組順序、各檔內來源順序編為 Figure 4.1.1 起的連續圖說。流程圖不納入 ER 圖；即使未提供 SQL 連線，仍可產生 ER 圖，但欄位表與 schema 一致性結論必須等實際資料庫連線。

WU-P0-032 測試情境（`DocumentGenerationTests.SdsDatabaseDesign_EmbedsErDiagramsInModuleAndSourceOrderWithoutSchema`）：

| 類別 | 情境與驗證 | 層級 |
|---|---|---|
| 正常 | 兩模組多張 ER 圖內嵌成 PNG，圖說依模組與來源順序連號 | U、隔離產出 |
| 邊界 | 未連 SQL 時仍有 ER 圖，欄位表保留未讀 schema 說明；無 ER 來源時顯示明確佔位 | U |
| 重複與衝突 | 同模組多圖不覆蓋、不同模組同序號不衝突；資料庫併發修改不適用於文件產生 | U |
| 部分結果 | 缺少某模組 ER 圖時其餘圖照來源順序產生；批次資料變更不適用 | U |
| 權限、狀態、關聯 | DocGen 是離線文件產生器，沒有用戶／公司授權或業務單據狀態；來源關聯由既有一致性檢查負責 | 不適用 |
| 錯誤、檔案 | Mermaid 渲染錯誤應使產生失敗，不得把圖說當成成功；非 ER Mermaid 區塊不納入 | U、隔離產出 |
