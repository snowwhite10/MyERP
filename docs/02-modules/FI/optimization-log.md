# FI optimization log

## WU-FI-001b1 limit write review — 2026-11-04

- 問題：更新／停用需要客戶編號才能取鎖，但在鎖外讀到的列可能已被他人修改。原因：鎖鍵依客戶而定。修改：鎖外只讀不可變的 `customer_id`（不追蹤），取鎖後在同一交易重讀整列並檢查版本與狀態；效果：舊版本一律 409，不會以鎖外資料寫入。
- 問題：MD 客戶與本位幣檢查若放在交易內，會延長持有信用鍵的時間並阻擋 SD 信用判斷。修改：兩項唯讀檢查在取鎖前完成，交易內只做重疊、版本與寫入；效果：鎖持有時間最短，失敗時亦不會留下新信用鍵（測試已驗證 0 筆）。
- 問題：重疊查詢若包含停用列，停用歷史會永久阻擋同期新額度；若允許重新啟用，則舊列可能悄悄恢復生效。修改：只比對 Active 列，不提供重新啟用（ADR-074）。

## WU-FI-001a implementation review — 2026-11-04T09:21:58+08:00

- 問題：首版 `AcquireCreditLockAsync` 依設計以 `UPDLOCK, HOLDLOCK` 讀取尚不存在的信用鍵。原因：HOLDLOCK 在唯一索引的空隙取 key-range 鎖，第一筆未提交的鍵（客戶 501）令同公司另一客戶 502 等待至 30 秒命令逾時（真實雙連線測試紅燈）。修改：pair applock 已序列化同一對的查找／新增，改為 `UPDLOCK, ROWLOCK` 並以 `SCOPE_IDENTITY()` 取回新鍵（ADR-073）。效果：不同客戶互不阻塞，FI-CREDIT-021 通過，整組 10/10 由約 50 秒降至約 21 秒。
- 問題：初稿以 `SET LOCK_TIMEOUT` 變數設定逾時並在批次尾重設為 -1。原因：會覆寫呼叫端連線的工作階段設定。修改：移除，只用 `sp_getapplock @LockTimeout`；仍把 SQL 1222 對應為 `FI.CREDIT.LOCK_TIMEOUT`。
- 問題：未平衡應收加總可能超過 decimal(19,4) 令讀取溢位。修改：SQL 端超出上限時回傳 -1 哨兵值，服務轉為 `ExposureUnavailable`，不拋未預期例外、不當作零。
- 問題：子表原計劃以代理鍵 `credit_key_id` 指向信用鍵，無法由 SQL 保證子列的公司／客戶與鍵一致。修改：以複合替代鍵 `(company_id, customer_id)` 作 FK，既有查詢索引同時覆蓋 FK，免多一個索引。

## WU-FI-001a first-key concurrency review — 2026-10-29T16:06:35+08:00

Review found that relying on the permanent row's unique index alone leaves a first-use race: the losing insert can raise a duplicate-key error after waiting, and its transaction may no longer be usable for the required locked exposure read. The design now acquires a transaction-owned SQL application lock scoped to the exact company/customer pair before row creation or lookup, then retains the row lock until caller commit or rollback. FI-CREDIT-021 explicitly covers the waiting, rollback and independent-pair paths. No production change is claimed; the real SQL red test remains gated by the 2.5 GB free-memory threshold.

## WU-FI-001a schema naming review — 2026-10-29T15:57:57+08:00

Review found that the provisional `fi_credit_key` and `fi_credit_limit` table names violate the database prefix map, which assigns receivables to `ar_`. Changed the schema assertion and planned data structure to `ar_credit_key` and `ar_credit_limit`, preserving the existing AR receivable name. This prevents the migration from cementing a nonstandard prefix. No migration exists yet. A serial integration build was stopped when free physical memory fell to about 0.40 GB; therefore compilation of the renamed SQL test and its red result remain unverified. The existing credit contract tests passed 3/3 from the prior build.

## WU-FI-001a schema test checkpoint — 2026-10-29T13:22:28+08:00

