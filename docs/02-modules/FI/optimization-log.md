# FI optimization log

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
