# FI optimization log

## WU-FI-004a3g repeated account snapshot consistency — 2026-11-05T16:52:27+08:00

- SQL memory gate returned 1.121/1.110/1.114 GiB DEFER, so no SQL fixture or full integration suite ran. Review found that a valid draft could repeat one account with different code/name/version or reuse one code for different IDs. That could yield internally contradictory immutable line history.
- Test-first compilation failed at missing `SnapshotConflict`; after adding per-draft ID and code indexes, the targeted class passed 7/7. Repeated identical account legs remain separate and balanced. The later transaction must still reload MD and mapping authority before persistence.

## WU-FI-004a3f whole-journal offline draft — 2026-11-05T16:45:51+08:00

- SQL memory gate returned 1.093/1.102/1.109 GiB DEFER. A posting service could otherwise assemble and retain a valid first leg before discovering an invalid second account or amount. Added one pure whole-journal draft rule that emits ordered scalar snapshots and totals only after every account and amount passes; failure returns zero lines and totals.
- Test-first: new tests failed compilation at the missing `GlJournalLineCandidate`, then passed 5/5. Review found repeated account legs and invalid amount precision needed explicit follow-up assertions; added both without collapsing line order. SQL snapshot storage, MD usage, transaction revalidation and full SQL acceptance remain open.

## WU-FI-004a3e account snapshot preflight — 2026-11-05T16:39:54+08:00

- MD's public `AccountInfo` lacked account ID, company ID and version, so FI could not match the candidate account reference to an authoritative MD result. Added those scalar fields to the MD projection and a pure FI preflight for mapping version, company, ID/code, active leaf state and bounded snapshot data. No MD table access was added in FI.
- Test-first loop: the new test initially failed to compile on missing `GlAccountSnapshotStatus`. Targeted tests then passed. Full Architecture went 347/348 because the first implementation put the MD contract reference in FI Domain. The architecture test identified the exact dependency; moving the pure adapter rule to FI Application preserved Domain isolation. Architecture then passed 348/348. Added input-code and error-order boundary tests afterward.
- Debug solution build passed with zero warnings/errors, DocGen 22/22 and `git diff --check` passed. The SQL memory gate returned 1.187/1.165/1.265 GiB DEFER, so the changed MD integration assertion, SQL posting and full suite were not executed. The account/mapping/name/version snapshot still needs durable FI storage and transaction-level revalidation; `GlAccountSnapshotRules` alone cannot establish posting authority.

## WU-FI-004a2k third resource checkpoint — 2026-11-05T16:29:54+08:00

- The three-sample SQL memory gate returned DEFER at 1.809/1.855/1.876 GiB; an independent `sqlcmd` query returned 1. This is the third consecutive resource checkpoint for this slice, so it is BLOCKED under the project rule. The already compiled endpoint/adjacency/rollback test has not reached the SQL fixture or a red/green result.
- No code, test, threshold or SQL setting changed. The earlier fixture `READ_COMMITTED_SNAPSHOT` and company applock timeouts still need a red-capable diagnosis after the gate passes; the retry sequence is in blocked.md. This checkpoint does not establish product acceptance.

## WU-FI-004a2k second resource checkpoint — 2026-11-05T16:26:18+08:00

- The three-sample memory gate returned DEFER at 1.801/1.807/1.787 GiB. An independent `sqlcmd` probe returned 1, so SQL connectivity is presently available, but the established 2.5 GiB gate still prevents starting the fixture.
- Static review confirmed the new test reads the saved January row through a fresh DbContext after each of two overlap rejections and after the accepted adjacent update. It asserts dates, Open state and version. No test or product code changed; SQL red/green, the earlier RCSI/applock timeout diagnosis and the full SQL suite remain unverified.

## WU-FI-004a2k period update SQL checkpoint — 2026-11-05T16:22:54+08:00

