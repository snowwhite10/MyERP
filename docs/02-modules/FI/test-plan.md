# FI test plan

WU-FI-001e (2026-11-05): `myerp-test-scenarios` applied to the UI-only form change. The existing FI-CREDIT-004–024 scenarios retain server validation, conflict, permission, state and network coverage. FI-CREDIT-025 checks visible required marks, dirty cancel stay/discard, route/browser leave protection and zero writes; no file or batch mode exists. The unit test was red because the required marks and unsaved methods were absent, then green. API and data structure are unchanged.

WU-FI-001d checkpoint (2026-11-04, ADR-077): `myerp-test-scenarios` was applied to the label read path. Real SQL `CustomerLabelTests` proves one-company labels for active, inactive and soft-deleted customers, with foreign/unknown IDs absent, empty input and no tracked MD entities. `CreditLimitQueryTests` checks one batch for the distinct IDs of a page, detail null on a missing label, and `CreditLimitHttpTests` checks create/list/detail/deactivate responses through the real API without foreign-company disclosure. Frontend unit and Playwright tests assert server labels override the picker, a deep-linked customer filter gains its label, and missing labels use the translated ID. Screenshots: `tests/qa/FI/screenshots/6.17-*` (26; new label states in light zh-Hant and dark en). This is read-only enrichment of existing FI results: there is no file or batch write, no partial success, and credit-limit write permissions and state checks remain as in FI-CREDIT-004–019. Frontend full 738/738, E2E full 457 passed/5 skipped, targeted SQL/HTTP green. SQL-backed isolated DocGen reached 0 Error / 581 Warning; Debug build had 0 warnings/errors. On the third full-backend acceptance attempt at 2026-11-04T20:57:27+08:00, the memory gate passed and the two prior failing tests each passed in isolation (1/1); the first CustomerLabelTests invocation had instead timed out during shared fixture database creation. Full solution testing passed DocGen, Shared and Architecture but NumberGeneratorTests timed out in SQL after 30 seconds; free physical memory fell to 1.72 GiB and integration was stopped. Full backend acceptance is BLOCKED under the three-attempt rule; see blocked.md for the retry sequence. FI-CREDIT-022–024 targeted PASS status does not imply full-suite PASS.

WU-FI-001c result (2026-11-04): memory gate PASS (≈4.0 GiB ×3). The Credit Limit page (interfaces 6.17–6.19) was built test-first from the scenarios below. Unit: `credit-limit.service.spec.ts` (4) and `credit-limit-page.spec.ts` (8). HTTP: `CreditLimitHttpTests.DecimalTextAmountKeepsEveryDigitOnCreateAndUpdate` (amount sent as text; the maximum value is stored exactly; `"12,5"` is 400 with no write). E2E: `tests/e2e/specs/finance-credit-limit.spec.ts` (7) with 24 screenshots in `tests/qa/FI/screenshots/6.17-*`. Every E2E write asserts the visible message and the mock store's saved row plus the re-read list. The UX review found one P1, fixed here: a stale edit used to keep the dialog open with a version that could never succeed; it now closes, shows the reason and re-reads the list. P2 customer labels moved to WU-FI-001d (ADR-076). P3 required-field markers moved to the backlog. Frontend 737/737.

WU-FI-001b2 result (2026-11-04): memory gate PASS (≈3.7 GiB ×3). New `CreditLimitHttpTests` (4, real login/roles/MD customer and base currency through `/api/v1/ar/credit-limits`), `CreditLimitQueryTests` (11 incl. theory rows: customer/status/inclusive date-window filters, default `-effectiveFrom` with id tie-break, paging, company-bound detail with history, missing company / VIEW → 403, other-company authorization → 403, seven field-specific 400s incl. rejected keyword) and two new `FinanceModuleTests` (permission catalogue; FINANCE all four, MANAGER/VIEWER VIEW only, SALES none). Credit and seed suites 54/54. Evidence added to FI-CREDIT-007/009/012/013/017 below.

WU-FI-001b1 result (2026-11-04T09:46:41+08:00): memory gate PASS (≈3.0 GiB free ×3). The scenarios already listed in this plan (from `myerp-test-scenarios`, WU-FI-000d) drove the new real-SQL `CreditLimitCommandTests` for `CreditLimitCommandService`, which all pass (FI credit suites 32/32). They cover: server base currency and adjacent periods (001/005/007), four inclusive-overlap shapes including touching endpoints with zero rows changed (007/011), per-customer/per-company independence, edit by version and stale edit/deactivate 409 with the saved row unchanged (009), self-exclusion on edit but no overlap with another active row (007), immutable customer (400), deactivated history kept, not editable or re-deactivatable, and not blocking a new identity (008), inactive MD customer and missing base currency leaving no key or limit (015/011), changed base currency blocking edit but allowing deactivation (015), seven field-specific 400 cases with no key written (004/005/011), maximum amount on a leap-day single-day period (004/005), per-action permission and other-company 404 at service level (012/013), and a two-connection proof that the write waits for the credit key lock and then rejects the overlap the holder committed (007/010). Full backend suite: 1359/1359. HTTP, Swagger and permission catalogue evidence remain in WU-FI-001b2.

WU-FI-001a result (2026-11-04T09:21:58+08:00): memory gate PASS (≈14.8 GiB free). `FinanceCreditSchemaTests` observed red (tables absent) and then green after `Finance_CreditFoundation`. New `CustomerCreditServiceTests` (9 real-SQL tests) all pass: first-use race with rollback and with commit (FI-CREDIT-021, PASS), different customer independent while a pair is locked (FI-CREDIT-003/021 lock part), named retryable `FI.CREDIT.LOCK_TIMEOUT` and no snapshot without the lock (FI-CREDIT-010/016 lock part), rollback leaves no key (FI-CREDIT-018 FI part), active limit with only open posted AR, version and `ObservedAt` (FI-CREDIT-001/002/014 service part), zero limit available versus deactivated/expired/unseen `MissingLimit` (FI-CREDIT-004/014/016), overlapping active rows `AmbiguousLimit` and foreign-currency exposure `ExposureUnavailable` (FI-CREDIT-016), SQL rejection of reversed interval, negative amount, open > original, orphan limit, duplicate source and duplicate key (FI-CREDIT-004/005/008 SQL part), argument validation. `FinanceModuleTests` checks the composition root. Backend full suite 1336/1336 before the module test was added; architecture suite 234/234 after. Rows below stay TODO where the authorised HTTP, limit write path (overlap 409 FI-CREDIT-007, stale version FI-CREDIT-009), MD/SYS checks, SD consumption or UI still belongs to WU-FI-001b/001c, WU-FI-002a or WU-SD-006b.

WU-FI-001a checkpoint (2026-10-29T16:06:35+08:00): reviewed the first-use key race and specified a transaction-owned SQL application lock for the exact company/customer pair before finding or inserting the permanent row. FI-CREDIT-021 now asserts the second connection waits before insertion, rollback permits it to create the row, and a different pair proceeds independently. The existing credit contract tests passed 3/3; available memory was about 0.95 GB, so no integration build, SQL red result or migration is claimed. Next, once free physical memory is stable at 2.5 GB, compile and run the schema red test and add/run FI-CREDIT-007/009/010/018/021 before implementing the schema/service.

WU-FI-001a checkpoint (2026-10-29T16:02:08+08:00): FI-CREDIT-021 now covers first-use key creation across two SQL connections, loser lock acquisition, rollback and independence of different customers. The matching `data-structure.md` lock protocol is explicit. Free physical memory was approximately 0.77 GB during verification, below the 2.5 GB gate. Previously built credit contract tests passed 3/3 and `git diff --check` passed; no new integration build or SQL red result is claimed. Next, observe the schema red test and add/run FI-CREDIT-007/009/010/018/021 before migration.

WU-FI-001a checkpoint (2026-10-29T15:57:57+08:00): schema acceptance now uses the permitted `ar_` ownership prefix for `ar_credit_key` and `ar_credit_limit` (with `ar_posted_receivable`), correcting the provisional `fi_` names before migration. The existing SQL test still needs an observed red result. This round did not execute SQL: available physical memory was 0.64 GB and fell to about 0.40 GB during an attempted serial integration build, below the 2.5 GB gate. The build was stopped without an outcome; the already built credit contract tests passed 3/3. Next, when memory is stable above the gate, run the schema red test, then add period overlap, two-connection lock and rollback tests before migration.

WU-FI-001a checkpoint (2026-10-29T15:52:06+08:00): the SQL acceptance model now names the permanent key, limit and receivable columns, FK/index paths, row checks and lock order in `data-structure.md`. The worktree started clean. After stopping idle .NET build servers, free physical memory was about 0.69 GB, below the documented 2.5 GB threshold; no SQL test, migration, service or product verification was attempted. The previously compiled schema test still requires an observed red result before implementation. The next tests must exercise FI-CREDIT-007 (inclusive overlap), 010 (two connections and distinct keys) and 018 (rollback), then stale version under FI-CREDIT-009. These tests must assert persisted rows as well as returned errors.

WU-FI-001a checkpoint (2026-10-29T13:22:28+08:00): `CustomerCreditContractTests` now has three passing assertions toward FI-CREDIT-004/016, including the exact decimal(19,4) maximum, invalid version and unknown unavailable reason. `FinanceCreditSchemaTests` defines the first real SQL migration assertion for the permanent credit key, effective limit and posted AR source tables, unique keys, lookup indexes and amount/date checks. The integration project compiles with zero warnings. The schema test has not been executed because available physical memory remained below the established 2.5 GB SQL test threshold; its expected red result must be observed before migration/service implementation. The two-connection lock, interval overlap and rollback tests still need to be written and observed red. FI-CREDIT-001–019 remain TODO because the FI provider, SQL schema and authorised HTTP/UI do not exist.

## WU-FI-000d credit scenarios (design; all product tests TODO)

The `myerp-test-scenarios` checklist covers normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file cases. U=unit, I=real SQL/authorised HTTP integration, E=Playwright. E cases must assert both the displayed result and a fresh read of saved data. WU-FI-001a/b/c first write failing tests, then implement. WU-FI-000d adds scenarios only.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-CREDIT-001 | Normal | Create a dated TWD limit; snapshot returns same company/customer, base currency, limit, zero posted AR, date and version. | I,E | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-002 | Normal | Post and allocate AR; snapshot reports committed open balance only, and SD can add its own commitment without FI counting it. | I | REQ_F4116.7.2 | TODO |
| FI-CREDIT-003 | Normal | Limit edit and two order approvals for different customers can proceed independently; same customer serializes. | I | REQ_F4116.7.3 | TODO |
| FI-CREDIT-004 | Boundary | Empty customer/currency, zero/negative ID, negative amount, over DECIMAL(19,4), fifth fractional digit and maximum valid amount return field-specific results; zero limit is valid. | U,I,E | REQ_F4116.7.1 | I service PASS (001b1); I HTTP decimal text PASS, U PASS and E PASS (001c: local errors send nothing, maximum saved and re-read exactly) |
| FI-CREDIT-005 | Boundary | Inclusive dates at month end, year end and leap day select exactly one limit; invalid/reversed date interval returns 400. | U,I | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-006 | Boundary | Evaluation of backdated SD order uses server `IClock.Today`, never order or client date; expired limit fails closed. | I | REQ_F4116.7.2 | TODO |
| FI-CREDIT-007 | Duplicate/conflict | Overlapping approved periods, including touching endpoints, reject with 409; adjacent nonoverlapping periods work. | I | REQ_F4116.7.1 | I service PASS (001b1); HTTP PASS (001b2) |
| FI-CREDIT-008 | Duplicate/conflict | Soft-deleted/deactivated history and same customer key cannot silently resurrect or reuse a conflicting interval. | I | REQ_F4116.7.1 | I PASS (001b1) |
| FI-CREDIT-009 | Duplicate/conflict | Stale edit/deactivate version returns 409 and preserves the saved limit and audit. | I,E | REQ_F4116.7.1 | I service PASS (001b1); HTTP PASS (001b2); E PASS (001c: stale edit closes the dialog with a named reason, the list re-reads the other version, amount unchanged) |
| FI-CREDIT-010 | Duplicate/conflict | Two SQL connections contend on one credit key: at most one approval consumes final headroom; lock timeout is named and retryable. | I | REQ_F4116.7.2–3 | TODO |
| FI-CREDIT-011 | Partial result | One invalid field/interval causes the single limit write to roll back fully; zero changed rows and no success message. Batch and partial-success modes are not offered. | I,E | REQ_F4116.7.1 | I service PASS (001b1); E PASS (001c: invalid create and overlap send/keep zero writes and show no success) |
| FI-CREDIT-012 | Permission | Anonymous 401; VIEW/CREATE/EDIT/DEACTIVATE are separate permissions, and denied action returns 403 without mutation. | I,E | REQ_F4116.7.1–2 | I HTTP PASS (001b2: anonymous 401 on all 5 routes, VIEW-only 403 ×3 and CREATE-only read 403 with row/amount/version unchanged; catalogue and seed grants in FinanceModuleTests); E PASS (001c: VIEW-only hides create/edit/deactivate; no permission → /forbidden, no menu, zero reads) |
| FI-CREDIT-013 | Permission | Cross-company and SYS out-of-scope customer/limit IDs return 404; forged company ID or base currency is ignored or rejected. | I,E | REQ_F4116.7.1–2 | I HTTP PASS (001b2: foreign company GET/PUT/deactivate 404 and empty list, home customer 404 in foreign company, forged companyId/baseCurrency/status ignored; query service other-company 404); E TODO (001c) |
| FI-CREDIT-014 | State | Draft/Pending/failed AR has no exposure; Posted AR does; deactivated limit is unavailable, not a zero limit. | I | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-015 | Relation | Disabled/deleted MD customer or missing company base currency blocks new limit and snapshot; existing historical AR remains traceable. | I | REQ_F4116.7.1–2 | Limit write PASS (001b1); snapshot MD/SYS check TODO (SD consumer) |
| FI-CREDIT-016 | Error | Missing/ambiguous limit, mismatched identity/currency, negative/overflowed AR, stale snapshot and unavailable FI all fail closed with distinct codes. | U,I | REQ_F4116.7.2 | TODO |
| FI-CREDIT-017 | Error | HTTP 400/409/422/500, timeout and network interruption show localized reason or traceId; reread saved data before claiming success. | I,E | REQ_F4116.7.1–2 | I HTTP PASS (001b2: 400 field errors, 409 overlap and stale version, 422 not active, 404, each with errorCode and traceId; generic 500 traceId covered by ProblemDetailsTests); E PASS (001c: list 500 traceId and retry, network loss and retry, 409 overlap/stale and 422 not-active named in dialog or above list, no success message); client timeout uses the shared HTTP interceptor (network message) |
| FI-CREDIT-018 | Transaction | Inject failure after FI lock/limit or AR update: FI, SD, SYS and audit changes all roll back; replay does not double count. | I | REQ_F4116.7.2–3 | TODO |
| FI-CREDIT-019 | UI | Normal, empty/search-empty, no permission, validation, server error and success in three languages and light/dark modes; message agrees with saved limit. | E | REQ_F4116.7.1–2 | E PASS (001c: normal ×3 languages ×2 themes, empty, search-empty, invalid window, validation, overlap, stale, created, deactivated, 500 traceId, network, view-only, no permission) |
| FI-CREDIT-020 | File | No upload/export is in this credit slice; empty file, format, size, row count and encoding cases are not applicable until a separately specified file feature exists. | — | REQ_F4116.7.1 | N/A |
| FI-CREDIT-021 | Duplicate/conflict | On a previously unseen company/customer, two SQL connections race to create the permanent key: the second waits on the transaction-owned pair lock before insertion, exactly one committed key survives, and both acquire that row before reading exposure. Roll back the first insert and prove the waiting caller can establish a key; a different customer remains independent while the first pair is locked. A duplicate-key exception or negative application-lock result never yields approval. | I | REQ_F4116.7.3 | PASS (CustomerCreditServiceTests, 2026-11-04) |
| FI-CREDIT-022 | Normal, state, relation | A credit-limit page, detail and saved write result show the MD customer code/name without first using the picker. Inactive and soft-deleted customers retain historical labels; an unknown ID has null labels and the UI displays a translated ID. FI reads MD through one read-only batch contract per page, with no tracking or writes. | U,I,E | REQ_F4116.7.1 | PASS (CustomerLabelTests, CreditLimitQueryTests, CreditLimitHttpTests, frontend unit, Playwright; 001d) |
| FI-CREDIT-023 | Boundary, permission, error | Empty input returns no MD labels; repeated IDs are deduplicated; another company's customer ID is absent even if requested beside a visible one. Missing company yields no labels. A deep-linked customer filter recovers a server label, and server values override a stale picker label. No public MD label route or new permission exists. | U,I,E | REQ_F4116.7.1 | PASS (CustomerLabelTests, CreditLimitQueryTests, CreditLimitHttpTests, frontend unit, Playwright; 001d) |
| FI-CREDIT-024 | Error, transaction | If MD label lookup fails during a credit-limit write, the API must not report failure after a committed limit. The FI key, limit and audit remain rolled back when label enrichment fails before commit; no misleading success is shown. | I | REQ_F4116.7.1 | PASS (real SQL red: 1 committed limit; green: 0 limits/keys, 001d) |
| FI-CREDIT-025 | UI, boundary, conflict, permission, state, error | Create/Edit show required marks before input; pristine Cancel closes directly; changed Cancel offers stay/discard, route leave and browser unload warn. Staying keeps input, discard sends no write and saved row is unchanged. Existing 400/409/422/500 handling, active/history visibility and permission gating remain covered by FI-CREDIT-009/012/017/019; batch/partial and file cases are N/A because this is one form with no upload. | U,E | REQ_F4116.7.1 | PASS (001e: frontend unit and Playwright with saved-row assertion, three languages and light/dark screenshots) |

Traceability: REQ_F4116.7.1 → BR-FI-002/004/006a–006b → FI-CREDIT-001,004–009,011–15,17,19,022–025; REQ_F4116.7.2 → BR-FI-001/002/005/006 → FI-CREDIT-001–002,006,010,012–19; REQ_F4116.7.3 → BR-FI-003/006 → FI-CREDIT-003,010,018,021. The earlier WU-FI-000d design-only statement does not apply to completed 001a–e slices.

## WU-FI-002d sales source scenarios (design; all product tests TODO)

