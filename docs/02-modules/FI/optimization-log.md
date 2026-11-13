# FI optimization log

## WU-FI-002a4a SD invoice account map — 2026-11-14T01:03:01+08:00

- **One row, one version.** Problem: the account read and the GL version read are two ports; separate storage could drift and make every posting 409. Change: one store implements both and uses the 6.4 `version` column as the mapping version. Effect: they agree by construction; only a real concurrent change is rejected (FI-SALES-MAP-003/005).
- **Version can never be reused.** Problem: deleting and re-creating the row would restart at version 1, so a journal line's `mapping_version` could later name different accounts. Change: guard trigger 51025 blocks DELETE, company change and non-increasing version (FI-SALES-MAP-001).
- **Test fixture honesty.** Two SD/Invoice sources are a 500 configuration fault (ADR-103), so test fakes now replace the production registration instead of silently adding a second one; the "no source" 500 test removes the production source explicitly and a new assertion covers "source but no row" → 409.
- **Not changed:** each posting reads the map row twice (account codes before the draft, version after the GL locks). Both are a unique-index seek; caching would defeat the concurrent-change check.

## WU-FI-002a2b invoice posting service — 2026-11-14T00:21:28+08:00

- **Replay before the SD read.** Problem: once SD marks the invoice Posted (or later soft-deletes it), re-reading SD could turn an identical replay into 422/404. Change: under the credit lock FI first checks its own permanent `ar_sales_source` key and returns Unchanged with the saved receipt. Effect: replay needs no SD read (FI-SALES-INV-003 asserts zero reads) and is stable against later SD state.
- **No DI start-up failure while SD has no implementation.** The SD port and the account map are resolved as `IEnumerable` and a missing/duplicate registration is a named 500/422 result instead of a host start-up error.
- **Fault keeps ADR-107's promise.** A GL Unchanged without an FI source and a failed FI row write both roll the caller back, so every Fault means "nothing remains"; the change tracker is cleared so a later SaveChanges cannot resend the failed rows.
- **Not changed:** account lookups run one MD query per leg (≤ 3); a batched MD lookup is not justified for three rows.

## WU-FI-002a2a joined GL posting — 2026-11-14T00:04:28+08:00

- **Reuse instead of a second posting path.** Problem: invoice posting must write GL inside the caller's transaction (BR-FI-013), but `IGlPostingService` owns its scope, transaction and retry loop. Change: `GlPostingAttemptSession` takes an optional owned scope; without one it joins the caller's transaction, its commit is a no-op and disposal leaves everything to the caller. `GlPostingAttemptRunner.PostJoinedAsync` reuses the private single-attempt order. Effect: lock order, authority reads, decision and write plan stay one implementation for both modes; the standalone posting tests pass unchanged.
- **Safety: no retry or lookup inside a caller transaction.** A fresh-connection lookup after a failure could report Unchanged while the caller's own transaction had already been rolled back by the writer. `ClassifyJoined` therefore only reports (503 `TRANSACTION_ABORTED` / 500 `WRITE_FAILED`), and the joined port's lookup throws `NotSupportedException` so that misuse is caught at once.
- **Failures that leave the transaction open.** An authority-read SQL error is not rolled back by the writer. `GlJoinedPostingService` rolls back on every Fault and every exception, so the outcome contract "Fault ⇒ the caller's transaction is gone" holds for all paths (FI-GL-JOIN-004 proves this for a pre-write failure).
- **Not changed:** the applock timeout is still the writer default (15 s). Making it configurable is deferred to 002a2c if its race tests need a shorter wait.

## WU-FI-002a1 sales source schema — 2026-11-13T23:47:09+08:00

- **Defect found by a red test.** Problem: the lowercase currency `usd` passed `[currency] LIKE '[A-Z][A-Z][A-Z]'`. Cause: the database's default collation is case-insensitive, so the `[A-Z]` range matches lowercase letters too. Change: the new `ck_ar_sales_source_currency` uses `COLLATE Latin1_General_BIN`. Effect: the theory row now passes. The older checks on `ar_credit_limit`, `ar_posted_receivable` and `gl_journal` have the same weakness and are logged as P3 WU-FI-001f (ADR-106).
- **False-green guard.** Problem: before the migration, 9 of 13 tests passed only because the INSERT failed with error 208 (missing table). Change: rejection assertions go through `AssertRejectedAsync`, which accepts only 547/2601/2627/51024. Effect: a genuine red of 13/13.
- **Migration hygiene.** Running `dotnet ef migrations add --no-build` after `migrations remove` used a stale assembly and produced a full-schema migration. The fix was to discard it, restore the snapshot from git, build, and add the migration again. For later rounds: always build before `--no-build` EF commands. The generated FK-covering indexes `ix_ar_sales_source_company_id_gl_journal_id` and `ix_ar_credit_allocation_company_id_credit_source_id` are kept because the composite FKs lead with `company_id`, matching the GL tables.

## WU-FI-004c3 posted journal view — 2026-11-13T19:46:53+08:00

- Reuse: the credit-limit page kept its own `"amount"` regex and private text-error helper. Journals have four money keys in nested header/lines, so a shared `parseJsonWithDecimalText` (rewrites only listed numeric keys to strings before `JSON.parse`; an escaped key inside a string value cannot match) and `formatDecimalText`/`isZeroDecimalText` moved to `shared/decimal-text.ts`; `jsonTextErrors` moved to `core/http/api-error.ts`. `formatCreditAmount` now delegates and the credit tests still pass unchanged. `validIsoDate` moved to `shared/date.ts` so the journal chunk does not import the period page.
- Screenshot review (19 images, 6.22-*): no P1/P2. P3 (E3) fixed in this unit: business dates, period ranges and posted times broke at the hyphen in zh-Hant (`2026-11-` / `12`); those cells and sortable headers are now `nowrap`, with an E2E computed-style assertion.
- `web-design-guidelines`: code filters (source module/type, account code) now have `spellcheck="false"`; labels wrap their controls, `autocomplete="off"`, validation uses `role="alert"`, number columns are tabular and right-aligned, focus-visible outline present. Not changed (P3, recorded here): the journal number is a button that sets `journal=<id>` in the URL rather than an `<a>` that supports Ctrl+click; the `例如 SD` placeholder has no trailing ellipsis, matching existing filter placeholders.
- Design decisions: the theme menu is under the modal overlay, so screenshots close the dialog before switching theme. A journal-only role cannot read periods, so the period filter is hidden, labels show `#id` and a URL `periodId` is ignored rather than filtering by a value the user cannot see. Detail 404/403 offer no retry because retrying cannot succeed.
- The first i18n helper re-serialised `fi.json`; as in 004c2 it was replaced by a text append so only the new `glJournal` block changes.

## WU-FI-004c2 accounting period maintenance — 2026-11-13T19:12:35+08:00

- Screenshot review (13 images, 6.21-*): no P1. P2 (A4): a refused row action (stale version, Posted delete, missing period) showed the reason without saying which period; the error now starts with the period dates (`fi-gl-period-action-target`) and unit/E2E assert it. P3 to backlog: repeated row button labels ("Change dates") lack period context for screen readers; the shared stale-version text says "reload and retry" although the list has already been re-read.
- The first draft used "開放/开放" in new messages while the existing Open tag says "開啟/开启"; all new texts now use the tag's wording (D3).
- Overlap, Posted-date exclusion and 5xx keep the dialog and input so the user can correct dates (B5); stale version and 404 close it because the held version can never succeed, then re-read the list.
- The first JSON helper re-serialised `fi.json` and re-indented the credit-limit block; it was reverted and replaced by text insertion so only GL keys change.

## WU-FI-004c1 accounting period view — 2026-11-13T18:47:08+08:00

- Split WU-FI-004c into period list (c1), period write dialogs (c2), journal view (c3) and real-API dual assertion with QA report (c4), so each slice stays within the 90-minute limit.
- Screenshot review found the Retry button stretched across the grid page under the error; it now keeps its own width. The web-interface review added `autocomplete="off"` to the date filters. Title Case and fixed `yyyy-MM-dd` dates follow ui-conventions instead of the generic guideline.
- A deep link past the last page would show an empty table while saved periods exist; the page now re-reads page 1 (FI-GL-UI-008). A company switch drops filters so another company is never filtered by stale criteria (FI-GL-UI-005).
- The first E2E run failed on the test itself: after Reset the page correctly shows the unfiltered empty message, not search-empty. The assertion was corrected; the product was right.

## WU-FI-004b2 period write HTTP — 2026-11-12T21:28:12+08:00

