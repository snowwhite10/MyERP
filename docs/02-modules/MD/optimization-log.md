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
| 2026-10-12 | 供應商主檔若照抄客戶，服務、EF 設定與前端頁面會有三份近乎相同的代碼 | 客戶與供應商只差資料表、錯誤碼前綴、分類型別與文字 | 後端抽出 `BusinessPartner<TCategory>`、`BusinessPartnerService<TPartner,TCategory>`、`BusinessPartnerModel`；前端抽出 `BusinessPartnerService` 與 `PartnerList`（`PARTNER_PAGE` 提供 API、權限、i18n 與 testid 前綴），`customerErrorKey` 改為各服務的 `errorKey` | 客戶 migration 無差異（`MasterData_Supplier` 只新增 `md_supplier`），客戶原有後端 5 個、前端 4 個、E2E 2 個案例不改斷言全部通過 |
| 2026-10-12 | 供應商分類刪除改查 `md_supplier` 後，舊的分類整合測試只註冊分類模型 | 分類引用檢查需要供應商實體在 EF 模型中 | `SupplierCategoryTests` 的 DbContext 加入 `SupplierDbModel`（與客戶分類測試做法一致） | 供應商分類 4 個整合測試維持通過 |
| 2026-10-12 | 供應商 E2E 首次執行因重複代碼 409 的瀏覽器 console 訊息失敗 | 自動 fixture 把任何 console error 視為失敗，預期中的 409 也被計入 | 該測試以 `pageErrors.allow(/status of 409/)` 明確允許（與審批頁 E2E 一致） | 完整 E2E 75 通過、3 略過 |
| 2026-10-12 | 計量單位若另寫一套 CRUD，代碼、名稱、軟刪除、版本規則會與分類重複 | 單位只比分類多一個小數位數欄位 | `UnitOfMeasure` 繼承 `MasterCategory`；`MasterCategoryService` 新增受保護的 `CreateCoreAsync`／`UpdateCoreAsync`（可設定子類欄位），`UomService` 只加小數位數驗證及 IN_USE 檢查 | 客戶／供應商分類整合測試不改即通過，分類 migration 無差異 |
| 2026-10-12 | 換算係數原定 decimal(18,6)，前端 JS number 只保證 15 位有效數字，最大值往返會失真 | JSON 數字在瀏覽器內是雙精度浮點 | 係數改為 decimal(15,6)（9 位整數 + 6 位小數），試算數量以原文字放入查詢字串，數量運算全在後端（ADR-034） | 前端測試驗證 `999999999.999999` 序列化不變 |
| 2026-10-12 | 只靠服務檢查路徑時，並行新增 A→B 與 B→A 可能都成功，形成矛盾換算 | 兩個交易各自讀到沒有路徑 | 新增換算在交易內取公司級 `sp_getapplock` 再檢查路徑 | 兩個 DbContext 並行新增的整合測試只有一個成功 |
| 2026-10-12 | 物料分類 GET 一次回傳全部節點，若頁面照抄分頁列表，子分類可能與上層分在不同頁 | 樹需要完整節點才能排序與縮排 | 前端 `flattenCategoryTree` 一次組樹，搜尋改為前端過濾並保留命中節點的上層；不另加後端分頁 API | Vitest 組樹／過濾案例與 Playwright 搜尋案例通過 |
| 2026-10-12 | 上層下拉若列出全部分類，用戶常選到會被後端拒絕的節點（自己、子孫、停用、超過 5 層） | 錯誤要送出後才看到 | `parentOptions` 以子樹 Id 與子樹高度先排除，後端檢查不變，仍以錯誤碼兜底 | `MD-ICAT-004` 上層選項案例通過 |
| 2026-10-12 | 物料若只檢查參照 Id，跨公司或停用單位／品牌／分類可能被寫入 | FK 僅保護公司一致，不表達啟用狀態 | 服務先驗證目前公司及新選參照啟用，資料庫另以三條 `(company_id, id)` 複合外鍵保護 | `MD-ITEM-001`／`002` 驗證公司隔離、停用參照與刪除引用；單位／品牌／分類有引用時回 409 |
| 2026-10-12 | 物料表單若只抓參照 API 第一頁，超過 200 筆的單位或品牌無法選取，既有物料的名稱也會空白 | 主檔列表 API 有每頁最多 200 筆限制 | 以 `allPages` 依總筆數載齊參照，並在公司切換時取消舊請求；補跨頁測試 | 參照選單覆蓋全部公司資料，仍只在有對應 VIEW 權限時請求 |
| 2026-10-12 | 重構換算時把 `FindRatio` 移出 try，分子分母累乘溢位會變成 500 而非 400 `quantity` | 原本累乘與相除都在同一個 try 內 | `Apply` 改收比率委派，在 try 內計算比率與相除，公司級與物料換算共用 | 公司級換算行為不變，`UomTests` 5/5 通過 |
| 2026-10-12 | 物料換算若可在基本單位改變後保留，係數會指向錯的單位；並行時檢查與修改可能交錯 | 係數的意義依附在基本單位上 | 有專屬換算時改基本單位回 409 `MD.ITEM.BASE_UOM_LOCKED`，新增換算與改基本單位共用公司級 applock；基本單位不變的修改不取鎖 | `MD-ITEM-CONV-002` 驗證鎖定與刪除後可改 |
| 2026-10-12 | 物料需求 ID 誤用 `REQ_F4118.12.x`（BlueWhale 為 Income & Expense Category），日後建立收支分類時會撞號 | WU-MD-012a 未對照 4.1.1.8.3 Product Management | 改為 `REQ_F4118.3.1–3`，物料換算追加 `REQ_F4118.3.5`，同步 requirements／data-structure／api／ui-spec／test-plan／reference-notes | docgen 追溯檢查 0 Error |
| 2026-10-12 | 代碼／名稱驗證在 `MasterCategoryService`、`BusinessPartnerService`、`ItemService` 各有一份相同實作，倉庫與儲位會再多兩份 | 每個主檔服務各自複製驗證 | 抽出 `MasterDataInput`（`Code`／`Name`／`OptionalText`），MD 五個服務共用；分類基底加 `FilterByKeyword` 讓倉庫可多搜地址 | 規則只有一處；錯誤欄位與訊息不變，後端全量通過 |
| 2026-10-12 | 新增儲位與刪除倉庫若各自檢查，並行時儲位可能掛到剛刪除的倉庫 | 「倉庫存在且啟用」與「倉庫沒有儲位」是兩個交易內的讀取 | 兩者都在公司級 applock `md_warehouse` 內檢查（ADR-036），複合外鍵再保證同公司 | `MD-WH-003` 驗證刪除規則 |
| 2026-10-12 | 設定新本位幣時要先取消舊本位幣（篩選唯一索引），若之後名稱或代碼重複才失敗，交易雖回滾但 DbContext 仍記著舊本位幣已取消 | 取消舊本位幣需先 `SaveChanges`，之後的檢查失敗只回滾資料庫 | 幣別新增在取消前先驗證名稱、符號、小數位與重複代碼；修改先檢查版本、`BASE_REQUIRED`、`BASE_INACTIVE` 再取消 | `MD-CUR-002` 驗證本位幣切換與鎖定 |
| 2026-10-12 | 匯率重疊若在記憶體逐筆比較，每日匯率一年就有數百筆 | 只需要知道有沒有重疊 | 以單一 `AnyAsync`（其他.生效日 ≤ 本.到期日 且 其他.到期日 ≥ 本.生效日）在資料庫判斷；篩選唯一索引不能支援外鍵檢查，另建 `ix_md_exchange_rate_company_id_currency_id` | 四種重疊情況與期間空檔查詢皆由 `MD-CUR-003` 覆蓋 |
| 2026-10-12 | 含稅金額若獨立捨入稅額，淨額加稅額可能與原含稅金額差一個最小貨幣單位 | 三個金額各自捨入會累積尾差 | `Tax.Calculate` 先捨入含稅金額與反推未稅金額，再以差額得稅額；未稅路徑先捨入淨額，再計稅額 | `TaxTests` 驗證含稅／未稅、負數、精度覆寫與 `Net + Tax = Gross` |