- The memory gate read 1.293/1.328/1.344 GiB (DEFER); independent `sqlcmd` timed out during login. The fixture was not started, so neither the earlier RCSI timeout nor the company applock timeout has a reproducible SQL signal this round. No timeout, gate, or product code changed.
- Review found the existing SQL tests checked shared endpoints on create but did not prove update adjacency or that an overlap rejection preserved the saved row and version. Added a test that reads from a separate context after two rejected updates and after an accepted adjacent update. The test compiles; SQL execution and the red/green observation await a PASS gate.
- Debug solution build passed with zero warnings/errors, Architecture 338/338 passed, and `git diff --check` passed. The full SQL suite and period/posting shared-lock acceptance remain open.

## WU-FI-004a3d persisted source shape preflight — 2026-11-05T16:17:38+08:00

- SQL gate read 1.482/1.475/1.363 GiB (DEFER) and an independent `sqlcmd` login timed out, so no fixture or migration ran. The intermittent fixture RCSI/applock failures still need diagnosis after a PASS gate; this slice does not alter SQL timeouts or tests.
- Review found `GlSourceReplayRules` accepted source text longer than the `gl_source_key` varchar columns, non-ASCII characters that can be stored lossily, and versions above the SQL `int` field. A unit test went red with `Expected: InvalidSource; Actual: New`. The preflight now enforces the 20/30/128 character bounds, printable ASCII and `int` version bound before source-key comparison. Follow-up assertions cover invalid candidates against saved keys and invalid saved receipts.
- Architecture 338/338, DocGen 22/22, Debug solution build 0 warnings/errors and `git diff --check` passed. No product SQL/HTTP/UI success is claimed. The SQL posting service must still validate source authority and use the company period lock.

## WU-FI-004a3c base journal amount preflight — 2026-11-05T16:09:26+08:00

- The 004a2 SQL endpoint/rollback validation could not run: the three-sample memory gate read 1.749/1.751/1.760 GiB (DEFER); independent `sqlcmd` returned 1. This is the gate's stated resource condition, not evidence of a code defect. The earlier intermittent fixture `READ_COMMITTED_SNAPSHOT` and applock timeout still need a red-capable loop when the gate passes; no SQL setting or test threshold was changed.
- Review found that future GL posting still lacked a reusable BR-FI-026 line amount decision. Added a pure preflight with named outcomes, one-based invalid line, exact `decimal(19,4)` range and checked aggregate bound. It returns zero totals for every rejection, so a caller cannot mistake a partial sum for a valid journal. It keeps repeated account legs separate; source authority and MD account eligibility remain later service checks.
- Test-first evidence: the new test file initially failed to compile because `GlJournalBalanceStatus` did not exist. After implementation, its 8 tests passed; Architecture 337/337, Shared 381/381, DocGen 22/22, Debug solution build 0 warnings/errors. SQL/full integration and HTTP/UI tests were deferred by the memory gate. No data structure, API or UI contract changes in this pure slice.

## WU-FI-004a2j same-version mutation races — 2026-11-05T15:59:23+08:00

- The interrupted turn left two uncommitted real SQL race tests. Review found they assert persisted version and exactly one committed update or delete without changing product code. A three-sample memory gate passed at 2.519/2.517/2.517 GiB and independent `sqlcmd` returned 1.
- Diagnosis loop: `dotnet test --project tests/backend.integration/MyERP.IntegrationTests/MyERP.IntegrationTests.csproj --filter-class '*FinancePeriodWriteTests*' --max-threads 1` reached the new test but failed at `GL period lock was not granted` after 15 seconds; the existing simultaneous-create test failed the same way. A subsequent `--filter-method '*SimultaneousUpdate*' --max-threads 1` passed both new tests in 32 seconds. A DMV query after the failure found no lingering `gl_period` application lock. The failure is intermittent and not yet a deterministic product regression. Likely causes to distinguish next time: SQL instance pauses (independent probe would also slow), fixture contention (failure follows whole-class runs), or a long transaction (DMV shows a granted lock while waiting). No lock timeout or test assertion was weakened.
- Review and follow-up: the new tests cover a useful same-version race seam. They do not cover a simultaneous posting write because no posting service exists. Keep 004a2 open for adjacent overlap/rollback SQL tests, and require 004a3 to acquire the same company lock before treating Posted protection as concurrent-safe.
- A wider class retry at 2026-11-05T16:02:31+08:00 failed before tests: fixture database creation timed out after 65 seconds on `ALTER DATABASE [myerp_test] SET READ_COMMITTED_SNAPSHOT ON` (0/7). This is separate from the first run's applock timeout and prevents a full-class pass claim. No SQL fixture or migration setting was changed to bypass it.