- Review found that the SQL period writer already serialized updates and guarded Posted dates, but its required `isOpen` argument would let a general date-edit route change state. Added a nullable state/date overload that reads omitted values after taking the company lock; the public PUT explicitly rejects a `status` field. Close/reopen have distinct permissions and routes.
- The HTTP response reports `changed=false` when the writer finds no effective mutation, using the saved version after the command. This prevents a UI from claiming a successful change on a repeat close/reopen or unchanged date edit.
- The initial real-SQL HTTP test failed before the request because the new permissions were absent from the catalogue. After adding them, the target test passed; a later full run exposed two exact permission-list architecture assertions, updated to include the new codes while preserving the FINANCE/MANAGER/VIEWER distinction. No SQL schema or journal write path changed.
- Commit-time recheck 2026-11-13T18:36:11+08:00 (uncommitted work from the previous round): Debug build 0 warnings/errors, GL HTTP 5/5, Architecture 1938/1938; two-thread full run 3128/3129 with only the existing `InventoryValuationTests.HundredThousandMovementsUseValuationIndex` SQL timeout (host memory pressure), passing 1/1 when run alone. No FI code changed.
- Full validation reached 3129/3129 with one test thread; SQL-backed DocGen reported 0 Error/579 Warning. A two-thread run reproduced the previously documented PUR quotation HTTP 404→500 flake (targeted 1/1 and serial full green); its investigation is recorded in PUR's optimization log. Additional HTTP assertions verify invalid dates/version and all five OpenAPI write operations; GL HTTP targeted 5/5 passed before the final full rerun.

## WU-FI-004a3 GL posting parent acceptance — 2026-11-12T17:23:00+08:00

- Review found that the existing SQL writer and service covered atomic insert, replay, cross-company isolation and rollback, but `gl_source_key`, `gl_journal` and `gl_journal_line` still allowed direct UPDATE/DELETE after commit. A read-only public API would not protect against another application path using the shared DbContext.
- Added one reversible migration with set-based UPDATE/DELETE triggers on the three FI-owned permanent tables. Affected rows raise 51023; a parent FK can reject DELETE first with 547. The trigger allows zero-row statements and INSERT, so normal atomic posting remains possible. No model columns or cross-module dependencies changed.
- The real-SQL test was red because source-key UPDATE succeeded. It then passed after migration, checking all three tables, unchanged counts and replay. The first green attempt showed that FK 547 precedes the parent trigger on DELETE; the assertion now accepts either integrity error while still requiring unchanged rows. No business assertion was weakened. The full backend suite and SQL DocGen were rerun after the change; see progress for final counts.
- The first parallel full run passed 3123/3124; an existing PUR quotation HTTP test expected 404 after soft-deleting a currency but received 500. Its targeted rerun passed 1/1, and a complete run with `--max-threads 1` passed 3124/3124. The failure did not recur, so no PUR product or test change was justified in this FI unit. Parallel SQL resource contention is the leading unconfirmed explanation; retained log: `logs/fi-004a3-full-retry.txt` (ignored by Git). SQL DocGen against `myerp` passed 0 Error/579 Warning, and EF reported no pending model changes.

## WU-FI-001d blocked retry — 2026-11-12T16:54:08+08:00

- The three-sample SQL memory gate passed at 5.130/5.028/5.133 GiB. The previously failing `NumberGeneratorTests.ParallelGenerationFromManyContextsNeverDuplicatesOrSkips` passed alone (1/1), and the full backend suite passed 3121/3121 with `--max-threads 1`. The FI-CREDIT-024 rollback test `CustomerLabelFailureRollsBackTheCreditWrite` passed alone (1/1), confirming zero committed limit/key rows on label failure.
- The first SQL DocGen run found seven missing PUR invoice/order/receipt tables in the production `myerp` schema. Existing committed migrations had not been applied there; `dotnet ef database update` completed, and SQL DocGen then reported 0 Error/579 Warning. No product code or test assertion changed. Review found no remaining FI-001d defect; the warnings are existing source coverage work, not schema mismatches.

## WU-FI-004a2k SQL checkpoint retry — 2026-11-12T16:40:45+08:00

- The memory gate passed at 4.802/4.819/4.774 GiB and independent SQL login passed. Initial fixture failure was EF `PendingModelChangesWarning`: the PUR source migration had created four tables but its model snapshot omitted them. Regenerating the snapshot closed the model gap; `dotnet ef migrations has-pending-model-changes` then reported no changes.
- The next fixture failure showed the invoice claim identity FK migration sorted before both referenced PUR tables. The production `myerp` migration history had no `Purchasing_Invoice*` entries, so its ID was moved after the invoice draft table migration. A fresh test database now migrates successfully.
- Targeted period endpoint/rollback test 1/1, `FinancePeriodWriteTests` 8/8, and full backend 3121/3121 passed on real SQL. Review: no FI business logic or assertion was weakened; the same test catches shared endpoints, adjacent dates and unchanged saved row/version after rejection. Parent 004a2 still owns posting shared-lock acceptance.

## WU-FI-004a3x1b2a GL posting service composition — 2026-11-06T13:45:54+08:00

- SQL memory gate 1.935/1.866/1.866 GiB: DEFER, so 004a3x1b2 was split into 004a3x1b2a (offline service composition, this unit) and 004a3x1b2b (SQL port, DI and real SQL acceptance). Problem: no `IGlPostingService` type existed, and a cross-company request would only be noticed by the SQL port after a transaction was opened. Change: `IGlPostingService` / `GlPostingService` in FI Application — company-scope check before any I/O, then `GlPostingAttemptRunner.PostAsync`. Tests failed compilation first (CS0246); Architecture 1540/1540, Debug solution 0 warnings/errors.
- Review: (1) The scope check applies only to well-formed requests, so malformed input stays 400 regardless of company context. (2) No DI registration yet: registering the service without a port would fail resolution at runtime; 004a3x1b2b registers port and service together. (3) No duplicated logic — retry budget, step order and outcome mapping stay in the runner.

## WU-FI-004a3x1b1 GL posting authority assembly and connection-loss rules — 2026-11-06T13:39:21+08:00

- SQL memory gate 1.988/2.081/2.027 GiB: DEFER, so no SQL fixture ran. 004a3x1b was split into 004a3x1b1 (offline assembly and connection-loss rules, this unit) and 004a3x1b2 (SQL port, service, DI and real SQL acceptance). Problem: the port's `ReadAuthorityAsync` had to map raw rows into `GlPostingAuthority`, keep MD accounts aligned with draft lines and detect corrupt reads; that logic would only be testable with SQL. Change: `GlPostingAuthorityRules` (Application) and `GlPostingConnectionRules` (Infrastructure). Tests failed compilation first (CS0246); after implementation the Debug build reported CA1859 (private helper returned an interface) and CA2201 (test used `new Exception`) — both fixed. Architecture 1533/1533, Debug 0 warnings/errors.
- Review: (1) Account codes are re-keyed with an ordinal dictionary, so a caller passing an ignore-case map cannot match `cash-a` to `CASH-A`. (2) Each distinct code is read once even when several lines use it; the result still has one entry per line because `GlJournalDraftRecheckRules` indexes by line. (3) The assembler deliberately does not reject closed periods, currency or mapping drift — the decision returns the specific 409/422 codes; only untrustworthy reads become 500. (4) Severity ≥ 20 SqlException is classified but cannot be constructed offline; 004a3x1b2 must cover it against SQL (killed session).

## WU-FI-004a3x1a GL posting attempt orchestration — 2026-11-06T13:32:11+08:00

- SQL memory gate 2.440/2.445/2.442 GiB: DEFER, so no SQL fixture ran. 004a3x1 was split into 004a3x1a (offline attempt order, this unit) and 004a3x1b (SQL port and service). Problem: the attempt delegate of 004a3x2 still left the step order, per-step failure phase, plan rebuilding and disposal to the SQL service, where they could only be tested with real SQL. Change: `GlPostingAttemptRunner` fixes that order over `IGlPostingTransactionPort`/`IGlPostingAttemptSession`, so 004a3x1b only implements I/O. Tests failed compilation first (CS0246); after implementation targeted 18/18, plus one boundary test (FI-GL-ATTEMPT-015) → 19/19, Architecture 1519/1519, Debug 0 warnings/errors.
- Review: (1) The runner, not the port, decides the failure phase, so an adapter cannot mislabel a commit failure as BeforeCommit and cause a blind retry after a possible commit. (2) Commit gets `CancellationToken.None`: a cancelled commit would leave the caller with an exception while the post might be committed. (3) Source identity and company are prechecked before the lock, because `GlPostingWriter.LockAsync` throws `ArgumentException` for them, which would otherwise become 500 `WRITE_FAILED` instead of 400/404. (4) Dispose failures are swallowed after the result is decided so a broken rollback cannot hide a Posted result.

## WU-FI-004a3x2 GL posting retry loop — 2026-11-06T13:23:39+08:00

- SQL memory gate 2.498/2.505/2.497 then 2.277/2.264/2.492 GiB: DEFER, so no SQL fixture ran. 004a3x was split into 004a3x2 (offline loop, this unit) and 004a3x1 (the SQL service). Problem: 004a3u and 004a3t each covered one step, but nothing tied them into a loop. The service and source adapters would each have rewritten the order, the shared counter and the lookup-failure handling, and those copies could drift. Change: `GlPostingRetryLoop.RunAsync` reuses `Classify`, `Resolve` and `RetryOrExhausted` and adds no second mapping. Tests failed compilation first (CS0246); after implementation the targeted run passed 24/24 and Architecture 1500/1500. The Debug build has 0 warnings and 0 errors.
- Review: (1) A lookup that throws is treated as incomplete (503) rather than propagated, so a broken recovery connection never surfaces as a raw 500 that hides a possibly committed post. A cancellation with a cancelled token is rethrown. (2) An exception from the attempt delegate is deliberately not caught: guessing a phase would risk a blind retry after a commit. Mapping exceptions belongs to the attempt adapter (004a3x1). (3) There is no backoff delay, because the writer's applocks already wait up to their timeout before 51021. Any backoff should be added only if real SQL retries in 004a3x1 show contention.