The `myerp-test-scenarios` checklist covers normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file cases. U=unit, I=two-connection real SQL and authorised HTTP integration, E=Playwright. Every E case asserts both visible message and fresh persisted FI/SD/GL data. WU-FI-002d adds scenarios only; WU-FI-002a/b/c write failing product tests before implementation.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-SALES-001 | Normal | Saved SD invoice posts one balanced AR/revenue/output-tax journal; source, original/open AR and FI result equal SD snapshots. | I,E | REQ_F4116.8.1,8.3 | TODO |
| FI-SALES-002 | Normal | Several invoice lines and tax rates aggregate by saved mapping; zero tax omits a zero leg, journal remains balanced. | U,I | REQ_F4116.8.1 | TODO |
| FI-SALES-003 | Normal | Under one credit key lock, invoice adds base gross to AR and releases the same approved SD commitment; total exposure is unchanged. | I | REQ_F4116.8.1 | TODO |
| FI-SALES-004 | Normal | Billed return referencing two invoice postings creates one balanced FI credit source and reduces only their credited open AR. | I,E | REQ_F4116.8.2 | TODO |
| FI-SALES-005 | Normal | Mixed billed/unbilled return credits billed AR and SD releases only unbilled commitment; FI creates no credit for unbilled portion. | I,E | REQ_F4116.8.2 | TODO |
| FI-SALES-006 | Boundary | Empty source/lines, zero or negative quantity, >6 quantity decimals, >4 amount decimals, maximum DECIMAL(19,4), overflow and net+tax mismatch fail by field with zero writes. | U,I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-007 | Boundary | Exact final partial invoice/credit uses saved remainder; repeated portions reconcile original/base net, tax and gross without rounding plug. | U,I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-008 | Boundary | Month/year/leap-day business date, later MD rate/account changes and disabled reference do not rewrite historical posted source; current ineligible account blocks new post. | U,I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-009 | Duplicate/conflict | Same source and fingerprint replays stable result with changed=false; new number/journal/AR/credit/audit count is zero. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-010 | Duplicate/conflict | Same source with different fingerprint or stale source version returns 409; soft-deleted history cannot free the source key. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-011 | Duplicate/conflict | Two SQL connections posting same source or final creditable portion serialize; one wins, other replays or conflicts, never double-counts. | I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-012 | Partial result | Failure after FI source, journal, AR, SD commitment, INV movement, SYS decision or audit rolls back whole command and number; zero-change result has no success toast. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-013 | Partial result | Partial success, all skipped and all failed batch outcomes are not offered for one atomic source; duplicate replay is explicitly unchanged. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-014 | Permission | Anonymous read 401, missing `AR.SALES_SOURCE.VIEW` 403; posting requires authorised SD ISSUE/final decision and approved internal caller. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-015 | Permission | Other-company and SYS out-of-scope source/customer/invoice IDs give 404 without financial leakage; forged company/actor/time/amount is rejected. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-016 | State | Draft/Pending/Rejected/deleted SD source cannot post; issuing source in the same transaction may post before final Posted flag, but rollback leaves neither Posted nor FI rows. | I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-017 | State | Posted FI source and journal are immutable; later correction uses a new credit source and retains original trace/audit. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-018 | Relation | Credit rejects wrong invoice line, customer, currency, company, missing original FI posting and disabled/deleted related source, with zero allocations. | U,I | REQ_F4116.8.2 | TODO |
| FI-SALES-019 | Relation | Prior receipts and credits constrain open AR; over-credit or credit after full settlement is rejected pending separate refund design, never negative AR. | I | REQ_F4116.8.2 | TODO |
| FI-SALES-020 | Error | Missing MD account, journal mapping, FI/SD contract, lock timeout, mismatched FI result and balance residual return named 422/409, no Posted claim. | U,I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-021 | Error | HTTP 400/409/422/500 with traceId, timeout and network break show translated cause; key lookup distinguishes committed result from absent safe retry. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-022 | Read/UI | Source read filters company/customer/type/date, pages deterministically, hides uncommitted and inaccessible IDs, and displays saved original/open amount and credit links. | I,E | REQ_F4116.8.3 | TODO |
| FI-SALES-023 | UI | Three-language light/dark normal, empty/search-empty, permission, validation, success, replay and server-error states match fresh persisted SD/FI/GL reads. | E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-024 | File | No upload/import/export is specified for this source slice; empty, malformed, oversized, wrong-encoding and column cases are inapplicable until a separate file feature exists. | — | REQ_F4116.8.3 | N/A |

Traceability: REQ_F4116.8.1 → BR-FI-007–010,013–014 → FI-SALES-001–003,006–017,020–021,023; REQ_F4116.8.2 → BR-FI-007–008,011–014 → FI-SALES-004–013,015–021,023; REQ_F4116.8.3 → BR-FI-008,014 → FI-SALES-009,014–015,017,021–024. No executable product test or SQL acceptance is claimed by this design unit.

## WU-FI-003d purchase payable source scenarios (design; all product tests TODO)

The `myerp-test-scenarios` checklist covers normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file cases. U=unit, I=real SQL/authorised HTTP integration, E=Playwright. Every E test must assert both the visible result and a fresh read of persisted PUR/FI/GL data. WU-FI-003a/b/c first write failing product tests; this design unit adds scenarios only.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-PUR-001 | Normal | Matched saved invoice posts one FI source, payable and balanced purchase/input-tax/AP journal; PUR becomes Posted with identical gross. | I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-002 | Normal | Several receipt lines, accepted price/tax differences and zero-tax line preserve each saved component; zero tax emits no zero GL leg. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-003 | Normal | One receipt is invoiced in portions; exact final remainder uses saved original/base values without a balancing plug or over-invoicing. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-004 | Boundary | Empty/zero source, zero lines, negative/zero quantity, seventh quantity decimal, fifth money decimal, maximum valid decimal and overflow give field/line-specific failure and zero writes. | U,I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-005 | Boundary | Month end, year end and leap day retain saved date/rate; later MD exchange or tax change cannot restate historical posting. | U,I | REQ_F4116.9.1,9.3 | TODO |
| FI-PUR-006 | Boundary | Missing active account, disabled mapping, nonrecoverable tax without cost mapping, net+tax mismatch or base journal residual gives named 422, no suspense/rounding plug. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-007 | Duplicate/conflict | Same company/PUR/invoice key and identical fingerprint returns original payable/journal with changed=false and zero additional rows/audit. | I,E | REQ_F4116.9.2 | TODO |
| FI-PUR-008 | Duplicate/conflict | Same key with changed fingerprint or stale source version returns 409; soft-deleted historical key remains reserved. | I | REQ_F4116.9.2 | TODO |
| FI-PUR-009 | Duplicate/conflict | Two SQL connections post same key; one commits, other replays or conflicts, never two payables or a deadlock. | I | REQ_F4116.9.1–2 | TODO |
| FI-PUR-010 | Duplicate/conflict | Two invoices race for the final receipt quantity; PUR locks permit only one, FI has no orphan payable. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-011 | Partial result | Inject failure after source, journal, payable, PUR quantity/state or audit: all effects and number allocation roll back, original version remains retryable. | I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-012 | Partial result | One invalid line rejects the entire invoice; zero changed rows, no success toast. Partial success, all skipped and all failed batch outcomes are inapplicable because one source is atomic. | I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-013 | Permission | Anonymous read 401, absent `AP.PURCHASE_SOURCE.VIEW` 403; posting requires authorised PUR POST and authenticated internal caller. | I,E | REQ_F4116.9.1,9.3 | TODO |
| FI-PUR-014 | Permission | Cross-company or SYS out-of-scope supplier/source is 404; forged company, supplier, actor, time, source amount or account is rejected without leakage. | I,E | REQ_F4116.9.1,9.3 | TODO |
| FI-PUR-015 | State | Draft, Disputed, Rejected, deleted and unsaved invoice cannot post; only Matched with all disputes accepted may call FI, then PUR marks Posted in one transaction. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-016 | State | Posted source, payable origin and journal cannot be edited/deleted or posted again; later correction requires a new specified compensating source. | I,E | REQ_F4116.9.1–3 | TODO |
| FI-PUR-017 | Relation | Wrong supplier/company/currency, non-Posted receipt, cancelled order, expired dispute decision or returned receipt quantity fails before FI posting. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-018 | Relation | Invoiced receipt cannot be returned without separately specified FI credit/debit note flow; no negative payable or silent journal rewrite. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-019 | Error | FI/GL/MD/PUR contract unavailable, lock timeout and mismatched FI result yield named error and leave PUR not Posted. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-020 | Error | 400/409/422/500 with traceId, timeout and network break show translated reason; committed source lookup distinguishes success from safe retry. | I,E | REQ_F4116.9.1–3 | TODO |
| FI-PUR-021 | Read/UI | Paged source read is stable and company/SYS scoped; list/detail show saved original/open payable and journal, never uncommitted data. | I,E | REQ_F4116.9.3 | TODO |
| FI-PUR-022 | UI | Three-language light/dark normal, empty/search-empty, denied, validation, success, unchanged replay and server-error states match fresh PUR/FI/GL reads. | E | REQ_F4116.9.1–3 | TODO |
| FI-PUR-023 | Audit/database | Source uniqueness survives soft deletion; FI FKs/indexes, immutable history, rollback audit and reversible migration are proved against SQL. | I | REQ_F4116.9.1–3 | TODO |
| FI-PUR-024 | File | No upload/import/export exists in this FI source slice; empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable until specified. | — | REQ_F4116.9.3 | N/A |

Traceability: REQ_F4116.9.1 → BR-FI-015,017–022 → FI-PUR-001–006,009–019,022–023; REQ_F4116.9.2 → BR-FI-016,020 → FI-PUR-007–011,020,023; REQ_F4116.9.3 → BR-FI-019,021–022 → FI-PUR-005,013–014,016,020–024. No executable FI payable test or real SQL acceptance is claimed here.

## WU-FI-004d GL foundation scenarios (design; all product tests TODO)

U = unit, I = real SQL integration, E = Playwright with UI and persisted-data assertions. The later implementation units must write failing tests first. No scenario below is reported as passed by this design unit.

### WU-FI-004a1 period-date policy (2026-11-05T06:47:20+08:00)

This offline slice tests only pure date selection for REQ_F4116.10.1. The SQL period table, lock, version, status persistence, authorisation and posting remain WU-FI-004a2/004a3; no product posting acceptance is claimed.

| ID | Class | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-DATE-001 | Normal | A saved business date resolves exactly one Open period in the requested company. | U | REQ_F4116.10.1 | PASS |
| FI-GL-DATE-002 | Boundary | Same-day interval is valid; reversed interval invalid; end day and leap day are inclusive. | U | REQ_F4116.10.1 | PASS |
| FI-GL-DATE-003 | Duplicate/conflict | Shared endpoints and containment overlap; next-day adjacency does not; another company does not overlap. | U | REQ_F4116.10.1 | PASS |
| FI-GL-DATE-004 | Partial result | Not applicable: a pure selection returns one outcome and performs no batch or mutation. | — | REQ_F4116.10.1 | N/A |
| FI-GL-DATE-005 | Permission | Pure policy filters by company ID; authentication, SYS scope and 403/404 belong to WU-FI-004b. | U,I | REQ_F4116.10.1 | Company filter PASS; I TODO |
| FI-GL-DATE-006 | State | A Closed period is rejected; two matching periods are ambiguous even when one is Closed. | U | REQ_F4116.10.1 | PASS |
| FI-GL-DATE-007 | Relation | Posted journal protection requires persistent journal/period FK and belongs to WU-FI-004a2/004a3. | I | REQ_F4116.10.1 | TODO |
| FI-GL-DATE-008 | Error | Missing, Closed and ambiguous periods have distinct outcomes; HTTP 400/409/422/500, timeout and translated UI belong to WU-FI-004b/c. | U,I,E | REQ_F4116.10.1 | U PASS; I/E TODO |
| FI-GL-DATE-009 | File | Not applicable: no import or export is in this policy. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2a posted-date mutation policy (2026-11-05T06:54:20+08:00)

Pure policy only. The future SQL service must load Posted business dates for the saved period inside the company-locked transaction, check version and overlap, then apply this policy. These unit results do not prove SQL locking or persistence.

| ID | Class | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-MOVE-001 | Normal | Extending a period while retaining its Posted dates is allowed; an unreferenced period may move or delete. | U | REQ_F4116.10.1 | PASS |
| FI-GL-MOVE-002 | Boundary | Posted dates on both inclusive endpoints are retained; moving either endpoint past one is rejected. | U | REQ_F4116.10.1 | PASS |
| FI-GL-MOVE-003 | Duplicate/conflict | Date-change candidate with a different saved period or company identity is rejected. Stale version, overlap and concurrent mutation need SQL tests FI-GL-007/008/010. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-MOVE-004 | Partial result | Inapplicable: this pure decision changes no data and has no batch mode. | — | REQ_F4116.10.1 | N/A |
| FI-GL-MOVE-005 | Permission | Company identity is checked; actor permission and SYS scope need authorised HTTP tests FI-GL-013/014. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-MOVE-006 | State | Closed period still protects Posted dates; closing/reopening and versioned state persistence need FI-GL-015. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-MOVE-007 | Relation | A period with any Posted journal cannot be deleted; moving it cannot exclude a Posted business date. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-MOVE-008 | Error | Reversed date range is distinct from identity mismatch and Posted-date conflict; 400/409/422/500, timeout and translated UI need FI-GL-020/022. | U,I,E | REQ_F4116.10.1 | U PASS; I/E TODO |
| FI-GL-MOVE-009 | File | Inapplicable: no file input or output exists in this policy. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2b candidate overlap policy (2026-11-05T06:59:48+08:00)

Pure policy only. The SQL service must load saved company periods inside its company lock and call this rule before insert or date change. These results do not prove database exclusion or concurrency.

| ID | Class | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-OVERLAP-001 | Normal | A new period after the previous end day has no conflict. | U | REQ_F4116.10.1 | PASS |
| FI-GL-OVERLAP-002 | Boundary | A one-day candidate on leap day conflicts with an inclusive end; a next-month start does not. | U | REQ_F4116.10.1 | PASS |
| FI-GL-OVERLAP-003 | Duplicate/conflict | Creation sharing either endpoint conflicts; edit ignores its own row but conflicts with another saved row. Concurrent insert and stale version remain FI-GL-007/008/010. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-OVERLAP-004 | Partial result | Inapplicable: pure decision makes no batch mutation. | — | REQ_F4116.10.1 | N/A |
| FI-GL-OVERLAP-005 | Permission | Other-company periods do not conflict; actor permission and SYS scope remain FI-GL-013/014. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-OVERLAP-006 | State | Closed as well as Open saved periods reserve their inclusive dates. | U | REQ_F4116.10.1 | PASS |
| FI-GL-OVERLAP-007 | Relation | Posted-date retention and delete checks remain FI-GL-MOVE-001–009 and locked SQL service. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-OVERLAP-008 | Error | Empty saved set is conflict-free; HTTP 400/409/422/500, timeout and translated UI remain FI-GL-020/022. | U,I,E | REQ_F4116.10.1 | U PASS; I/E TODO |
| FI-GL-OVERLAP-009 | File | Inapplicable: no file input or output exists in this policy. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2c composed write preflight (2026-11-05T10:54:58+08:00)

Pure decision over caller-supplied snapshots only. WU-FI-004a2 must obtain the version, all saved company periods and Posted journal business dates under one company SQL lock, then map the result to named API errors. No SQL, permission or UI acceptance is claimed here.

| ID | Class | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-WRITE-001 | Normal | A new nonoverlapping period is allowed; an edit retaining Posted dates and a deletion without Posted journals are allowed. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-002 | Boundary | Reversed dates fail; a leap-day or inclusive endpoint overlapping a Closed period fails; another company's period does not conflict. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-003 | Duplicate/conflict | Create with a persisted ID cannot bypass own-row exclusion; stale update/delete versions fail before later decisions. Two-connection exclusion and persisted version 409 remain FI-GL-007/008/010. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-004 | Partial result | Inapplicable: pure preflight has no mutation or batch result. | — | REQ_F4116.10.1 | N/A |
| FI-GL-WRITE-005 | Permission | Nonpositive company identity is rejected on create and changed company identity on edit; actor permission and SYS scope remain FI-GL-013/014. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-006 | State | Open to Closed edit may retain Posted dates; Posted-date exclusion and deleting a referenced period are rejected. SQL closure/reopen remains FI-GL-015. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-007 | Relation | Caller must provide saved Posted dates for the target period under lock; a nonempty set prevents deletion. Actual journal FK and lock remain FI-GL-018/023. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-008 | Error | Pure results distinguish invalid range, stale version, identity, overlap and Posted conflicts; HTTP 400/409/422/500, timeout and translated UI remain FI-GL-020/022. | U,I,E | REQ_F4116.10.1 | U PASS; I/E TODO |
| FI-GL-WRITE-009 | File | Inapplicable: no file input or output exists in this policy. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2d persisted identity and version preflight (2026-11-05T11:00:33+08:00)

The following cases use pure snapshots only. U=unit; real SQL identity, version, lock and rollback cases remain FI-GL-007–010,013–015,018,023. All categories from `myerp-test-scenarios` are recorded.

| ID | Class | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-WRITE-010 | Normal | Positive saved identity and matching positive versions keep an otherwise valid update/delete eligible. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-011 | Boundary | Zero/negative expected or current version cannot establish a saved update/delete. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-012 | Duplicate/conflict | Equal zero/negative versions are rejected instead of bypassing stale-version protection; unequal versions still conflict. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-013 | Partial result | Inapplicable: one pure decision has no batch or saved mutation. | — | REQ_F4116.10.1 | N/A |
| FI-GL-WRITE-014 | Permission | Zero company identity is rejected on update; actor/SYS permission remains FI-GL-013/014. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-015 | State | Open/Closed transition remains governed by the Posted-date rule; persisted state recheck remains FI-GL-015. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-016 | Relation | Zero saved ID cannot identify a period whose Posted journals are checked; SQL FK and lock remain FI-GL-018/023. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-017 | Error | Identity and stale-version outcomes are distinct; HTTP translation/timeout remain FI-GL-020/022. | U,I,E | REQ_F4116.10.1 | U PASS; I/E TODO |
| FI-GL-WRITE-018 | File | Inapplicable: period preflight accepts no file. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2e physical period schema (2026-11-05T11:10:23+08:00)

The `myerp-test-scenarios` categories are covered for this database-only slice. Each SQL check is tested against the deployed migration, not an in-memory model.

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-SCHEMA-001 | Normal | Migration creates `gl_period` with required company, date, state, version and audit columns plus company/date index. | I | REQ_F4116.10.1 | PASS; missing-table red observed |
| FI-GL-SCHEMA-002 | Boundary | Reversed date interval and nonpositive company/version fail at SQL constraint. | I | REQ_F4116.10.1 | PASS |
| FI-GL-SCHEMA-003 | State | State outside Open/Closed fails at SQL constraint. | I | REQ_F4116.10.1 | PASS |
| FI-GL-SCHEMA-004 | Duplicate/conflict | Overlap, shared endpoints, concurrent insert and stale version require the company transaction service in FI-GL-007/008/010. | I | REQ_F4116.10.1 | TODO in 004a2 |
| FI-GL-SCHEMA-005 | Partial result | No batch or partial result exists in a one-row migration/schema check. | — | REQ_F4116.10.1 | N/A |
| FI-GL-SCHEMA-006 | Permission | The table has no direct public API; authenticated company and SYS scope checks belong to FI-GL-013/014. | I | REQ_F4116.10.1 | TODO in 004b |
| FI-GL-SCHEMA-007 | Relation | Posted journal FK and deletion protection require journals from 004a3 and FI-GL-018/023. | I | REQ_F4116.10.1 | TODO |
| FI-GL-SCHEMA-008 | Error | SQL constraint errors are proven; HTTP 400/409/422/500, timeout and translated UI belong to FI-GL-020/022. | I,E | REQ_F4116.10.1 | SQL PASS; HTTP/E TODO |
| FI-GL-SCHEMA-009 | File | No file input or export in the period schema. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2f unchanged period update preflight (2026-11-05T11:21:53+08:00)

This offline decision keeps an unchanged saved period distinct from a successful write. It is evaluated only after the saved version, identity, Posted-date retention and overlap checks; the eventual SQL transaction must re-read these facts under its company lock. The full WU-FI-004a2 service and SQL acceptance remain open.

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-WRITE-019 | Normal | A changed Open/Closed state remains an allowed update. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-020 | Boundary | Equal saved dates and state return Unchanged, without implying a version increment. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-021 | Duplicate/conflict | A stale expected version still returns StaleVersion even when fields are identical. | U | REQ_F4116.10.1 | PASS |
| FI-GL-WRITE-022 | Partial result | Inapplicable: one pure decision makes no batch or saved change. | — | REQ_F4116.10.1 | N/A |
| FI-GL-WRITE-023 | Permission | Actor and company scope checks belong to the authorised SQL/HTTP service (FI-GL-013/014). | I,E | REQ_F4116.10.1 | TODO |
| FI-GL-WRITE-024 | State | A state-only change is not treated as unchanged; persisted Open/Closed checks remain FI-GL-015. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-025 | Relation | Posted-date protection still runs before the unchanged result; actual journal FK remains FI-GL-018/023. | U,I | REQ_F4116.10.1 | U PASS; I TODO |
| FI-GL-WRITE-026 | Error | SQL lock timeout and HTTP 409/422/500 translation remain FI-GL-020/022. | I,E | REQ_F4116.10.1 | TODO |
| FI-GL-WRITE-027 | File | Inapplicable: this preflight accepts no file. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a3a source replay preflight (2026-11-05T12:02:24+08:00)

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-SOURCE-001 | Normal | No saved key permits a new posting; identical committed key, version and fingerprint returns saved journal ID unchanged. | U | REQ_F4116.10.3 | PASS |
| FI-GL-SOURCE-002 | Boundary | Zero/negative company or source ID and version, blank or outer-whitespace module/type/fingerprint, or nonpositive saved journal ID are invalid. | U | REQ_F4116.10.3 | PASS |
| FI-GL-SOURCE-003 | Duplicate/conflict | Existing key with changed version or fingerprint conflicts; a saved row for a different company/module/type/source ID cannot replay. | U,I | REQ_F4116.10.3 | U PASS; SQL uniqueness I TODO in 004a3b |
| FI-GL-SOURCE-004 | Partial result | A replay decision carries the saved ID without a write; batch partial/all skipped is inapplicable to one source key. | U,I | REQ_F4116.10.3 | U PASS; persisted zero-change I TODO in 004a3 |
| FI-GL-SOURCE-005 | Permission | Pure identity includes company; authenticated caller and SYS scope belong to 004a3/004b. | U,I | REQ_F4116.10.3 | U PASS; I TODO |
| FI-GL-SOURCE-006 | State | Saved committed source is immutable, including soft-deleted history; pure rule cannot establish commit state. | U,I | REQ_F4116.10.3 | U PASS for conflict; I TODO |
| FI-GL-SOURCE-007 | Relation | Saved journal ID is returned only on exact replay; journal FK and MD account checks belong to 004a3. | U,I | REQ_F4116.10.3 | U PASS; I TODO |
| FI-GL-SOURCE-008 | Error | Changed version/fingerprint returns Conflict; HTTP 409, timeout recovery and translated feedback belong to 004a3/004b/c. | U,I,E | REQ_F4116.10.3 | U PASS; I/E TODO |
| FI-GL-SOURCE-009 | File | No file input exists for source replay. | — | REQ_F4116.10.3 | N/A |