## WU-FI-004a2i saved Posted date mutation — 2026-11-05T12:27:53+08:00

- Problem: the SQL period service only created rows; update/delete could not enforce the existing version and Posted-date rules against persisted journals. New integration tests first failed to compile because both methods were missing. The service now takes the company transaction applock, reloads the saved period and actual Posted dates, applies the pure decisions, and commits only allowed changes. An unchanged update retains its version.
- Review: the first green run found a duplicate tracked `GlPeriod` when the caller reused one DbContext. The service detaches only an unchanged local instance before attaching the freshly read row; a pending local edit raises an error instead of being discarded. Five targeted real SQL tests pass, including endpoint exclusion, deletion protection, stale versions, wrong company, unchanged and state-only update. Posting must acquire the same period lock, and update/delete races plus full integration remain open.
- Full-suite checkpoint (2026-11-05T12:31:10+08:00): Architecture, Shared and DocGen projects passed. Integration again failed in existing `AccountTests.ConcurrentParentDeleteAndChildCreateNeverOrphanChildren` and `ItemCategoryTests.ConcurrentOppositeMovesCannotCreateCycle` with `Company lock was not granted`; `QuotationMutationTests.TwoConnectionsCannotWithdrawAndReviseSameVersion` reported a SQL timeout. The full run was stopped after these failures. This slice does not claim full integration acceptance.

## WU-FI-004a2f unchanged period update preflight — 2026-11-05T11:23:14+08:00

- Problem: a period update with the same saved dates and state returned `Allowed`; a later service could increment version and report success despite no data change. The new focused test first failed to compile because `Unchanged` was absent. The decision now returns `Unchanged` only after checking version, identity, Posted-date retention and overlap; a state-only change stays `Allowed`. Review kept the no-op check last so stale or inconsistent snapshots are not silently accepted.
- SQL checkpoint: initial memory gate 3.071/3.136/3.073 GiB PASS and independent `sqlcmd` succeeded. The all-integration run exposed existing `AccountTests.ConcurrentParentDeleteAndChildCreateNeverOrphanChildren` company-lock timeout and `NumberGeneratorTests.ParallelGenerationFromManyContextsNeverDuplicatesOrSkips` login timeout. A second gate fell to 2.532/2.525/2.407 GiB DEFER, so the run was stopped. The focused red-capable command is `dotnet test --project tests/backend.integration/MyERP.IntegrationTests/MyERP.IntegrationTests.csproj --no-build --max-threads 2`; its long shared fixture and falling memory prevent a deterministic short diagnosis in this checkpoint. No unrelated lock code or tests were changed.
- Verification: Architecture 324/324, DocGen 22/22, Debug solution build 0 warnings/errors and diff check. This is an offline decision only; full SQL regression, company transaction service, persisted Posted journal protection, HTTP and UI acceptance remain open in WU-FI-004a2/004a3/004b.

## WU-FI-004a2e physical period schema — 2026-11-05T11:13:32+08:00