## WU-FI-004a3w GL posting writer — 2026-11-06T13:12:42+08:00

- SQL memory gate 2.948/2.904/2.910 GiB PASS. Tests were written first and failed compilation (CS0246 `GlPostingWriter`, CS0117 `ApplockNotGranted`); after implementation `FinancePostingWriterTests` 7/7 on real SQL and Architecture 1476/1476.
- Problem found while designing: an applock timeout surfaced as a custom SQL number that 004a3u would have classified as 500 `WRITE_FAILED`, so a briefly busy source would fail instead of retrying. Change: the writer raises 51021 for timeout/lock-deadlock and 51022 for other lock failures, and `GlPostingFailureRules` retries 51021 like 1205/1222 within the same budget.
- Review: (1) the period lock resource string was duplicated in `GlPeriodWriteService`; it now calls `GlPostingWriter.PeriodLockResource`, so period edits and postings cannot drift onto different resources. (2) A failed `SaveChanges` leaves plan rows tracked as Added and would re-insert them on the next save; the writer detaches them and rolls back, and FI-GL-WRITE-003 proves the same context can post afterwards. (3) The writer refuses unrelated pending changes and already-written plans so a retry cannot carry stale rows into the posting transaction. (4) Rollback failures after a broken connection are swallowed only for `DbException`/`InvalidOperationException` so the original SQL error number reaches 004a3u.

## WU-FI-004a3v GL posting error translations — 2026-11-06T09:02:10+08:00

- SQL memory gate returned 1.441/1.462/1.437 GiB DEFER; no SQL fixture ran. Problem: 22 `FI.GL_POSTING.*` codes had no frontend translation or whitelist, so any future posting screen would show only "unknown error" — and a generic failure text for 503 `RESULT_UNKNOWN` would mislead users into thinking nothing was posted. Change: `gl-posting-errors.ts` (whitelist, `glPostingErrorKey`, `glPostingResultUncertain`) plus trilingual `errors.FI.GL_POSTING`. Tests failed first (ArchitectureTests 5 red: missing keys/file), then green.
- Review (myerp-ux-review message checks, no screen yet): 500 messages point to the trace ID (B2); uncertain-commit messages never claim nothing was posted and tell the user to reload first (A1/A5); rolled-back messages say so explicitly. An ArchitectureTests check keeps the frontend whitelist aligned with backend constants by reflection so new codes cannot be forgotten. Screenshot review deferred to WU-FI-004c.

## WU-FI-004a3u posting SQL failure classification — 2026-11-06T08:53:21+08:00

- SQL memory gate returned 0.824/0.861/0.906 GiB DEFER; no SQL fixture ran. Problem: 004a3t resolved unknown commits, but nothing said which writer exceptions are unknown, retryable or final, nor bounded retries, so a deadlock loop or a timeout reported as 500 was possible. Change: `GlPostingFailureRules` (pure, takes the SQL error number, no SqlClient dependency) plus `RETRY_EXHAUSTED`/`WRITE_FAILED` codes; tests failed compilation first (CS0246), then 36/36.
- Review: unknown-result paths deliberately skip the budget check (the work may be committed; the committed lookup must run), and the timeout resolution's `RetryPosting` reuses `RetryOrExhausted` with the same counter so the two rules cannot loop forever. Architecture 1448/1448, Debug build 0 warnings/errors.

## WU-FI-004a3t posting timeout resolution — 2026-11-06T08:46:57+08:00

- SQL memory gate returned 1.462/1.597/1.609 GiB DEFER; no SQL fixture ran. Problem: BR-FI-028 required a fresh committed lookup after an unknown commit, but no rule said which lookup results may be reported, retried or must stay unknown, so each source adapter could call an in-flight result a success. Change: `GlPostingTimeoutRules` reuses `GlSourceReplayRules` and `GlPostingOutcomeRules` (no second mapping) and adds 503 `RESULT_UNKNOWN`; tests failed compilation first (CS0246), then 9/9.
- Review: the candidate is validated before the lookup-completed flag so caller errors are not hidden as 503; supplementary boundaries added (version > int.MaxValue, foreign company with an incomplete lookup). Architecture 1412/1412, Debug build 0 warnings/errors.

## WU-FI-004a3s posting outcome mapping — 2026-11-06T08:40:41+08:00

- SQL memory gate returned 2.017/2.018/2.029 GiB DEFER; no SQL fixture ran. Problem: decision, preflight and write-plan statuses had no single external mapping, so 004b HTTP and each source adapter would each choose `changed` and 400/404/409/422/500 themselves. Change: `GlPostingOutcomeRules` plus `GlPostingErrors` constants; tests failed compilation first (CS0246), then 11/11.
- Review: the implementation's guard that a `Ready` plan cannot turn a malformed `Ready` decision (no period or a journal ID) into `ProceedToWrite` had no test; a supplementary test now covers it, plus keeping the line number on internal draft faults without exposing a journal ID. An exhaustive enum test guards future statuses. Architecture 1403/1403, Debug build 0 warnings/errors.

## WU-FI-004a3r posting write plan — 2026-11-06T08:33:24+08:00

- SQL memory gate returned 1.893/1.888/1.894 GiB DEFER; no SQL fixture ran. Problem: a `Ready` decision carried only a period ID, so the future writer would have had to map draft snapshots to entities itself and could save a mutated draft or a prefix. Change: `GlPostingWritePlanRules` builds all rows or none, re-validating draft sequence 1..n and recomputing the BR-FI-026 balance against header totals, and reusing `GlSourceReplayRules` for canonical source text instead of copying its checks. Tests failed compilation first (CS0246), then 8/8.
- Review: `checked` int conversion is safe because the replay rule already bounds the version; empty results are shared immutable instances, while Ready rows are new per call (tested). Architecture 1390/1390 and Debug build with zero warnings/errors. SQL insert order, locks and whole rollback remain WU-FI-004a3.

## WU-FI-004a3o cross-line posting recheck — 2026-11-05T21:21:10+08:00

- SQL memory gate returned 2.151/2.153/2.153 GiB DEFER; no SQL fixture ran. Review found that the posting-time recheck compared each line with a fresh MD result but could accept an internally contradictory set of repeated account snapshots if the draft or supplied results changed after assembly. Tests first failed compilation for missing `SnapshotConflict`; the recheck now indexes fresh scalar snapshots by account ID and code after each line's individual checks.
- A later conflicting ID/code/name/version returns the later one-based line and no allowed decision, while identical repeated legs remain valid. Supplementary malformed-code cases pass. Architecture 406/406, DocGen 22/22 and Debug build with zero warnings/errors passed. Authoritative MD reads, period lock, permanent snapshot and atomic SQL posting remain WU-FI-004a3.

## WU-FI-004a3m posting preflight composition — 2026-11-05T21:08:09+08:00

- SQL memory gate returned 1.484/1.468/1.522 GiB DEFER; no SQL fixture ran. The header and account/mapping recheck were separately callable, leaving a posting writer able to take only the header decision. A new combined pure gate requires both and exposes a period ID only on complete success. Tests first failed compilation without the gate; targeted 4/4 then passed.
- Review added missing-account, Closed-period, stale-account and malformed-source cases. Rejections retain the appropriate reason and line without leaking an allowed period. Architecture 392/392, DocGen 22/22 and Debug build with zero warnings/errors passed. SQL source authority, durable snapshots, MD usage and atomic posting still require WU-FI-004a3.

## WU-FI-004a3l header preflight — 2026-11-05T21:01:57+08:00

- SQL memory gate returned 1.778/1.775/1.773 GiB DEFER, so no SQL fixture or full integration suite ran. The posting header lacked a single offline decision combining saved source company, canonical base currency, complete balanced draft and exactly one Open period for the saved business date. New tests first failed compilation without the rule; the nine targeted tests then passed.
- Review found a purported allowed draft with a missing line threw `NullReferenceException`. A supplementary test reproduced it, then an explicit incomplete-line check returned `InvalidDraft` without a period ID. Architecture 386/386, DocGen 22/22 and Debug build with zero warnings/errors passed. The transactional writer must still reload saved source, company currency, MD accounts, mapping and periods under the company lock.

## WU-FI-004a3k posting-time amount recheck — 2026-11-05T17:20:01+08:00