Review found that the existing value tests omitted the exact SQL decimal ceiling, invalid version and unknown unavailable reason. Added these boundaries; the credit contract suite passes 3/3. The new real SQL schema assertion fixes the minimum permanent key, limit and posted AR tables, unique/lookup indexes and date/amount checks before implementation. Integration project build passes with zero warnings. SQL execution remains gated by available physical memory; the test has not yet demonstrated a red result, and no migration or service has been claimed.

## WU-FI-001a contract checkpoint

Review found that a nullable limit alone could let an SD caller treat missing FI data as zero. The contract uses an explicit availability reason and only creates an available snapshot after checking company/customer identity, ISO currency shape, `decimal(19,4)` range and nonnegative AR. The interface carries the caller's `DbTransaction`, making the lock and read transaction boundary visible. No FI provider is registered, so SD cannot use a placeholder result. A normal test build expanded compiler processes and reduced free memory below 0.2 GB; serial isolated build plus two targeted tests passed. SQL red tests, migration, transaction lock and AR persistence remain required before WU-FI-001a can close.

## WU-FI-000d design review

The existing architecture text permitted deferred FI posting after SD/PUR document completion, while current SD/PUR acceptance requires atomic FI effects. ADR-071 records the transaction rule for financial source posting. No production code was changed in this design unit. The planned contract separates FI open AR from SD unbilled commitments and requires a database credit-key lock, avoiding double counting and process-local race conditions. Real SQL tests remain necessary before this is considered working credit control.

## WU-FI-002d design review

Review found three risks in the initial source concept: trusting an SD-supplied snapshot without checking its saved issuing state, crediting an invoice already fully settled into negative AR, and treating an unknown timeout as permission to post a new FI source. BR-FI-007 requires an SD-owned saved-source verification contract; BR-FI-012 rejects credit below zero until a refund/unapplied-credit flow is designed; BR-FI-008 requires source-key lookup and immutable payload comparison. FI source uniqueness includes soft-deleted history, and FI returns `changed=false` for identical replay. No production code or executable financial test was added. DocGen unit tests passed 7/7; direct artifact generation was blocked by another process holding `MyERP_SRS.docx` open, so generated-document consistency was not claimed. Real SQL and authorised HTTP remain required for 002a/b/c.

## WU-FI-003d design review

The initial proposal risked deriving AP directly from caller totals, posting a stock purchase to an unspecified account and retrying an unknown timeout as a fresh source. BR-FI-015/017 require FI to verify persisted PUR source and saved components; BR-FI-018 rejects missing account policy instead of assuming receipt accrual or changing INV valuation; BR-FI-016 resolves committed source by permanent key. BR-FI-020 fixes lock order and transaction scope. No production code or payable product test exists in this design slice; real SQL and authorised HTTP are mandatory for WU-FI-003a/b/c.

## WU-FI-004d design review

The first GL model could have accepted arbitrary caller-provided lines, selected a period from processing time or treated an unbalanced base journal as a rounding difference. BR-FI-023–027 instead require a saved source date, FI-owned mapping and MD postable-account validation, plus an exact checked base balance. BR-FI-028–029 require permanent source uniqueness and same-transaction rollback with the upstream state. The design separates a reusable internal posting service from public read and period APIs, so an HTTP client cannot create a journal directly. No production code or executable GL test was added; WU-FI-004a/b/c must prove the invariants against real SQL, HTTP and UI before any Posted claim.

## WU-FI-005d design review

Review found that a receipt could be mistaken for settled AR, allowing SD credit to reopen before an invoice is actually paid. BR-FI-034/038 separate unapplied cash from committed receipt allocations and expose only the latter in FI open AR. The initial row-key idea also risked forbidding a legitimate second partial allocation of the same receipt to the same invoice; uniqueness is scoped to a settlement and receivable. BR-FI-036 fixes credit, receipt, receivable, settlement and GL lock order to guard two-connection over-allocation. No product code, migration, UI or executable financial test was added; FI-AR-COLL-001–028 are planned acceptance cases for 005a/b/c.

## WU-FI-006d design review