- Problem: period rules had no persisted table, so a SQL caller could not retain inclusive dates, state or version. A new real SQL test failed because `gl_period` had no columns. Added `GlPeriod` with audit/version fields, SQL checks and a company/date index through reversible migration `Finance_GlPeriod`. Schema and four invalid-row checks passed 5/5 after migration; `myerp` was updated.
- Review: the initial full suite exposed an architecture test that assumed FI had exactly one model registration. Its focused failure identified the stale assertion; it now checks the two concrete FI models and passes 1/1. Ranked alternatives were duplicate DI registration (would show the same model twice), test-host registration leakage (would affect only the host), and fixture state (would affect SQL only); the two distinct types in the service collection identified the cause. No production code change was needed for this assertion.
- Verification: Debug build 0 warnings/errors; Architecture 323/323, Shared 381/381, DocGen 22/22; isolated consistency 0 Error/580 Warning without SQL schema comparison. Full SQL regression was stopped after existing concurrent tests reported company-lock timeouts and memory gate fell to 2.403/2.380/2.383 GiB (DEFER). The 004a2e targeted SQL 5/5 result is valid, but full SQL regression and transaction behavior remain unverified.

## WU-FI-004a2d persisted identity/version preflight — 2026-11-05T11:02:19+08:00

- Problem: equal zero or negative versions returned `Allowed`, and update accepted a saved period with ID/company zero. Those values cannot describe a persisted company period. Tests first failed with `Allowed`; the pure write decision now rejects invalid versions as `StaleVersion` and invalid saved identity as `IdentityMismatch`, while preserving stale-version precedence. Review found no SQL caller yet; the new check does not substitute for locked persisted reads.
- Verification: Architecture 323/323, DocGen 22/22, Debug solution build zero warnings/errors, diff check. Memory gate 2.126/2.132/2.148 GiB DEFER; independent `sqlcmd` returned 1. No fixture, migration, SQL concurrency or HTTP acceptance was run.

## WU-FI-004a2c composed write preflight — 2026-11-05T10:56:33+08:00

- Problem: the separate date, overlap and Posted-reference helpers did not define a single ordered outcome for create/update/delete. A caller could pass a persisted ID to the create overlap helper and have its own row excluded. Change: compose the pure checks with stale-version precedence and require an unsaved ID plus positive company ID on create. The identity bypass test failed with `Allowed` before the guard and passed afterward. Update checks identity/date retention before overlap; delete checks version before Posted references. This keeps the future SQL transaction's response deterministic without claiming the offline snapshot is current.
- Verification: five focused tests, Architecture 321/321, DocGen 22/22 and Debug build with zero warnings/errors. SQL memory gate was 2.322/2.356/2.374 GiB (DEFER); independent `sqlcmd` returned 1. WU-FI-004a2 must re-read all inputs under the company SQL lock, prove concurrent exclusion and saved version, and run real SQL regression.

## WU-FI-004a2a Posted-date mutation policy — 2026-11-05T06:54:20+08:00

- Problem: BR-FI-023 protected Posted journals from period movement/deletion only in prose. Change: add a pure decision for candidate identity, valid inclusive range, retention of every Posted business date and deletion with no Posted references. Two new tests first failed to compile, then passed 11/11 in the period suite. Review: the helper consumes the supplied dates once with `Any`; the SQL caller must supply saved Posted dates under its company lock and still check version, overlap and commit atomically. No SQL acceptance is claimed.
- Resource checkpoint: memory gate 1.817/1.763/1.778 GiB DEFER; independent `sqlcmd` connection returned 1. SQL fixture, migration, concurrency and product assertions remain WU-FI-004a2.

## WU-FI-004a1 period-date policy review — 2026-11-05T06:47:20+08:00

- Problem: the GL design described inclusive overlap and exactly one Open period but had no executable common rule. Change: add a pure company-scoped interval and resolution policy, with distinct Missing, Closed and Ambiguous outcomes. It reads a maximum of two matching periods, so ambiguity cannot silently select one. Nine targeted tests first failed to compile, then passed; Architecture 312/312 and Debug build had zero warnings/errors. WU-FI-004a2 must use this policy inside its locked SQL transaction, rather than treating an offline result as an exclusion guarantee.
- SQL diagnosis: the initial gate passed at 2.527/2.524/2.559 GiB, but the integration fixture failed before its first assertion at SQL connection open (`Win32Exception: semaphore timeout`). The SQL service was Running; a second gate fell to 1.624/1.650/1.716 GiB. The direct fixture command is the red-capable loop, but this attempt did not reach the new schema assertion. Ranked causes for a later controlled retry are memory pressure (predicts recovery when three gate samples remain above 2.5 GiB), SQL connection exhaustion (predicts active sessions/pool saturation despite sufficient memory), and instance responsiveness (predicts an independent connection also fails). No SQL schema, migration or product pass is claimed; the uncommitted schema test was removed because it could not meet the red/green gate.