### WU-FI-004a2k period update endpoint and rollback SQL checkpoint (2026-11-05T16:22:30+08:00)

`FinancePeriodWriteTests.UpdateRejectsSharedEndpointWithoutChangingSavedRowAndAcceptsAdjacentDay` uses a fresh reader after each decision. The three-sample memory gate was 1.293/1.328/1.344 GiB (DEFER) and independent `sqlcmd` login timed out, so these SQL assertions are compiled but **not executed**; no PASS is claimed.

Second checkpoint (2026-11-05T16:26:18+08:00): the gate again returned DEFER at 1.801/1.807/1.787 GiB, while independent `sqlcmd` returned 1. Static review found the assertions intact; the SQL result remains TODO.

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-WRITE-028 | Normal/boundary | Extending January through February 28 is adjacent to a March 1 period and persists version 2. | I | REQ_F4116.10.1 | Compiled; SQL TODO |
| FI-GL-WRITE-029 | Duplicate/conflict | Extension through the March 1 shared endpoint, and later through March 2, returns Overlap. | I | REQ_F4116.10.1 | Compiled; SQL TODO |
| FI-GL-WRITE-030 | Partial result/error | After each rejected update, a separate connection sees the original dates, state and version; no partial write. | I | REQ_F4116.10.1 | Compiled; SQL TODO |
| FI-GL-WRITE-031 | Permission/state/relation/file | Existing FI-GL-013–018/020/023 cover authority, Posted state, references and errors; this one-row update has no file or batch input. | I,E | REQ_F4116.10.1 | Existing checks remain TODO |

Traceability: REQ_F4116.10.1 → BR-FI-023 → FI-GL-WRITE-028–030 → `FinancePeriodWriteTests.UpdateRejectsSharedEndpointWithoutChangingSavedRowAndAcceptsAdjacentDay`.

### WU-FI-004a3d persisted source shape preflight (2026-11-05T16:16:11+08:00)

This is a pure BR-FI-025/028 check against the current `gl_source_key` SQL column shapes. It makes no SQL or source-authority claim. U=unit; FI-GL-009/010/016/023 still require real SQL.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|
| FI-GL-SOURCE-010 | Normal/boundary | Exact maximum module 20, type 30 and fingerprint 128 printable ASCII characters plus `int.MaxValue` version remain eligible. | U | REQ_F4116.10.3 | PASS |
| FI-GL-SOURCE-011 | Boundary/error | One character above each SQL text maximum, version above SQL `int`, non-ASCII and control characters return InvalidSource before a write. | U | REQ_F4116.10.3 | PASS |
| FI-GL-SOURCE-012 | Duplicate/conflict | Reject a lossy identity or fingerprint before comparing a saved permanent key; SQL key race and persisted replay remain FI-GL-008–010. | U,I | REQ_F4116.10.3 | U PASS; I TODO |
| FI-GL-SOURCE-013 | Partial result | One source preflight returns one decision with no partial or batch result. | — | REQ_F4116.10.3 | N/A |
| FI-GL-SOURCE-014 | Permission/state/relation | The pure rule gives no actor, company scope, committed source or MD authority; FI-GL-014/016/017 remain required. | I | REQ_F4116.10.2–3 | TODO |
| FI-GL-SOURCE-015 | File | No file input or output is accepted by this source preflight. | — | REQ_F4116.10.3 | N/A |

Traceability: REQ_F4116.10.3 → BR-FI-025/028 → FI-GL-SOURCE-010–015 → `GlSourceReplayRulesTests.SourceTextAndVersionMustFitPersistedSqlColumnsWithoutLoss`. SQL/HTTP acceptance remains pending.

### WU-FI-004a3b journal schema (2026-11-05T12:17:07+08:00)

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-SCHEMA-001 | Normal | Three FI tables, permanent source unique index, company FKs, line sequence uniqueness and Posted-date lookup exist. | I | REQ_F4116.10.1–3 | PASS |
| FI-GL-SCHEMA-002 | Boundary | Positive source identity and one-sided positive line amounts are enforced by SQL checks; invalid rows are rejected. | I | REQ_F4116.10.2–3 | PASS |
| FI-GL-SCHEMA-003 | Duplicate/conflict | Reusing a company/module/type/source ID with different version/fingerprint fails unique index. Same source under another company remains separate. | I | REQ_F4116.10.3 | PASS for duplicate; other-company case belongs to 004a3 |
| FI-GL-SCHEMA-004 | Partial result | N/A: migration creates schema, no batch posting or partial acceptance path. | — | REQ_F4116.10.2 | N/A |
| FI-GL-SCHEMA-005 | Permission | N/A at schema layer; source authorization and company-scoped HTTP reads remain 004a3/004b. | I,E | REQ_F4116.10.2–3 | TODO |
| FI-GL-SCHEMA-006 | State | Posted status is the only persisted journal status; SQL immutability and reversal remain 004a3. | I | REQ_F4116.10.2 | Partial; TODO |
| FI-GL-SCHEMA-007 | Relation | Composite company FKs link header to period/source and lines to header; MD account stays scalar. | I | REQ_F4116.10.2–3 | PASS for FK metadata; cross-company rejection TODO 004a3 |
| FI-GL-SCHEMA-008 | Error | Missing table was observed red; malformed source/line SQL raises SqlException. HTTP 400/409/422/500/timeout feedback remains 004b/c. | I,E | REQ_F4116.10.2–3 | I PASS; E TODO |
| FI-GL-SCHEMA-009 | File | N/A: journal schema accepts no file. | — | REQ_F4116.10.2 | N/A |

### WU-FI-004a3l GL header preflight (2026-11-05T21:00:00+08:00)

U=unit. This pure decision uses caller-supplied saved-source and company-currency claims; real SQL must reload both and all periods under the posting transaction's company lock. It is not evidence of a Posted journal.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-HEADER-001 | Normal | Canonical saved source, matching company/base currency, balanced whole draft and inclusive leap-day Open period return only that period ID. | U | REQ_F4116.10.1–2 | PASS |
| FI-GL-HEADER-002 | Boundary | Zero company, missing period set, invalid source text, lowercase/short/empty currency and altered draft totals return distinct failure without period ID. | U | REQ_F4116.10.1–2 | PASS |
| FI-GL-HEADER-003 | Duplicate/conflict | Two matching periods are ambiguous; permanent source replay and SQL period overlap remain FI-GL-008–010. | U,I | REQ_F4116.10.1–3 | U PASS; I TODO |
| FI-GL-HEADER-004 | Partial result | One invalid or missing draft leg rejects the whole header; no period ID or partial journal is returned. Batch posting is not in scope. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-HEADER-005 | Permission | Foreign source company and foreign company periods cannot authorise a period; actor and SYS scope remain FI-GL-013–014. | U,I | REQ_F4116.10.1–2 | U PASS; I TODO |
| FI-GL-HEADER-006 | State | Closed, missing and ambiguous periods have distinct decisions; source Draft/Pending/Rejected authority remains FI-GL-016. | U,I | REQ_F4116.10.1–2 | U PASS; I TODO |
| FI-GL-HEADER-007 | Relation | Source company, currency, balanced lines and period agree; authoritative MD accounts, mapping and saved source remain FI-GL-017–019. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-HEADER-008 | Error | Invalid input/currency/draft and period failures are named; HTTP 400/409/422/500, timeout and translated UI remain FI-GL-020–022. | U,I,E | REQ_F4116.10.1–2 | U PASS; I/E TODO |
| FI-GL-HEADER-009 | File | No file input or export is accepted by this pure header decision. | — | REQ_F4116.10.1–2 | N/A |

Traceability: REQ_F4116.10.1–2 → BR-FI-023/025–026/029 → FI-GL-HEADER-001–009 → `GlJournalHeaderRulesTests`. Permanent SQL posting and full FI-GL acceptance remain TODO.

### Remaining GL foundation product scenarios


| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-001 | Normal | Create Open period; saved business date within inclusive interval posts one balanced immutable journal and lines through FI. | I,E | REQ_F4116.10.1–2 | TODO |
| FI-GL-002 | Normal | SD invoice and PUR matched invoice each use their FI source adapter and one GL journal; totals equal saved AR/AP components. | I | REQ_F4116.10.2 | TODO |
| FI-GL-003 | Normal | Multiple accounts and tax legs retain deterministic sequence and saved MD account/mapping snapshots; zero tax omits a zero line. | U,I | REQ_F4116.10.2 | TODO |
| FI-GL-004 | Boundary | First/last inclusive day, month/year boundary and leap day resolve exactly one Open period from saved business date. | U,I | REQ_F4116.10.1–2 | TODO |
| FI-GL-005 | Boundary | Empty source/lines, one line, zero/negative amount, fifth money decimal, both debit/credit on one line and overflow return field/line error with zero writes. | U,I,E | REQ_F4116.10.2 | TODO |
| FI-GL-006 | Boundary | Maximum valid DECIMAL(19,4) total works; checked sum overflow and one-unit base imbalance fail without a rounding/suspense plug. | U,I | REQ_F4116.10.2 | TODO |
| FI-GL-007 | Duplicate/conflict | Inclusive period overlap, including shared endpoint, returns 409; adjacent nonoverlapping periods work. | I | REQ_F4116.10.1 | TODO |
| FI-GL-008 | Duplicate/conflict | Two SQL connections create overlapping periods or post the same source; only one period/journal wins, second conflicts or replays. | I | REQ_F4116.10.1,10.3 | TODO |
| FI-GL-009 | Duplicate/conflict | Same permanent source/version/fingerprint replays committed ID with changed=false, zero added lines/number/audit and no success toast. | I,E | REQ_F4116.10.3 | TODO |
| FI-GL-010 | Duplicate/conflict | Changed fingerprint/stale version returns 409; soft-deleted history cannot free source key; period stale edit returns 409. | I | REQ_F4116.10.1,10.3 | TODO |
| FI-GL-011 | Partial result | One invalid line or fault after source/header/line/AR/AP/upstream state/audit rolls back all rows and number; no partial success. | I,E | REQ_F4116.10.2 | TODO |
| FI-GL-012 | Partial result | All-skipped/all-failed batch and zero-row batch are inapplicable: one posting is atomic; unchanged replay is explicitly reported as unchanged. | I,E | REQ_F4116.10.2–3 | TODO |
| FI-GL-013 | Permission | Anonymous read is 401; missing GL.JOURNAL.VIEW or period permission is 403; arbitrary client journal POST is unavailable. | I,E | REQ_F4116.10.1–3 | TODO |
| FI-GL-014 | Permission | Cross-company or SYS out-of-scope period/journal/source/account ID returns 404; forged company/actor/date/amount cannot alter posting. | I,E | REQ_F4116.10.1–3 | TODO |
| FI-GL-015 | State | Missing, overlapping or Closed period blocks posting; close/reopen uses version, and Posted journal remains readable and immutable. | I,E | REQ_F4116.10.1–2 | TODO |
| FI-GL-016 | State | Draft/Pending/Rejected/deleted/unsaved source cannot post; an issuing source may post only in its same transaction before final Posted flag. | I | REQ_F4116.10.2 | TODO |
| FI-GL-017 | Relation | Missing, inactive, deleted, non-leaf or wrong-company MD account and missing mapping fail before GL write; `IAccountUsage` protects Posted references. | I | REQ_F4116.10.2 | TODO |
| FI-GL-018 | Relation | Posted journal prevents period deletion/date move excluding its date; later MD changes cannot rewrite saved account and amount snapshots. | I | REQ_F4116.10.1–3 | TODO |
| FI-GL-019 | Error | Wrong base currency, source version, saved totals, balance or unavailable MD/FI contract returns named 409/422 and leaves upstream not Posted. | U,I | REQ_F4116.10.2 | TODO |
| FI-GL-020 | Error | HTTP 400/409/422/500 with traceId, timeout and network break show translated reason; fresh committed source lookup distinguishes success from safe retry. | I,E | REQ_F4116.10.1–3 | TODO |
| FI-GL-021 | Read/UI | Stable paged company/SYS-scope list/detail shows saved lines/totals, empty/search-empty/no-permission; no uncommitted or foreign-company data. | I,E | REQ_F4116.10.3 | TODO |
| FI-GL-022 | UI | Three languages and light/dark normal, validation, success, unchanged replay, permission and server error states match fresh GL/source reads. | E | REQ_F4116.10.1–3 | TODO |
| FI-GL-023 | Database/audit | Real SQL proves permanent source unique key, period lock, FI FKs/indexes/checks, immutable Posted history, audit rollback and reversible migration. | I | REQ_F4116.10.1–3 | TODO |
| FI-GL-024 | File | No import/export in this GL slice: empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable until a separate feature exists. | — | REQ_F4116.10.3 | N/A |

### WU-FI-004a3c base amount preflight (2026-11-05T16:06:30+08:00)

This pure preflight covers BR-FI-026 before the SQL posting service. It never authorises a source, account or period and never writes a journal. U=unit; later I/E checks remain FI-GL-001–024.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-BAL-001 | Normal | Two or more one-sided base legs with equal positive totals return exact debit and credit totals. | U | REQ_F4116.10.2 | PASS |
| FI-GL-BAL-002 | Boundary | Null/empty/one line, zero/negative/both-sided leg, fifth decimal, SQL decimal(19,4) excess and sum overflow have distinct invalid outcomes with no rounded plug. | U | REQ_F4116.10.2 | PASS |
| FI-GL-BAL-003 | Duplicate/conflict | A repeated account is valid as a separate ordered leg; balance validation does not merge lines or decide source replay. | U | REQ_F4116.10.2–3 | PASS for separate repeated account; source replay remains FI-GL-009/010 |
| FI-GL-BAL-004 | Partial result | Invalid leg rejects the whole candidate; there is no batch partial, all-skipped or zero-row success. | U | REQ_F4116.10.2 | PASS |
| FI-GL-BAL-005 | Permission | No actor or company access in a pure amount rule; FI source and HTTP authorization remain FI-GL-013/014. | I,E | REQ_F4116.10.2 | N/A here; later TODO |
| FI-GL-BAL-006 | State | No source or journal state in a pure amount rule; Posted immutability remains FI-GL-015/016/023. | I | REQ_F4116.10.2 | N/A here; later TODO |
| FI-GL-BAL-007 | Relation | Positive account ID is required, but MD account eligibility/mapping remains FI-GL-017. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-BAL-008 | Error | Unbalanced totals report imbalance; arithmetic/SQL range excess reports overflow without throwing or returning misleading totals. HTTP errors remain FI-GL-019/020. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-BAL-009 | File | No file is accepted in the base amount preflight. | — | REQ_F4116.10.2 | N/A |

### WU-FI-004a3e account reference and snapshot preflight (2026-11-05T16:35:48+08:00)

This pure rule consumes an MD public-contract result plus FI-supplied expected/current mapping versions. U=offline unit; it does not query MD, authorise a caller, persist a line or prove a SQL rollback.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-ACCOUNT-001 | Normal | Matching same-company ID/code, active leaf account and mapping version yields scalar account/name/version snapshot. | U | REQ_F4116.10.2 | PASS |
| FI-GL-ACCOUNT-002 | Boundary | Zero company/ID/version, empty, spaced, nondigit or over-30-character code and blank/over-200-character name reject without snapshot. | U | REQ_F4116.10.2 | PASS |
| FI-GL-ACCOUNT-003 | Duplicate/conflict | Changed FI mapping version wins before missing account; changed MD ID or code rejects. Concurrent mapping/account change and version conflict require SQL posting. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-ACCOUNT-004 | Partial result | One invalid account rejects its candidate without a partial snapshot; atomic multi-line rollback belongs to FI-GL-011. No batch/all-skipped operation exists. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-ACCOUNT-005 | Permission | Wrong company rejects; actor and SYS scope remain FI-GL-013/014. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-ACCOUNT-006 | State | Inactive and nonleaf accounts reject; MD `RequirePostableAsync` handles deleted/missing account as 404. Posting source state remains FI-GL-016. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-ACCOUNT-007 | Relation | MD public result must match FI account reference; posted usage protection via `IAccountUsage` and persisted immutable snapshot remain FI-GL-017/018. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-ACCOUNT-008 | Error | Pure outcomes identify invalid input, mapping, missing, company, changed, inactive, nonleaf and malformed snapshot; HTTP 400/409/422/500/timeout feedback remains FI-GL-019/020. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-ACCOUNT-009 | File | No file input is accepted by this pure account preflight. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.1 → BR-FI-023,029–030 → FI-GL-001,004,007–008,010,013–015,018,020,022–023; REQ_F4116.10.2 → BR-FI-024–027,029–030 → FI-GL-001–006,008,011–017,019–020,022–023 and FI-GL-ACCOUNT-001–009; REQ_F4116.10.3 → BR-FI-028–030 → FI-GL-008–010,012–014,018,020–024. Real SQL and UI tests remain TODO.

### WU-FI-004a3f whole-journal offline draft (2026-11-05T16:45:51+08:00)

This pure assembly uses already checked account snapshots and BR-FI-026 amounts. U=offline unit. It does not fetch authoritative accounts or mapping, hold a SQL lock, persist a line, or accept an HTTP/file request.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-DRAFT-001 | Normal | Two same-company, same-mapping account snapshots and balanced legs produce ordered complete lines and equal totals. | U | REQ_F4116.10.2 | PASS |
| FI-GL-DRAFT-002 | Boundary | Zero company/mapping, no legs, malformed snapshot name or excessive amount precision rejects. | U | REQ_F4116.10.2 | PASS |
| FI-GL-DRAFT-003 | Duplicate/conflict | Repeated account legs retain separate sequence; changed mapping rejects. Concurrent mapping change remains SQL TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-DRAFT-004 | Partial result | An invalid second account or amount returns no first line and zero totals; there is no batch skip. | U,I | REQ_F4116.10.2 | U PASS; SQL rollback TODO |
| FI-GL-DRAFT-005 | Permission | Foreign company rejects; caller/SYS scope checks remain FI-GL-013/014. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-DRAFT-006 | State | Inactive/nonleaf account result rejects; saved source and period states remain FI-GL-004/016. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-DRAFT-007 | Relation | Scalar account ID/code/name/account version/mapping version remain attached to each line; durable snapshots and MD usage remain FI-GL-017/018. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-DRAFT-008 | Error | Result carries the failing one-based line or amount status; HTTP 400/409/422/500/timeout feedback remains FI-GL-019/020. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-DRAFT-009 | File | No file input or output in this pure draft. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-026/027/029 → FI-GL-DRAFT-001–009 → `GlJournalLineDraftRulesTests`. Durable SQL and UI acceptance remain FI-GL-001–024.

### WU-FI-004a3g repeated account snapshot consistency (2026-11-05T16:52:27+08:00)