- SQL memory gate returned 1.726/1.727/1.728 GiB DEFER; no SQL fixture ran. Code review found the existing recheck accepted a draft whose line amounts or header totals had changed after assembly. New tests first failed compilation without `InvalidBalance` and `BalanceStatus`; the recheck now recomputes BR-FI-026 amounts and compares both totals before allowing the draft.
- A test initially used a valid one-sided credit as a purported invalid-side case. Corrected it to a real both-sided leg. Review also separated line-balance failure from header-total mismatch so the latter does not report nested `BalanceStatus.Allowed`. The fresh MD/mapping check and pending SQL transaction boundary remain intact.

## WU-FI-004a3j posting-time draft recheck — 2026-11-05T17:11:34+08:00

- SQL memory gate returned 1.701/1.707/1.710 GiB DEFER, so no SQL fixture or migration ran. A draft could be valid when assembled but stale by the time a posting transaction saves it. Added a pure recheck against caller-supplied fresh MD results and FI mapping version; tests first failed to compile because the rule was absent.
- Review found that a changed mapping was masked by a missing account list. Moved mapping comparison ahead of list-count validation; supplementary repeated-account/version test passed, then Architecture 371/371 passed. Nine new cases cover matching, rename/version, mapping, inactive/nonpostable, missing/foreign/changed account, malformed draft and repeated accounts. DocGen 22/22 and Debug build 0 warnings/errors passed. The caller still must acquire the company period lock and fetch authoritative data within the same SQL transaction.

## WU-FI-004a3i account rejection reason — 2026-11-05T17:04:11+08:00

- SQL memory gate returned 1.806/1.787/1.776 GiB DEFER; no SQL fixture ran. A failed MD account preflight was flattened to `InvalidAccountSnapshot`, so later posting error mapping could not identify the reason. Tests first failed to compile without `AccountStatus`; the draft now returns the original MD status and line while exposing zero lines and totals.
- Review found that a nominally allowed snapshot with null account code threw `NullReferenceException`. A follow-up red test reproduced it, then a code shape check returned `InvalidSnapshot`. Missing candidate/result remains reasonless, and amount failures have no account reason. Authoritative MD checks and user facing mapping remain the parent work unit.

## WU-FI-004a3h incomplete draft leg — 2026-11-05T16:58:05+08:00

- SQL memory gate returned 1.967/1.966/1.965 GiB DEFER, so no SQL fixture ran. Review found that an incomplete candidate array or a missing account result threw `NullReferenceException` after earlier lines had been assembled in memory.
- Test-first: the targeted test failed with that exception. A guard now returns `InvalidAccountSnapshot` with the one-based line and empty lines/totals. The same test covers both missing candidate and missing account result; targeted class passed 8/8. The transactional writer and permanent snapshot remain open.

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

## WU-FI-004a3n decision review (2026-11-05T21:14:40+08:00)

- Test-first signal: the new decision tests failed compilation with CS0246 for `GlPostingDecision`; the implementation then passed the five initial cases. Two supplementary cases cover changed mapping for a new source and a mismatched saved identity.
- Review finding: a committed replay must bypass current period/account checks, since those conditions can change after posting. A foreign-company caller must be rejected before any saved journal ID is returned. The pure decision exposes one of `JournalId` or `PeriodId`, never both; failed decisions expose neither.
- Verification: Architecture 399/399, DocGen 22/22, Debug solution build zero warnings/errors. SQL memory gate sampled 1.609/1.562/1.563 GiB and returned DEFER; no SQL fixture, migration, HTTP or UI acceptance is claimed. The future writer must still prove its committed lookup and lock order.

## WU-FI-012f depreciation schedule review (2026-11-05T21:31:36+08:00)

- Test-first signal: the new tests failed to compile with CS0246 because `FixedAssetDepreciationInput` did not exist. The first implementation then failed to build with CS1573: a partial `<param>` doc was not allowed under warnings-as-errors. The note was moved into the summary.
- Review finding: a 0-decimal currency with a sub-unit base (cost 1, life 3) produced 0-amount months that were still marked `Allowed`, and a 0 journal would fail BR-FI-026. Added `ZeroAmount` and kept `FullyDepreciated` for months after the cap. A further case (cost 5, life 8, 0 decimals) proves that a rounded-up base is capped and no month goes negative (ADR-078).
- Verification: targeted 28/28, then 29 tests with the life-1/exhausted supplement, Architecture 435/435, DocGen 22/22, Debug solution build 0 warnings/errors. SQL memory gate 2.050/1.971/1.974 GiB returned DEFER, so no SQL, HTTP or UI acceptance is claimed.

## WU-FI-012g depreciation batch outcome review (2026-11-05T21:39:42+08:00)

- Test-first signal: the new tests failed to compile with 18 CS0246 errors because `FixedAssetDepreciationCandidate` and related types did not exist. The first implementation then failed the warnings-as-errors build with CA1859 (`IReadOnlyDictionary` parameter on a private helper); it now takes the concrete `Dictionary`.
- Review finding: a summary could silently accept attempts for skipped or unknown assets, or a rolled-back attempt without an errorCode, which would show a failed asset without a cause (BR-FI-102). `Summarize` now rejects unexpected, duplicate, missing and inconsistent attempts with the asset ID and returns no counts. A supplementary test proves a closed period accepts no posting attempt and keeps asset-ID order.
- Verification: SQL memory gate 1.879/1.887/1.885 GiB DEFER; targeted 11 batch tests, Architecture 446/446, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-012h disposal cutoff rule review (2026-11-05T21:46:18+08:00)

- Test-first signal: the new tests failed to compile (CS0246/CS0103) because `FixedAssetDisposalInput`, `FixedAssetDisposalStatus` and related types did not exist. The first implementation passed all 28 targeted tests.
- Review finding: the gap scan stops at the end of the useful life, so a disposal far after life end (year 9999) costs at most 600 iterations. It must still find a gap inside the life. A supplementary test proves that the gap is reported and that a complete history gives zero book value. Card validation reuses `FixedAssetDepreciationRules`, so field errors cannot drift between depreciation and disposal.
- Verification: SQL memory gate 1.817/1.845/1.844 GiB DEFER; targeted 29 disposal tests, Architecture 475/475, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-012i acquisition Draft field rule review (2026-11-05T21:53:31+08:00)

- Test-first signal: the new tests failed to compile (22 CS0246, 52 CS0103) because `FixedAssetAcquisitionDraftInput` and related types did not exist. The first implementation passed all 47 targeted tests.
- Review finding: with an invalid cost, a residual above the SQL `decimal(19,4)` maximum must still be reported. Otherwise the user fixes cost and only then learns of a second error. A supplementary test proves both errors are returned. Amount scale is skipped when the currency is invalid, so the form shows only the currency error.
- Verification: SQL memory gate 1.945/1.946/1.960 GiB DEFER; targeted 48 Draft tests, Architecture 523/523, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-013e tax detail line component rule review (2026-11-05T22:00:59+08:00)

- Test-first signal: the new tests failed to compile (CS0246/CS0103) because `TaxDetailLineInput`, `TaxRole` and related types did not exist. The first implementation passed 32 of 34 targeted tests; both failures were test inputs that broke two rules at once (a cost account on recoverable input, and an invalid gross next to an invalid tax). The inputs were corrected and a separate assertion now proves both errors are reported together.
- Review finding: when one credit component has a wrong scale, over-credit on another valid component must still be reported, otherwise the user fixes the scale and only then learns the credit is too large. A supplementary test proves both errors are returned. Account checks are skipped while the treatment is missing or invalid, so the form shows only the treatment error.
- Verification: SQL memory gate 1.902/1.891/1.887 GiB DEFER; targeted 35 tax line tests, Architecture 558/558, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-013f tax return period rule review (2026-11-05T22:08:19+08:00)

- Test-first signal: the new tests failed to compile (20 CS0246) because `TaxReturnPeriod`, `TaxReturnSource` and related types did not exist. Before the first test run, review found that a total above the SQL maximum would also blame the derived net, so one cause produced two errors; the net is now checked only when its components fit. The first test run then passed 40 of 40.
- Review finding: two calendar rows with the same period ID covering one date must not be collapsed and silently assigned. Assignment counts covering rows, not distinct IDs, so the date is `Ambiguous`; a supplementary test proves it.
- Verification: SQL memory gate 1.988/2.006/1.995 GiB DEFER; targeted 41 period tests, Architecture 599/599, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-013g tax-to-GL reconciliation rule review (2026-11-05T22:15:42+08:00)

- Test-first signal: the new tests failed to compile (24 CS0246) because `TaxGlReconciliationRules` and its records did not exist. Before the first run, review corrected one test expectation: a nonzero tax without account is a `glAccountId` error even when its amount scale is also wrong, so both fields are reported once each. The first test run then passed 36 of 36.
- Review finding: comparing only account or period totals would let two wrong sources offset each other. Matching is per detail through the stamped tax detail ID, totals are informational only, and a supplementary test proves offsetting differences still block approval.
- Review finding: a manual journal on a tax account or a leg of another period's detail would be invisible to a detail-driven loop. Every untagged or unknown-detail line is reported as `Unmatched` with its GL line and journal IDs.
- Verification: SQL memory gate 2.061/2.063/2.105 GiB DEFER; targeted 36 tests, Architecture 635/635, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-014e realised exchange-difference rule review (2026-11-05T22:24:13+08:00)