Review found that an approved payment could incorrectly clear AP before any payable was allocated. BR-FI-043 makes the payment an unapplied clearing balance and reduces open AP only for committed settlements. A per-payment lock alone would permit two payments to over-allocate one payable, so BR-FI-045 uses a common supplier lock before payment and payable keys, with reread under the SQL transaction. Separate partial settlements of the same payment and payable remain valid; row uniqueness is per settlement. BR-FI-047 resolves unknown timeout through a committed source lookup and never treats an in-flight row as success. The FI-owned payable ID prevents trusting a client-supplied PUR invoice number. No production code, migration, UI or executable financial test was added; FI-AP-PAY-001–028 are planned acceptance cases for 006a/b/c.

## WU-FI-007d design review

The first other-receipt concept could have treated a customer-named receipt as AR payment, or posted an unidentified advance directly to income. BR-FI-051/055 require a non-invoice earned-income category and keep AR, SD commitment and unapplied-sales-cash unchanged; an advance needs a separate liability contract. BR-FI-052/053 require FI-owned account mapping, MD validation and one saved-source GL transaction, avoiding client-selected accounts and partial SYS approval. BR-FI-054 preserves source/reference uniqueness through soft deletion and resolves unknown timeouts by committed lookup. No product code, migration, UI or executable financial test was added; FI-AR-OTHER-001–020 are planned acceptance cases for 007a/b/c.

## WU-FI-008d design review

The first other-payment concept could have treated any supplier transfer as settled AP or expensed a supplier advance/prepayment immediately. BR-FI-059/063 constrain it to an incurred non-invoice expense, leaving open AP and PUR unchanged; advances and prepaid assets require separate contracts. BR-FI-060 rejects AP control, payment clearing and prepaid-asset mappings. BR-FI-061/062 fix same-transaction posting, permanent source/reference uniqueness and committed-source timeout recovery. The reference key is scoped to withdrawal direction so a deposit does not collide accidentally; 008a must prove its exact SQL constraint alongside the existing purchase-payment rule. No product code, migration, UI or executable financial test was added; FI-AP-OTHER-001–020 are planned acceptance cases for 008a/b/c.

## WU-FI-009d design review

Review found three risks: using today's open AR/AP as an old opening balance, treating an unapplied receipt/payment as an allocation, and paging by a maximum identity that could include a transaction committed later with a lower identity. BR-FI-068–070 use signed committed source movements, exclude unapplied cash and require a durable cutoff for the exact committed set. FI-STMT-008/009 require two-connection and token tests before BI exposes paging/export. BI retains ownership of BlueWhale statement reports and cannot read FI tables. No product code, migration, UI or executable financial test was added; SQL-dependent acceptance remains TODO.

## WU-FI-010d design review

Review found that selecting GL bank lines by the current MD account mapping could silently move old transactions to a renamed or remapped fund account. BR-FI-075/076 require a saved fund identity on each cash leg. A second risk was deriving historical opening from today's balance or paging with a maximum identity despite out-of-order commits; BR-FI-078/079 require signed posted history and a proven committed cutoff. BR-FI-082 keeps external statement differences separate from the book and forbids an automatic balancing journal. FI-CASH-009/016/018 cover these boundaries. No product code, migration, UI or executable financial test was added; WU-FI-010a/b and WU-FI-011d retain their distinct acceptance work.

## WU-FI-011d design review

Review found three risks: an import retry could duplicate bank evidence, an automatic match could consume the same FI cash leg twice, and a reviewer might mistake an approved difference for a posted journal. BR-FI-085 fixes durable row identity and per-row outcomes; BR-FI-088–089 require unique eligible one-to-one matches, SQL uniqueness and fixed lock order; BR-FI-090–091 keep approval and accounting adjustments separate. The CSV contract rejects split deposits, FX and unsafe formula-bearing text until explicit policy exists. No product code, migration, UI or executable financial test was added. FI-RECON-001–030 remain planned acceptance for 011a/b/c; real SQL is unavailable while the service is stopped.
## WU-FI-012d design review

Review found that storing an editable card book value would let it diverge from GL; BR-FI-097 derives it only from committed depreciation sources and requires journal reconciliation before period close. Full-month depreciation and last-month residual rounding are explicit in BR-FI-095–096 to avoid cumulative over-depreciation. Disposal can otherwise understate accumulated depreciation or post proceeds twice; BR-FI-099 requires scheduled depreciation through disposal month and a distinct, checked proceeds source. Permanent source keys and the asset/period lock guard retries and concurrent runs. No product code, migration, UI or executable financial test was added; FI-FA-001–029 are planned acceptance for 012a/b/c/e while SQL is unavailable.