U=offline unit. This slice accepts already checked scalar snapshots; it does not read MD or SQL.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-CONSIST-001 | Normal | Distinct account snapshots build a balanced ordered draft. | U | REQ_F4116.10.2 | PASS via FI-GL-DRAFT-001 |
| FI-GL-CONSIST-002 | Boundary | Same account ID and code with identical name/version can appear twice as separate legs. | U | REQ_F4116.10.2 | PASS |
| FI-GL-CONSIST-003 | Duplicate/conflict | Same ID with changed code/name/version, or same code with different ID, returns `SnapshotConflict` at later line. | U | REQ_F4116.10.2 | PASS |
| FI-GL-CONSIST-004 | Partial result | Conflict on second leg returns zero lines and totals; no partial batch result is exposed. | U | REQ_F4116.10.2 | PASS |
| FI-GL-CONSIST-005 | Permission | Foreign company remains rejected before consistency comparison; actor scope remains SQL/HTTP TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-CONSIST-006 | State | Inactive or nonpostable MD account remains rejected by account preflight; fresh state check remains SQL TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-CONSIST-007 | Relation | Two lines cannot claim incompatible snapshots for one MD account; authoritative MD/usage checks remain SQL TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-CONSIST-008 | Error | Conflict reports one-based line; 400/409/422/500/timeout and translated feedback remain WU-FI-004b/c. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-CONSIST-009 | File | No file is accepted by this pure rule. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-027/029 → FI-GL-CONSIST-001–009 → `GlJournalLineDraftRulesTests`. Durable snapshots, posting and UI remain FI-GL-001–024.

### WU-FI-004a3h missing draft leg guard (offline)

The pure draft builder may receive an incomplete candidate array or account result from an upstream assembler. It must report the later line and expose no earlier line or totals. U=unit; authoritative SQL posting remains FI-GL-001–024.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-NULL-001 | Normal | Complete account results still create two ordered balanced legs. | U | REQ_F4116.10.2 | PASS via FI-GL-DRAFT-001 |
| FI-GL-NULL-002 | Boundary | Null candidate or null account result on the second leg gives `InvalidAccountSnapshot` and line 2 without an exception. | U | REQ_F4116.10.2 | PASS |
| FI-GL-NULL-003 | Duplicate/conflict | Repeated valid account legs remain separate; incomplete later leg is rejected even after a valid repeated leg. No source replay is decided here. | U,I | REQ_F4116.10.2 | U PASS via draft tests; I TODO |
| FI-GL-NULL-004 | Partial result | Invalid later leg returns zero lines and zero totals; batch skip and all-skipped are inapplicable to one journal. | U,I | REQ_F4116.10.2 | U PASS; SQL rollback TODO |
| FI-GL-NULL-005 | Permission | The pure builder has no actor; wrong-company snapshots already reject, while SYS permission/scope remain FI-GL-013/014. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-NULL-006 | State | Inactive/nonpostable account results already reject; fresh saved-source and period state remain SQL TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-NULL-007 | Relation | A missing MD account result cannot become a durable FI account reference; MD authoritative recheck and usage remain FI-GL-017/018. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-NULL-008 | Error | Missing leg reports a stable result instead of an exception; HTTP 400/409/422/500/timeout feedback remains FI-GL-019/020. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-NULL-009 | File | The draft builder accepts no file. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-026/027 → FI-GL-NULL-001–009 → `GlJournalLineDraftRulesTests`. This does not establish SQL posting acceptance.

### WU-FI-004a3i account rejection reason (offline)

U=offline unit. The result identifies the rejected MD preflight reason and line; SQL authority and user facing messages remain FI-GL-001–024.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-REASON-001 | Normal | Allowed accounts still produce a complete balanced draft with no account error. | U | REQ_F4116.10.2 | PASS via FI-GL-DRAFT-001 |
| FI-GL-REASON-002 | Boundary | Null account code in an allowed snapshot reports `InvalidSnapshot` at line 2 without throwing. | U | REQ_F4116.10.2 | PASS |
| FI-GL-REASON-003 | Duplicate/conflict | A changed account or mapping reason survives in the rejected draft; saved-source replay and concurrent mapping change require SQL. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-REASON-004 | Partial result | Any rejected second account yields no lines or totals; batch skip is inapplicable to one journal. | U,I | REQ_F4116.10.2 | U PASS; SQL rollback TODO |
| FI-GL-REASON-005 | Permission | Wrong-company MD result retains `WrongCompany`; actor scope remains SQL/HTTP TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-REASON-006 | State | Inactive and nonpostable MD statuses stay distinct; fresh account state remains SQL TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-REASON-007 | Relation | Missing MD account retains `Missing`; `IAccountUsage` and permanent snapshot remain TODO. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-REASON-008 | Error | Failed MD result reports its reason and one-based line; missing result has no reason. HTTP 400/409/422/500/timeout and translated feedback remain FI-GL-019/020. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-REASON-009 | File | Pure draft accepts no file input. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-027/029 → FI-GL-REASON-001–009 → `GlJournalLineDraftRulesTests`. SQL and UI acceptance remain open.

### WU-FI-004a3j posting-time draft recheck (offline)

At the eventual transaction boundary, compare every draft line against a fresh MD public result and the current FI mapping version. This pure rule receives those values; it does not fetch authoritative data. U=unit, I=real SQL, E=browser.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-RECHECK-001 | Normal | Every fresh account has the same company, identity, code, name and version as its ordered draft line. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-RECHECK-002 | Boundary | Missing fresh result rejects at its line; mismatched count or sequence rejects as invalid input; malformed input has no successful decision. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-RECHECK-003 | Duplicate/conflict | A renamed or reversioned account, changed mapping version or repeated account with inconsistent fresh state rejects the whole draft. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-RECHECK-004 | Partial result | A later rejection yields no accepted line prefix; batch skip and all-skipped do not apply to one journal. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-RECHECK-005 | Permission | Wrong company is rejected; actor scope is checked at SQL/HTTP layer. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-RECHECK-006 | State | Inactive or nonpostable fresh account is rejected; saved source/period state remains SQL work. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-RECHECK-007 | Relation | MD usage protection and permanent FI snapshot remain SQL work. | I | REQ_F4116.10.2 | TODO |
| FI-GL-RECHECK-008 | Error | Returns reason and one-based line; 400/409/422/500/timeout and translated UI remain 004b/c. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-RECHECK-009 | File | No file input exists. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-027/029 → FI-GL-RECHECK-001–009 → `GlJournalDraftRecheckRulesTests`; transactional authority remains FI-GL-001–024.

### WU-FI-004a3o cross-line consistency at posting recheck (offline)

The preflight must reject a draft mutated after construction or contradictory fresh MD results across repeated account references. U=unit; the writer's SQL authority and transaction remain FI-GL-001–024.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-GL-RECHECK-CONSIST-001 | Normal | Repeated identical account snapshots on separate debit and credit legs remain valid. | U | REQ_F4116.10.2 | PASS |
| FI-GL-RECHECK-CONSIST-002 | Boundary | Empty or malformed account code on a later line is rejected at that line. | U | REQ_F4116.10.2 | PASS |
| FI-GL-RECHECK-CONSIST-003 | Duplicate/conflict | Same account ID with a different code, name or version, or one code with two IDs, rejects the whole draft at the later line even when each fresh result matches its line. | U | REQ_F4116.10.2 | PASS |
| FI-GL-RECHECK-CONSIST-004 | Partial result | No successful decision or accepted prefix follows a cross-line conflict. Batch partial/all skipped do not apply to one journal. | U | REQ_F4116.10.2 | PASS |
| FI-GL-RECHECK-CONSIST-005 | Permission | Each fresh result still belongs to the same company; actor scope requires SQL/HTTP. | U,I | REQ_F4116.10.2 | U existing; I TODO |
| FI-GL-RECHECK-CONSIST-006 | State | Inactive/nonpostable fresh result is rejected before cross-line consistency; Posted writes remain immutable in SQL. | U,I | REQ_F4116.10.2 | U existing; I TODO |
| FI-GL-RECHECK-CONSIST-007 | Relation | FI checks MD's public results and does not read its table or create a cross-module FK; permanent snapshot and usage protection remain SQL work. | U,I | REQ_F4116.10.2 | U existing; I TODO |
| FI-GL-RECHECK-CONSIST-008 | Error | Conflict returns named status and one-based later line; HTTP 400/409/422/500/timeout and translated UI remain 004b/c. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-RECHECK-CONSIST-009 | File | No file input exists. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-027/029 → FI-GL-RECHECK-CONSIST-001–009 → `GlJournalDraftRecheckRulesTests`. Durable acceptance remains FI-GL-001–024.

### WU-FI-004a3k posting-time draft amount recheck (offline)

U=offline unit. This rule receives the existing whole-journal draft and caller-supplied current MD results; SQL source, mapping authority, company lock and persistence remain FI-GL-001–024.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-AMOUNT-001 | Normal | Unchanged balanced lines and matching positive totals pass the full recheck. | U | REQ_F4116.10.2 | PASS |
| FI-GL-AMOUNT-002 | Boundary | A line changed to both sides or to over-four-decimal precision is rejected with line and amount reason. | U | REQ_F4116.10.2 | PASS |
| FI-GL-AMOUNT-003 | Duplicate/conflict | A balanced draft changed to an unbalanced amount or mismatched header totals is rejected; no persisted key is claimed. | U,I | REQ_F4116.10.2 | U PASS; SQL TODO |
| FI-GL-AMOUNT-004 | Partial result | Any invalid line or total rejects the complete draft; no partial journal is returned. | U,I | REQ_F4116.10.2 | U PASS; SQL TODO |
| FI-GL-AMOUNT-005 | Permission | Pure recheck cannot grant posting permission; company scope and source authorization belong to 004a3/004b. | I,E | REQ_F4116.10.2 | TODO |
| FI-GL-AMOUNT-006 | State | A changed draft cannot be treated as Posted; committed journal immutability belongs to 004a3. | U,I | REQ_F4116.10.2 | U PASS; SQL TODO |
| FI-GL-AMOUNT-007 | Relation | Current MD account identity is checked before amount balance; no MD table is read directly. | U,I | REQ_F4116.10.2 | U PASS; SQL TODO |
| FI-GL-AMOUNT-008 | Error | Invalid amount returns a named status, line and reason where available; HTTP 400/409/422/500, timeout and translated UI belong to 004b/c. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-AMOUNT-009 | File | No file input is accepted in this rule. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.2 → BR-FI-026/027 → FI-GL-AMOUNT-001–009 → `GlJournalDraftRecheckRulesTests`. SQL posting acceptance remains open.

## WU-FI-005d sales collection and settlement scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are all covered below. U=unit, I=real SQL/authorised HTTP, E=Playwright. Every E case must compare message/style/count/reason with a fresh API or list read of receipt, allocation, GL and AR exposure. WU-FI-005a/b/c first write failing tests, then implement; no executable financial product test is delivered here.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-AR-COLL-001 | Normal | Save a positive Draft receipt; no journal, clearing or AR change before final approval. | I,E | REQ_F4116.1.2 | TODO |
| FI-AR-COLL-002 | Normal | SYS final approval posts one bank debit/clearing credit; unapplied equals received and open AR is unchanged. | I,E | REQ_F4116.1.4 | TODO |
| FI-AR-COLL-003 | Normal | Apply one receipt to one invoice; clearing debit/AR credit and committed FI open AR decrease equally. | I,E | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-004 | Normal | Partial settlement of several invoices leaves exact unapplied receipt and open AR; subsequent settlement uses remainder. | I,E | REQ_F4116.5.1,5.4 | TODO |
| FI-AR-COLL-005 | Boundary | Empty customer/bank/reference/currency/date, zero/negative ID/amount, fifth decimal, max valid DECIMAL(19,4) and overflow identify fields and save nothing. | U,I,E | REQ_F4116.1.2,5.2 | TODO |
| FI-AR-COLL-006 | Boundary | Month/year/leap-day receipt and settlement business dates resolve an inclusive Open GL period; missing/Closed period posts nothing. | U,I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-007 | Boundary | Zero or >1000 allocation lines, duplicate invoice row, zero line and sum overflow fail all; exact final remainder succeeds. | U,I,E | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-008 | Duplicate/conflict | Same bank reference in one company/bank, including deleted Posted history, is 409; distinct bank/company references are independent. | I | REQ_F4116.1.2,1.4 | TODO |
| FI-AR-COLL-009 | Duplicate/conflict | Identical committed receipt or settlement source replay returns same IDs, changed=false, zero extra GL/audit/exposure and warning UI. | I,E | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-010 | Duplicate/conflict | Changed fingerprint or stale version is 409; deleted history cannot free source key. | I | REQ_F4116.1.3,5.3 | TODO |
| FI-AR-COLL-011 | Duplicate/conflict | Two SQL connections allocate the same receipt or invoice concurrently; locks serialize and reject the losing over-allocation. | I | REQ_F4116.5.4 | TODO |
| FI-AR-COLL-012 | Partial result | Invalid second allocation or fault after receipt, line, journal, SYS/audit save rolls back every row, number and exposure; row reason appears. | I,E | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-013 | Partial result | Batch partial/all-skipped/all-failed/zero-row outcomes are inapplicable: one receipt/settlement is atomic and zero lines are rejected. | U,I | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-014 | Permission | Anonymous 401, missing distinct receipt/settlement action permission 403; VIEW alone cannot submit or approve. | I,E | REQ_F4116.1.1–6,5.1–6 | TODO |
| FI-AR-COLL-015 | Permission | Cross-company and SYS out-of-scope receipt/invoice/bank IDs return 404; forged company/customer/actor/amount cannot change FI. | I,E | REQ_F4116.1.1–6,5.1–6 | TODO |
| FI-AR-COLL-016 | State | Draft/Rejected edit/delete succeeds by version; Pending, Posted and soft-deleted edit/delete fail with named reason. | I,E | REQ_F4116.1.3,1.6,5.3,5.6 | TODO |
| FI-AR-COLL-017 | State | Pending rejection preserves reason/history and changes no GL/AR; zero-step final approval still posts in same transaction. | I,E | REQ_F4116.1.4–5,5.4–5 | TODO |
| FI-AR-COLL-018 | State | Unsaved, Draft, Rejected or deleted receipt cannot fund a settlement; Posted allocation cannot be changed or deleted. | I | REQ_F4116.5.2,5.4,5.6 | TODO |
| FI-AR-COLL-019 | Relation | Missing, inactive/deleted, nonpostable or wrong-company MD bank/account fails before GL; posted reference protects history. | I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-020 | Relation | Wrong customer/company/currency, credited/fully settled invoice or amount above invoice open balance fails with specific row reason. | I,E | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-021 | Relation | Receipt approval leaves SD unbilled commitments and FI open AR unchanged; settlement changes only committed FI AR and credit snapshot. | I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-022 | Error | HTTP 400/409/422/500 with traceId, timeout and network loss display translated reason; unknown commit uses fresh source lookup. | I,E | REQ_F4116.1.1–6,5.1–6 | TODO |
| FI-AR-COLL-023 | Error | FI, GL, SYS, MD or audit failure after an intermediate write leaves no Posted receipt/settlement, GL or AR change. | I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-024 | Read/UI | Stable paged reads show original, allocated/unapplied, open AR and journal; empty/search-empty/denied states and foreign-company hiding. | I,E | REQ_F4116.1.1,5.1 | TODO |
| FI-AR-COLL-025 | UI | Three languages and light/dark normal, validation, success, unchanged, error and timeout states; receipt toast says unapplied, settlement toast matches fresh saved balances. | E | REQ_F4116.1.1–5,5.1–5 | TODO |
| FI-AR-COLL-026 | Database/audit | SQL unique source/reference constraints, ordered locks, FI FKs/indexes, checked balances, immutable Posted history and reversible migration. | I | REQ_F4116.1.2,1.4,5.2,5.4 | TODO |
| FI-AR-COLL-027 | File | No import/export in this slice: empty, malformed, oversized, wrong-encoding, missing/extra-column files are inapplicable. | — | REQ_F4116.1.1,5.1 | N/A |
| FI-AR-COLL-028 | Scope | Cross-currency, refund, bank reconciliation, bad-debt forgiveness and posted reversal are rejected until separate authorised contracts exist. | I,E | REQ_F4116.1.4,5.4 | TODO |

Traceability: REQ_F4116.1.1–6 → BR-FI-031–034,036,038–040 → FI-AR-COLL-001–002,005–006,008–010,012,014–017,019,021–028. REQ_F4116.5.1–6 → BR-FI-031–032,034–040 → FI-AR-COLL-003–007,009–018,020–028. All real SQL, HTTP and UI cases remain TODO.

## WU-FI-006d purchase payment and AP settlement scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are covered below. U=unit, I=real SQL/authorised HTTP, E=Playwright. Each E case asserts translated message, style, count and row reason against a fresh API/list read of payment, allocation, GL and open AP. WU-FI-006a/b/c first write failing tests, then implement; no executable payment product test is delivered here.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-AP-PAY-001 | Normal | Save positive Draft payment; no journal, unapplied payment or open AP change before final approval. | I,E | REQ_F4116.3.2 | TODO |
| FI-AP-PAY-002 | Normal | SYS final approval posts one clearing debit/bank credit; unapplied equals posted amount while open AP stays unchanged. | I,E | REQ_F4116.3.4 | TODO |
| FI-AP-PAY-003 | Normal | Apply one payment to one payable; AP debit/clearing credit equals committed allocation and open AP decreases equally. | I,E | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-004 | Normal | Partial settlement of several payables leaves exact unapplied payment and open AP; a later settlement may use the remainder. | I,E | REQ_F4116.6.1,6.4 | TODO |
| FI-AP-PAY-005 | Boundary | Missing supplier/bank/reference/currency/date, zero/negative ID or amount, fifth money decimal, maximum DECIMAL(19,4) and overflow return field errors without writes. | U,I,E | REQ_F4116.3.2,6.2 | TODO |
| FI-AP-PAY-006 | Boundary | Month/year/leap-day saved dates resolve exactly one inclusive Open GL period; missing/Closed period leaves payment or settlement unposted. | U,I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-007 | Boundary | Zero or >1000 allocation lines, duplicate payable row, zero line and sum overflow reject all; exact final remainder succeeds. | U,I,E | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-008 | Duplicate/conflict | Same bank reference within company/bank, including deleted Posted history, returns 409; other bank or company is independent. | I | REQ_F4116.3.2,3.4 | TODO |
| FI-AP-PAY-009 | Duplicate/conflict | Identical committed payment/settlement replay returns stable IDs and changed=false with no extra journal/audit/AP effect; UI warns unchanged. | I,E | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-010 | Duplicate/conflict | Changed fingerprint, stale version or reuse of a deleted historical source key returns 409. | I | REQ_F4116.3.3,6.3 | TODO |
| FI-AP-PAY-011 | Duplicate/conflict | Two SQL connections allocate same payment or payable concurrently; loser rereads and cannot over-allocate. | I | REQ_F4116.6.4 | TODO |
| FI-AP-PAY-012 | Partial result | Invalid second row or injected failure after number, allocation, GL, SYS or audit write rolls back all changes; UI names the row and shows zero change. | I,E | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-013 | Partial result | Partial/all-skipped/all-failed/zero-row batch outcomes are inapplicable: one atomic settlement rejects zero lines and never silently skips. | U,I | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-014 | Permission | Anonymous 401, absent payment/settlement action permission 403; VIEW does not grant create/submit/final approval. | I,E | REQ_F4116.3.1–6,6.1–6 | TODO |
| FI-AP-PAY-015 | Permission | Cross-company and SYS out-of-scope payment/payable/bank IDs return 404; forged company/supplier/actor/amount cannot change FI. | I,E | REQ_F4116.3.1–6,6.1–6 | TODO |
| FI-AP-PAY-016 | State | Draft/Rejected edit/delete by version succeeds; Pending, Posted or deleted edits/deletes fail with state reason. | I,E | REQ_F4116.3.3,3.6,6.3,6.6 | TODO |
| FI-AP-PAY-017 | State | Pending rejection preserves reason/history without GL/AP effect; zero-step SYS final approval still posts in one transaction. | I,E | REQ_F4116.3.4–5,6.4–5 | TODO |
| FI-AP-PAY-018 | State | Unsaved, Draft, Rejected or deleted payment cannot fund settlement; Posted allocation remains immutable. | I | REQ_F4116.6.2,6.4,6.6 | TODO |
| FI-AP-PAY-019 | Relation | Missing/inactive/deleted/nonpostable or wrong-company MD bank/account/supplier fails before GL; Posted reference protects history. | I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-020 | Relation | Wrong supplier/company/currency, fully settled or credited payable, or row above open payable rejects whole settlement with specific row reason. | I,E | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-021 | Relation | Payment approval leaves FI open AP unchanged; only committed allocations decrease it, without editing PUR source or original payable. | I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-022 | Error | HTTP 400/409/422/500 with traceId, timeout and network loss display translated reason; unknown commit requires fresh source lookup. | I,E | REQ_F4116.3.1–6,6.1–6 | TODO |
| FI-AP-PAY-023 | Error | FI, GL, SYS, MD or audit failure after intermediate write leaves no Posted payment/settlement, journal or AP change. | I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-024 | Read/UI | Stable paged scoped reads show saved original/allocated/unapplied/open AP and journal; empty/search-empty/denied states and foreign-company hiding. | I,E | REQ_F4116.3.1,6.1 | TODO |
| FI-AP-PAY-025 | UI | Three-language light/dark normal, validation, success, unchanged, server-error and timeout states; payment toast says unapplied, settlement toast matches fresh balances. | E | REQ_F4116.3.1–5,6.1–5 | TODO |
| FI-AP-PAY-026 | Database/audit | SQL unique permanent source/reference, supplier lock order, FI FKs/indexes, checked balances, immutable Posted rows, audit rollback and reversible migration. | I | REQ_F4116.3.2,3.4,6.2,6.4 | TODO |
| FI-AP-PAY-027 | File | No import/export exists in this slice; empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable. | — | REQ_F4116.3.1,6.1 | N/A |
| FI-AP-PAY-028 | Scope | FX settlement, refund, bank reconciliation, prepayment reclassification, debt forgiveness and reversal fail closed pending separate authorised contracts. | I,E | REQ_F4116.3.4,6.4 | TODO |