## WU-FI-001e form UX review — 2026-11-05

- `web-design-guidelines` review: `credit-limit-page.ts` form labels already had explicit input associations, local field errors and focus after invalid submit. At the form customer/date/amount labels, required state was invisible until failed Save (E4); add visible `*`, native `required` for inputs and `aria-required` for the customer picker. Screenshots `6.17-required-*` inspected in zh-Hant, zh-Hans and en, light/dark; the first dark capture was taken before the modal rendered, so the test now waits for the form to be visible before capture.
- Cancel previously called `closeDialog()` even after an edit; it silently lost input. Reuse `ErpDialogService.confirm` and the shared `unsavedChangesGuard`, plus native `beforeunload`. Keep the immediate close for pristine forms and the direct close after a successful save or stale-version response. The modal intercepts pointer events on the background menu, so the Playwright regression checks Cancel stay/discard and saved-row immutability; the shared guard's unit suite covers route navigation. No API or schema changes.
- The prior overlap E2E timed out at theme selection because its dirty Cancel now opened the expected confirmation. It explicitly confirms discard before continuing. Credit-limit E2E 9/9, frontend 748/748, DocGen 22/22, isolated consistency 0 Error/580 Warning. Review found no remaining P1/P2 issue in the five new screenshots.

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
- 2026-11-04T20:57:27+08:00 third verification attempt: memory gate passed (2.842/2.840/2.776 GiB). The first isolated CustomerLabelTests run timed out in the shared fixture while setting READ_COMMITTED_SNAPSHOT during database creation; it never entered the test method. A SQL DMV query showed no persistent myerp_test session afterward. A second isolated run passed 1/1, and the isolated AuditLogApiTests passed 1/1. The single-thread full solution run passed DocGen, Shared and Architecture, then NumberGeneratorTests.ParallelGenerationFromManyContextsNeverDuplicatesOrSkips timed out at its SQL command after 30 seconds. Free physical memory fell to 1.72 GiB, so the incomplete integration run was stopped. No code or test was changed because the observed failures do not isolate a FI defect; full integration acceptance remains blocked and is recorded in blocked.md.
# WU-FI-004a2b overlap policy — 2026-11-05T06:59:48+08:00

- Resource checkpoint: memory gate 1.802/1.825/1.794 GiB DEFER; independent `sqlcmd` connection returned 1. SQL fixture, migration, company lock and concurrent exclusion remain WU-FI-004a2.
- Review: an edit must exclude its own persisted row while a create must compare with every row. The new pure helper uses the existing inclusive, company-scoped `Overlaps` rule, compares Closed rows too, and short-circuits on the first conflict. Targeted tests failed to compile before implementation and passed 2/2 afterward.

## WU-FI-004a2g review (2026-11-05T11:31:16+08:00)

- Test-first signal: the new real-SQL creation test failed compilation because `GlPeriodWriteService` did not exist, then passed after implementation. A second integration case confirms reversed ranges, company mismatch and zero saved rows.
- The create service locks the company before re-reading periods; its own-row exclusion cannot apply to a new row. The transaction owns the applock until commit/rollback, and the current-company check prevents a caller from selecting another company ID.
- The earlier MD `AccountTests.ConcurrentParentDeleteAndChildCreateNeverOrphanChildren` timeout reproduced in isolation with `dotnet test --project tests/backend.integration/MyERP.IntegrationTests/MyERP.IntegrationTests.csproj --no-build --filter-method '*ConcurrentParentDeleteAndChildCreateNeverOrphanChildren*' --max-threads 2`: `CompanyLock.RunCoreAsync` returned SQL error 51000 after its 15-second applock wait. Ranked causes to investigate: opposing parent/child lock order, a SQL row lock retained while waiting on applock, or an independent lingering transaction. This is not attributed to FI and no MD lock behavior was changed.
- Remaining FI risks: test two concurrent create connections with a barrier; implement update/delete only after a real Posted journal source exists. The service has no HTTP entry point yet, so UI and permission acceptance remain 004b/004c.