## WU-FI-013d design review

Review found four risks: treating an internal approved draft as a government filing, deriving current tax from today''s MD rate, reconciling a tax draft against an unrelated GL period, and using a maximum source ID as a committed cutoff. BR-FI-103–111 bind source treatment and tax point to saved policy, separate approval from external evidence, compare tax detail to the exact Posted GL leg by source/period/account, and require a durable committed set for pagination. FI-TAX-007/011–12/021/027/030 require real SQL and UI proof. Nonrecoverable input remains visible as gross tax but cannot inflate claimable input. No product code, migration, screen or executable financial test was added; WU-FI-013a/b/c retain acceptance. SQL service is stopped.

## WU-FI-014d design review

Review found that applying today's MD rate to a Posted invoice would rewrite history; BR-FI-113–114 and FI-FX-001–002 require immutable rate and base snapshots. A settlement journal could double-count an unrealised valuation if it computes realised gain against the already valued base; BR-FI-115–118 separate historical base, saved settlement base and overlay release, and FI-FX-006–007/015 test partial/full settlement against valuation. A maximum source ID is unsafe under out-of-order SQL commits, so BR-FI-117/121 require a durable committed set and a conflict check when settlement races valuation. Each exposure result is atomic and run counts reflect only committed results. No product code, migration, screen or executable financial test was added; WU-FI-014a/b/c retain acceptance while SQL is stopped.
# WU-FI-015d design review — 2026-10-24T17:45:48+08:00

- 發現：只按 period 的最大 journal ID 判斷月底來源會漏掉先取得 ID、後提交的來源。修改：BR-FI-134 規定可驗證的已提交來源清單及月結前重新比對；FI-CLOSE-011/012 留雙連線驗收。效果待真實 SQL 測試證明。
- 發現：稅務申報、銀行對帳、BI 匯出與 GL 關帳可能被混為一個狀態。修改：BR-FI-141 明確分離；重開要求記錄下游修訂依賴。無產品程式可優化。
- 發現：年結重開若直接刪除結轉分錄將破壞 Posted 不可變性。修改：BR-FI-140 要求新補償來源與反向次序；FI-CLOSE-004/019/021 留驗收。
- 本切片只做設計；編譯、migration、真實 SQL、HTTP 和畫面驗收未執行，不計產品完成。

## WU-FI-001b2 credit limit HTTP (2026-11-04)

- 發現：查詢服務需要與寫入服務相同的「目前公司 + 該動作權限」檢查，複製一份會令兩處 403 規則日後分歧。修改：抽出 `FinanceAccess.RequireAsync`，`CreditLimitCommandService` 與 `CreditLimitQueryService` 共用。效果：寫入既有 `CreditLimitCommandTests` 不變仍通過，查詢 403 測試直接覆蓋同一程式路徑。
- 發現：`CreditLimitStatus` 預設以整數輸出，與資料庫文字及 UI 顯示不一致，HTTP 測試讀 `status` 時失敗。修改：列舉加 `JsonStringEnumConverter`（與 INV／MD 列舉做法一致）。效果：API 回 `"Active"`／`"Deactivated"`，查詢字串 `status=Deactivated` 亦可綁定。
- 發現：列表若靜默忽略 `keyword` 會令使用者以為已搜尋。修改：明確 400 `keyword`（ADR-075），客戶搜尋留給 001c 的 MD 選擇器。
- 發現：分頁排序同值時次序不穩。修改：每種排序都以 id 同方向作最後排序；測試以兩筆同 `effectiveFrom` 證明。

## WU-FI-001c（2026-11-04）