Traceability: REQ_F4116.3.1–6 → BR-FI-041–043,045,047–050 → FI-AP-PAY-001–002,005–006,008–010,012,014–017,019,021–028. REQ_F4116.6.1–6 → BR-FI-043–050 → FI-AP-PAY-003–007,009–018,020–028. All real SQL, HTTP and UI cases remain TODO.

## WU-FI-007d other collection scenarios (design; product tests TODO)

The myerp-test-scenarios normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are covered. U=unit, I=real SQL/authorised HTTP, E=Playwright. Every E case compares translated message/style/reason with a fresh document, GL and AR credit-exposure read. WU-FI-007a/b/c first write failing product tests; this design unit delivers no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-AR-OTHER-001 | Normal | Save valid Draft; no cash/GL/AR mutation before final approval. | I,E | REQ_F4116.2.2 | TODO |
| FI-AR-OTHER-002 | Normal | SYS final approval posts one bank debit/income credit in an Open period and immutable source; fresh read matches amount and journal. | I,E | REQ_F4116.2.4 | TODO |
| FI-AR-OTHER-003 | Normal | Authorised paged detail shows category/account snapshot, payer, bank, state and journal with non-AR label. | I,E | REQ_F4116.2.1 | TODO |
| FI-AR-OTHER-004 | Boundary | Missing/oversize payer, category, bank, reference or date; zero/negative/fifth-decimal/max DECIMAL(19,4)/overflow amount yields field reason and zero writes. | U,I,E | REQ_F4116.2.2 | TODO |
| FI-AR-OTHER-005 | Boundary | Month/year/leap-day business date resolves exactly one inclusive Open period; missing/Closed/overlapping period rejects posting. | U,I | REQ_F4116.2.4 | TODO |
| FI-AR-OTHER-006 | Duplicate/conflict | Same company/bank/reference, including deleted Posted history, is 409; different bank/company can reuse reference. | I | REQ_F4116.2.2,2.4 | TODO |
| FI-AR-OTHER-007 | Duplicate/conflict | Identical committed source replay returns same journal/ID and changed=false with zero extra GL/audit/AR effect; UI warns unchanged. | I,E | REQ_F4116.2.4 | TODO |
| FI-AR-OTHER-008 | Duplicate/conflict | Changed fingerprint, stale edit version and two-connection final approval conflict without second journal. | I | REQ_F4116.2.3,2.4 | TODO |
| FI-AR-OTHER-009 | Partial result | Invalid category or injected GL/SYS/audit failure after intermediate write rolls back document state, number and every journal row; UI shows reason. | I,E | REQ_F4116.2.4 | TODO |
| FI-AR-OTHER-010 | Partial result | Batch partial/all-skipped/all-failed/zero-row outcomes are inapplicable: no batch endpoint exists, one document succeeds or fails atomically. | U,I | REQ_F4116.2.2,2.4 | TODO |
| FI-AR-OTHER-011 | Permission | Anonymous 401; missing separate VIEW/CREATE/EDIT/SUBMIT/APPROVE/DELETE permission 403; VIEW grants no posting. | I,E | REQ_F4116.2.1–6 | TODO |
| FI-AR-OTHER-012 | Permission | Foreign-company or SYS out-of-scope document/category/bank is hidden as 404; forged account/company/actor/amount cannot post. | I,E | REQ_F4116.2.1–6 | TODO |
| FI-AR-OTHER-013 | State | Draft/Rejected versioned edit/delete works; Pending/Posted/deleted edit/delete gives named state error. | I,E | REQ_F4116.2.3,2.6 | TODO |
| FI-AR-OTHER-014 | State | Pending rejection saves reason/history and no GL; zero-step SYS approval posts atomically; Posted cannot be rejected. | I,E | REQ_F4116.2.4,2.5 | TODO |
| FI-AR-OTHER-015 | Relation | Inactive/deleted/wrong-company category or bank, missing/ambiguous FI mapping or nonpostable MD account rejects before GL and preserves Posted snapshots. | I | REQ_F4116.2.2,2.4 | TODO |
| FI-AR-OTHER-016 | Relation | Posted other income changes neither FI open AR nor SD commitment; attempting invoice settlement/advance through this route is rejected. | I,E | REQ_F4116.2.4 | TODO |
| FI-AR-OTHER-017 | Error | HTTP 400/409/422/500 with traceId, timeout and network break show translated reason; unknown commit needs fresh source lookup. | I,E | REQ_F4116.2.1–6 | TODO |
| FI-AR-OTHER-018 | Read/UI | Stable scoped list supports empty/search-empty/denied, light/dark and three languages; success/unchanged/error messages match fresh GL/AR reads. | I,E | REQ_F4116.2.1,2.4 | TODO |
| FI-AR-OTHER-019 | Database/audit | SQL permanent source/reference uniqueness, ordered locks, FI FKs/indexes/checks, immutable Posted history, audit rollback and reversible migration. | I | REQ_F4116.2.2,2.4 | TODO |
| FI-AR-OTHER-020 | File | No import/export endpoint: empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable until separately designed. | — | REQ_F4116.2.1 | N/A |

Traceability: REQ_F4116.2.1–6 → BR-FI-051–058 → FI-AR-OTHER-001–020. Real SQL, HTTP and UI tests remain TODO.

## WU-FI-008d other payment scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are covered. U=unit, I=real SQL/authorised HTTP, E=Playwright. Every E case compares translated message, style and reason with fresh document, GL and open AP reads. WU-FI-008a/b/c first write failing product tests; this design unit delivers no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-AP-OTHER-001 | Normal | Save valid Draft; bank, GL and open AP remain unchanged before final approval. | I,E | REQ_F4116.4.2 | TODO |
| FI-AP-OTHER-002 | Normal | SYS final approval posts one expense debit/bank credit in an Open period and immutable source; fresh read matches amount and journal. | I,E | REQ_F4116.4.4 | TODO |
| FI-AP-OTHER-003 | Normal | Authorised paged detail shows category/account snapshot, payee, bank, state and journal with non-AP label. | I,E | REQ_F4116.4.1 | TODO |
| FI-AP-OTHER-004 | Boundary | Missing/oversize payee, category, bank, reference or date; zero/negative/fifth-decimal/max DECIMAL(19,4)/overflow amount gives field reason and zero writes. | U,I,E | REQ_F4116.4.2 | TODO |
| FI-AP-OTHER-005 | Boundary | Month/year/leap-day saved date resolves exactly one inclusive Open period; missing/Closed/overlapping period rejects posting. | U,I | REQ_F4116.4.4 | TODO |
| FI-AP-OTHER-006 | Duplicate/conflict | Same company/bank/withdrawal reference, including deleted Posted history, is 409; distinct bank/company can reuse it. | I | REQ_F4116.4.2,4.4 | TODO |
| FI-AP-OTHER-007 | Duplicate/conflict | Identical committed source replay returns same journal/ID, changed=false and zero extra GL/audit/AP effect; UI warns unchanged. | I,E | REQ_F4116.4.4 | TODO |
| FI-AP-OTHER-008 | Duplicate/conflict | Changed fingerprint, stale edit version and concurrent two-connection approval conflict without second journal. | I | REQ_F4116.4.3,4.4 | TODO |
| FI-AP-OTHER-009 | Partial result | Inactive category or injected GL/SYS/audit failure after intermediate write rolls back document state, number and journal; UI names reason. | I,E | REQ_F4116.4.4 | TODO |
| FI-AP-OTHER-010 | Partial result | Partial/all-skipped/all-failed/zero-row batch outcomes are inapplicable: no batch endpoint, one document succeeds or fails atomically. | U,I | REQ_F4116.4.2,4.4 | TODO |
| FI-AP-OTHER-011 | Permission | Anonymous 401; missing distinct VIEW/CREATE/EDIT/SUBMIT/APPROVE/DELETE permission 403; VIEW grants no posting. | I,E | REQ_F4116.4.1–6 | TODO |
| FI-AP-OTHER-012 | Permission | Foreign-company or SYS out-of-scope document/category/bank hidden as 404; forged account/company/actor/amount cannot post. | I,E | REQ_F4116.4.1–6 | TODO |
| FI-AP-OTHER-013 | State | Draft/Rejected versioned edit/delete works; Pending/Posted/deleted edit/delete yields named state error. | I,E | REQ_F4116.4.3,4.6 | TODO |
| FI-AP-OTHER-014 | State | Pending rejection saves reason/history without GL; zero-step SYS approval posts atomically; Posted cannot be rejected. | I,E | REQ_F4116.4.4,4.5 | TODO |
| FI-AP-OTHER-015 | Relation | Inactive/deleted/wrong-company category or bank, missing/ambiguous FI mapping or nonpostable MD account rejects before GL; Posted snapshot persists. | I | REQ_F4116.4.2,4.4 | TODO |
| FI-AP-OTHER-016 | Relation | Posted expense changes neither FI open AP nor PUR invoice state; attempts to settle invoice, record advance or prepayment here are rejected. | I,E | REQ_F4116.4.4 | TODO |
| FI-AP-OTHER-017 | Error | HTTP 400/409/422/500 with traceId, timeout and network break display translated reason; unknown commit needs fresh source lookup. | I,E | REQ_F4116.4.1–6 | TODO |
| FI-AP-OTHER-018 | Read/UI | Scoped list supports empty/search-empty/denied, light/dark and three languages; success/unchanged/error messages match fresh GL/AP reads. | I,E | REQ_F4116.4.1,4.4 | TODO |
| FI-AP-OTHER-019 | Database/audit | SQL permanent source/reference uniqueness, ordered locks, FI FKs/indexes/checks, immutable Posted history, audit rollback and reversible migration. | I | REQ_F4116.4.2,4.4 | TODO |
| FI-AP-OTHER-020 | File | No import/export endpoint: empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable until separately designed. | — | REQ_F4116.4.1 | N/A |

Traceability: REQ_F4116.4.1–6 → BR-FI-059–066 → FI-AP-OTHER-001–020. Real SQL, HTTP and UI tests remain TODO.

## WU-FI-009d statement source scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories follow. U=unit, I=real SQL/authorised HTTP, E=Playwright. Each E case compares the translated screen and export with a fresh FI source read using the same cutoff. WU-FI-009a/b first write failing tests; this design unit has no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-STMT-001 | Normal | Posted invoice then partial allocation: customer opening, positive charge, negative allocation and closing reconcile to saved FI movements. | I,E | REQ_F4116.11.1 | TODO |
| FI-STMT-002 | Normal | Posted purchase payable then partial payment allocation reconciles supplier opening, movements and closing. | I,E | REQ_F4116.11.2 | TODO |
| FI-STMT-003 | Normal | Posted credit source lowers customer balance once and retains immutable source and GL references. | I,E | REQ_F4116.11.1 | TODO |
| FI-STMT-004 | Boundary | Empty period has zero lines and opening = closing; zero opening is explicit, not missing data. | U,I,E | REQ_F4116.11.3 | TODO |
| FI-STMT-005 | Boundary | Inclusive same-day, month end, year end and leap-day ranges classify opening and period lines exactly. | U,I | REQ_F4116.11.1–3 | TODO |
| FI-STMT-006 | Boundary | Missing IDs/dates, reversed or >366-day range, unsupported currency and amount overflow fail by field with no guessed total. | U,I,E | REQ_F4116.11.3 | TODO |
| FI-STMT-007 | Duplicate/conflict | Replayed source/allocation and soft-deleted historical key produce no duplicate line or extra balance. | I | REQ_F4116.11.1–3 | TODO |
| FI-STMT-008 | Duplicate/conflict | Two SQL connections post, including backdated posting, during paging; old cutoff pages/export stay identical; fresh cutoff includes new line. | I,E | REQ_F4116.11.3 | TODO |
| FI-STMT-009 | Duplicate/conflict | Tampered/mismatched/expired token gives named error; no fallback to a mixed cutoff. | U,I,E | REQ_F4116.11.3 | TODO |
| FI-STMT-010 | Partial result | A failed source transaction contributes zero lines; a read failure returns no partial statement or success count. | I,E | REQ_F4116.11.1–3 | TODO |
| FI-STMT-011 | Partial result | Partial-success, all-skipped, all-failed and zero-row batch outcomes are inapplicable: each request is one atomic read, no batch write route. | U | REQ_F4116.11.3 | N/A |
| FI-STMT-012 | Permission | Anonymous 401; missing distinct AR/AP statement permission 403; a BI role alone cannot bypass FI authorization. | I,E | REQ_F4116.11.1–3 | TODO |
| FI-STMT-013 | Permission | Foreign-company or SYS out-of-scope partner is 404; cross-company token cannot expose a line. | I,E | REQ_F4116.11.1–3 | TODO |
| FI-STMT-014 | State | Draft, Pending, Rejected, deleted unposted and in-flight sources contribute no balance; committed Posted source does. | I | REQ_F4116.11.1–2 | TODO |
| FI-STMT-015 | Relation | Unapplied receipt/payment and other collection/payment do not alter AR/AP; only committed allocations do. | I,E | REQ_F4116.11.1–2 | TODO |
| FI-STMT-016 | Relation | Inactive partner/account after posting does not recalculate historical saved labels or amounts; inaccessible partner remains hidden. | I | REQ_F4116.11.1–2 | TODO |
| FI-STMT-017 | Error | 400/409/422/500 with traceId, timeout and network break show translated specific reason and no invented balance. | I,E | REQ_F4116.11.3 | TODO |
| FI-STMT-018 | Read/UI | Deterministic order and running balance across page boundaries; light/dark, three languages, empty/search-empty/denied states and export match same cutoff. | I,E | REQ_F4116.11.3 | TODO |
| FI-STMT-019 | Database/audit | Read, page, print and export cause no FI/GL/workflow mutation; indexed source reads and concurrent committed snapshot prove total conservation. | I | REQ_F4116.11.1–3 | TODO |
| FI-STMT-020 | File | Import, malformed upload, oversize upload and wrong encoding are inapplicable; export is tested for exact row/order/total/cutoff agreement. | I,E | REQ_F4116.11.3 | TODO |

Traceability: REQ_F4116.11.1 → BR-FI-067–074 → FI-STMT-001,003–019; REQ_F4116.11.2 → BR-FI-067–074 → FI-STMT-002,004–019; REQ_F4116.11.3 → BR-FI-069–074 → FI-STMT-004–020. BI REQ_F4117.5.4–5 and interface 7.20–21 consume this FI contract; all product tests remain TODO.

## WU-FI-010d cash-book source scenarios (design; product tests TODO)

The `myerp-test-scenarios` categories all apply below. U=unit, I=real SQL/authorised HTTP, E=Playwright. Every E test compares translated screen/export totals with committed FI data at the same cutoff. WU-FI-010a/b must write failing tests before implementation; this design unit has no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-CASH-001 | Normal | Posted sales receipt makes one positive bank movement with saved fund/source/journal identity. | I,E | REQ_F4116.12.1 | TODO |
| FI-CASH-002 | Normal | Posted purchase payment makes one negative bank movement; later AP allocation adds no second cash leg. | I,E | REQ_F4116.12.1 | TODO |
| FI-CASH-003 | Normal | Other collection/payment post signed cash legs; AR/AP balances remain governed by their own sources. | I,E | REQ_F4116.12.1 | TODO |
| FI-CASH-004 | Normal | Opening plus period debits minus credits equals closing and last running balance, including a posted opening source. | U,I,E | REQ_F4116.12.2 | TODO |
| FI-CASH-005 | Boundary | Empty period has zero lines and opening equals closing; zero balance is explicit, not omitted. | U,I,E | REQ_F4116.12.2 | TODO |
| FI-CASH-006 | Boundary | Inclusive same-day, month/year end and leap-day boundaries place lines in opening or period exactly once. | U,I | REQ_F4116.12.2 | TODO |
| FI-CASH-007 | Boundary | Missing account/dates, reversed or >366-day range, invalid ID, unsupported currency and decimal overflow give named error, no guessed total. | U,I,E | REQ_F4116.12.1–2 | TODO |
| FI-CASH-008 | Duplicate/conflict | Identical source replay or soft-deleted Posted history yields one movement; changed fingerprint is 409. | I | REQ_F4116.12.1 | TODO |
| FI-CASH-009 | Duplicate/conflict | Two SQL connections post during paging, including a backdated post; old pages/export remain identical, fresh cutoff includes it. | I,E | REQ_F4116.12.2 | TODO |
| FI-CASH-010 | Duplicate/conflict | Tampered, filter-mismatched or expired page token fails; no mixed-cutoff fallback. | U,I,E | REQ_F4116.12.2 | TODO |
| FI-CASH-011 | Partial result | A failed source transaction or one invalid cash leg rolls back journal, projection, workflow and audit together; failed read returns no partial balance. | I,E | REQ_F4116.12.1–2 | TODO |
| FI-CASH-012 | Partial result | Partial-success/all-skipped/all-failed/zero-row batch outcomes are inapplicable: this contract reads one account and has no batch write. | — | REQ_F4116.12.2 | N/A |
| FI-CASH-013 | Permission | Anonymous 401, absent `CM.CASH_BOOK.VIEW` 403; BI role alone cannot bypass FI authority. | I,E | REQ_F4116.12.1 | TODO |
| FI-CASH-014 | Permission | Foreign-company or SYS out-of-scope account/source is 404; cross-company token cannot reveal movements. | I,E | REQ_F4116.12.1–2 | TODO |
| FI-CASH-015 | State | Draft/Pending/Rejected/deleted unposted/in-flight source is absent; committed Posted source appears once. | I | REQ_F4116.12.1 | TODO |
| FI-CASH-016 | Relation | MD account rename/deactivation cannot rewrite saved fund or GL mapping; missing/ambiguous mapping fails posting rather than entering an arbitrary book. | I | REQ_F4116.12.1 | TODO |
| FI-CASH-017 | Relation | Two-leg fund transfer has opposite per-account changes and zero company net; no transfer is inferred from coincident references. | I | REQ_F4116.12.1–3 | DEFERRED: transfer source needs separate requirements; 010a must reject unsupported source |
| FI-CASH-018 | Reconciliation boundary | Unmatched bank row or balance difference leaves FI/GL unchanged; only later approved adjustment source can create a new movement. | I,E | REQ_F4116.12.3 | DEFERRED: external rows/adjustments belong to 011d; 010a has no write route |
| FI-CASH-019 | Error | 400/409/422/500 with traceId, timeout and network break show translated reason and no invented book/bank balance. | I,E | REQ_F4116.12.1–3 | TODO |
| FI-CASH-020 | Read/UI | Deterministic running balance across pages; three languages, light/dark, empty/search-empty/denied and export match fresh FI data at one cutoff. | I,E | REQ_F4116.12.2–3 | TODO |
| FI-CASH-021 | Database/audit | Read/page/print/export make no FI/GL/workflow changes; indexed cash-line read and two-connection cutoff proof conserve totals. | I | REQ_F4116.12.1–2 | TODO |
| FI-CASH-022 | File | Import, empty/malformed/oversize/wrong-encoding upload is inapplicable to this read source; report export agrees on row order, totals and cutoff. | I,E | REQ_F4116.12.2–3 | TODO |