- Test-first signal: the new tests failed to compile (CS0246/CS0103) because `FxRealisedDifferenceRules` and its records did not exist. The first test run then passed 35 of 35.
- Design finding (problem → cause → change → effect): per-allocation rounding of a proportional base can over-release at a midpoint (four allocations of 0.005 rounding to 0.01 each exceed a 0.02 base) and leave the final allocation negative. Release is computed cumulatively and the allocation's base is the increment, so every release is non-negative and the total equals the historical base exactly (ADR-084).
- Review finding: `historical base × cumulative original` can exceed the decimal range for `decimal(19,4)` maxima. The rule falls back to multiplying by the ratio (< 1, 28 significant digits); a supplementary test proves the half-way release of a maximum exposure is exact.
- Review finding: 014a will run allocations in separate transactions. A supplementary test proves that continuing from the committed discharge gives the same per-allocation results as one batch, and a committed base that does not match the cumulative formula is rejected as `Inconsistent` instead of being continued.
- Verification: SQL memory gate 2.066/2.066/1.985 GiB DEFER; targeted 37 tests, Architecture 672/672, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-014f period-end valuation and unrealised overlay review (2026-11-05T22:33:32+08:00)

- Test-first signal: the new tests failed to compile (CS0246) because `FxUnrealisedOverlayRules` and its records did not exist. The first run then failed 9 of 36.
- Design finding (problem → cause → change → effect): the "no valuation yet" state `(0, 0, 0)` was rejected as `Inconsistent` because its anchor is below the open original. An anchor of 0 now means no overlay (overlay and release must be 0), so a first valuation and a release before any valuation are valid (ADR-085).
- Test finding: two expectations were derived wrongly (scale case used an anchor above the gross, so `OutOfRange` correctly won; the decimal-overflow case did not overflow). Inputs were corrected, not the assertions weakened, and the overflow case now really exercises the ratio fallback.
- Review finding: per-allocation proportional overlay release could change sign at a midpoint or leave a residual overlay after full settlement. Attached overlay is cumulative `round(anchor overlay × open ÷ anchor open)`, so every release keeps the overlay's sign and full settlement leaves exactly zero; supplementary tests cover a value → revalue → settle round trip and a release recorded without an anchor.
- Verification: SQL memory gate 1.932/1.931/1.932 GiB DEFER; targeted 38 tests, Architecture 710/710, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-014g valuation policy and closing-rate eligibility review (2026-11-05T22:42:52+08:00)

- Test-first signal: the new tests failed to compile (CS0246/CS0103) because `FxValuationEligibilityRules` and its records did not exist. The first run then passed 41/41.
- Design finding (problem → cause → change → effect): BR-FI-117 makes each exposure its own atomic result, so a run with some excluded sources must still post the eligible ones. `PostingAllowed` is therefore "Post mode, resolved policy, at least one eligible", and exclusions are reported per source (ADR-086).
- Review finding: the reversed effective range check sat inside policy resolution, where it could never run (an applicable version always has from ≤ date ≤ to), so a malformed version would be silently ignored. It is now an input error on `policies`; a supplementary test covers it.
- Review finding: rate selection rescanned every rate for every candidate (candidates × rates). Rates of the policy source/type are now indexed by currency pair once per call; a supplementary 400-candidate × 240-rate test checks per-pair resolution.
- Supplementary tests: inclusive effective range on both ends, invalid content winning over unsupported rate type, duplicate policy key across companies, invalid candidate ID/side/company/account.
- Verification: SQL memory gate 1.996/1.991/1.996 GiB DEFER; targeted 48 tests, Architecture 758/758, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-014h FX journal composition review (2026-11-05T22:51:36+08:00)

- Test-first signal: the new tests failed to compile (CS0246) because `FxJournalCompositionRules` and its records did not exist. After one nullable-flow compile fix (CS8602 on the checked `Realised` operand) the first run passed 27/27.
- Design finding (problem → cause → change → effect): BR-FI-118 asks for a *distinct* overlay reversal leg, but folding it into the control discharge line would still balance and hide the reclassification from reconciliation. The reversal is its own `OverlayReversalControl` line plus a reversal of the original unrealised account (ADR-087), so 014b can reconcile historical discharge and overlay separately.
- Review finding: a caller could pass a valuation/release result computed for the other side; line directions would then flip silently while still balancing. The rule re-derives the signed difference from the side and rejects a mismatch as `Inconsistent`; tests cover both sides.
- Review finding: control or clearing reused as a gain/loss account would net lines inside one account and act as a plug. Such collisions are `Duplicate`; shared gain/loss accounts stay allowed because a policy may use one FX difference account.
- Supplementary tests: 128-case settlement grid and ±max valuation grid asserting balance, control movement = historical + overlay (or delta), clearing = settlement base and P&L = signed difference.
- Verification: SQL memory gate 1.827/1.802/1.845 GiB DEFER; targeted 29 tests, Architecture 787/787, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-014i FX valuation run summary review (2026-11-05T22:59:13+08:00)

- Test-first signal: the new tests failed to compile (CS0246) because `FxValuationBatchRules` and its records did not exist; the first run passed 26/26.
- Design finding (problem → cause → change → effect): a zero valuation delta is a valid outcome, but counting it as posted would report a change with no GL journal, while counting it as skipped would hide that it was valued. It is its own `Zero` result with no attempt, and it prevents `Success` (BR-FI-122), so the response never shows unqualified success when some sources produced nothing (ADR-088).
- Design finding: Preview must not claim a Posted valuation (BR-FI-119). Preview sources with lines are `Previewed` with a separate preview net, and any posting attempt in Preview rejects the summary.
- Review finding: the summary trusted `PostingAllowed`, so a Post-mode eligibility claiming no posting (or a Preview claiming posting) would still be summarised. It is now cross-checked against mode and eligible count (`InvalidEligibility`); 2 assertions added.
- Review finding: a journal composed for another delta would be counted with the wrong amount; the rule requires entry ⇔ non-zero delta and journal debit total = |delta| (`InconsistentComputation`).
- Verification: SQL memory gate 2.039/2.006/2.011 GiB DEFER; targeted 26 tests, Architecture 813/813, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-FI-004a3p persisted line account snapshot (2026-11-06T02:53:12+08:00)

- Test-first signal: SQL memory gate 2.798/2.794/2.794 GiB PASS. Baseline AccountTests + Finance* real SQL 25/25 passed. The extended `FinanceJournalSchemaTests` then failed 2/4 on real SQL (missing `account_name`/`account_version`/`mapping_version` columns and check).
- Design finding (problem → cause → change → effect): the offline draft carried account name/version/mapping version but the table could only persist ID/code, so a Posted line could not prove which MD account version or mapping it used after a rename. Added three NOT NULL columns and a positive/non-blank check (ADR-089); history no longer depends on current MD.
- Design finding: EF adds empty/zero defaults when adding NOT NULL columns. Instead of inventing a backfill, the check makes the migration fail if any legacy line exists, so no fabricated snapshot can enter history.
- Verification: migration Up/Down/Up on `myerp`; targeted real SQL (AccountTests, Finance*, MasterDataUsageTests) 30/30; full backend result recorded in progress.

## WU-FI-014j FX difference read／export reconciliation review (2026-11-06T03:13:13+08:00)

- Test-first signal: SQL memory gate 2.430/2.343/2.345 GiB DEFER, so this offline slice was chosen. The new tests failed to compile (CS0246) because `FxDifferenceReconciliationRules` and its records did not exist.
- Test defect (problem → cause → change → effect): the first run failed one test because the source helper used `100 + id` as commit sequence, so 10,001 generated sources passed the cutoff 500 and were rejected as `AfterCutoff`. This showed the cutoff check works; the helper now caps the sequence at the cutoff and 40/40 passed.
- Design finding: a source state could be counted from journal presence, but a deleted draft or rolled-back attempt carrying a GL leg would then look Posted. State is authoritative: only `Posted` sets `CountsAsPosted`, non-Posted sources must have no journal ID, and any leg tagged to them is a `LegOnNonPostedSource` difference (ADR-090).
- Design finding: CSV escaping of every cell would turn negative amounts into text (`'-100`). Only text cells (headers, reference, codes, enum names, ID lists) are escaped; numbers are written invariant and stay numeric. Leading tab／CR is also escaped in addition to the INV `= + - @` rule.
- Review finding: page offset `(pageNumber − 1) × pageSize` could overflow `int` for large page numbers; it is computed as `long` and a page past the end is empty. Two boundary tests added (exact-multiple last page, `int.MaxValue` page) plus a deleted-source leg test; a weak `Assert.False(x is false)` was replaced by explicit assertions.
- Verification: targeted 42 tests, Architecture 855/855, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## WU-BI-001a0 FI AR／AP detail as-of rule review (2026-11-06)