## WU-FI-004a2h concurrent creation (2026-11-05T11:58:57+08:00)

- Test: `SimultaneousCreatesSerializeByCompany` releases two same-company creates through one `TaskCompletionSource` barrier, then holds another company's `gl_period` applock in an open transaction and proves a third company's create completes within 5 seconds. The cross-company check was strengthened from two unrelated parallel creates (left by the interrupted previous round), which could not distinguish "not locked" from "locked briefly".
- Diagnosis (diagnosing-bugs): the first three runs failed with error 51020 after the 15-second applock wait; the next six runs passed (single method, a 4-run loop, `--max-threads 1`) and the class passed 8/8 with `--max-threads 2`. The hypothesis "xunit synchronization-context starvation" was falsified because `--max-threads 1` passed. During the failing window an independent `sqlcmd` login to `localhost\MSSQLSERVER2022` timed out with a pre-login delay, so the leading explanation is an intermittent SQL instance stall, not the lock design; this matches the earlier AccountTests/NumberGenerator timeouts. No product code changed.
- Remaining risk: a 15-second applock timeout surfaces as a raw `SqlException`; 004b must map it to a retryable conflict/unavailable error rather than a 500.

## WU-FI-004a3a source replay preflight (2026-11-05T12:07:22+08:00)

- Test-first: the five replay tests initially failed compilation because the source decision types did not exist. The first implementation passed, then a new outer-whitespace case failed (`InvalidSource` expected, `New` actual); canonical text validation made it pass. The rule uses ordinal identity and fingerprint comparison, returns a saved journal ID only for an exact replay, and rejects a mismatched saved row rather than treating it as a new key.
- Review: this is a pure decision over a caller-supplied committed row. It cannot establish that a row committed, reserve the permanent key, validate MD accounts or post GL. These remain 004a3b/004a3. No SQL schema or HTTP/UI changed in this slice.
- Verification: Architecture 329/329, Shared and DocGen test projects passed, Debug solution build had zero warnings/errors. Full SQL integration did not pass: `AccountTests.ConcurrentParentDeleteAndChildCreateNeverOrphanChildren` hit `Company lock was not granted` after 15 s, `NumberGeneratorTests.ParallelGenerationFromManyContextsNeverDuplicatesOrSkips` hit a 29 s post-login timeout, and `SalesOrderAuthorizedHttpTests.AuthorizedManualDraftUsesRealReferencesAndPersistsHttpChanges` saw an unexpected 500. The run was stopped after these failures; independent sqlcmd succeeded before and after, so SQL availability is intermittent. No unrelated test or product code was changed. The existing targeted AccountTests loop and SQL-instance diagnostics in 004a2g/h remain the repro path; full integration acceptance remains open.

## WU-FI-004a3b journal schema review (2026-11-05T12:17:07+08:00)

- Real SQL red showed the three tables absent. The generated migration was checked for a reversible Down, permanent unfiltered source uniqueness, same-company composite FKs, indexed FK columns and the Posted-date index. The initial generated C# triggered CA1861; the repository's migration pragma was applied, after which the focused integration tests passed.
- `gl_journal` and `gl_journal_line` preserve saved business date and account snapshots without direct MD table access. A database check cannot compare header totals with an arbitrary number of lines, so WU-FI-004a3 must validate and write all rows atomically. Current schema does not expose an unsafe posting API.