Traceability: REQ_F4116.12.1–3 → BR-FI-075–082 → FI-CASH-001–022. BI REQ_F4117.5.1/interface 7.17 consume this FI contract. WU-FI-011d must design and test external statement import separately; all product tests remain TODO.

## WU-FI-011d external bank reconciliation scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories all apply. U=unit, I=real SQL/authorised HTTP, E=Playwright. Each E case compares translated text/style/counts and bank/book totals with fresh saved rows and FI cash-book data at the same cutoff. WU-FI-011a/b/c first write failing product tests. This design unit has no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-RECON-001 | Normal | Import valid base-currency statement: all rows/evidence/digest saved, bank arithmetic valid, GL/book unchanged. | I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-002 | Normal | Unique same-sign/amount/date candidate auto-matches one bank row to one Posted FI cash leg. | I,E | REQ_F4116.13.3 | TODO |
| FI-RECON-003 | Normal | Separate reviewer approves fully disposed, arithmetically valid frozen revision; GL stays unchanged. | I,E | REQ_F4116.13.4 | TODO |
| FI-RECON-004 | Normal | Approved export/page shows bank/book opening, closing, difference, source links, digest and identical cutoff. | I,E | REQ_F4116.13.2,13.6 | TODO |
| FI-RECON-005 | Boundary | Empty file/header only fails; zero rows cannot masquerade as a successful import. | U,I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-006 | Boundary | 10 MiB/10,000-row limits, exact maximum description/ID lengths, 366-day span, leap/month/year edge, fifth decimal and decimal overflow have field/row reasons. | U,I | REQ_F4116.13.1 | TODO |
| FI-RECON-007 | Boundary | Bank opening and every running balance reconcile to signed movements; zero and negative balances are valid, missing opening is incomplete. | U,I,E | REQ_F4116.13.1,13.2 | TODO |
| FI-RECON-008 | Boundary | Date tolerance is inclusive at 3 days; manual exception at 30 days needs reason/reviewer; day 31 fails. | U,I | REQ_F4116.13.3 | TODO |
| FI-RECON-009 | Duplicate/conflict | Exact file/row replay returns imported 0, skipped N, changed=false and warning; GL/audit unchanged. | I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-010 | Duplicate/conflict | Same bank transaction ID with changed payload, including old/superseded history, is rejected with row reason and no duplicate. | I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-011 | Duplicate/conflict | Two SQL connections import same ID or match same bank row/cash leg: one wins, other reports 409; no double match. | I | REQ_F4116.13.1,13.3 | TODO |
| FI-RECON-012 | Duplicate/conflict | Stale revision, tampered idempotency token or later/backdated FI post cannot silently change frozen cutoff. | U,I,E | REQ_F4116.13.3,13.4 | TODO |
| FI-RECON-013 | Partial result | Mixed valid, exact duplicate and conflicting rows report imported/skipped/rejected counts and per-row reasons; saved count equals response. | I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-014 | Partial result | All skipped and all rejected use warning with reasons; injected storage/audit failure rolls back all newly imported rows. | I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-015 | Partial result | Auto-match one unique, one ambiguous, one absent candidate reports matched/skipped/ambiguous and saves only unique match. | I,E | REQ_F4116.13.3 | TODO |
| FI-RECON-016 | Permission | Anonymous 401 and each missing VIEW/IMPORT/MATCH/SUBMIT/APPROVE/EXPORT permission 403; VIEW cannot match or approve. | I,E | REQ_F4116.13.1–6 | TODO |
| FI-RECON-017 | Permission | Foreign-company/out-of-scope statement or MD bank account is hidden as 404; forged actor/company/book leg cannot cross scope. | I,E | REQ_F4116.13.1–6 | TODO |
| FI-RECON-018 | State | Draft can match/unmatch; Pending/Approved cannot edit evidence or decisions; Rejected creates versioned draft, preserving history. | I,E | REQ_F4116.13.3,13.4 | TODO |
| FI-RECON-019 | State | Importer/matcher cannot approve own revision, including zero-step SYS workflow; rejection records reason and leaves GL untouched. | I,E | REQ_F4116.13.4 | TODO |
| FI-RECON-020 | Relation | Deleted/inactive/wrong-company/cash-till/foreign-currency MD fund account is rejected; earlier approved evidence remains reproducible. | I | REQ_F4116.13.1,13.2 | TODO |
| FI-RECON-021 | Relation | Draft/Pending/rolled-back FI leg is ineligible; Posted saved identity/sign/amount wins over current MD mapping. | I | REQ_F4116.13.2,13.3 | TODO |
| FI-RECON-022 | Relation | Bank-only fee/interest or book-only item has explicit disposition/reason; no automatic GL posting or Posted mutation. | I,E | REQ_F4116.13.4,13.5 | TODO |
| FI-RECON-023 | Relation | Later separately approved adjustment creates new FI source/book cutoff; old approved revision remains unchanged. | I,E | REQ_F4116.13.5 | DEFERRED: adjustment source needs separate requirement |
| FI-RECON-024 | Error | 400/409/422/500 with traceId, timeout and network break display translated cause; unknown commit triggers fresh manifest lookup, no false success. | I,E | REQ_F4116.13.1–6 | TODO |
| FI-RECON-025 | Error | Incomplete FI opening/cutoff expiry or bank arithmetic mismatch blocks approval with named reason, no guessed difference. | I,E | REQ_F4116.13.2,13.4 | TODO |
| FI-RECON-026 | File | Missing/extra/duplicate/reordered header, malformed quote, invalid UTF-8/replacement character, binary/empty file reject before storage. | U,I,E | REQ_F4116.13.1 | TODO |
| FI-RECON-027 | File | UTF-8 BOM optional, quoted comma/newline and CRLF/LF parse consistently; formula-leading text is rejected, not executed or exported unsafely. | U,I,E | REQ_F4116.13.1,13.6 | TODO |
| FI-RECON-028 | Read/UI | Normal, empty/search-empty/denied, partial/all-skipped/all-rejected and error screens in three languages/light/dark show exact saved data and disabled reasons. | I,E | REQ_F4116.13.1–6 | TODO |
| FI-RECON-029 | Database/audit | Unique row/match keys, indexed FKs, immutable evidence, reviewer audit and reversible migration; read/export changes no FI/GL/workflow rows. | I | REQ_F4116.13.1–6 | TODO |
| FI-RECON-030 | Boundary | Split/combined deposits, netted fees and FX matching are rejected with an unsupported-policy reason; no approximate auto-match. | U,I,E | REQ_F4116.13.3,13.5 | TODO |

Traceability: REQ_F4116.13.1–6 → BR-FI-083–092 → FI-RECON-001–030. Real SQL, HTTP and UI acceptance remains TODO pending WU-FI-011a/b/c; this design does not satisfy the product Definition of Done.
## WU-FI-012d fixed asset scenarios (design; product tests TODO)

`myerp-test-scenarios` normal, boundary, duplicate/conflict, partial result, permission, state, relation, error and file categories were reviewed. U=unit, I=real SQL/authorised HTTP, E=Playwright. Every E case must compare translated screen text, severity, amounts and counts with fresh saved FI source/journal data. WU-FI-012a/b/c/e write failing product tests first. This design unit has no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-FA-001 | Normal | Draft acquisition saves valid card with no GL change; approval creates one balanced asset/clearing journal and Posted card. | I,E | REQ_F4116.14.1–2 | TODO |
| FI-FA-002 | Normal | Authorised card read shows committed depreciation, calculated book value and exact journal links. | I,E | REQ_F4116.14.3 | TODO |
| FI-FA-003 | Normal | Preview then post first/middle/last depreciation month; cumulative amount equals cost less residual exactly. | U,I,E | REQ_F4116.15.1–3 | TODO |
| FI-FA-004 | Normal | Approved disposal removes cost/accumulated depreciation and posts exact proceeds/gain or loss; no later depreciation. | I,E | REQ_F4116.16.1–3 | TODO |
| FI-FA-005 | Boundary | Missing fields, maximal lengths, cost zero, residual equal/above cost, life 0/601 and five-decimal/overflow amounts fail by field. | U,I,E | REQ_F4116.14.1 | TODO |
| FI-FA-006 | Boundary | Acquisition/in-service on month end, year end and leap day follows full-month policy without a phantom month. | U,I | REQ_F4116.14.1,15.1 | TODO |
| FI-FA-007 | Boundary | Last depreciation absorbs rounding remainder; zero-residual and nonzero-residual cards never depreciate below residual. | U,I | REQ_F4116.15.1–2 | TODO |
| FI-FA-008 | Boundary | Disposal before service date, after prior disposal, or with negative/overflow proceeds fails without posting. | U,I | REQ_F4116.16.1–2 | TODO |
| FI-FA-009 | Duplicate/conflict | Active or soft-deleted asset code reuse is rejected; soft deletion never frees the acquisition source. | I,E | REQ_F4116.14.1–2 | TODO |
| FI-FA-010 | Duplicate/conflict | Two SQL connections capitalise same card: one wins; stale version and changed fingerprint return 409. | I | REQ_F4116.14.2 | TODO |
| FI-FA-011 | Duplicate/conflict | Two workers post same asset/period: one Posted entry/journal; replay changed=false and no duplicate audit. | I | REQ_F4116.15.2–3 | TODO |
| FI-FA-012 | Duplicate/conflict | Two disposals or disposal racing depreciation obey asset lock; only one valid committed outcome. | I | REQ_F4116.15.2,16.2 | TODO |
| FI-FA-013 | Partial result | Mixed eligible, already-posted, closed-period and invalid cards return exact posted/skipped/failed counts and per-asset causes; saved entries match. | I,E | REQ_F4116.15.2–3 | TODO |
| FI-FA-014 | Partial result | All skipped, all failed and zero eligible cards produce warning, no success claim, no GL change; retry is idempotent. | I,E | REQ_F4116.15.2–3 | TODO |
| FI-FA-015 | Partial result | Injected GL/audit failure rolls back one asset's entry, journal, state and audit; other committed assets are counted correctly. | I,E | REQ_F4116.15.2 | TODO |
| FI-FA-016 | Permission | Anonymous 401; missing each distinct asset/depreciation/disposal authority 403, including preview versus post. | I,E | REQ_F4116.14.1–3,15.1–3,16.1–3 | TODO |
| FI-FA-017 | Permission | Foreign company, forged company header or SYS out-of-scope asset/source gives 404 and leaks no values. | I,E | REQ_F4116.14.3,16.3 | TODO |
| FI-FA-018 | State | Draft/Pending/Rejected acquisition or disposal posts no GL; only approved final callback changes state. | I,E | REQ_F4116.14.2,16.2 | TODO |
| FI-FA-019 | State | Posted/Disposed card cannot edit cost/life, delete history, reopen or post further depreciation. | I,E | REQ_F4116.14.3,15.2,16.3 | TODO |
| FI-FA-020 | Relation | Inactive/deleted/wrong-company MD class or non-postable mapped account blocks new posting; historic snapshot remains readable. | I | REQ_F4116.14.2,15.2 | TODO |
| FI-FA-021 | Relation | AP-linked acquisition uses public FI/AP source once; no duplicate payable or cross-module table access. | I | REQ_F4116.14.2 | DEFERRED: linked source requires 012a and FI-003a |
| FI-FA-022 | Relation | Missing/closed earlier depreciation period blocks disposal rather than understating book value; proceeds source cannot double-post. | I | REQ_F4116.16.2 | TODO |
| FI-FA-023 | Error | 400/409/422/500 with traceId, timeout and network loss show translated specific cause and no invented Posted success. | I,E | REQ_F4116.14.1–3,15.1–3,16.1–3 | TODO |
| FI-FA-024 | Error | Unknown timeout resolves committed acquisition/depreciation/disposal by permanent source key before retry. | I,E | REQ_F4116.14.2,15.2,16.2 | TODO |
| FI-FA-025 | Database/audit | Source and FK indexes, unique constraints, reversible migrations, UTC+8 audit and GL/subledger totals verified with real SQL. | I | REQ_F4116.14.2,15.2,16.2 | TODO |
| FI-FA-026 | Read/UI | Three languages, light/dark, normal/empty/search-empty/denied/disabled states and journal totals match saved data. | I,E | REQ_F4116.14.3,15.3,16.3 | TODO |
| FI-FA-027 | File | Empty, malformed, oversize and wrong-encoding import are inapplicable: this slice has no file upload; export must reproduce saved source totals. | I,E | REQ_F4116.14.3,15.3,16.3 | N/A import; export TODO |
| FI-FA-028 | Boundary | Period closing or backdated posting cannot silently change an already Posted asset period; reconciliation names any difference. | I | REQ_F4116.15.2–3 | TODO |
| FI-FA-029 | State | Preview, read, page, print and export do not mutate card, source, GL, workflow or audit. | I,E | REQ_F4116.14.3,15.1,15.3,16.3 | TODO |

Traceability: REQ_F4116.14.1–3 → BR-FI-093–095,097–098,101–102 → FI-FA-001–2,005–6,009–10,016–21,023–27,029; REQ_F4116.15.1–3 → BR-FI-095–098,101–102 → FI-FA-003,006–7,011–15,016–20,023–29; REQ_F4116.16.1–3 → BR-FI-099–102 → FI-FA-004,008,012,016–19,022–27,029. All product tests remain TODO.

### WU-FI-012f straight-line monthly depreciation schedule (offline)

Pure `FixedAssetDepreciationRules.CalculateMonth` from saved cost, residual, life, in-service date and base-currency decimals (ADR-078). U=unit only; the unit-level parts of FI-FA-003/005/006/007 pass here, while card persistence, period state, source keys, GL posting and HTTP/UI remain 012a/b/e.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-FA-DEPR-001 | Normal | Even 1,200/12 card yields 100 per month, period numbers 1–12, accumulated and remaining after each month. | U | REQ_F4116.15.1 | PASS |
| FI-FA-DEPR-002 | Boundary | Non-even base rounds away from zero to currency decimals; last month absorbs the remainder; sum equals cost less residual exactly; nonzero residual is never crossed. | U | REQ_F4116.15.1–2 | PASS |
| FI-FA-DEPR-003 | Boundary | Rounded-up base is capped: later months become `FullyDepreciated` with 0; base below one currency unit gives `ZeroAmount` months and the last month catches up. | U | REQ_F4116.15.1–2 | PASS |
| FI-FA-DEPR-004 | Boundary | In-service on leap day, month end, year end or first day starts in that calendar month; next calendar month is period 2 without a phantom month. | U | REQ_F4116.14.1,15.1 | PASS |
| FI-FA-DEPR-005 | Boundary | Life 1 and 600, SQL maximum cost at 4 decimals compute exactly without overflow; month before service and after life give `BeforeInService`/`LifeExhausted`. | U | REQ_F4116.15.1 | PASS |
| FI-FA-DEPR-006 | Error | Null card, decimals outside 0–4, cost ≤0/over SQL maximum/excess scale, residual negative/≥cost/excess scale, life 0/601 and invalid year/month return a named status with no amount. | U | REQ_F4116.14.1,15.1 | PASS |
| FI-FA-DEPR-007 | Duplicate/conflict | Not applicable to a pure calculation; one Posted source per asset/period is FI-FA-011 (SQL). | — | REQ_F4116.15.2 | N/A |
| FI-FA-DEPR-008 | Partial result | Not applicable to a single month; batch posted/skipped/failed counts are FI-FA-013/014. Skip reasons (`BeforeInService`, `LifeExhausted`, `FullyDepreciated`, `ZeroAmount`) are supplied for that batch. | U | REQ_F4116.15.2–3 | U PASS; batch rule U PASS (FI-FA-BATCH, 012g); SQL TODO |
| FI-FA-DEPR-009 | Permission | Not applicable: no actor or company input; authority is FI-FA-016/017. | — | REQ_F4116.15.1–2 | N/A |
| FI-FA-DEPR-010 | State | Draft/Disposed card and closed periods are not inputs; preview and post must check them (FI-FA-018/019). Rule is side-effect free (FI-FA-029 unit part). | U,I | REQ_F4116.15.1 | U PASS; I TODO |
| FI-FA-DEPR-011 | Relation | Currency decimals come from MD public currency contract; the rule never reads MD tables. | I | REQ_F4116.15.1 | TODO |
| FI-FA-DEPR-012 | File | No file input exists. | — | REQ_F4116.15.1 | N/A |

Traceability: REQ_F4116.15.1–2 → BR-FI-095/096, ADR-078 → FI-FA-DEPR-001–012 → `FixedAssetDepreciationRulesTests`.

### WU-FI-012g depreciation batch per-asset outcome rule (offline)

Pure `FixedAssetDepreciationBatchRules.Plan` (post/skip/fail per asset, asset ID ascending) and `Summarize` (combine per-asset transaction attempts into requested/posted/skipped/failed counts and an outcome). U=unit only; the unit-level parts of FI-FA-013/014 pass here. Committed-source lookup, period state, per-asset SQL transactions, GL posting and HTTP/UI remain 012b/e. myerp-test-scenarios classes were reviewed.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-FA-BATCH-001 | Normal | All eligible assets committed → `Success`, changed=true, posted total equals the sum of planned amounts. | U | REQ_F4116.15.2 | PASS |
| FI-FA-BATCH-002 | Partial result | Mixed Draft, Disposed, already-Posted, before-service, life-exhausted, invalid-residual and eligible assets give one specific reason each, ordered by asset ID; committed plus rolled-back attempts give `Partial` with exact 4/1/1/2 counts and the rolled-back errorCode. | U | REQ_F4116.15.2–3 | PASS |
| FI-FA-BATCH-003 | Partial result | Empty request `NoEligibleAssets`; all skipped or skipped+failed `NoChange`; all failed `AllFailed`; none is success and changed=false. | U | REQ_F4116.15.2–3 | PASS |
| FI-FA-BATCH-004 | Boundary | Closed period skips every asset with `PeriodNotOpen` without calculating (even an invalid card) and accepts no posting attempt. | U | REQ_F4116.15.1–2 | PASS |
| FI-FA-BATCH-005 | Boundary | Schedule `ZeroAmount`/`FullyDepreciated` become skips; invalid decimals/cost/useful life become failures with the same-named reason. | U | REQ_F4116.15.2 | PASS |
| FI-FA-BATCH-006 | Duplicate/conflict | Duplicate asset in a request rejects the whole plan with the second index; duplicate, unexpected (skip/unknown asset) or missing attempt rejects the summary with the asset ID and no counts. Real double-worker posting stays FI-FA-011. | U | REQ_F4116.15.2–3 | PASS |
| FI-FA-BATCH-007 | Error | Null list/item, undefined card state, asset ID ≤0, invalid year/month, null card on an Active asset (item fail only), committed attempt with errorCode, rolled-back attempt without errorCode. | U | REQ_F4116.15.2 | PASS |
| FI-FA-BATCH-008 | Permission | Not applicable: no actor input; FI-FA-016/017. | — | REQ_F4116.15.2 | N/A |
| FI-FA-BATCH-009 | State/Relation | Saved card state and committed-source flag are caller inputs; real reads, locks and rollback are FI-FA-011/013/015 (SQL). | I | REQ_F4116.15.2–3 | TODO |
| FI-FA-BATCH-010 | File | No file input exists. | — | REQ_F4116.15.2 | N/A |