- Test-first signal: SQL memory gate 1.832/1.759/1.697 GiB DEFER, so this offline slice was chosen. The 33 new tests failed to compile (32 × CS0246) because `ArApDetailAsOfRules` and its records did not exist; after implementation they passed on the first run.
- Review finding (problem → cause → change → effect): two principal records for the same FI source (module, type, source ID) passed validation because only the principal ID was checked for duplicates, so a replayed source would be counted twice in rows and totals (FI-RPT-005). A second `sourceId` Duplicate input error was added with a test; the existing duplicate-ID test now uses a distinct source so each error is isolated.
- Design finding: an allocation dated before its principal (including a principal after `asOf`) would reduce a balance that did not yet exist as of that date; it is a named `AllocationBeforePrincipal` incompleteness instead of being silently dropped or counted.
- Design finding: per-principal credit and settlement can each be within `decimal(19,4)` while their sum is not; the overflow check covers the sum as well, and full-set totals are checked separately with a null principal ID.
- Supplementary tests: negative open when zero-open history is off, same-source replay. Verification: targeted 35, Architecture 890/890, Debug solution build 0 warnings/errors. No SQL, HTTP or UI acceptance is claimed.

## 2026-11-06T03:44:44+08:00 WU-BI-001a2 — FI report continuation token rule

- Test-first signal: SQL memory gate 1.498/1.505/1.481 GiB DEFER, so this offline slice was chosen. `FiReportCursorTokenRulesTests` failed to compile (CS0246) because `FiReportCursorTokenRules` and its records did not exist. After implementation all 89 cases passed on the first run.
- Review finding (problem → cause → change → effect): the first draft serialized the page size through a stray conditional on the page number. It was harmless, because the page number is always ≥ 2 after validation, but it obscured the canonical format. It was replaced with a plain `Number(b.PageSize)`, and the exact-payload test pins the format.
- Design finding: `Enum.TryParse` accepts numeric, whitespace and comma forms, and integer parsing would accept alternative spellings. The parsed payload is re-serialized and must equal the signed text byte for byte. Base64url must also be canonical (zero trailing bits). Each signed state therefore has exactly one token text.
- Design finding: check order decides the HTTP code. Request, then format, then MAC, then payload, then binding, then expiry. A tampered or mismatched token is 400 even when the manifest has expired, and a missing or short key is reported as a 500 misconfiguration rather than a client error. The MAC is compared with `CryptographicOperations.FixedTimeEquals`.
- Verification: targeted 89/89, Architecture 1023/1023, DocGen 22/22, Debug solution build 0 warnings/errors. No SQL manifest, permission, HTTP or UI acceptance is claimed.

## 2026-11-06T03:53:23+08:00 WU-BI-001a3 — FI report manifest rule

- Test-first signal: the SQL memory gate returned DEFER (1.876/1.881/1.870 GiB), so this offline slice was chosen. `FiReportManifestRulesTests` failed to compile (CS0246) because `FiReportManifest`／`FiReportManifestRules` did not exist. After implementation, all 63 initial cases passed on the first run.
- Review finding (problem → cause → change → effect): the row-level AR／AP check `open == original − credit − settlement` used unchecked-context decimal arithmetic. A hand-built or corrupt row near `decimal.MaxValue` would throw `OverflowException` instead of returning `Inconsistent`. The check now runs in `checked` with a catch that maps overflow to `rows` Inconsistent, and a supplementary test pins this (64 cases).
- Design finding: binding validation is shared with ADR-093 (`FiReportCursorTokenRules.ValidateBinding` became `internal`), so a manifest can only bind what a token can carry.
- Design finding: the digest uses length-prefixed fields, trailing-zero-free amounts and UTC ticks. SQL `decimal(19,4)` scale or a different stored offset therefore cannot cause a false `Inconsistent`, while a text-boundary shift still changes the digest.
- Design finding: member IDs are compared as an ordered sequence, not as a set. A reordered re-read (for example, a posting-time change) is inconsistent rather than silently re-paged.
- Verification: targeted 64/64, Architecture 1087/1087, DocGen 22/22, Debug solution build with 0 warnings and 0 errors. No SQL manifest, permission, HTTP or UI acceptance is claimed.

## 2026-11-06T04:01:27+08:00 WU-BI-001a4 — FI AR／AP detail export rule

- Test-first signal: the SQL memory gate returned DEFER (1.752/1.758/1.790 GiB), so this offline slice was chosen. `ArApDetailExportRulesTests` failed to build because `ArApDetailAsOfRules.Export` did not exist (CS1574 on the cref, then missing members). After implementation, all 34 initial cases passed on the first run.
- Review finding (problem → cause → change → effect): the row check and the totals sum shared one `checked` block whose overflow catch returned `rows`. Consistent rows whose sum overflows were therefore reported as a bad row instead of bad totals. Row checks now run first (per-row overflow → `rows`), and the totals sum has its own catch (→ `totals`). A supplementary test pins this (35 cases).
- Design finding: `PageArAp` and the new `ExportArAp` share one `ArApRereadErrors` helper, so the export can never accept a re-read that paging would reject.
- Design finding: export ignores the bound page size and renders every manifest member; it is byte-identical to a direct `Export` of the same report, so screen and file cannot diverge.
- Verification: targeted 35/35, Architecture 1122/1122, DocGen 22/22, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06T04:07:12+08:00 WU-BI-001a5 — FI other income／expense manifest-verified export

- Test-first signal: the SQL memory gate returned DEFER (1.882/1.872/1.863 GiB), so this offline slice was chosen. `OtherIncomeExpenseManifestExportRulesTests` failed to build with CS0117 because `FiReportManifestRules.ExportOtherIncomeExpense` did not exist. After implementation, all 13 initial cases passed on the first run.
- Refactor (problem → cause → change → effect): the AR／AP export had its request checks inline, and `PageOtherIncomeExpense` had its re-read checks inline. A second export would have copied both. `ExportInputErrors` is now shared by both exports, and `OtherRereadErrors` by the other income／expense page and export. Page and export therefore cannot diverge, and a test asserts identical errors for one re-read. The existing AR／AP and manifest tests stayed green.
- Review addition: a re-read row whose signed amount disagrees with its gross must return only `Inconsistent` errors without an exception or file. This is now covered (14 cases).
- Verification: targeted 14/14, Architecture 1136/1136, DocGen 22/22, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a6 FI report problem mapping (ADR-096)

- Test-first: the SQL memory gate returned DEFER (1.768/1.762/1.694 GiB), so this offline slice was chosen. `FiReportProblemRulesTests` failed to build with CS0246 (`FiReportProblem` missing). After implementation, all 66 initial cases passed.
- Review finding (problem → cause → change → effect): the rule results put client filters and server-wired inputs in one list, so an endpoint could report a wiring bug as a client 400 or leak `Inconsistent` field names. The cause was that nothing classified which fields the client actually sends. A frozen `ClientFields` whitelist now marks the client filters, and everything else is 500 with details kept for the log only. As a result, a wiring bug can no longer become a client 400 and no internal field name reaches the client. The first draft exposed `ClientFields` as a `HashSet` that a caller could cast and mutate; it is now a `FrozenSet`.
- Added tests: Expired／KeyInvalid／Mismatch in an unexpected collection is 500, and the whitelist is immutable (71 cases).
- Verification: targeted 71/71, Architecture 1207/1207, DocGen 22/22, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a7 FI report actor scope digest (ADR-097)

- Test-first: the previous round wrote `FiReportActorScopeRulesTests` and `FiReportActorScopeRules` but ran out of quota before committing. This round the SQL memory gate returned PASS (3.103/3.097/3.101 GiB). The uncommitted slice was still finished first, as the round rules require. The build was clean and all 36 initial cases passed.
- Review finding (problem → cause → change → effect): the canonical text writes the report kind by enum name, so renaming a `FiReportKind` member would silently change every digest. No test pinned the names. A theory now pins each kind name, and ADR-097 requires a version-prefix bump for any format change. As a result, an accidental rename fails a test instead of silently invalidating live tokens. The digest also includes the granting role IDs, beyond the backlog wording, so a revoked role invalidates tokens too.
- Added tests: kind-name pinning (3 cases, 39 total).
- Verification: targeted 39/39, Architecture 1246/1246, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a8 FI report continuation coordinator (ADR-098)

- Test-first: the SQL memory gate returned DEFER (2.215/2.284/2.294 GiB). `FiReportContinuationRulesTests` was written before the code and failed to build with CS0246 (missing types). After the implementation, 34 of 35 cases passed. The one failure was a defect in the test itself: it compared rows that hold an `AllocationIds` list, and that list is compared by reference. The test now compares principal IDs and totals.
- Review finding 1 (problem → cause → change → effect): the first draft passed the page number through a `[ThreadStatic]` field. The cause was that the per-kind page delegate took only the manifest. That hidden state was fragile under async continuations. The delegate now receives `(manifest, pageNumber)` explicitly, so the coordinator has no static state.
- Review finding 2: the token's expiry cannot be checked before the manifest is loaded. The token is therefore validated twice: once with `DateTimeOffset.MaxValue`, which proves the signature and binding before any I/O, and once with the loaded `ExpiresAt`. This costs one extra HMAC and guarantees that no database read happens for a forged token.
- Added tests: an empty page beyond the last page, and the signed manifest ID carried into the next token (37 total).
- Verification: targeted 37/37, Architecture 1283/1283, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a9 FI report first-page coordinator (ADR-099)

