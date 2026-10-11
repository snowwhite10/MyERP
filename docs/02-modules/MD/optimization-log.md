# MD 優化記錄

| 日期 | 問題 | 原因 | 改進 | 效果 |
|---|---|---|---|---|
| 2026-10-11 | 模組骨架若預先建立空資料 API 會形成無意義的公開端點 | WU-MD-001 尚未有主檔實體 | 只註冊權限與前端入口；CRUD 隨資料模型加入 | API 與權限可逐功能對應，減少空端點 |
| 2026-10-12 | 前端完整測試在多 worker 下因 VM 記憶體耗盡而無法完成 | Vitest 預設並行數過高，與 SQL Server 和其他常駐程序爭用記憶體 | 將 `maxWorkers` 設為 2，維持原測試案例 | 前端 432/432 通過；E2E 67 通過、3 略過 |
| 2026-10-12 | 客戶分類頁首次 E2E 有瀏覽器 404 與圖示錯誤 | 側欄用了尚未註冊的 `database` 圖示 | 將 `DatabaseOutline` 加入集中圖示清單 | 客戶分類 E2E 2/2，完整 E2E 69 通過、3 略過，無 console error |
| 2026-10-12 | 客戶分類資料表的追溯被 docgen 視為未知需求 | Markdown 以 `REQ_F4118.8.1–4` 縮寫多個 ID | 改為逐一列出四個 ID，並採用 docgen 可解析的介面及測試欄名 | docgen 一致性檢查 0 Error；保留 45 個未完成模組相關 Warning |
| 2026-10-12 | 客戶頁只有服務層測試，表單驗證、分類權限、重複送出與錯誤顯示未受保護 | 上一輪中斷前只完成 API 服務 spec | 新增 `customer-list.spec.ts`（3 個案例）：無效代碼／空白名稱不送出、無 `MD.CUSTOMER_CATEGORY.VIEW` 不查分類、連按儲存只送一次、409 保留對話框並顯示錯誤、搜尋回第 1 頁 | 前端 437/437；客戶頁邏輯修改有回歸保護 |
| 2026-10-12 | 分類參照完整性只靠服務檢查時，競態或直接寫入可指向別公司分類 | 單欄 FK 無法表達「同公司」 | 分類加備用鍵 `(company_id, id)`，客戶以 `(company_id, category_id)` 複合 FK 參照 | 整合測試證實跨公司寫入在資料庫層即被拒絕 |
| 2026-10-12 | 供應商分類若照抄客戶分類，服務、EF 設定與前端頁面會重複約 400 行，之後每改一條分類規則要改兩處 | 兩種分類欄位、規則、API 形狀完全相同，只差資料表、錯誤碼前綴與刪除前引用檢查 | 後端抽出 `MasterCategory` 基底、`MasterCategoryModel.Configure<T>` 與 `MasterCategoryService<T>`（引用檢查用可覆寫的 `EnsureNotInUseAsync`）；前端抽出 `MasterCategoryService` 與共用 `CategoryList`，路由以 `CATEGORY_PAGE` 提供 API／權限／i18n 前綴。客戶分類 schema 無變更（migration 只新增 `md_supplier_category`） | 客戶分類 5 個整合測試與 E2E 不改即通過；新增供應商分類只需約 20 行設定 |
| 2026-10-12 | 新增的供應商分類頁元件測試顯示原始 i18n key | `testing/i18n-fixtures.ts` 沒有登記 `md` 語言檔，md 三語 key 一致性也因此未受 `i18n-keys.spec` 檢查 | 三種語言都登記 `md.json` | md 文字可在元件測試中驗證；三語 key 一致性檢查涵蓋 md |