- 發現：API 以 JSON 數字回傳 decimal(19,4)，超過約 9×10¹¹ 的 4 位小數在 JavaScript double 會被四捨五入；若編輯時以此值回填再保存，會靜默改小額度。修改：服務以文字模式讀取回應並保留 `"amount"` 原始字串（`amountText`），顯示以 BigInt 加千分位，寫入以十進位文字送出（ADR-076）；新增 HTTP 測試證明最大值逐位保存。效果：單元與 E2E 證明 999,999,999,999,999.9999 建立、重讀與編輯回填都不失真。
- 發現：文字模式下錯誤本文變成字串，`apiErrorKey` 讀不到 `errorCode`／`traceId`。修改：`jsonErrors()` 把字串本文還原為 JSON 再拋出。效果：409／422／500 具名提示與 traceId 均有單元測試。
- 發現（UX 審查 P1）：版本衝突後對話框仍持有舊版本，每次重按保存都 409。修改：過期版本或已停用時關閉對話框、在列表上方顯示原因並重讀。效果：E2E 斷言對話框關閉、列表顯示對方版本、金額未變且沒有成功訊息。
- 發現（web-design 審查）：成功訊息非即時區域、表單欄位缺 name/autocomplete、驗證失敗未聚焦。修改：成功訊息放入 `role=status` 區域（`display:contents` 避免空白間距）、補 name/autocomplete/spellcheck、驗證失敗聚焦第一個錯誤（未選客戶時聚焦選擇按鈕）。效果：E2E 斷言焦點位置。
# WU-FI-001d customer labels (2026-11-04)

- Problem: credit-limit rows opened without the MD picker showed only `Customer #id`, and a deep-linked filter could not name its customer. Cause: FI responses had only an ID and FI cannot query MD tables. Change: MD added `ICustomerLabels` as one untracked, company-filtered batch read including soft-deleted history; FI enriches only its already scoped page/detail/committed write result; the frontend uses server labels and keeps the picker as a fallback (ADR-077). Effect: real SQL/HTTP, unit and Playwright assertions show names without a picker, historical names, null for foreign/unknown IDs, and no cross-company disclosure.
- Review: the initial label tests reused FI-CREDIT-021, which already names the permanent key race. New tests were renumbered 022/023 and traceability updated. The list sends distinct customer IDs in one call rather than querying once per row. A second check found that storing server labels in the picker cache could leave an obsolete name after MD returned null; separate server/picker maps now revoke an old server value and retain a genuinely selected picker value as fallback, with a unit assertion. Screenshot review (26 images) found no new P1/P2 issue; the pre-existing required-field marker and unsaved-form guard remain WU-FI-001e. Web Interface Guidelines review of the changed label flow found no new accessibility, keyboard, focus, URL, theme or locale issue; existing form findings are tracked in 001e.
- Playwright regression: after the separate server/picker maps, selecting a customer in the create form returned one correct row and the dialog epoch matched, but the DOM still said "not selected" in 3/3 focused runs. Cause: zoneless Angular did not schedule a render for the reactive-form update because server labels had removed the template's dependency on the picker signal. The picker callback now calls `ChangeDetectorRef.markForCheck()` after setting the form control. The existing FI-CREDIT-004/011 Playwright assertion is the regression guard; temporary `[DEBUG-fi-label]` probes were removed.
- Transaction review: the first implementation looked up an MD label after `InLockedTransactionAsync` committed. A real SQL fault-injection test showed a 500-shaped exception with one committed `ar_credit_limit` row (FI-CREDIT-024 red). The command now enriches its result before commit; the same test is green with zero limit/key rows. This keeps the API result aligned with the write outcome. The same transaction and current-company filter guard the read; no MD table is accessed by FI directly.
- 2026-11-04T15:54:44+08:00 verification review: SQL-backed DocGen initially reported 21 errors because FI item columns did not use the parser's `Item` / `Related REQ_F` headings and SD's interface table used a full SRS path rather than the `{module}.{interface}` source ID. Correcting the source tables yielded 0 Error / 581 Warning. Debug build passed. A full backend run then hit a SQL write timeout in `CustomerLabelTests` and a missing JSON property in `AuditLogApiTests` while free physical memory fell to about 1.55 GiB. The run was stopped; neither failure is counted as a product regression or as a passing full suite without isolated reproduction. Resource diagnostics found no safe background process to reclaim; the largest groups were interactive. Next attempt must gate memory, reproduce each targeted test separately, then rerun the full suite.