- Test-first: the SQL memory gate returned DEFER (1.982/1.976/2.019 GiB). The 28 new cases in `FiReportContinuationRulesTests.First.cs` (the class became `partial` to reuse the 001a8 fixtures) failed to build with CS0117 (missing `FirstArAp`／`FirstOtherIncomeExpense`), then passed 65/65 after the implementation.
- Review finding 1 (problem → cause → change → effect): `FiReportCursorTokenRules.Issue` only checks the key at the end, so a short key would have been found after an FI read and a manifest save. The coordinator checks the key length (and the TTL and clock) before the read, so a misconfigured server never writes an orphan manifest.
- Review finding 2: the page-1 call after save re-validates a report that `Create*` just verified, so it cannot fail in practice. It is kept as a defensive check (one extra pass over at most 10,000 rows); if it ever failed after save, the saved manifest is orphaned until its TTL and no page is returned. Recorded in ADR-099.
- Verification: targeted 65/65, Architecture 1311/1311, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a10 FI report single-entry dispatch (ADR-100)

- Test-first: the SQL memory gate returned DEFER (1.900/1.951/1.963 GiB). The 21 new cases in `FiReportContinuationRulesTests.Read.cs` failed to build with CS0117 (missing `ReadArAp`／`ReadOtherIncomeExpense`). After the implementation, two cases failed because they compared page rows by record equality: the rows hold nested collections, so records compare them by reference. Those two tests now compare row IDs and totals; the coverage is the same. Then 86/86.
- Review finding 1 (problem → cause → change → effect): if the dispatcher only checked the callbacks of the chosen path, an endpoint that forgot `loadManifest` would pass every first-page check and fail only when a user reached page 2. The entry checks all four callbacks (plus request and actor) before dispatching, so miswiring fails on the first call.
- Review finding 2: dispatching on `string.IsNullOrWhiteSpace(token)` would turn a blank `token` query value into a new first page with a new cutoff, which BR-FI-131 forbids. Dispatch is on `null` only; a blank token reaches `Continue*` and is 400 `token: Required` with no I/O. Recorded in ADR-100.
- Verification: class 86/86, Architecture 1332/1332, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a11 FI report frozen export coordinator (ADR-101)

- Test-first: the SQL memory gate returned DEFER (2.053/2.048/1.979 GiB). I wrote the 27 cases in `FiReportContinuationRulesTests.Export.cs` against the missing `ExportArAp`／`ExportOtherIncomeExpense`／`FiReportExportResult`. A separate red build was not recorded because the implementation was written in the same step. After implementation, one case failed twice because of a wrong assumption in the test about the CSV layout: the CSV has a metadata preamble before the header row, so it does not start with the header and has more than header + 5 lines. That test now checks that the header row and every `INV-n` document appear, and still compares the full content with the direct ADR-095 export. Then 113/113.
- Review finding 1 (problem → cause → change → effect): `Continue*` and `Export*` would each need the digest → token → load → expiry → binding／cutoff chain, and two copies can drift (for example, one copy loading before the token is validated). I extracted the chain into one private `Open` used by both. Continuation behavior is unchanged: all 86 existing continuation, first-page and entry cases still pass.
- Review finding 2: header errors were only detected by `FiReportManifestRules.Export*` after the manifest load and FI re-read. A miswired endpoint would therefore hit the database on every export before failing with 500. Headers (null, a null entry, wrong column count) are now checked with the other wiring inputs before any I/O.
- Review finding 3 (not fixed in this unit): `First*` saves a manifest for a single-page report but issues no token, so that report cannot be exported through the coordinator. Recorded in ADR-101 and as backlog WU-BI-001a12.
- Verification: class 113/113, Architecture 1359/1359, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-BI-001a12 FI report export-only token (ADR-102)

- Test-first: the SQL memory gate returned DEFER (1.938/1.931/1.930 GiB). I wrote `FiReportContinuationRulesTests.ExportToken.cs` (13 cases) and `FiReportCursorTokenRulesTests.Purpose.cs` (10 cases), and made the shared `AssertProblem` also require `ExportToken` to be null. The build failed as expected (CS0246 `FiReportCursorPurpose`). After implementation, the first run passed: FI report classes 399/399.
- Design finding (problem → cause → change → effect): the backlog's example was a page-1 token. ADR-093 continuation tokens reject page 1 at issue and at parse time, so `Continue*` can never repeat page 1. Reusing the page-2 continuation token would let a single-page report answer `Continue*` with an empty page past the end. Instead, the export token is a separate purpose with its own MAC domain (`MyERP.FI.ReportExport.v1\n`) and must point at page 1. Continuation tokens stay byte-identical, so every existing ADR-093 case passes unchanged. `Continue*` now rejects an export token with 400 and loads nothing.
- Review finding: `Export*` must still accept continuation tokens (ADR-101 cases). `Open` tries the export purpose first and falls back to continuation only when the export signature check fails. Any other export-purpose error, such as a malformed payload, a binding mismatch or a misconfigured key, is returned as is, so the token is never validated twice for a different outcome. The cost is one extra HMAC for continuation tokens on export only.
- Verification: FI report classes 399/399, Architecture 1382/1382, Debug solution build with 0 warnings and 0 errors. No SQL, permission, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-FI-004a3x1b2b1 GL posting SQL port offline (ADR-103)

- Test-first: the SQL memory gate returned DEFER (1.847/1.821/1.825 GiB). I wrote `GlPostingTransactionPortTests` (7 tests, 11 cases) first. The build then failed with CS0246 (`GlPostingTransactionPort` missing), before any implementation existed.
- Design finding (problem → cause → change → effect): there is no FI mapping table yet, so "current mapping version" had no source. A constant version would let every draft pass the mapping check. I added `IGlPostingMappingVersionSource` with a single-match rule: no match or an ambiguous match → null authority (500, nothing written). Each source adapter must now declare its mapping explicitly.
- Review finding 1: the first build failed CA1001. The port kept the latest session, which is disposable, in a field so it could read the connection state. It now keeps only a state reader, and the runner stays the only owner of the session.
- Review finding 2: after an attempt failed on a broken connection, a following `BeginAsync` failure would reuse the previous session's Broken state. That reports connection loss before any commit. `BeginAsync` now clears the state reader first.
- Verification: port class 11/11, Architecture 1551/1551, Debug solution build 0 warnings and 0 errors. No SQL, HTTP or UI acceptance is claimed.

## 2026-11-06 WU-FI-004a3x1b2b2 GL posting SQL port real SQL acceptance (ADR-103)

- Gate: the SQL memory gate passed (5.261/5.209/5.203 GiB). The port had been implemented before any SQL test (ADR-103), so this unit records the first real-SQL run instead of a red build.
- First run: `FinancePostingServiceTests` (FI-GL-SQL-001–010, 7 tests) passed 7/7 on its first execution in about 30 s. No product change was needed and `GlPostingTransactionPort` is unchanged.
- The failure paths were checked to be real, not merely green. The deadlock test (FI-GL-SQL-008) builds a genuine 1205 with a global temp table, opposite row order and DEADLOCK_PRIORITY LOW against HIGH. The KILL test (FI-GL-SQL-009) kills the posting SPID from a second connection after the locks are held. Both assert that the mapping source was called exactly twice: attempt 1 failed and one rerun posted. Both assert one journal. This confirms that the 1205 → RetryPosting path and the connection-lost → committed-lookup → rerun path run through the real port.
- Review finding 1 (problem → cause → change → effect): to inject faults into the transaction without a test-only hook in product code, the test uses the production extension point. `IGlPostingMappingVersionSource` is resolved from the attempt's own scope after both locks, so the test source shares the attempt's DbContext and transaction. Product code stays free of test seams.
- Review finding 2 (not changed): the deadlock test sets DEADLOCK_PRIORITY LOW on a pooled connection that is then broken or returned to the pool. It only affects which session is chosen as a victim in a later deadlock, never correctness. It is limited to the test connection string and needs no action.
- Not covered: retry exhaustion through real SQL would take 3 × 15 s lock timeouts. It stays covered offline in FI-GL-RETRY. The 004a2k BLOCKED retry list is not part of this unit.
## 2026-11-12T17:04:16+08:00 WU-FI-004a2 period and posting lock review