Traceability: REQ_F4116.15.2–3 → BR-FI-096/098/102 → FI-FA-BATCH-001–010 → `FixedAssetDepreciationBatchRulesTests`.

### WU-FI-012h disposal cutoff and gain/loss rule (offline)

Pure `FixedAssetDisposalRules.Evaluate` from saved card state/values, disposal date, proceeds, the disposal-period Open flag and the asset's committed depreciation sources (ADR-079). U=unit only; the unit-level parts of FI-FA-004/008/022 pass here. Asset lock, source keys, cash-receipt proceeds relation, GL posting and HTTP/UI remain 012c/e. myerp-test-scenarios classes were reviewed.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-FA-DISP-001 | Normal | Disposal month not yet committed is caught up once; cost, accumulated depreciation, book value and gain are exact and the journal balances; proceeds equal to book value give neither gain nor loss; zero proceeds give a loss. | U | REQ_F4116.16.2 | PASS |
| FI-FA-DISP-002 | Boundary | Disposal in the in-service month catches up period 1; after life end no catch-up and residual remains as book value; sub-unit (`ZeroAmount`) months are not required and the last month uses the remainder; disposal in 9999-12 still finds a gap inside the life. | U | REQ_F4116.16.1–2 | PASS |
| FI-FA-DISP-003 | Boundary | Disposal date before in-service date (same month or earlier year) rejected; proceeds negative, beyond currency scale or above the SQL maximum rejected; maximum proceeds accepted. | U | REQ_F4116.16.1 | PASS |
| FI-FA-DISP-004 | Relation | Earliest missing earlier month blocks with `MissingDepreciation` and its period (FI-FA-022 unit part). | U | REQ_F4116.16.2 | PASS |
| FI-FA-DISP-005 | Duplicate/conflict | Duplicate committed period, committed period after the disposal month, amount different from schedule, before in-service or in a `ZeroAmount` month are rejected with index/period; input order does not matter. Real concurrent disposal/depreciation stays FI-FA-012. | U | REQ_F4116.16.2 | PASS |
| FI-FA-DISP-006 | State | Draft → `CardNotActive`; Disposed → `AlreadyDisposed`; undefined state invalid; closed disposal period → `PeriodNotOpen`. | U | REQ_F4116.16.1–2 | PASS |
| FI-FA-DISP-007 | Error | Null input/card/committed list, null or malformed committed entry (index), invalid cost/residual/life/decimals fail by field; every rejection returns zero amounts. | U | REQ_F4116.16.1 | PASS |
| FI-FA-DISP-008 | Permission | Not applicable: no actor input; FI-FA-016/017. | — | REQ_F4116.16.1–2 | N/A |
| FI-FA-DISP-009 | Partial result | Not applicable: one asset, all-or-nothing result. | — | REQ_F4116.16.2 | N/A |
| FI-FA-DISP-010 | File | No file input exists. | — | REQ_F4116.16.1 | N/A |

Traceability: REQ_F4116.16.1–2 → BR-FI-097/099/100, ADR-079 → FI-FA-DISP-001–010 → `FixedAssetDisposalRulesTests`.

### WU-FI-012i acquisition Draft field validation (offline)

Pure `FixedAssetAcquisitionDraftRules.Validate` from raw Draft request fields plus caller MD class/base-currency snapshots (ADR-080). U=unit only; the unit-level parts of FI-FA-005/006 pass here. Code uniqueness (FI-FA-009), company scope, version, HTTP 400 mapping and form messages remain 012a/e. myerp-test-scenarios classes were reviewed.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| FI-FA-DRAFT-001 | Normal | Valid Draft is trimmed/upper-cased and converts to a depreciation card whose first month is the in-service month; blank source reference is stored as null. | U | REQ_F4116.14.1 | PASS |
| FI-FA-DRAFT-002 | Boundary | Code exactly 30 with `-`/`_` accepted, 31 rejected; description 200 (CJK, trimmed) accepted, 201 rejected; source reference 100/101; life 1/600 accepted, 0/601 rejected; residual just below cost accepted; maximum cost at scale 4 and whole cost at scale 0 accepted. | U | REQ_F4116.14.1 | PASS |
| FI-FA-DRAFT-003 | Boundary | In-service same day (leap day) and next day over year end accepted; earlier in-service date rejected (FI-FA-006 unit part). | U | REQ_F4116.14.1 | PASS |
| FI-FA-DRAFT-004 | Error | Missing code/description/class ID/dates/life/currency → `Required`; space, dot, CJK or full-width code and description with line break → `InvalidFormat`; cost 0/negative/above SQL maximum → `OutOfRange`; extra decimals → `InvalidScale`; residual negative/equal/above cost → `OutOfRange`/`NotLessThanCost`; null request rejected. | U | REQ_F4116.14.1 | PASS |
| FI-FA-DRAFT-005 | Relation | MD class snapshot missing → `NotFound`, different ID → `Mismatch`, inactive → `Inactive`; currency code lower-case/length/digit or decimals outside 0–4 rejected without blaming amount scale. | U | REQ_F4116.14.1 | PASS |
| FI-FA-DRAFT-006 | Error | Several wrong fields report each field once in form order; residual is not compared with an invalid cost, but residual above the SQL maximum is still reported. | U | REQ_F4116.14.1 | PASS |
| FI-FA-DRAFT-007 | Duplicate/conflict | Not applicable offline: code reuse and stale version need SQL (FI-FA-009/010). | — | REQ_F4116.14.1 | N/A |
| FI-FA-DRAFT-008 | Permission / State / Partial | Not applicable: no actor, saved state or batch input; FI-FA-016–019. | — | REQ_F4116.14.1 | N/A |
| FI-FA-DRAFT-009 | File | No file input exists. | — | REQ_F4116.14.1 | N/A |

Traceability: REQ_F4116.14.1 → BR-FI-093/095, ADR-080 → FI-FA-DRAFT-001–009 → `FixedAssetAcquisitionDraftRulesTests`.

## WU-FI-013d tax source and return scenarios (design; product tests TODO)

The myerp-test-scenarios normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file classes were reviewed. U=unit, I=real SQL/authorised HTTP, E=Playwright. E checks translated text, severity and counts against fresh saved tax sources and Posted GL at the same cutoff. WU-FI-013a/b/c write failing product tests first; this design unit has no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-TAX-001 | Normal | Posted SD invoice creates output detail and matching Posted tax GL leg in same transaction. | I,E | REQ_F4116.17.1,17.4 | TODO |
| FI-TAX-002 | Normal | Billed SD return creates signed credit linked to original invoice line; unbilled part creates none. | I | REQ_F4116.17.1 | TODO |
| FI-TAX-003 | Normal | Matched PUR invoice creates recoverable input detail; nonrecoverable component enters configured cost and claimable total excludes it. | I,E | REQ_F4116.17.2 | TODO |
| FI-TAX-004 | Normal | Period draft freezes sources and calculates output minus eligible input plus approved adjustments; net credit is labelled credit. | U,I,E | REQ_F4116.17.3 | TODO |
| FI-TAX-005 | Normal | Separate reviewer approves zero-difference return; filing evidence then records external reference separately from approval. | I,E | REQ_F4116.17.4–5 | TODO |
| FI-TAX-006 | Boundary | Missing source/line/tax code, zero tax, maximum DECIMAL(19,4), fifth decimal and overflow have field/source reason and no partial post. | U,I | REQ_F4116.17.1–2 | TODO |
| FI-TAX-007 | Boundary | Inclusive month/year/leap-day period and policy tax point assign each source once; absent/ambiguous policy blocks approval. | U,I | REQ_F4116.17.3 | TODO |
| FI-TAX-008 | Boundary | No eligible sources yields explicit zero totals and Not Filed; no success claim or journal. | I,E | REQ_F4116.17.3 | TODO |
| FI-TAX-009 | Duplicate/conflict | Same source/version/fingerprint replay returns changed=false; changed payload or soft-deleted source identity returns 409. | I | REQ_F4116.17.1–2 | TODO |
| FI-TAX-010 | Duplicate/conflict | Two SQL connections post same source: one tax detail and one GL leg; second sees committed result or conflict. | I | REQ_F4116.17.1–2 | TODO |
| FI-TAX-011 | Duplicate/conflict | Two preparers or reviewers race on same period/revision; stale version and changed cutoff fail 409. | I | REQ_F4116.17.3–5 | TODO |
| FI-TAX-012 | Duplicate/conflict | Backdated source commits during paging/export; frozen revision remains identical, fresh revision includes source. | I,E | REQ_F4116.17.3,17.6 | TODO |
| FI-TAX-013 | Partial result | Mixed eligible, nonrecoverable and invalid source candidates report included/excluded/blocked counts and per-source reasons; saved manifest matches. | I,E | REQ_F4116.17.2–3 | TODO |
| FI-TAX-014 | Partial result | All excluded, all blocked and zero candidates use warning and cannot imply filing; failed source transaction leaves no detail/GL. | I,E | REQ_F4116.17.1–3 | TODO |
| FI-TAX-015 | Permission | Anonymous 401; each VIEW/PREPARE/SUBMIT/APPROVE/FILE/EXPORT authority is separate and missing one returns 403. | I,E | REQ_F4116.17.3–6 | TODO |
| FI-TAX-016 | Permission | Foreign company, forged header or SYS out-of-scope tax source/return is hidden as 404. | I,E | REQ_F4116.17.1–6 | TODO |
| FI-TAX-017 | State | Draft/Pending/Rejected/rolled-back SD or PUR source has no tax detail; only committed Posted source contributes. | I | REQ_F4116.17.1–2 | TODO |
| FI-TAX-018 | State | Draft/Pending/Approved/Filed revision controls edits and evidence; reviewer differs from preparer, including zero-step SYS workflow. | I,E | REQ_F4116.17.3–5 | TODO |
| FI-TAX-019 | Relation | Inactive/deleted MD tax code or nonpostable mapped account blocks new post; historic saved treatment remains readable. | I | REQ_F4116.17.1–2 | TODO |
| FI-TAX-020 | Relation | Over-credit of original invoice tax/base, unmatched purchase treatment or stock-cost policy fails atomically. | I | REQ_F4116.17.1–2 | TODO |
| FI-TAX-021 | Reconciliation | Missing/duplicate/wrong-account/wrong-period/mismatched GL leg names source and GL IDs and blocks approval; no balancing plug. | I,E | REQ_F4116.17.4 | TODO |
| FI-TAX-022 | Error | 400/409/422/500 with traceId, timeout and network break show translated cause; unknown commit needs fresh committed lookup. | I,E | REQ_F4116.17.1–6 | TODO |
| FI-TAX-023 | File | Return export uses frozen source order/totals, escapes formula-leading values and is read-only; no upload exists, so empty/malformed/oversize/wrong-encoding import is inapplicable. | I,E | REQ_F4116.17.6 | N/A import; export TODO |
| FI-TAX-024 | Database/audit | Unique source/revision keys, indexed FI FKs, reversible migration, reviewer audit and source-to-GL equality verified on real SQL. | I | REQ_F4116.17.1–6 | TODO |
| FI-TAX-025 | Read/UI | Three languages, light/dark, empty/search-empty/denied/disabled/partial/timeout screens match saved status, counts and cutoff. | I,E | REQ_F4116.17.3–6 | TODO |
| FI-TAX-026 | State | Read, preview, page, print and export change no tax detail, GL, workflow or audit rows. | I,E | REQ_F4116.17.3,17.6 | TODO |
| FI-TAX-027 | Relation | Filing evidence without external acknowledgement cannot mark Filed; later adjustment uses a linked revision and separate approved GL source. | I,E | REQ_F4116.17.5 | TODO |
| FI-TAX-028 | Boundary | Original/base net + tax = gross per source; zero-tax line omits zero GL leg; rounding remainder cannot become an implicit tax plug. | U,I | REQ_F4116.17.1–2,17.4 | TODO |
| FI-TAX-029 | Partial result | Injected audit, GL or manifest failure rolls back source/return mutation together; no partial Approved or Filed response. | I | REQ_F4116.17.1–5 | TODO |
| FI-TAX-030 | Error | Tampered, expired or filter-mismatched cutoff token fails explicitly; refresh creates a new revision rather than mixing sources. | U,I,E | REQ_F4116.17.3,17.6 | TODO |

Traceability: REQ_F4116.17.1–2 → BR-FI-103–106 → FI-TAX-001–3,006,009–10,013–14,017,019–20,024,028–29; REQ_F4116.17.3–6 → BR-FI-107–112 → FI-TAX-004–5,007–8,011–16,018,021–27,029–30. All product tests remain TODO.

## WU-FI-014d exchange-difference scenarios (design; product tests TODO)

The myerp-test-scenarios normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file classes were reviewed. U=unit, I=real SQL/authorised HTTP, E=Playwright. Each E case asserts translated screen text, severity, count and reason plus a fresh API/FI/Posted GL read at the same cutoff. WU-FI-014a/b/c write failing product tests first; this unit has no executable financial product test.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-FX-001 | Normal | Posted SD foreign invoice preserves saved currency/rate/original/base and one AR GL source. | I | REQ_F4116.18.1 | TODO |
| FI-FX-002 | Normal | Posted PUR foreign invoice preserves saved rate and AP GL source; MD rate edit does not restate either. | I | REQ_F4116.18.1 | TODO |
| FI-FX-003 | Normal | Foreign collection allocation computes AR gain/loss sign and balanced control/clearing/FX GL. | U,I,E | REQ_F4116.18.2 | TODO |
| FI-FX-004 | Normal | Foreign payment allocation computes AP gain/loss sign and balanced control/clearing/FX GL. | U,I,E | REQ_F4116.18.3 | TODO |
| FI-FX-005 | Normal | Period valuation saves rate/cutoff/manifest and posts exact open AR/AP deltas. | U,I,E | REQ_F4116.18.4 | TODO |
| FI-FX-006 | Normal | Partial settlement releases proportional prior overlay; next valuation adjusts remaining exposure only. | U,I | REQ_F4116.18.5 | TODO |
| FI-FX-007 | Normal | Full settlement assigns rounding remainder to final leg and leaves zero original/base/overlay. | U,I | REQ_F4116.18.5 | TODO |
| FI-FX-008 | Boundary | Zero gain/loss omits zero GL leg; base-currency sources are excluded with named reason. | U,I,E | REQ_F4116.18.2–4 | TODO |
| FI-FX-009 | Boundary | Missing/zero/negative/fifth-decimal amount or invalid currency/date/rate precision returns field error; no write. | U,I | REQ_F4116.18.1–4 | TODO |
| FI-FX-010 | Boundary | `decimal(19,4)` amount and `decimal(19,8)` rate maximum/overflow and rounding across many partial settlements never drift. | U,I | REQ_F4116.18.1–5 | TODO |
| FI-FX-011 | Boundary | Month/year/leap-day closing rate and period selection use saved inclusive date policy, not today's rate. | U,I | REQ_F4116.18.4 | TODO |
| FI-FX-012 | Boundary | Empty exposure set or no delta gives zero counts, changed=false, warning and no GL/audit mutation. | I,E | REQ_F4116.18.4,18.6 | TODO |
| FI-FX-013 | Duplicate/conflict | Identical source/version/fingerprint replay returns same IDs and changed=false; changed payload or soft-deleted key is 409. | I,E | REQ_F4116.18.2–5 | TODO |
| FI-FX-014 | Duplicate/conflict | Two SQL connections settle same exposure or post same valuation: one committed result, no duplicate journal/gain. | I | REQ_F4116.18.2–5 | TODO |
| FI-FX-015 | Duplicate/conflict | Settlement races valuation; locks or cutoff conflict prevent stale open exposure and double gain. | I | REQ_F4116.18.4–5 | TODO |
| FI-FX-016 | Duplicate/conflict | Backdated source commits while paging/exporting; frozen manifest stays fixed and fresh cutoff includes it. | I,E | REQ_F4116.18.4,18.6 | TODO |
| FI-FX-017 | Partial result | Mixed eligible, base-currency, missing-rate and closed-period candidates have exact included/skipped/failed counts and per-row reasons. | I,E | REQ_F4116.18.4,18.6 | TODO |
| FI-FX-018 | Partial result | All skipped, all failed and zero candidates warn without a Posted success claim or residual journal. | I,E | REQ_F4116.18.4,18.6 | TODO |
| FI-FX-019 | Partial result | Injected allocation/valuation GL or audit failure rolls back that atomic source; committed others alone count as posted. | I | REQ_F4116.18.2–5 | TODO |
| FI-FX-020 | Permission | Anonymous 401; missing VIEW/PREVIEW/POST/EXPORT or AR/AP settlement authority gives 403 for each route/action. | I,E | REQ_F4116.18.2–6 | TODO |
| FI-FX-021 | Permission | Foreign company, forged header and SYS out-of-scope exposure/valuation are hidden as 404. | I,E | REQ_F4116.18.1–6 | TODO |
| FI-FX-022 | State | Draft/Pending/Rejected/rolled-back invoice or allocation has no realised FX or Posted valuation input. | I | REQ_F4116.18.1–4 | TODO |
| FI-FX-023 | State | Posted source/valuation cannot edit, delete or reopen; closed period blocks new posting and correction uses compensating source. | I,E | REQ_F4116.18.4–6 | TODO |
| FI-FX-024 | Relation | Inactive/deleted MD currency/rate/account blocks new post but saved historic snapshot remains readable. | I | REQ_F4116.18.1–6 | TODO |
| FI-FX-025 | Relation | Missing/unpostable FX account mapping, wrong company/currency or cross-currency netting rejects atomically. | I | REQ_F4116.18.2–5 | TODO |
| FI-FX-026 | Relation | Over-allocation of original amount or missing FI receivable/payable prevents FX and AR/AP balance change. | I | REQ_F4116.18.2–3 | TODO |
| FI-FX-027 | Reconciliation | Missing/duplicate/wrong-account/wrong-period GL leg names source and blocks valuation approval; no balancing plug. | I,E | REQ_F4116.18.4,18.6 | TODO |
| FI-FX-028 | Error | 400/409/422/500 with traceId and network timeout show translated reason; unknown commit resolves by fresh source lookup. | I,E | REQ_F4116.18.1–6 | TODO |
| FI-FX-029 | Error | Expired, tampered or filter-mismatched cutoff token fails explicitly; retry cannot mix two manifests. | U,I,E | REQ_F4116.18.4,18.6 | TODO |
| FI-FX-030 | Database/audit | Permanent unique keys, indexed FI FKs, reversible migration, audit and source-to-GL totals verified on real SQL. | I | REQ_F4116.18.1–6 | TODO |
| FI-FX-031 | Read/UI | Three languages, light/dark, empty/search-empty/denied/partial/timeout views agree with saved source/rate/GL. | I,E | REQ_F4116.18.6 | TODO |
| FI-FX-032 | File/state | No import exists: empty/malformed/oversize/wrong-encoding upload is inapplicable. Export escapes formula text, shares frozen totals; read/preview/export writes nothing. | I,E | REQ_F4116.18.4,18.6 | N/A import; export TODO |

Traceability: REQ_F4116.18.1 → BR-FI-113–114 → FI-FX-001–2,009–11,021–25,030; REQ_F4116.18.2–3 → BR-FI-115–116,118,120–122 → FI-FX-003–4,006–10,013–15,019–26,028,030; REQ_F4116.18.4–6 → BR-FI-117–123 → FI-FX-005–8,011–32. All product tests remain TODO.
## WU-BI-001d FI report source scenarios (design; product tests TODO)