- Review: `GlPeriodWriteService` takes an exclusive transaction applock on `GlPostingWriter.PeriodLockResource(companyId)` before rereading saved periods and Posted dates. `GlPostingWriter` takes a shared lock on the same resource before reading posting authority. The lock order is period then source for posting; period writes only take the period lock. No product change was needed.
- SQL evidence: FI-GL-SQL-011 holds a real posting after its shared lock, starts a period move on a second connection, then commits the posting; the move sees the Posted date and rejects without changing the period/version. FI-GL-SQL-012 holds the exclusive period lock and closes the period; the waiting posting then rejects Closed with zero GL rows. Existing FI-GL-SQL-010 proves an uncommitted journal write rolls back and releases its locks.
- Boundary review: invalid ranges, inclusive overlap and adjacent dates, stale versions, foreign company, Posted date retention, delete protection, concurrent create/update/delete, and unchanged update are covered by the existing FI-GL period SQL and pure rule tests. Batch, file and UI error presentation belong to later GL work units.
- Verification: memory gate 4.796/4.774/4.771 GiB PASS; both new targeted SQL tests passed individually, backend full suite 3123/3123, SQL-backed DocGen 0 Error/579 Warning. A first combined `--filter-method` command selected zero tests because this test platform does not interpret `|` as method-name OR; running one method per command corrected the invocation. No debug instrumentation or product code was introduced.

## 2026-11-12T21:16:06+08:00 WU-FI-004b1 GL read API review

- The read service uses `AsNoTracking`, current-company query filters, separate period and journal permissions, stable secondary ID ordering and saved FI source/account labels. Journal detail loads only after a Posted company-scoped header is found. The database trigger still rejects mutation of Posted rows. No query accesses MD tables.
- The initial full suite exposed two architecture tests that asserted the old exact four-permission FI catalogue. The production catalogue correctly includes the two new GL VIEW permissions; the tests now assert all six and the intended seeded FINANCE, MANAGER and VIEWER grants. The targeted architecture regression is 4/4.
- Added HTTP boundary checks for page/size, invalid dates and sort, invalid IDs, maximum page size and empty subsequent page. SQL-backed `GlReadHttpTests` pass 3/3. UI presentation and period mutation remain separate work units.

## 2026-11-13 WU-FI-004c4a demo seed

- **Problem**: In the full backend run, all 10 `FinancePostingServiceTests` failed with `Unable to resolve IHostEnvironment for GlDemoSeedMapping`.
  **Cause**: Those tests build DI from FinanceModule + MasterDataModule only, without a host. The port resolves every `IGlPostingMappingVersionSource`, so the demo mapping's required dependency broke resolution for unrelated sources (SD/Invoice).
  **Change**: `IHostEnvironment` is now an optional constructor parameter. A missing environment returns version 0, the same fail-closed result as outside Development. Added a test that resolves the mapping from host-less DI and gets 0.
  **Effect**: The posting suite passes again (28/28 in the posting, seed and valuation rerun). In the same run, `InventoryValuationTests` had timed out on SQL, a known low-memory issue on the host; it passes when rerun alone.
- **Problem**: An architecture test asserted that no mapping source was registered.
  **Change**: It now asserts that exactly one source is registered, `GlDemoSeedMapping`, and that it is Scoped. This is stricter than before, not weaker.
- **Review**: Each seeder uses a fresh DI scope per company, so one DbContext never tracks entities from two companies. Posting reuses the existing service and rules; the seed adds no direct SQL writes. `SeedCompanyContext` is ignored whenever an HTTP context exists.

## WU-FI-004c4b real-API GL E2E (2026-11-13T23:28:55+08:00)

- **Problem**: Run alone, the new GL spec passed 5/5. In the full real-API run (4 workers), the GL and partner-approval specs failed with 429 `COMMON.REQUEST.RATE_LIMITED`. Together the specs make 11 logins, and the login limit is 5 per IP per minute.
  **Cause**: Each GL test logged in through the UI, and nothing in the specs waited for the limit to reset.
  **Change**: The new `realApiLogin` in `tests/e2e/support/real-api.ts` waits for the server's `Retry-After` on 429 and tries again (up to 4 attempts). The GL spec now logs in once per user for the whole file and stores the real tokens the way AuthService does. The UI login itself is still covered by `real-api-login`. `Invoke-RealApiE2E.ps1` runs with `--workers=1`, because the specs share one database and one rate-limit window. `beforeAll` hooks get 240 s for the wait. The rate limit was not raised or disabled.
  **Effect**: Full real-API run passes 10/10 in 2.0 min.
- **Problem**: sqlcmd 13 ignores `-f o:65001` and writes `NVARCHAR` in the console code page, which garbles account names.
  **Change**: Names are selected as UTF-16 hex and decoded in Node, and decimals are selected as `varchar`, so no digit goes through a JS number.
- **Review**: The spec is read-only on SQL Server. All writes go through the API and are cleaned up in `finally`, and the new period uses a random far-future year. Expected screen formats (grouped decimals, trailing zeros dropped) are derived from SQL values rather than hard-coded.

## WU-FI-002a2c invoice posting races review (2026-11-14T00:31:03+08:00)

- **Review**: `SalesInvoicePostingService` lock order (credit key → FI replay → SD read → GL applock) was checked against two live connections. The waiter is blocked on the credit applock before reading SD or touching GL, so the 15 s GL applock is never the first wait and does not need a setting. No product change was made.
- **Problem**: The tests could not show red before implementation, because the behaviour already existed.
  **Change**: A mutation run removed the `AcquireCreditLockAsync` call; several assertions failed (including INV-012), and the code was restored. The fake SD port gained an `OnRead` hook so tests can hold a connection inside the caller's transaction and record read order.
  **Effect**: FI-SALES-INV-009～013 now guard against a future reordering of locks; all 26 `FinancePostingServiceTests` pass in about 31 s.

## WU-FI-002a4b SD invoice account map maintenance review (2026-11-14T04:22:02+08:00)

- **Problem**: After `FinanceAccountUsage` started reading `gl_sales_invoice_account_map`, three `FinanceAccountUsageTests` failed in the full run with "SalesInvoiceAccountMapping is not included in the model": the test builds a `MyErpDbContext` with only the MD account and GL models. The previous round had only run the new test classes.
  **Change**: The fixture now also registers `SalesInvoiceAccountMapDbModel` (the production context always registers every module model, so product code is unchanged).
  **Effect**: FinanceAccountUsage 4/4 and SalesInvoiceAccountMapHttpTests 7/7 pass; full backend 3236/3236. Lesson: when an `IAccountUsage` implementation reads a new table, search for partial-model test contexts.
- **Review**: `SalesInvoiceAccountMapService` checks version before account validation (409 wins over 422), skips MD lookups for zero-change saves, and maps both duplicate-key (concurrent first save) and EF concurrency failures to the same 409 after clearing the tracker. No further change.

## WU-FI-002a4c1 sales invoice account map screen (2026-11-14T04:37:28+08:00)

- **Problem** (UX review B4): after a refused save (blank field on screen, or 422 from the server) the reason appeared under the field but focus stayed on Save, so on a short screen the user might not see which field failed.
  **Change**: `focusFirstError()` focuses the first `aria-invalid` input after render (client validation and 422 `accountErrors`).
  **Effect**: E2E asserts the receivable input is focused in both cases; 7/7 pass.
- **Problem** (web-design-guidelines): inputs had no `name`; account codes could be machine-translated by the browser.
  **Change**: `[attr.name]` per field; `translate="no"` on saved codes.
  **Effect**: no behaviour change; tests unchanged.
- **Review**: 409 handling first lost the conflict message because the reload reset the form error; fixed before commit by passing the message through `load(conflictKey)` (unit FI-SALES-MAP-UI-009). Late save results after a company switch are dropped by a write epoch (UI-012).

## WU-FI-002a4c2 sales invoice account map demo seed and real API (2026-11-14T05:00:09+08:00)

- **Problem**: the first real-API run aborted at `--seed` with "2141 of HK01 is not postable: MD.ACCOUNT.NOT_FOUND".
  **Cause**: the API binary was still the build from the mutation check (2141 removed from the MD seed); the source had been restored but not rebuilt.
  **Change**: rebuilt and reran; no code change. The failure itself confirmed the seed guard: an unusable demo code stops the seed with company, code and reason instead of saving a map that fails only at posting time.
  **Effect**: seed added 2141 and both maps on `myerp`; real-API finance specs 9/9.
- **Problem**: `real-api-finance-gl.spec.ts` held the `sqlcmd` read-back helpers privately; the new spec would have copied them.
  **Change**: moved `sql`, `utf16`, `companyId` to `tests/e2e/support/real-api-sql.ts`; both specs import it.
  **Effect**: GL real-API spec unchanged in behaviour (5/5).
- **Finding (not fixed here)**: the real screenshot `6.23-real-account-invalid` shows field reasons in a pale pink: `.field-error` uses `--erp-status-error` (#f7b7a3, a status-tag fill; ≈1.7:1 on white) instead of `--erp-color-error-text`. Same rule in four other FI/MD pages. Recorded as backlog `WU-UI-ERRCOLOR` (needs UX review and new screenshots for all five pages).
- **Review**: the seed writes `gl_sales_invoice_account_map` directly (FI's own table) because the maintenance service needs an HTTP user with `GL.SALES_MAP.EDIT`; it reuses `SalesInvoiceAccountMapRules.AccountReason`, so seed and API accept the same accounts. Existing rows are never touched, so a re-seed cannot bump the map version and reject in-flight postings (ADR-109).