U=unit, I=real SQL/authorised HTTP contract, E=BI Playwright. WU-BI-001a must first write failing tests against each implemented FI source; no executable test is claimed here. Each E case compares displayed text/severity/count/totals and cutoff with an authorised FI-backed API read of the same manifest. Each class from `myerp-test-scenarios` is represented.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-RPT-001 | Normal | Posted AR principal plus dated credit/collection allocations yields exact as-of original, credit, settled and open base totals. | U,I,E | REQ_F4116.19.1 | TODO |
| FI-RPT-002 | Normal | Posted AP principal plus dated credit/payment allocations yields exact as-of totals; unapplied payment stays separate. | U,I,E | REQ_F4116.19.2 | TODO |
| FI-RPT-003 | Normal | Posted OtherCollection and OtherPayment produce saved category/account/source links, positive income/expense totals and signed net. | U,I,E | REQ_F4116.19.3 | TODO |
| FI-RPT-004 | Boundary | Empty partner/day and zero-open history return valid cutoff and zero/unchanged totals; 29 February, year end, 366 versus 367 days, max page and decimal overflow are checked. | U,I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-005 | Duplicate/conflict | Same source replay yields one row; two SQL connections with out-of-order and backdated commits preserve old pages while fresh cutoff sees the new source. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-006 | Duplicate/conflict | Token tamper, filter/page-size/actor mismatch is 400; expired manifest is 409; no duplicate or missing row across page boundaries. | U,I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-007 | Partial result | One missing source/journal/category snapshot, negative principal or inconsistent full-set total fails whole read with IDs and 422; no invented balancing row or partial file. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-008 | Partial result | All sources excluded by Posted/date/kind or zero eligible returns exact zero counts and a clear empty message, never an unqualified success. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-009 | Permission | Anonymous 401; BI permission without FI DETAIL/OTHER_REPORT permission 403; revoked role between pages denies continuation. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-010 | Permission | Forged company, foreign or SYS out-of-scope partner/source is 404; unfiltered list contains only visible partner rows. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-011 | State | Draft, Pending, Rejected, rolled-back, soft-deleted unposted and unapplied funds are absent; Posted historical rows remain immutable. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-012 | Relation | Inactivated/soft-deleted MD partner/category retains saved historical label but gives no new authority; missing immutable FI-to-GL linkage is incomplete. | I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-013 | Error | Malformed filter 400, changed/expired cutoff 409, currency/provenance/overflow 422, unexpected 500 with traceId and timeout/network unknown state have distinct translated messages. | U,I,E | REQ_F4116.19.1–3 | TODO |
| FI-RPT-014 | File | Export at same cutoff has identical row order/totals, escapes formula-leading text, includes empty header/zero totals and never returns a partial file on failure. Upload is N/A: these reads accept no file. | I,E | REQ_F4116.19.3 | TODO |
| FI-RPT-015 | Read/audit | Repeated view, paging, print and export change no FI/GL/workflow/business audit rows or source version. | I,E | REQ_F4116.19.1–3 | TODO |

Traceability: REQ_F4116.19.1–2 → BR-FI-124–128,131–132 → FI-RPT-001–2,004–13,015; REQ_F4116.19.3 → BR-FI-124,129–132 → FI-RPT-003–15. Implementation remains dependent on FI Posted source migrations and real SQL.

## WU-FI-015d month/year close scenarios (design; product tests TODO)

U=unit, I=real SQL plus authorised HTTP, E=Playwright. Implementation begins with failing U/I tests after FI sources and SQL are available; no executable product test is claimed here. Every E case asserts translated screen message/severity/counts and a fresh FI status/source/journal read with matching revision and cutoff. All scenario classes in `myerp-test-scenarios` are covered.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-CLOSE-001 | Normal | Complete, balanced source manifest closes an empty or active month exactly once with state, reviewer, cutoff and audit. | U,I,E | REQ_F4116.20.1–2 | TODO |
| FI-CLOSE-002 | Normal | Approved adjustment posts one balanced immutable GL source before close; Draft/Rejected have zero effect. | U,I,E | REQ_F4116.20.3 | TODO |
| FI-CLOSE-003 | Normal | Gapless Closed months and reconciled annual sources produce exact retained-earnings and next-year carry-forward journals atomically. | U,I,E | REQ_F4116.20.4 | TODO |
| FI-CLOSE-004 | Normal | Reviewed reopen links reason, compensation, prior revision and new Open state; reclose creates another revision. | I,E | REQ_F4116.20.5 | TODO |
| FI-CLOSE-005 | Boundary | Inclusive first/last day, December/January, leap day and policy-defined fiscal year assign every date exactly once. | U,I | REQ_F4116.20.1–4 | TODO |
| FI-CLOSE-006 | Boundary | Zero-source complete period may close with zero counts; missing provider remains blocked even when GL has zero rows. | I,E | REQ_F4116.20.1–2 | TODO |
| FI-CLOSE-007 | Boundary | Empty reason/evidence, maximum field length, invalid date, zero/negative amount, fifth decimal and checked total overflow fail by field/source. | U,I,E | REQ_F4116.20.3,20.5 | TODO |
| FI-CLOSE-008 | Boundary | First/last required fiscal period, non-12-month policy, gap/overlap and absent next Open year have explicit eligibility results. | U,I | REQ_F4116.20.4 | TODO |
| FI-CLOSE-009 | Duplicate/conflict | Two SQL connections close same month/year: one committed revision and set of journals, other unchanged or 409. | I | REQ_F4116.20.2,20.4 | TODO |
| FI-CLOSE-010 | Duplicate/conflict | Identical permanent source replay is changed=false with no new audit/journal; changed payload or soft-deleted key is 409. | I,E | REQ_F4116.20.3–5 | TODO |
| FI-CLOSE-011 | Duplicate/conflict | Backdated/out-of-order source commits after preview: stale manifest is 409, refreshed preview names new source. | I,E | REQ_F4116.20.1–2 | TODO |
| FI-CLOSE-012 | Duplicate/conflict | Concurrent post and close under documented lock order cannot commit a source into Closed period or deadlock indefinitely. | I | REQ_F4116.20.2–3 | TODO |
| FI-CLOSE-013 | Partial result | One pending SD/PUR/FI source, missing journal, cash/AR/AP/asset/FX/tax difference or unknown in-flight posting blocks whole close with each reason. | I,E | REQ_F4116.20.1–2 | TODO |
| FI-CLOSE-014 | Partial result | All blocked, some waived and zero candidates show exact complete/blocked/waived counts; no false close success. | I,E | REQ_F4116.20.1–2,20.6 | TODO |
| FI-CLOSE-015 | Partial result | Injected source, journal, carry-forward, state or audit failure rolls back entire month/year decision and exposes no partial Posted source. | I,E | REQ_F4116.20.2–4 | TODO |
| FI-CLOSE-016 | Permission | Anonymous 401; missing each VIEW/CLOSE/REOPEN/ADJUSTMENT/EXPORT permission 403; actor cannot approve own close. | I,E | REQ_F4116.20.1–6 | TODO |
| FI-CLOSE-017 | Permission | Forged company and SYS out-of-scope period, source or reviewer identity are hidden as 404; page token never conveys authority. | I,E | REQ_F4116.20.1–6 | TODO |
| FI-CLOSE-018 | State | Closed/Closing/Reopening periods reject all new or backdated posting; Posted journal remains immutable and readable. | I,E | REQ_F4116.20.2–5 | TODO |
| FI-CLOSE-019 | State | Cannot close month before earlier month or year before final month; reopen proceeds year then latest month backwards. | I,E | REQ_F4116.20.2,20.4–5 | TODO |
| FI-CLOSE-020 | State | Preview/read/export leaves period, journal, workflow and audit unchanged; timeout does not claim Closed until committed lookup. | I,E | REQ_F4116.20.1,20.6 | TODO |
| FI-CLOSE-021 | Relation | Filed tax return, approved bank reconciliation or BI export neither closes GL nor permits reopening without linked supersession/dependency decision. | I,E | REQ_F4116.20.1,20.5 | TODO |
| FI-CLOSE-022 | Relation | Inactive/deleted MD account or stale mapping blocks new adjustment, while historical account snapshot and Posted journal remain readable. | I | REQ_F4116.20.3,20.6 | TODO |
| FI-CLOSE-023 | Reconciliation | AR/AP, cash, asset, FX and tax-to-GL totals each match Posted sources; duplicate/missing/wrong-period legs name IDs and block close. | U,I,E | REQ_F4116.20.1–2 | TODO |
| FI-CLOSE-024 | Error | 400/409/422/500 with traceId, timeout/network interruption have distinct three-language messages and fresh committed lookup. | I,E | REQ_F4116.20.1–6 | TODO |
| FI-CLOSE-025 | Error | Expired/tampered/mismatched readiness or pagination token and revoked authority fail without silently refreshing the cutoff. | U,I,E | REQ_F4116.20.1–2,20.6 | TODO |
| FI-CLOSE-026 | Database/audit | Real SQL proves unique source/revision, nonoverlapping dates, indexed FI FKs, immutable history, lock order, reversible migration and audit. | I | REQ_F4116.20.1–6 | TODO |
| FI-CLOSE-027 | UI | Three languages/light/dark normal, empty, search-empty, validation, blocked, success, unchanged and unknown states match fresh saved data. | E | REQ_F4116.20.1–6 | TODO |
| FI-CLOSE-028 | File | Export exact frozen revision/totals with formula-safe text and no partial file; empty export includes header/zero counts. | I,E | REQ_F4116.20.6 | TODO |
| FI-CLOSE-029 | File | Upload is not applicable: this slice accepts no file import, so empty/malformed/oversize/wrong-encoding input cannot reach a close endpoint. | I | REQ_F4116.20.1–6 | N/A import |
| FI-CLOSE-030 | History | Pagination/export keep same order, revision and source links after later postings; superseded revision remains visible to authorised actor. | I,E | REQ_F4116.20.6 | TODO |

Traceability: REQ_F4116.20.1–2 → BR-FI-133–136,141–144 → FI-CLOSE-001,005–6,008–9,011–20,023–27; 20.3 → BR-FI-137,139,142,144 → 002,007,010,012,015–18,022,024,026; 20.4 → BR-FI-138–139 → 003,008–10,015,019,026; 20.5 → BR-FI-140–143 → 004,007,010,016–21,024; 20.6 → BR-FI-141–144 → 014,016–17,020,024–30. All product tests remain TODO.

### WU-FI-004a2g SQL period creation (2026-11-05T11:31:16+08:00)

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-CREATE-001 | Normal | Create Open period, then adjacent Closed period; saved status, version and row count match. | I | REQ_F4116.10.1 | PASS |
| FI-GL-CREATE-002 | Boundary | Two periods sharing an endpoint conflict; next day is allowed. Reversed range writes nothing. | I | REQ_F4116.10.1 | PASS |
| FI-GL-CREATE-003 | Duplicate/conflict | Every creation reads company periods while holding transaction-owned company applock; two connections released by one barrier yield exactly one Allowed and one Overlap with one saved row; while another company's applock is held open, a different company still creates within 5 s (WU-FI-004a2h). | I | REQ_F4116.10.1 | PASS |
| FI-GL-CREATE-004 | Partial result | One create is atomic; rejected create writes zero rows. Batch partial/all skipped is inapplicable. | I | REQ_F4116.10.1 | PASS |
| FI-GL-CREATE-005 | Permission | Company context mismatch is rejected before SQL. Actor permissions and SYS scope require 004b HTTP. | I,E | REQ_F4116.10.1 | I PASS; E TODO |
| FI-GL-CREATE-006 | State | Both Open and Closed save; update/close/reopen remain 004a2/004b. | I | REQ_F4116.10.1 | PASS |
| FI-GL-CREATE-007 | Relation | Creation has no Posted journal dependency; later update/delete read real saved Posted dates under the company lock (FI-GL-MUTATE-007). | I | REQ_F4116.10.1 | PASS in mutation tests |
| FI-GL-CREATE-008 | Error | Invalid range and overlap have distinct service decisions. HTTP 400/409/422/500, timeout and translated UI remain 004b/004c. | I,E | REQ_F4116.10.1 | I PASS; E TODO |
| FI-GL-CREATE-009 | File | Period creation has no file input. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2i SQL period update and delete (2026-11-05T12:27:53+08:00)

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-MUTATE-001 | Normal | A state/date update retains a Posted endpoint, increments version and saves Closed; empty period deletes. | I | REQ_F4116.10.1 | PASS |
| FI-GL-MUTATE-002 | Boundary | Shortening a period by one day to exclude a saved Posted endpoint is rejected; unchanged request retains version. Leap-day and year boundary are covered by pure FI-GL-MOVE tests; broader SQL date matrix remains TODO. | U,I | REQ_F4116.10.1 | PASS for Posted endpoint and no-op; broader SQL TODO |
| FI-GL-MUTATE-003 | Duplicate/conflict | A stale update or delete returns StaleVersion; simultaneous update/delete and overlap race remain TODO. | I | REQ_F4116.10.1 | PASS for stale version; races TODO |
| FI-GL-MUTATE-004 | Partial result | Each write is atomic; rejected changes leave the saved row unchanged. Batch partial/all-skipped/all-failed and zero-row results are inapplicable. | I | REQ_F4116.10.1 | PASS |
| FI-GL-MUTATE-005 | Permission | Wrong company context is rejected; authorised HTTP and SYS data scope remain WU-FI-004b. | I,E | REQ_F4116.10.1 | I PASS; E TODO |
| FI-GL-MUTATE-006 | State | Open-to-Closed state-only change saves; Posted history remains immutable. Close/reopen HTTP semantics remain WU-FI-004b. | I,E | REQ_F4116.10.1 | I PASS; E TODO |
| FI-GL-MUTATE-007 | Relation | Real saved Posted journal business date blocks exclusion and deletion; an empty adjacent period deletes. | I | REQ_F4116.10.1 | PASS |
| FI-GL-MUTATE-008 | Error | Service distinguishes excluded Posted date, contained journal, stale version and identity mismatch. HTTP 400/409/422/500, timeout and translated UI remain WU-FI-004b/c. | I,E | REQ_F4116.10.1 | I PASS; E TODO |
| FI-GL-MUTATE-009 | File | Period update and delete accept no file. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a2j concurrent period mutations (2026-11-05T15:59:23+08:00)

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-RACE-001 | Normal | Two connections start updates against one saved version; one change commits, the other reports stale version, and the saved row has version 2. | I | REQ_F4116.10.1 | PASS 2/2 targeted suite |
| FI-GL-RACE-002 | Boundary | The saved update may extend the start date across a year boundary; endpoint adjacency and rollback after rejected overlap still require SQL tests in 004a2. | I | REQ_F4116.10.1 | Year boundary PASS; adjacency/rollback TODO |
| FI-GL-RACE-003 | Duplicate/conflict | A simultaneous update and delete cannot both commit: a surviving row has version 2 and the competing writer is stale, or the deleted row makes the updater fail identity. | I | REQ_F4116.10.1 | PASS 2/2 targeted suite |
| FI-GL-RACE-004 | Partial result | Exactly one of the two writes commits; no partial batch result exists for a single period write. | I | REQ_F4116.10.1 | PASS |
| FI-GL-RACE-005 | Permission | Company identity remains checked by the service; actor permission and SYS data scope belong to 004b. | I,E | REQ_F4116.10.1 | Service PASS; HTTP TODO |
| FI-GL-RACE-006 | State | The update/delete race uses an empty Closed period; Posted concurrency with posting needs the shared lock in 004a3. | I | REQ_F4116.10.1 | Closed PASS; posting TODO |
| FI-GL-RACE-007 | Relation | A deleted period has no Posted references; concurrent posting against that period cannot be accepted until 004a3 shares its lock. | I | REQ_F4116.10.1 | Posting TODO |
| FI-GL-RACE-008 | Error | Loser gets StaleVersion or IdentityMismatch; an unrelated 15-second SQL applock timeout was observed in the wider class run. HTTP mapping belongs to 004b. | I,E | REQ_F4116.10.1 | Decision PASS; full class/HTTP TODO |
| FI-GL-RACE-009 | File | No file is accepted by period mutation. | — | REQ_F4116.10.1 | N/A |

### WU-FI-004a3m whole posting preflight composition (2026-11-05T21:07:20+08:00)

U=offline unit; this gate receives caller-supplied snapshots. It cannot replace the SQL transaction's authoritative reads or constitute a Posted journal.

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-GATE-001 | Normal | Valid saved source, base currency, complete balanced draft, one Open period and fresh MD accounts/mapping return its period ID. | U | REQ_F4116.10.1–2 | PASS |
| FI-GL-GATE-002 | Boundary | Leap-day endpoint is inclusive; missing or Closed period and malformed source expose no period ID. SQL boundary cases remain FI-GL-004/016. | U,I | REQ_F4116.10.1–2 | U PASS; I TODO |
| FI-GL-GATE-003 | Duplicate/conflict | A changed mapping or account version/name rejects the whole draft. Permanent source replay and two-connection conflicts remain FI-GL-006/010. | U,I | REQ_F4116.10.2–3 | U PASS; I TODO |
| FI-GL-GATE-004 | Partial result | Header or draft rejection never returns an accepted period; no partial line, skipped line or batch result exists. | U | REQ_F4116.10.2 | PASS |
| FI-GL-GATE-005 | Permission | Foreign company rejects; actor permission and SYS scope remain FI-GL-013/014. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-GATE-006 | State | Closed period rejects, even when accounts are current. Posted immutability and period state races remain FI-GL-015/016. | U,I | REQ_F4116.10.1–2 | U PASS; I TODO |
| FI-GL-GATE-007 | Relation | Missing fresh account results reject; MD account usage and durable snapshots remain FI-GL-017/018. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-GATE-008 | Error | Header and draft rejection report distinct reasons, with one-based changed account line; HTTP 400/409/422/500/timeout and translated UI remain FI-GL-019/020. | U,I,E | REQ_F4116.10.2 | U PASS; I/E TODO |
| FI-GL-GATE-009 | File | Posting preflight takes no file. | — | REQ_F4116.10.2 | N/A |

Traceability: REQ_F4116.10.1–2 → BR-FI-023/025–027/029 → FI-GL-GATE-001–009 → `GlPostingPreflightRulesTests`. SQL posting acceptance remains FI-GL-001–024.

### WU-FI-004a3n source replay and posting decision (2026-11-05T21:14:40+08:00)

U=offline unit. `Ready` is permission to attempt the SQL writer only after its own authority and lock checks; it is not a Posted result.

| ID | Category | Scenario | Level | REQ_F | Result |
|---|---|---|---|---|---|
| FI-GL-DECIDE-001 | Normal | New canonical source with complete current preflight returns `Ready` and one period ID. | U,I | REQ_F4116.10.2 | U PASS; I TODO |
| FI-GL-DECIDE-002 | Boundary | Missing period or changed mapping rejects a new source without period ID; leap day is covered by the existing gate. | U,I | REQ_F4116.10.1–2 | U PASS; I TODO |
| FI-GL-DECIDE-003 | Duplicate/conflict | Exact committed receipt returns `Unchanged` with its journal ID despite later period/mapping change; changed version/fingerprint or saved identity rejects without leaking the ID. Two-connection lock remains SQL work. | U,I | REQ_F4116.10.3 | U PASS; I TODO |
| FI-GL-DECIDE-004 | Partial result | Decision returns either one existing journal ID, one new period ID, or neither; no partial lines or batch/skipped result exists. | U,I | REQ_F4116.10.2–3 | U PASS; I TODO |
| FI-GL-DECIDE-005 | Permission | Foreign company cannot obtain the replay journal ID; actor and SYS scope remain authorised HTTP work. | U,I,E | REQ_F4116.10.2–3 | U PASS; I/E TODO |
| FI-GL-DECIDE-006 | State | A committed replay remains unchanged even if its period later closes; Posted immutability and fresh transaction reads remain SQL work. | U,I | REQ_F4116.10.3 | U PASS; I TODO |
| FI-GL-DECIDE-007 | Relation | Invalid saved receipt and stale mapping reject; MD usage protection and durable account snapshots remain FI-GL-017/018. | U,I | REQ_F4116.10.2–3 | U PASS; I TODO |
| FI-GL-DECIDE-008 | Error | Invalid source, malformed receipt and fingerprint/version conflict return distinct statuses; HTTP 400/409/422/500/timeout and translated UI remain 004b/c. | U,I,E | REQ_F4116.10.2–3 | U PASS; I/E TODO |
| FI-GL-DECIDE-009 | File | This decision accepts no file. | — | REQ_F4116.10.2–3 | N/A |

Traceability: REQ_F4116.10.2–3 → BR-FI-025–029 → FI-GL-DECIDE-001–009 → `GlPostingDecisionRulesTests`; durable SQL acceptance remains FI-GL-001–024.
