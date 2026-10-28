# SD test plan — WU-SD-000 baseline

WU-SD-002a real-MD checkpoint (2026-10-29T01:26:30+08:00): `SalesQuotationCreateTransactionTests.CreateUsesRealMdReferencesAndRollsBackCrossCompanyLine` uses real SQL MD customer, item, base currency and output tax lookups. It rejects other-company item/customer/tax IDs, a missing currency and mixed-currency lines; all five attempts leave no quotation and do not consume the SYS number. A valid retry saves real customer/item/tax snapshots and receives sequence 001. This adds integration evidence for SD-QUOTE-001/009/012/014/016. Edit/query/delete action scope, concurrent version checks, SYS approval, authorised HTTP and conversion still need coverage.

WU-SD-002a create-service checkpoint (2026-10-29T01:17:22+08:00): `SalesQuotationCreateTransactionTests` first failed to compile because the create service was absent, then passed against real SQL. The test covers SD-QUOTE-001/009/012/014/016: a real SYS `SAL.QUOTATION` rule allocates the first permanent number, MD contract responses produce saved customer/item/tax/amount snapshots, a missing currency rolls back both draft and number, and missing CREATE permission rejects without a second row. The test uses fixed MD contract responses; live MD rows, edit/query/delete commands, approval, authorised HTTP and conversion remain open. Partial/all-skipped and file cases remain N/A per SD-QUOTE-013/019.

WU-SD-002a transaction-store checkpoint (2026-10-29T01:04:58+08:00): `SalesQuotationDraftStoreTests` first failed to compile because the store was absent, then passed real SQL 3/3. These cover caller-owned transaction enforcement, a saved Draft and line, atomic replacement with version increment and permanent number, stale delete 409, soft delete visibility, full create rollback, and wrong-company insert rejection. They provide store-layer evidence for SD-QUOTE-001/002/010/012/014; SYS numbering, MD snapshot validation, action scope, approval and authorised HTTP remain unverified. SD-QUOTE-013/019 remain not applicable for the stated single-document and no-file reasons.

WU-SD-002a schema checkpoint (2026-10-29T00:51:18+08:00): `SalesQuotationSchemaTests` first failed because `sal_quotation` was absent, then passed against real SQL after migration. It verifies both tables, company-scoped parent FK, permanent number and line-order unique indexes, and date/deletion/quantity checks. This covers schema evidence for SD-QUOTE-001, 007 and 010 only; commands, SYS approval, rollback, authorised HTTP, UI and quotation conversion still require separate integration/E2E tests. SD-QUOTE-013 and 019 remain not applicable for the stated atomic-single-document and no-file reasons.

WU-SD-003a diagnostic wrapper checkpoint (2026-10-28T21:10:26+08:00): The support script's normal append, transient exclusive lock, persistent exclusive lock, absence of a partial append, and background job error propagation passed 5/5 in `Test-IntegrationDiagnosticsIo.ps1`. The complete real SQL integration suite passed 659/659 and its diagnostic wrapper returned 0. These are support checks for the SD-ORDER real SQL acceptance; quotation conversion still requires BLOCKED WU-SD-002a.

| Category | Diagnostic wrapper scenario | Result |
|---|---|---|
| Normal | Append a SQL DMV sample and complete the full integration run. | Pass: 659/659, wrapper 0. |
| Boundary | An exclusive file lock clears within the retry window; a lock lasting past the window fails. | Pass: both deterministic checks. |
| Duplicate/conflict | Competing file access cannot create a duplicate or partial sample. | Pass: original lines remain intact after failed append. |
| Partial result | A sampler failure must not be reported as an all-green diagnostic result. | Pass: background error is received; wrapper exits 2. |
| Permission, state, related, API error, file format | This local diagnostic has no user, document, API, or imported file. | Not applicable; business cases remain SD-ORDER-001–020. |

WU-SD-003a full EF plan checkpoint (2026-10-28T20:43:05+08:00): SD-ORDER-021 (I; REQ_F4112.2.3) now verifies the actual tracked full-line EF query through real SQL SHOWPLAN XML, asserting an Index Seek on the company/order/line index. This supplements the red/green cross-order insertion lock test. SalesOrder SQL/HTTP 31/31, Architecture 230/230 and Debug build with zero warnings/errors pass. Full integration remains open under approximately 1.3 GB free host memory; quotation conversion remains dependent on BLOCKED WU-SD-002a. The normal, boundary, duplicate/conflict, partial-result, permission, state, related, error and file categories remain SD-ORDER-001–020; single-order batch partial results and file upload are inapplicable as specified in 013/019.

WU-SD-003a line-index checkpoint (2026-10-28T20:28:17+08:00): SD-ORDER-011/012 retain real SQL and authorised HTTP concurrency assertions (200/409, 204/404 and persisted state). On `myerp_test`, SHOWPLAN of the company/order line predicate selected a clustered scan without a hint, even after separating the query from the header. With the existing `(company_id, order_id, line_no)` FORCESEEK hint it selected an index seek plus key lookup. The focused two-thread suite passed 29/29 four consecutive runs, and Architecture passed 230/230. A dedicated cross-order insert race and the full integration suite remain open; free memory was about 1.2 GB. The other normal, boundary, conflict, partial-result, permission, state, related, error and file cases remain SD-ORDER-001–020, with single-document partial results and files inapplicable per 013/019.

WU-SD-003a line-lock checkpoint (2026-10-28T20:19:54+08:00): SD-ORDER-011/012 reuse the real SQL and authorised HTTP concurrency regression after narrowing the store's serializable line read. Two two-thread focused runs passed 29/29; a pre-change run also passed, so this is regression evidence but not deterministic proof against the intermittent cross-test deadlock. The `system_health` graph identifies opposing header and clustered-line-key locks. Actual query plan, loaded race and full integration summary remain open; memory was about 1.35 GB free. Normal, boundary, conflict, partial-result, permission, state, related, error and file categories remain covered by SD-ORDER-001–020; atomic single-document and no-file inapplicability remain SD-ORDER-013/019.

WU-SD-003a authorised command race checkpoint (2026-10-28T16:01:39+08:00): SD-ORDER-011/012 now include two simultaneous authenticated HTTP PUT requests for the same Draft/version behind a held SQL row lock; exactly one returns 200 and the stale contender returns 409 `COMMON.CONCURRENCY.CONFLICT`. Two simultaneous authenticated DELETE requests for the resulting version yield 204 and 404 `SAL.ORDER.NOT_FOUND`. The existing test verifies the permanent number, final remark, owner and soft-delete state in SQL. The red test produced SQL deadlock 1205 and HTTP 500; acquiring an update lock before the action-scope read fixed it. Focused SalesOrder 29/29, Architecture 230/230 and Debug build 0 warnings/errors pass; the race test passed twice after the fix. Quotation conversion and full integration remain open. Batch partial result and file scenarios remain inapplicable under SD-ORDER-013/019.

WU-SD-003a concurrent mutation checkpoint (2026-10-28T15:52:20+08:00): SD-ORDER-011/012 now have four real-SQL two-connection cases for edit versus delete, each with the first transaction committed or rolled back. While the first transaction holds the saved header, the contender receives SQL lock timeout 1222. After commit, a stale delete gets 409 or a replacement of a deleted order gets 404; after rollback the original version can be retried. Each path independently checks the permanent number, version, deletion flag and saved line quantity. These are store-level transaction tests; authorised HTTP command races and quotation conversion remain open. Focused SalesOrder SQL/HTTP 29/29 and Debug build with zero warnings/errors pass. Batch partial results and file input remain inapplicable under SD-ORDER-013/019.

WU-SD-003a authorised CRUD HTTP checkpoint (2026-10-28T15:44:56+08:00): SD-ORDER-001/002/005/011/014/016 now use a real SYS user and company role with separate VIEW, CREATE, EDIT and DELETE permissions, plus persisted approved MD customer, active item, company base currency, output tax and SYS number rule. POST returns 201 with permanent HC001 and commercial names; GET reads the saved ID; PUT replaces the Draft and advances version to 2; stale DELETE returns 409; current DELETE returns 204; GET then returns 404. Independent SQL confirms the row is soft-deleted, retains number and revised remark, and belongs to the actor. The new test passed 1/1 after fixing a missing test-file using directive; focused SalesOrder SQL/HTTP passes 25/25 and Debug build has zero warnings/errors. Two-connection edit/delete races, quotation conversion and full integration remain open. Atomic single-order commands and no file input keep SD-ORDER-013/019 inapplicable.

Status: planned; no product tests or SQL assertions are claimed as passing. `U` = unit, `I` = real SQL and authorised HTTP integration, `E` = Playwright UI/API agreement. Each implementation unit first writes failing automated tests for its allocated cases, then implements and records results. E2E checks both visible message/state and persisted API/list data.

WU-SD-003a action-scope HTTP checkpoint (2026-10-28T15:37:40+08:00): SD-ORDER-005/014 now have a real SYS login and SQL test where company-wide VIEW reads another user's saved order, but Self-scoped EDIT and DELETE return 404 with `SAL.ORDER.NOT_FOUND` for that same ID. DELETE on the actor's own Draft returns 204, and independent SQL verification finds only that order soft-deleted. The test initially failed on an own-order delete because the direct SQL fixture omitted the document version; setting the fixture's Version to 1 fixed the setup without product changes. Focused SalesOrder tests pass 24/24 and Debug build has zero warnings/errors. Authorised create/edit with real MD references, two-connection races, quotation conversion and the full integration suite remain open. Atomic single-order commands and absent file input keep SD-ORDER-013/019 inapplicable.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-BASE-001 | Normal | Create quotation, approve through SYS and convert selected lines once; saved order retains source snapshot and neither draft changes INV. | I,E | REQ_F4112.1.2, REQ_F4112.1.3, REQ_F4112.2.2 |
| SD-BASE-002 | Normal | Create, edit, submit, approve, reject and cancel order through permitted states; approval history and document number persist. | I,E | REQ_F4112.2.1–6 |
| SD-BASE-003 | Normal | Draft delivery from approved order, SYS final approval posts exactly one saved INV outbound in same company; UI count/status matches ledger. | I,E | REQ_F4112.3.1–4 |
| SD-BASE-004 | Normal | Price/discount and output tax use business-date rule and currency precision; invoice covers posted balance; return records distinct source. | U,I,E | REQ_F4112.4.1, REQ_F4112.5.1, REQ_F4112.7.1 |
| SD-BASE-005 | Boundary | Empty customer, zero lines, 1001 lines, zero/negative quantity, more than six decimals and decimal overflow are rejected without partial rows. | U,I,E | REQ_F4112.1.2, REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-006 | Boundary | Month/year/leap-day effective rate, expiry and currency precision boundaries yield saved, repeatable totals. | U,I | REQ_F4112.1.2, REQ_F4112.4.1 |
| SD-BASE-007 | Boundary | Delivery exactly equal to remaining order quantity succeeds; one smallest unit above fails; serial/batch and warehouse validation comes from INV. | I,E | REQ_F4112.3.2, REQ_F4112.3.4 |
| SD-BASE-008 | Duplicate/conflict | Same company document code cannot repeat even after soft delete; two concurrent conversions or deliveries yield one winner and a clear 409/422. | I,E | REQ_F4112.1.3, REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-009 | Duplicate/conflict | Stale edit/submit/delete version returns 409 and preserves all saved lines, statuses and credit/stock. | I,E | REQ_F4112.2.3–6, REQ_F4112.3.3–6 |
| SD-BASE-010 | Partial result | Document writes are atomic: invalid middle line yields 0 new rows, 0 stock moves and no success toast; export aborts whole file on changed page. | I,E | REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-011 | Partial result | All skipped and partial-success cases are not applicable to these atomic document commands; a future bulk import/action must add counts, row reasons and tests before delivery. | U | REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-012 | Permission | Anonymous 401, missing action permission 403, other-company or out-of-scope document 404; CREATE/EDIT-only users access their own form options without VIEW. | I,E | REQ_F4112.1.1–2, REQ_F4112.2.1–3, REQ_F4112.3.1–3 |
| SD-BASE-013 | State | Draft, Pending, Rejected, Approved, Posted and Cancelled actions follow matrix; posted delivery and approved invoice cannot be directly edited/deleted. | I,E | REQ_F4112.2.3–6, REQ_F4112.3.3–6, REQ_F4112.5.1 |
| SD-BASE-014 | Related | Deleted/disabled customer, item, currency, tax, warehouse or quoted source is shown with reason; write revalidation rejects it. In-use documents prevent master deletion through usage contracts. | I,E | REQ_F4112.1.2, REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-015 | Error | 400 invalid field, 409 stale version, 422 status/credit/stock/reference, 500 with traceId, timeout and network loss show translated reasons and leave no false success. | I,E | REQ_F4112.2.2–6, REQ_F4112.3.2–6 |
| SD-BASE-016 | Error | INV posting failure rolls back SYS decision and SD Posted state; retry after correction posts once; cross-company same numeric ID does not collide. | I | REQ_F4112.3.4 |
| SD-BASE-017 | Credit | Missing FI/AR exposure contract blocks approval; limit excess blocks with amount/reason; authorised override is audited once. | U,I,E | REQ_F4112.2.4, REQ_F4112.6.1 |
| SD-BASE-018 | Invoice/return | Concurrent issue/return cannot exceed uninvoiced or unreturned delivery quantity; original Posted delivery remains immutable. | I,E | REQ_F4112.5.1, REQ_F4112.7.1 |
| SD-BASE-019 | File | No upload/import is in this baseline: empty, malformed, oversized, wrong encoding, missing/extra columns are not applicable. CSV export instead tests zero rows, formula escaping, 10,000 line limit and interrupted download. | I,E | REQ_F4112.2.1 |
| SD-BASE-020 | UI | Each interface has normal, empty, validation, server error and success states in three languages and two themes; error/success text agrees with persisted records. | E | REQ_F4112.1.1, REQ_F4112.2.1, REQ_F4112.3.1 |

Traceability: BR-SD-001–003 → 001, 008, 012, 014; BR-SD-004 → 002, 017; BR-SD-005–007 → 003, 007–010, 013, 016, 018; BR-SD-008–009 → 004, 006, 017–018; BR-SD-010 → 005, 010–015, 019–020. Later work units replace broad baseline scenarios with feature-level case IDs and results without weakening these checks.

## WU-SD-001 foundation scenarios

These cases cover the module foundation only. SQL service is stopped, so real SQL and authorised HTTP results remain unverified. No document endpoint or UI is released by this slice.

| ID | Category | Scenario and expected result | Layer | REQ_F | Result |
|---|---|---|---|---|---|
| SD-FOUND-001 | Normal | API composition registers SD module and a unique SAL action permission catalog. | U | REQ_F4112.1.1, REQ_F4112.2.1, REQ_F4112.3.1 | Pass, architecture test |
| SD-FOUND-002 | Boundary | Missing, zero or negative company ID is rejected before any document lookup. | U | REQ_F4112.1.1, REQ_F4112.2.1 | Pass, architecture test |
| SD-FOUND-003 | Duplicate/conflict | Duplicate permission code fails catalog construction; stale document version 409 awaits document persistence. | U,I | REQ_F4112.2.3 | Catalog covered by existing architecture rules; SQL pending |
| SD-FOUND-004 | Partial result | Not applicable: foundation has no batch or document write; future commands are atomic per SD-BASE-010–011. | U | REQ_F4112.2.2 | N/A |
| SD-FOUND-005 | Permission | Other-company document is hidden; anonymous, missing action permission and SYS action scope are enforced on future HTTP routes. | U,I | REQ_F4112.1.1–2, REQ_F4112.2.1–3 | Company and action-specific scope units pass; HTTP pending |
| SD-FOUND-006 | State | Not applicable: no persisted SD document or status transition exists in this slice. | I | REQ_F4112.2.4 | N/A |
| SD-FOUND-007 | Related | Customer, item, currency and output-tax options come only through MD public contracts, including disabled/deleted and company isolation. | I | REQ_F4112.1.2, REQ_F4112.2.2 | SQL test source compiled; fixture blocked by stopped SQL service |
| SD-FOUND-008 | Error | Invalid company produces forbidden; missing action 403, other scope 404 and backend 500 ProblemDetails await authorised HTTP. | U,I | REQ_F4112.1.1–2 | Company/action units pass; HTTP pending |
| SD-FOUND-009 | File | Not applicable: foundation has no upload, export or file endpoint. | I | REQ_F4112.2.1 | N/A |

Foundation review: the architecture test initially failed because the draft API spec used `SD.*`, while the established permission catalog requires the route/table prefix `SAL.*`. The spec and code now agree. The red test also caught the missing Sales assembly before implementation. No SQL acceptance is claimed.

`SalesSelectionOptionsTests` adds SD-FOUND-007 cases for approved/active versus pending/inactive/deleted/other-company references and inclusive output-tax effective dates. The initial build failed because `SalesSelectionOptions` was absent; the implementation now builds. These tests must execute against real SQL before WU-SD-001 can pass. Batch partial success, document status, HTTP error display and file cases remain as classified above because this contract only reads choices.

## WU-SD-002d quotation feature scenarios (planned, no product test result)

The following cases refine SD-BASE-001, 005–006, 008–015 and 020 for REQ_F4112.1.1–3. WU-SD-002a first adds failing `U`/real SQL `I` tests and then backend behavior; WU-SD-002b first adds failing `E` tests and then UI. Every E2E case asserts the displayed status/message and the persisted quotation, order or audit result. `I` includes authorised HTTP where relevant. No case below is marked passed by this design-only unit.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-001 | Normal | Create one-line and 1000-line Draft; permanent number, ordered line snapshots, totals and version persist; no INV or credit effect. | U,I,E | REQ_F4112.1.2 |
| SD-QUOTE-002 | Normal | Edit Draft at current version; replace all lines atomically, recalculate amounts, retain document number and audit edit. | U,I,E | REQ_F4112.1.2 |
| SD-QUOTE-003 | Normal | Submit Draft into SYS Pending; final approval/rejection updates SD state in same transaction and preserves decision history. | I,E | REQ_F4112.1.3 |
| SD-QUOTE-004 | Normal | No applicable SYS step immediately returns Approved in the submit transaction; final handler is not expected to run. | I | REQ_F4112.1.3 |
| SD-QUOTE-005 | Normal | Edit Rejected returns to Draft, retains rejected SYS round, then resubmit creates a distinct round. | I,E | REQ_F4112.1.3 |
| SD-QUOTE-006 | Normal | Convert Approved unexpired quote exactly once into Draft order with all lines and immutable quote/line IDs and monetary snapshots; quote and order are same-company. | I,E | REQ_F4112.1.3, REQ_F4112.2.2 |
| SD-QUOTE-007 | Boundary | Missing customer, no lines, 1001 lines, duplicate line number, zero/negative or >6-place quantity/price, negative price and discount outside 0–100 or >4 places reject with field/line reason and 0 rows. | U,I,E | REQ_F4112.1.2 |
| SD-QUOTE-008 | Boundary | Expiry equals business date and `IClock.Today` is valid; one day before business date rejects save, one day before today rejects submit/convert; month/year/leap day and clock rollover are exact. | U,I,E | REQ_F4112.1.2–3 |
| SD-QUOTE-009 | Boundary | Effective output tax and exchange rate on business-date endpoints are snapshotted; each line rounds net/tax/original/base amounts before header sums, including JPY zero decimals, half-way values and decimal(19,4) overflow. | U,I | REQ_F4112.1.2 |
| SD-QUOTE-010 | Duplicate/conflict | A document number remains unique after Draft soft delete; stale edit/delete/submit/void versions return 409 with saved rows and SYS state unchanged. | I,E | REQ_F4112.1.2–3 |
| SD-QUOTE-011 | Duplicate/conflict | Two simultaneous submits or conversions yield one winner; order-source unique constraints include soft-deleted/cancelled orders, and failed conversion leaves no extra order or permanent number. | I,E | REQ_F4112.1.3 |
| SD-QUOTE-012 | Partial result | Invalid middle line, SYS transaction failure or failed final callback rolls back the entire document/approval operation; UI reports failure and actual changed count is zero. | I,E | REQ_F4112.1.2–3 |
| SD-QUOTE-013 | Partial result | Partial success, all skipped and all failed are not applicable to a single atomic quotation command; no bulk quote import/action exists. Zero-change retry has no success toast. | I,E | REQ_F4112.1.2–3 |
| SD-QUOTE-014 | Permission | Anonymous is 401; missing exact SAL action is 403; other-company or action-scope-excluded ID is 404; a CREATE/EDIT-only user can fetch corresponding form options without VIEW. | I,E | REQ_F4112.1.1–3 |
| SD-QUOTE-015 | State | Draft edit/delete/submit, Rejected edit, Pending decision only, Approved convert/void, Voided read-only; attempts outside this matrix return named 422 and preserve history. | I,E | REQ_F4112.1.2–3 |
| SD-QUOTE-016 | Related | Disabled, deleted, pending-approval or other-company customer/item/currency/tax references fail create/edit/submit with field reason; saved historical snapshots stay readable after MD changes. | I,E | REQ_F4112.1.1–3 |
| SD-QUOTE-017 | Related | An approved quote already converted cannot be voided or converted again, even if its order is cancelled/soft-deleted; unconverted Approved quote may void, retaining number and history. | I,E | REQ_F4112.1.3 |
| SD-QUOTE-018 | Error | 400 field, 409 version/source, 422 status/reference/expiry/overflow, 500 with traceId, timeout and network loss display translated, specific text with no false success. | I,E | REQ_F4112.1.1–3 |
| SD-QUOTE-019 | File | Upload/import is not part of quotation View/Create/Edit: empty, malformed, oversized, wrong-encoding, missing/extra-column files are not applicable. Print/export needs separate permission, output and tests before UI exposure. | E | REQ_F4112.1.1 |
| SD-QUOTE-020 | UI | List distinguishes empty versus filter-empty versus no permission, saved status versus derived expiry, and disabled action reasons; normal, validation, conflict, server error and success states are checked in three languages and light/dark. | E | REQ_F4112.1.1–3 |

Traceability: BR-SD-011 → 002–005, 010, 015; BR-SD-012 → 008, 015, 017, 020; BR-SD-013 → 006, 011, 017; BR-SD-014 → 001, 007, 009, 016; BR-SD-015 → 003–004, 010, 012–015, 018–020. Exact SQL constraint, API and UI results are deferred to WU-SD-002a/b because SQL is stopped and no quotation persistence or screen exists.

WU-SD-002a partial result (2026-10-24): `SalesQuotationInputTests` and `SalesQuotationAmountTests` cover the pure input/amount portions of SD-QUOTE-001, 007–009 (16 assertions/tests), including 1000 lines, leap day, field errors, six/four-place precision, discount, line rounding and line/header overflow. Architecture project 99/99 passed. The input/amount helpers are not connected to a quotation persistence/API path yet; SQL, approval, permission, rollback and UI cases remain unverified. SD-QUOTE-013/019 remain not applicable for the reasons above; no bulk or file flow was added.

WU-SD-002a transition slice (2026-10-24T07:26:10+08:00): `SalesQuotationLifecycleTests` first failed because the transition types were absent, then passed 21/21. They cover Draft/Rejected edit, Pending and zero-step Approved submission, final approval/rejection, Approved conversion/void, seven invalid transitions, inclusive expiry on submit/convert, converted-source conflict, and derived expiry without mutating approval. These are pure `U` checks for SD-QUOTE-003–005, 008, 015 and 017; they do not prove SYS transaction, version, SQL uniqueness, HTTP or UI behavior. SD-QUOTE-013/019 remain not applicable to this single-document, file-free slice; permission, related-reference, 400/409/422/500, timeout and whole-document cases remain `I/E` gates.

## WU-SD-003d sales order scenarios (planned, no product test claimed)

`U` = pure unit, `I` = real SQL plus authorised HTTP, `E` = Playwright. Implementation units first add failing automated assertions, then code. Every E test asserts the visible message/count/status and the actual saved API/list data. These cases refine SD-BASE-002, 005–006, 008–015, 017 and 019–020; IDs remain stable.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-ORDER-001 | Normal | Manual one-line and 1000-line create save Draft, permanent number, line and rounded currency/base snapshots; no INV move or credit reserve. | U,I,E | REQ_F4112.2.2 |
| SD-ORDER-002 | Normal | Current-version Draft edit atomically replaces lines and retains number/audit; Rejected edit returns Draft while old SYS round remains. | I,E | REQ_F4112.2.3, REQ_F4112.2.5 |
| SD-ORDER-003 | Normal | Approved unexpired quotation converts all saved lines once; order retains immutable quote/line IDs and price/tax/rate snapshots despite later MD changes. | I,E | REQ_F4112.2.2 |
| SD-ORDER-004 | Normal | With valid FI/AR credit decision, submit reaches SYS Pending; final approve/reject or zero-step approve updates order in the same transaction and records each round. | I,E | REQ_F4112.2.4–5, REQ_F4112.6.1 |
| SD-ORDER-005 | Normal | Current-version Draft delete soft-deletes; Approved with no active/posted delivery cancels under separate permission and preserves number/source/history. | I,E | REQ_F4112.2.6 |
| SD-ORDER-006 | Boundary | Empty customer/currency/date, zero or 1001 lines, duplicate line number, zero/negative or >6-place quantity/unit price, invalid discount/tax and max field length produce per-field 400 with zero rows. | U,I,E | REQ_F4112.2.2–3 |
| SD-ORDER-007 | Boundary | Month/year/leap-day effective MD rate, currency zero/two-decimal half-way rounding, per-line before-header sums and decimal(19,4) overflow produce exact snapshots or named 422. | U,I | REQ_F4112.2.2 |
| SD-ORDER-008 | Boundary | Delivery balance exactly zero or one smallest unit positive is correct; approved order with any active pending or posted delivery cannot cancel. | I,E | REQ_F4112.2.1, REQ_F4112.2.6 |
| SD-ORDER-009 | Duplicate/conflict | Same-company number cannot be reused after soft delete; concurrent manual number allocation yields distinct committed numbers. | I | REQ_F4112.2.2 |
| SD-ORDER-010 | Duplicate/conflict | Two quote conversions yield one winner and 409 loser; cancelled/soft-deleted winner never frees source; losing transaction leaves no number/order/line. | I,E | REQ_F4112.2.2 |
| SD-ORDER-011 | Duplicate/conflict | Stale edit/delete/submit/cancel or simultaneous SYS decisions yield 409/unchanged saved rows and one approval effect. | I,E | REQ_F4112.2.3–6 |
| SD-ORDER-012 | Partial result | Invalid middle line, SYS callback failure, FI refusal or transaction abort rolls back whole order/approval/number; UI reports failure and saved change count is zero. | I,E | REQ_F4112.2.2–5 |
| SD-ORDER-013 | Partial result | Partial success, all skipped and all failed are not applicable to one atomic order command; no batch import/action exists. Zero-change retry gets no success toast. | I,E | REQ_F4112.2.2–6 |
| SD-ORDER-014 | Permission | Anonymous 401, missing exact action 403, other-company or action-scope-excluded ID/source 404; CREATE/EDIT-only actor can read its form options without VIEW. | I,E | REQ_F4112.2.1–6 |
| SD-ORDER-015 | State | Draft edit/delete/submit; Rejected revise; Pending SYS decision only; Approved delivery/cancel if no delivery; Cancelled read-only. Invalid command gives named 422 and no mutation. | U,I,E | REQ_F4112.2.3–6 |
| SD-ORDER-016 | Related | Pending/unapproved, disabled, soft-deleted or other-company MD customer/item/currency/tax rejects create/edit/submit with field reason; old saved snapshots remain readable. | I,E | REQ_F4112.2.1–4 |
| SD-ORDER-017 | Credit | Missing FI/AR contract returns `SAL.CREDIT.UNAVAILABLE` and blocks submit/final approval; over-limit refusal gives amount/reason; authorised reasoned override audits once and never bypasses customer status. | U,I,E | REQ_F4112.2.4, REQ_F4112.6.1 |
| SD-ORDER-018 | Error | 400 fields, 409 version/source, 422 status/reference/credit/overflow, 500 traceId, timeout and network loss show translated specific reasons and no false success; persisted data matches UI. | I,E | REQ_F4112.2.1–6 |
| SD-ORDER-019 | File | Upload/import cases (empty, malformed, oversized, encoding, missing/extra columns) are not applicable: order has no file input. CSV export later tests empty, injection escaping, row cap and interrupted download under EXPORT. | I,E | REQ_F4112.2.1 |
| SD-ORDER-020 | UI | View/Create/Edit show normal, empty, filtered-empty, no-permission, validation, conflict, server error, success and disabled-action reasons in three languages and light/dark; message agrees with saved data. | E | REQ_F4112.2.1–6 |

WU-SD-003a cross-company HTTP checkpoint (2026-10-28T15:30:04+08:00): SD-ORDER-014 now has a real SYS login test using one VIEW-only user assigned to two companies. A saved order in the first company returns 200 by ID there, but 404 by ID and an empty list with totalCount 0 in the second company. The test uses a persisted SD row to exercise HTTP scoping; authorised manual POST/PUT/DELETE with real MD references and action-scope 404 remain open. Focused SalesOrder integration tests pass 23/23; Debug solution build has 0 warnings/errors. Atomic single-order partial results and file upload remain inapplicable per SD-ORDER-013/019.

WU-SD-003a edit/delete checkpoint (2026-10-28T14:59:53+08:00): SD-ORDER-002/005/011/014 now have a real-SQL regression for a current-version edit, Branch-hidden edit/delete, stale delete conflict and successful Draft soft deletion; anonymous PUT/DELETE and OpenAPI registration are checked at HTTP level. The existing SD-ORDER-001–020 matrix retains normal, boundary, duplicate/conflict, partial result, permission, state, related, error, file and UI categories. Atomic single-order commands make batch partial/all-skipped cases in 013 inapplicable; no file input makes 019 upload cases inapplicable. Live authorised HTTP, cross-company and simultaneous write outcomes remain unverified, and UI/E2E is a later work unit.

WU-SD-003a list checkpoint (2026-10-28T15:06:01+08:00): SD-ORDER-001/006/014 gain a real-SQL `OrderListUsesViewScopeAndStablePages` regression. It first failed compilation before list DTO/service existed, then checks two same-date pages, scoped total count, hidden Branch empty state and invalid sort. The list has no batch mutation or file input, so partial result and file scenarios are inapplicable. Date/status/customer filters, invalid pagination, authorised HTTP and UI error display remain for later verification.

WU-SD-003a list coverage checkpoint (2026-10-28T15:17:16+08:00): The same real-SQL regression now checks inclusive date bounds, Pending status, customer ID, trimmed customer-code and document-number keywords, and that another Branch's row stays outside filtered totals. It rejects page 0, page size 0/201, overflowing skip, reversed dates, invalid customer/status and multi-field sort. Existing query behavior passed 1/1; this is added boundary and permission evidence for SD-ORDER-006/014, not new product behavior. Authorised HTTP, two-connection edit/delete races, quote conversion and UI/E2E remain open. Batch partial and file scenarios remain inapplicable under SD-ORDER-013/019.

WU-SD-003a authorised HTTP permission checkpoint (2026-10-28T15:22:24+08:00): SD-ORDER-014 now uses a real SYS user, company assignment and VIEW-only role to check the order list returns 200 with an empty scoped count, while POST, PUT and DELETE each return 403 with `COMMON.PERMISSION.DENIED`. The test sends valid command shapes so permission is decided before MD reference validation or order lookup. The full saved-row authorised CRUD, other-company 404, Branch/Department/Self HTTP scope, simultaneous writes and quotation conversion remain open. Single-order commands have no partial batch result; there is no file input, as already documented in 013/019.

SD-ORDER-009 SQL acceptance: while manual create holds its transaction after allocating `SO001`, a second connection must not allocate the same company's next `SAL.ORDER` number. After the first commit, retry must allocate `SO002`; both saved orders and the SYS sequence must agree. A failed create must roll back its allocation (SD-ORDER-012). Other-company sequences are independent (SD-ORDER-014). This is a single-document command, so partial-success and file cases remain inapplicable per 013/019.

WU-SD-003a MD item identity checkpoint (2026-10-27T16:10:46+08:00): SD-ORDER-016 has a real-SQL item lookup regression for active same-company identity, foreign ID, inactive item, missing ID and soft-deleted item. It checks the public MD Contract only. Normal sales order creation, date-effective currency/tax lookup, amount persistence, concurrent MD edits, batch partial results, permission/HTTP, state and translated error paths remain in SD-ORDER-001–020. Partial batch and file scenarios remain inapplicable for the reasons in 013/019.

WU-SD-003a partial result (2026-10-24T07:32:40+08:00): `SalesOrderDraftTests` first failed to compile because the order draft types did not exist, then passed 14/14. The pure `U` slice covers a leap-day date, 1000-line upper bound, per-field failures, duplicate/1001 lines, per-line rounding and header overflow in SD-ORDER-001, 006 and 007. It does not prove saved snapshots, reference validity, transactions, company/permission checks or HTTP behavior. The remaining `I/E` cases above retain their planned status. SD-ORDER-013 and 019 remain not applicable to a single atomic, file-free command as documented above.

WU-SD-003a source eligibility slice (2026-10-24T07:36:22+08:00): `SalesOrderQuotationSourceTests` first failed compilation because the source validator did not exist, then passed 9/9. The `U` portion of SD-ORDER-003, 010, 014 and 015 now checks Approved status, inclusive expiry date, prior conversion, same-company identity and invalid company. It delegates quotation state/expiry behavior to the existing quotation lifecycle. Real SQL locking/unique constraints, persisted snapshots, action scope, transaction rollback and authorised HTTP remain unverified `I` gates; no order conversion endpoint exists yet. File and batch cases remain inapplicable for the documented reasons.

WU-SD-003a lifecycle slice (2026-10-24T07:39:34+08:00): `SalesOrderLifecycleTests` first failed compilation because the lifecycle type did not exist, then passed 17/17. The `U` portion of SD-ORDER-002, 005, 008 and 015 covers Draft edit/delete/submit, Rejected revision to Draft, Pending SYS decision, Approved cancellation without active/posted delivery, immutable other states and named status/delivery errors. This pure rule does not perform credit checks, version checks, permission checks, deletion, SYS callbacks or SQL writes. Real SQL and HTTP cases remain pending; no frontend or file operation was introduced.

WU-SD-003a schema checkpoint (2026-10-27T10:50:16+08:00): `SalesOrderSchemaTests` first failed against the absent tables, then passed 2/2 after migration. SD-ORDER-001 and 010 now have SQL evidence for durable Draft header/line inserts, permanent quotation-source uniqueness, quantity CHECK and whole-document rollback on a bad line. Metadata asserts same-company parent FK and source indexes. Remaining create/edit/convert service, MD/SYS checks, two-connection competition, HTTP/E2E, down migration and full amount snapshots are still open. Partial/all-skipped batch and file cases remain inapplicable for the reasons in SD-ORDER-013/019.

WU-SD-003a source immutability checkpoint (2026-10-27T11:09:39+08:00): SD-ORDER-003/010 now have a real-SQL regression for saved order and line source IDs. The test first failed because an UPDATE could clear the source; the new migration rejects clearing or reassigning either ID, including after order soft deletion, while ordinary metadata updates remain allowed. The dedicated SQL tests pass 3/3 after migration; architecture passes 227/227 and the SALES seed role regression passes 1/1. The full-suite rerun on 2026-10-27T11:17:09+08:00 hit the previously recorded INV SafetyStock SQL timeout and OtherReceipt 422-versus-500 failure under load and was stopped. The separate create/edit/convert service, MD/SYS checks, two-connection race, authorised HTTP and full-suite pass remain open. Batch/file cases stay inapplicable per SD-ORDER-013/019.

WU-SD-003a source-pair checkpoint (2026-10-27T11:24:14+08:00): SD-ORDER-003/010/012 now include a real-SQL test for both mismatches: manual header with a quoted line and quoted header with a manual line. The test failed against the prior migration, then passed 4/4 after a reversible insert/update trigger and EF trigger-compatible save configuration; rejected inserts leave zero header and line rows. This protects source shape only; the later conversion service must still prove the quotation is Approved, same-company, unexpired and copied in full. The existing SD-ORDER-013/019 inapplicability reasons remain unchanged.

WU-SD-003a line-state checkpoint (2026-10-27T11:47:54+08:00): SD-ORDER-005/015 now include a real-SQL test that first demonstrated an Approved order line could be updated. A reversible trigger rejects INSERT, UPDATE and DELETE against Approved or soft-deleted order lines; focused schema suite passed 5/5 after migration. Normal Draft writes and source checks remain covered by the earlier schema tests. The full backend suite had load-sensitive INV valuation SQL failures, and the smaller failed case passed when rerun alone; no full-suite pass is claimed. This does not complete authorised CRUD, quotation conversion, MD/SYS snapshots, two-connection competition or HTTP; the batch/file inapplicability in SD-ORDER-013/019 is unchanged.

WU-SD-003a header-state checkpoint (2026-10-27T15:23:01+08:00): SD-ORDER-005/015 now include a real-SQL test for an Approved order reverting to Draft and a soft-deleted Draft being revived. It first failed on the Approved rollback, then passed 6/6 after a reversible header transition trigger; rejected statements leave the saved state intact. Draft/Pending/Rejected/Approved/Cancelled transitions follow BR-SD-016, including zero-step Draft approval and Approved cancellation. This is a persistence safeguard, not evidence of authorised SYS decisions or HTTP commands. Normal, boundary, duplicate, partial, permission, related, error and file categories remain covered by SD-ORDER-001–020; the single-document partial and file inapplicability reasons in 013/019 remain unchanged.

WU-SD-003a commercial-header checkpoint (2026-10-27T15:37:16+08:00): SD-ORDER-015 now tests that SQL cannot change a saved Approved order's customer name, monetary total or remark, while a Draft remark remains editable. The real-SQL test first failed because the Approved customer name changed, then passed with the reversible commercial-header trigger; schema suite 7/7. The trigger covers Pending, Approved, Cancelled and soft-deleted headers, with null-safe comparison of all commercial header fields. It allows audit/version changes and legal status transitions. This does not prove the still-missing authorised CRUD, MD/SYS snapshot resolution, quote conversion, two-connection competition or HTTP. Normal, boundary, duplicate, partial, permission, related, error and file categories remain SD-ORDER-001–020; single-command partial and file cases remain inapplicable per 013/019.

WU-SD-003a physical-delete checkpoint (2026-10-27T15:48:59+08:00): SD-ORDER-005/009/010 now include a real-SQL test for direct physical deletion of active Draft, soft-deleted Draft and Approved orders. The test first failed because DELETE succeeded; a reversible `AFTER DELETE` trigger now rejects it, and the test checks the saved number, quotation source, deletion state and child line remain intact. Focused schema suite passed 8/8 after migration; Debug build passed with zero warnings/errors and Shared/Architecture/DocGen passed in the wider run. Full integration had no completion summary after several minutes with `--max-threads 2` and was stopped, so no full-suite pass is claimed. This protects persistence identity only; authorised soft-delete CRUD, quotation conversion, MD/SYS snapshots, two-connection competition and HTTP remain open. Existing normal/boundary/conflict/partial/permission/state/related/error/file scenarios remain SD-ORDER-001–020, with 013/019 inapplicable as explained above.

## WU-SD-004d sales outbound scenarios (planned, no product test claimed)

### WU-SD-004a2b saved-line SQL selection checkpoint (2026-10-27T10:39:15+08:00)

`SalesDeliverySelectionStoreTests` first failed to compile because the SQL selection store did not exist. The new store requires a serializable transaction, reads the saved header in the current company, independently counts and selects every current line, then calls `SalesDeliverySelection.CreateLockPlan` to return unique source order line IDs in ascending order. Integration cases cover a two-line saved Draft, a missing document, and a document owned by another company. This verifies only the pre-lock selected-row part of SD-DEL-007/009/013. The caller still needs an order table, actual source locks, a complete reread, `CheckLockedDecision`, HTTP, SYS/INV effects, and two-connection race tests. Partial result and file cases remain inapplicable for the reasons in SD-DEL-012/018.

### WU-SD-004a2b schema checkpoint (2026-10-27T10:16:42+08:00)

The previously written persistence model test is green (Architecture 227/227). Migration `Sales_DeliveryDraft` built, applied to `myerp`, and SQL inspection found both tables, one same-company line-to-header FK, seven CHECK constraints, permanent unique indexes and quantity precision `(19,6)`. This establishes only the schema portion of SD-DEL-001/006/008. The following cases remain untested: draft CRUD and rollback (001/002/006/011), real saved-row balance and two-connection race (007/009/010), permissions and HTTP (013/017), SYS/INV atomic posting (003–005/011/020), UI and E2E (019). The full SD-DEL-001–020 acceptance list below remains open.

`U` = pure unit, `I` = real SQL and authorised HTTP, `E` = Playwright. WU-SD-004a/b/c first add failing assertions, then implement. Every E case checks the visible message/status/count and saved API/list data. These refine SD-BASE-003, 007–010, 013–016, 018–020 and INV-XMOD-002–006; IDs remain stable.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-DEL-001 | Normal | Approved order creates one-line Draft with permanent number, saved item/order link and tracking; no INV move or reservation. | I,E | REQ_F4112.3.2 |
| SD-DEL-002 | Normal | Draft edit replaces all lines; Rejected revision returns Draft and retains every SYS round. | I,E | REQ_F4112.3.3, REQ_F4112.3.5 |
| SD-DEL-003 | Normal | Submit reserves order balance while Pending; final SYS approval posts all saved Out lines exactly once, status Posted and movement IDs match INV. | I,E | REQ_F4112.3.4 |
| SD-DEL-004 | Normal | SYS rejection releases Pending reservation once, keeps saved lines/history and writes zero INV movements; subsequent revision may resubmit. | I,E | REQ_F4112.3.5 |
| SD-DEL-005 | Normal | Zero-step SYS submit uses the same transactional posting path; Draft delete is soft and versioned. | I,E | REQ_F4112.3.4, REQ_F4112.3.6 |
| SD-DEL-006 | Boundary | Missing order/date/warehouse, zero or 1001 lines, duplicate/nonpositive line number, zero/negative/>6-place quantity, invalid lot/serial length give field-level 400 and no saved rows. | U,I,E | REQ_F4112.3.2–3 |
| SD-DEL-007 | Boundary | Exact remaining quantity, one six-decimal unit over balance, multiple lines to one order line, and multiple Pending deliveries use aggregate decimal quantities without truncation. | U,I,E | REQ_F4112.3.2, REQ_F4112.3.4 |
| SD-DEL-008 | Boundary | Month/year/leap-day business dates persist exactly; a line at decimal(19,6) maximum is accepted or rejected without overflow/truncation according to DB bound. | U,I | REQ_F4112.3.2 |
| SD-DEL-009 | Duplicate/conflict | Two concurrent submits against the last order balance yield one winner and one 409/422 loser; no oversold reservation or orphan SYS instance. | I | REQ_F4112.3.4 |
| SD-DEL-010 | Duplicate/conflict | Stale edit/delete/submit, concurrent final decisions, repeated decision and changed INV replay yield one effect or named 409; same saved snapshot does not create duplicate movements. | I,E | REQ_F4112.3.3–6 |
| SD-DEL-011 | Partial result | Invalid middle line, stock/tracking failure after earlier line, callback failure or outer rollback leaves zero SD/SYS/INV partial effects; failed final decision retains Pending, failed zero-step retains Draft. | I,E | REQ_F4112.3.2–5 |
| SD-DEL-012 | Partial result | Partial success/all skipped/all failed are inapplicable to one atomic delivery command; no batch operation exists. Zero-change retry shows no success toast and saved counts remain unchanged. | I,E | REQ_F4112.3.2–6 |
| SD-DEL-013 | Permission | Anonymous 401, missing exact action 403, other-company or action-scope-excluded delivery/order 404; CREATE/EDIT-only actor can get form options without VIEW. | I,E | REQ_F4112.3.1–6 |
| SD-DEL-014 | State | Only Approved order can source Draft/submit; Draft edit/delete/submit, Rejected revise, Pending SYS decision only, Posted read-only; invalid command gives named 422 and no mutation. | U,I,E | REQ_F4112.3.2–6 |
| SD-DEL-015 | Related | Deleted/disabled/other-company item, warehouse/location, missing lot, serial in another location or untracked item with tracking fails with precise line/reason; saved historical snapshots remain readable. | I,E | REQ_F4112.3.1–4 |
| SD-DEL-016 | Related | Tracking quantities sum to line quantity, each serial is one unit, concurrent stock issue cannot go negative; SD passes no sales price as INV cost. | I | REQ_F4112.3.4 |
| SD-DEL-017 | Error | 400 field, 409 version/balance/replay, 422 state/reference/stock/tracking, 500 traceId, timeout and network failure show translated reason and no false success; saved data matches UI. | I,E | REQ_F4112.3.1–6 |
| SD-DEL-018 | File | Empty/malformed/oversized/wrong-encoding/missing/extra-column file cases are inapplicable: no upload/import exists. Print/export is unavailable until separate permission, output and tests exist. | E | REQ_F4112.3.1 |
| SD-DEL-019 | UI | View/Create/Edit show normal, empty, filtered-empty, no-permission, validation, conflict, server error, success, Pending reservation and disabled-action reasons in three languages and light/dark. | E | REQ_F4112.3.1–6 |
| SD-DEL-020 | Traceability | Posted cannot be deleted/reversed; original order, SYS rounds, saved lot/serial and INV movement IDs remain linked, including after MD reference deactivation. | I,E | REQ_F4112.3.1, REQ_F4112.3.6 |

### WU-SD-004a2b1 pure lifecycle guard (2026-10-27T01:06:23+08:00)

The complete SD-DEL-001–020 plan above remains the feature acceptance gate. This slice checks only the saved-status decision in BR-SD-021 and the Approved-order precondition at submit/final approval. Callers must load the current state and order under transaction locks; this pure result does not perform persistence, authority or INV posting.

| Case | Category | Expected result | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-LIFE-001 | Normal | Draft edit stays Draft, Rejected revision returns Draft, Draft submit becomes Pending or Posted after zero-step approval, Pending final approval becomes Posted and rejection becomes Rejected. | U | REQ_F4112.3.3–5 | PASS |
| SD-DEL-LIFE-002 | Boundary/state | Draft delete is allowed; Rejected cannot submit until revised; Pending cannot edit/delete/submit; Posted cannot change; repeated final decision is refused. | U | REQ_F4112.3.3–6 | PASS |
| SD-DEL-LIFE-003 | Related/conflict | Submit and final approval refuse a source order that is no longer Approved; rejection remains possible to release its reservation; a local Draft edit/delete is not mistaken for approval. | U | REQ_F4112.3.4–5 | PASS |

Duplicate document codes, soft-deleted references, stale versions and concurrent actions require 004a2b SQL tests. Partial success/all skipped do not apply to one atomic command; transaction rollback remains SD-DEL-011. Permission, company/action scope, HTTP 400/409/422/500, timeout and translated UI feedback remain SD-DEL-013/017/019. File inputs are inapplicable because delivery has no import. E2E must check visible status and persisted SD/SYS/INV data in 004b/c.

### WU-SD-004a2a Draft input guard (2026-10-27T00:55:46+08:00)

`SalesDeliveryDraftTests` first failed to compile because the input types did not exist, then passed 13/13. This covers the pure U portion of SD-DEL-006 and the leap-day/max-line input portion of SD-DEL-008. The full SD-DEL-001–020 list above remains the acceptance plan for 004a2b/b/c. No saved document, SQL migration, HTTP response, permission, tracking or UI was tested here.

| Category | Case and result | Level | REQ_F |
|---|---|---|---|
| Normal | One Draft line, leap-day business date, 1000 lines and repeated order-line IDs pass structural validation; saved order eligibility remains pending. | U pass | REQ_F4112.3.2 |
| Boundary | Missing order/date, 0/1001 lines, null line, nonpositive or duplicate line number, missing source/warehouse, invalid location, nonpositive, oversized or over-six-place quantity produce field errors. | U pass | REQ_F4112.3.2–3 |
| Duplicate/conflict | Duplicate line number fails; repeated order-line ID is valid and will be aggregated under SQL lock. Stale version and concurrent submit require 004a2b. | U pass / I pending | REQ_F4112.3.2–4 |
| Partial result | No mutation occurs in this pure guard. Whole-document rollback and no misleading UI result require 004a2b/c. | U not applicable / I,E pending | REQ_F4112.3.2–4 |
| Permission/state/related | The guard has no user, saved state or reference access. Company scope, Approved order and MD/INV validity require 004a2b. | U not applicable / I pending | REQ_F4112.3.2–4 |
| Error/file | Field keys are produced, but HTTP localisation and 500/timeout require 004a2b/c. File cases remain inapplicable because there is no import. | U pass / I,E pending | REQ_F4112.3.2–4 |

Traceability: BR-SD-021 → 002, 004–005, 010, 013–014, 019–020; BR-SD-022 → 001, 003, 007–010, 014; BR-SD-023 → 001, 006, 015–016, 020; BR-SD-024 → 003–005, 009–011, 016–017; BR-SD-025 → 011–012, 017–020. All cases are planned and unexecuted; WU-SD-004d adds no product test, schema or endpoint.

### WU-SD-004a1 pure balance guard (2026-10-27T00:43:09+08:00)

The complete delivery scenario list remains SD-DEL-001–020 above. This slice runs only the pure part of SD-DEL-007; SQL, permission, state, related-reference, error-response, UI and file cases remain assigned to 004a2/b/c as indicated. The guard itself has no user, state, file or batch context, so those categories cannot be asserted at this layer.

| Case | Category | Expected result | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-BAL-001 | Normal | Exact remainder after Posted and other Pending, including repeated requested lines, passes; current Pending is included only in requested quantity. | U | REQ_F4112.3.2, REQ_F4112.3.4 | PASS |
| SD-DEL-BAL-002 | Boundary | One six-place micro-unit above remainder or a separate order line above its own remainder fails without rounding; 1000 lines pass and 1001 fail. | U | REQ_F4112.3.2 | PASS |
| SD-DEL-BAL-003 | Invalid/conflict | Empty, missing or duplicate balance snapshot, nonpositive or over-precision quantity, negative Posted and out-of-range decimal are rejected before a decision. | U | REQ_F4112.3.2 | PASS |

Duplicate document codes and soft-deleted records: not inputs to this pure guard; 004a2 owns saved-source eligibility. Partial success/all skipped: inapplicable to one atomic delivery command; 004a2 owns rollback. Permission, company, state, 400/409/422/500/timeout and file cases: this pure guard has no identity, HTTP or file access; SD-DEL-009–020 remain planned for the transaction/API/UI slices. E2E must assert both visible result and saved SD/SYS/INV data when those slices exist.

### WU-SD-004a2b2 saved-row snapshot projection

| ID | Category | Scenario | Layer | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-SNAP-001 | Normal/state | Aggregate split Posted lines and other active Pending reservations; exclude the current Pending delivery once; Draft and Rejected do not reserve. | U | REQ_F4112.3.2, REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-002 | Boundary | Exact six-place remainder is accepted, including a historical Posted row with a delete flag; one micro-unit excess remains covered by SD-DEL-BAL-002. | U | REQ_F4112.3.2, REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-003 | Duplicate/conflict/related | Missing or duplicate source order line, foreign line ID, malformed saved quantity and a current delivery already Posted fail before posting; persisted total above ordered fails with named balance error. | U | REQ_F4112.3.2, REQ_F4112.3.4 | PASS |

Partial/all-skipped results do not apply to one atomic delivery. This projector has no user, company context, HTTP, file or UI; permission, soft-deleted reference eligibility, version races, SQL locks, rollback, HTTP errors and translated feedback remain SD-DEL-009–020 for 004a2b/b/c. The SQL caller must select the entire saved same-company row set while holding ordered line locks; this pure test cannot prove that selection.

### WU-SD-004a2b3 current Pending snapshot integrity

| ID | Category | Scenario | Layer | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-SNAP-004 | Normal/state | A saved active current Pending row is accepted and omitted from `OtherPending` exactly once. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-005 | Boundary/conflict | Current ID with no selected row, including a nonempty set of only other deliveries, fails before posting. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-006 | State/deletion | Deleted, Draft, Rejected, or mixed Pending and inactive rows for the current ID fail. | U | REQ_F4112.3.4 | PASS |

Duplicate document codes, concurrent changes, partial rollback, company/scope permissions, HTTP errors and E2E remain SD-DEL-009–020 under 004a2b/b/c. This pure function has no user, HTTP, UI, batch or file boundary; those categories are inapplicable at this layer. SQL tests must still establish complete saved-row selection and serialisation with two connections.

### WU-SD-004a2b4 saved current quantity at final decision

| ID | Category | Scenario | Layer | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-SNAP-007 | Normal/boundary | Derive all split current Pending lines from the saved row set; exact remaining balance across two order lines succeeds without a separate request. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-008 | Boundary/conflict | A saved current quantity one micro-unit above the remaining balance fails with `ORDER_BALANCE_EXCEEDED`, even if a caller could otherwise supply a smaller request. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-009 | State/boundary | Missing current saved rows and 1001 current saved lines fail; deleted or non-Pending rows remain covered by SD-DEL-SNAP-006. | U | REQ_F4112.3.4 | PASS |

Duplicate codes, soft-deleted references, concurrent SQL decisions, permissions, cross-company scope, rollback, HTTP 400/409/422/500/timeout and translated E2E outcomes remain SD-DEL-009–020 for 004a2b/b/c. This pure function has no identity, reference lookup, HTTP, UI or file boundary. Partial success, skipped rows and file cases are inapplicable to one atomic delivery command. SQL tests must prove that the selected saved-row set is complete while holding ordered locks; E2E must compare displayed results with saved SD/SYS/INV data.

### WU-SD-004a2b5 saved Draft quantity at submit (2026-10-27T05:39:07+08:00)

The complete SD-DEL-001–020 list remains the product acceptance gate. This pure helper covers only the saved-row part of REQ_F4112.3.4; SQL must prove complete company-scoped selection under ascending order-line locks.

| ID | Category | Scenario | Layer | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-SNAP-010 | Normal | Every split current Draft line is checked against Posted and other active Pending reservations, including exact remainder on another order line. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-011 | Boundary/conflict | One six-place unit beyond remaining quantity fails even if an external request could provide a smaller amount; missing and 1001 saved lines fail. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SNAP-012 | State/deletion | Deleted, Pending, Rejected or mixed current rows fail before submit; unrelated Draft rows do not reserve. | U | REQ_F4112.3.4 | PASS |

Duplicate codes and soft-deleted source eligibility require saved SQL references; two-client conflicts and whole-transaction rollback require I tests in 004a2b. Permission, cross-company scope, 400/409/422/500, timeout and visible E2E feedback remain SD-DEL-009–020 for 004a2b/b/c. Batch partial success/all skipped and file cases are inapplicable to a single atomic delivery without upload. This helper has no identity, HTTP or UI boundary.

## WU-SD-005d pricing scenarios (planned, no product test claimed)

`U` = pure unit, `I` = real SQL and authorised HTTP, `E` = Playwright. WU-SD-005a/b must write failing tests before implementation. Every E case asserts both visible message/style/count and persisted API/list data. All cases trace to REQ_F4112.4.1 Version 1.1 and refine SD-BASE-004/006 plus SD-QUOTE-009 and SD-ORDER-003.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-PRICE-001 | Normal | Authorised create/edit/deactivate saves one company-bound rule, version and old/new audit; detail/list show persisted state. | I,E | REQ_F4112.4.1 |
| SD-PRICE-002 | Normal | Customer-specific active rule wins over default item/currency rule on the business date; date/currency/customer change selects the matching rule only. | U,I,E | REQ_F4112.4.1 |
| SD-PRICE-003 | Normal | Quote is read-only; quotation/manual-order save resolves rule again, stores ID/version/price/discount, dated tax/rate and rounded original/base line/header amounts. | I,E | REQ_F4112.4.1 |
| SD-PRICE-004 | Normal | Granted manual price or above-ceiling discount with reason records actor/time/old/chosen value once; quote-to-order copies source commercial snapshot without a new pricing decision. | I,E | REQ_F4112.4.1 |
| SD-PRICE-005 | Boundary | `valid_from` and `valid_to` are inclusive across month/year/leap day; outside date or missing rule yields specific `RULE_NOT_FOUND`, with no guessed fallback. | U,I,E | REQ_F4112.4.1 |
| SD-PRICE-006 | Boundary | Blank/missing item, customer or currency, reversed/empty dates, negative or >6-place price, discount below 0/above 100/>4 places and maximum field lengths fail by field with zero rows. | U,I,E | REQ_F4112.4.1 |
| SD-PRICE-007 | Boundary | Exact discount ceiling is ordinary; one 0.0001% above requires override. `Money.Round` at original/base currency precision and decimal(19,4) line/header overflow are explicit, never truncated. | U,I,E | REQ_F4112.4.1 |
| SD-PRICE-008 | Duplicate/conflict | Equal-scope active intervals sharing a boundary or partially enclosing each other cannot both save; default and customer-specific intervals may coexist. | U,I,E | REQ_F4112.4.1 |
| SD-PRICE-009 | Duplicate/conflict | Two concurrent writers for the same nullable-default/customer key yield one committed active interval and one named conflict; no unguarded phantom interval. | I | REQ_F4112.4.1 |
| SD-PRICE-010 | Duplicate/conflict | Stale rule edit/deactivate or stale displayed quote/version returns 409 and leaves rule/document unchanged; retry must request current quote. | I,E | REQ_F4112.4.1 |
| SD-PRICE-011 | Partial result | Rule/document multi-line save with an invalid middle line, failed audit or rate lookup rolls back all rows/number/approval effects; zero-change retry has no success toast. | I,E | REQ_F4112.4.1 |
| SD-PRICE-012 | Partial result | Partial success/all skipped/all failed are inapplicable to one atomic command; no bulk endpoint exists. Verify zero changed records on failed command. | I,E | REQ_F4112.4.1 |
| SD-PRICE-013 | Permission | Anonymous 401, missing exact rule action or pricing override 403, cross-company/out-of-action-scope identity 404; form actor can quote without PRICING.VIEW. | I,E | REQ_F4112.4.1 |
| SD-PRICE-014 | State | Inactive/soft-deleted/expired rule is not selected, remains visible in authorised history; an Approved order/quote never changes when rule changes. | I,E | REQ_F4112.4.1 |
| SD-PRICE-015 | Related | Deleted/disabled/unapproved MD customer, deleted item or inactive currency rejects current quote/save; saved historical names and prices remain readable. | I,E | REQ_F4112.4.1 |
| SD-PRICE-016 | Error | 400 field/reason, 403 override, 404 hidden ID, 409 overlap/stale, 422 missing rule/overflow, 500 traceId and timeout/network all show translated reason and no false success. | I,E | REQ_F4112.4.1 |
| SD-PRICE-017 | File | Empty/malformed/oversized/wrong-encoding/missing/extra-column files are inapplicable: this unit has no upload/import/export. | E | REQ_F4112.4.1 |
| SD-PRICE-018 | UI | Rule View/Create/Edit and document price controls show normal, empty, filtered-empty, no-permission, validation, success and server error in three languages and light/dark; disabled action explains why. | E | REQ_F4112.4.1 |
| SD-PRICE-019 | Audit | Rule revision/deactivation and override persist actor/time/reason and old/new values once; forged client audit fields are ignored; no cross-company audit leak. | I,E | REQ_F4112.4.1 |
| SD-PRICE-020 | Snapshot | Rule/tax/rate change, soft deletion and date rollover leave saved Approved and converted line/header snapshots identical; only a new Draft revision may recalculate. | U,I,E | REQ_F4112.4.1 |

Traceability: BR-SD-026 → 001–002, 005–010, 014–015, 019; BR-SD-027 → 002–003, 005, 010, 013, 016; BR-SD-028 → 004, 006–007, 013, 016, 019; BR-SD-029 → 003–004, 007, 011, 014–015, 020; BR-SD-030 → 008–013, 016–018. All scenarios are planned and unexecuted because 005d changes documents only; migration, production tests, API and UI belong to 005a/b.

## WU-SD-006d credit contract scenarios (planned, not executed)

All cases trace to REQ_F4112.6.1 and REQ_F4112.2.4 unless stated otherwise. `U` = unit, `I` = real SQL/authorised HTTP integration, `E` = Playwright with both visible message and persisted order/credit/audit state assertions. These are test-first targets for 006a/b/c; 006d creates no production test or implementation.

| ID | Class | Scenario and expected result | Level |
|---|---|---|---|
| SD-CREDIT-001 | Normal | Same-company valid limit and AR plus saved SD commitment leave enough headroom; Draft submit becomes Pending and SYS final approval becomes Approved after two fresh checks. | U,I,E |
| SD-CREDIT-002 | Normal | Zero-step SYS workflow uses the same credit lock/check and saves Approved plus one decision; no bypass or duplicate audit. | I,E |
| SD-CREDIT-003 | Normal | Read-only check returns dated FI limit/AR, SD commitment, candidate, headroom and recheck marker; response causes zero rows changed and no success toast. | I,E |
| SD-CREDIT-004 | Boundary | Exact zero headroom succeeds; one smallest base-currency unit beyond limit refuses with exact excess. Zero limit is valid but refuses positive exposure. | U,I,E |
| SD-CREDIT-005 | Boundary | Month/year/leap-day effective-limit boundaries use server `IClock.Today`; a backdated order cannot select an older limit or AR balance; no applicable limit fails closed. | U,I |
| SD-CREDIT-006 | Boundary | Base-currency conversion uses saved rounded gross; maximum decimal(19,4), negative FI values, arithmetic overflow and currency mismatch produce named invalid-snapshot/amount errors. | U,I |
| SD-CREDIT-007 | Duplicate/conflict | Candidate counted once on Draft submit and Pending final decision, excluded by order ID from commitment query; Pending does not reserve credit and retry cannot consume headroom twice. | U,I |
| SD-CREDIT-008 | Duplicate/conflict | Two concurrent orders for the same company/customer cannot both approve against one remaining headroom; credit-key lock serializes, loser gets current refusal. | I |
| SD-CREDIT-009 | Duplicate/conflict | Stale order version, FI snapshot/version or SYS round gives 409; retry reloads and rechecks, preserving prior order/override state. | I,E |
| SD-CREDIT-010 | Duplicate/conflict | Same order/version/round override replay produces one audit row and never approves twice; soft deletion does not release its uniqueness. | I |
| SD-CREDIT-011 | Partial result | FI failure, audit insert failure, SYS callback failure or invalid middle order line rolls back the whole decision, number/lines/history/override and no partial success. | I,E |
| SD-CREDIT-012 | Partial result | Partial success, all skipped, all failed and zero-row batch outcomes are inapplicable: there is no batch credit endpoint; each command is atomic and failed commands change zero rows. | I,E |
| SD-CREDIT-013 | Permission | Anonymous is 401; missing CREDIT.VIEW or exact ORDER/SYS action is 403; missing OVERRIDE is 403 even with SUBMIT; CREATE/EDIT holder can view only own order check. | I,E |
| SD-CREDIT-014 | Permission | Cross-company and SYS action-scope-excluded customer/order IDs return 404 without leaking limit, AR or FI document details. | I,E |
| SD-CREDIT-015 | State | Draft can submit; Pending only accepts SYS decision; Rejected needs revision; Approved/Cancelled or delivered orders cannot be resubmitted or overridden. | U,I,E |
| SD-CREDIT-016 | State | Inactive/unapproved MD customer cannot be checked into approval, including with OVERRIDE; expired/disabled credit limit and unavailable FI service never become unlimited credit. | I,E |
| SD-CREDIT-017 | Related | Approved order commitment decreases only when matching invoice posts to FI AR in the same transaction; an unposted invoice keeps commitment and cancellation removes eligible commitment once. | I |
| SD-CREDIT-018 | Related | FI settlement changes AR only after posting; read-only preview changes neither component, and a return/credit note affects AR only after FI posting. | I |
| SD-CREDIT-019 | Override | Known numerical excess with exact grant and nonblank reason records actor, IClock time, components, excess, order version and SYS round once; later exposure change needs a new decision. | U,I,E |
| SD-CREDIT-020 | Override | Forged actor/time/limit/exposure and blank/oversize reason are rejected or ignored; unavailable/invalid FI snapshot, inactive customer or currency mismatch cannot be overridden. | U,I,E |
| SD-CREDIT-021 | Error | 400 field/date/reason, 403 permission, 404 hidden ID, 409 stale state, 422 over-limit/unavailable/invalid snapshot, 500 with traceId, timeout and network loss show translated specific reason and no false success. | I,E |
| SD-CREDIT-022 | File | Empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable because this unit has no upload/import/export. | E |
| SD-CREDIT-023 | UI | Credit panel normal, empty, filtered-empty, no-permission, validation, refusal, override success and server error display in three languages and light/dark; disabled action has a reason. | E |
| SD-CREDIT-024 | Audit | Check/override UI message matches saved order decision and single audit row; failed/zero-change check has no row/toast; later FI data change never rewrites historic decision evidence. | I,E |

Traceability: BR-SD-031 → 001, 004–006, 016, 020–021; BR-SD-032 → 001, 004, 007, 017–018; BR-SD-033 → 001–003, 008–009, 011; BR-SD-034 → 009–010, 013, 019–020, 024; BR-SD-035 → 003, 013–014, 021, 023; BR-SD-036 → 010–012, 017, 022, 024. All 24 cases are pending; 006d is a design-only work unit.

## WU-SD-007d invoice scenarios (planned, not executed)

All cases trace to REQ_F4112.5.1; 013–016 also trace to REQ_F4112.6.1. `U` = unit, `I` = real SQL and authorised HTTP, `E` = Playwright asserting both visible message and persisted SD invoice/FI posting. 007d adds no production test; 007a/b must write failing tests before implementation.

| ID | Class | Scenario and expected result | Level |
|---|---|---|---|
| SD-INVOICE-001 | Normal | ISSUE one full Posted delivery line: one permanent Posted invoice, matching saved source and one FI receivable posting. | U,I,E |
| SD-INVOICE-002 | Normal | ISSUE partial quantities over two Posted deliveries of the same order/customer/currency; later invoice consumes remaining balance and source line totals reconcile. | U,I,E |
| SD-INVOICE-003 | Normal | VIEW paginates company/scope invoices and detail shows saved tax, FI posting ID and immutable source links. | I,E |
| SD-INVOICE-004 | Boundary | Empty request, zero/negative quantity, >6 decimals, >1000 lines, blank/oversize request key and duplicate line number fail by field with zero rows. | U,I,E |
| SD-INVOICE-005 | Boundary | Exact available quantity succeeds; one 0.000001 unit above or zero balance fails; delivery/order quantities at decimal(19,6) bounds do not truncate. | U,I |
| SD-INVOICE-006 | Boundary | Month/year/leap-day invoice date and saved output tax/rate remain stable; current MD tax/rate changes cannot alter source snapshots. | U,I |
| SD-INVOICE-007 | Boundary | Repeated partial allocation and final remainder exactly equal each saved order-line original/base net, tax and gross; no one-unit rounding drift or overflow. | U,I |
| SD-INVOICE-008 | Duplicate/conflict | Same request key and identical canonical payload returns existing invoice/replay warning and unchanged FI posting count; changed payload with same key is 409. | I,E |
| SD-INVOICE-009 | Duplicate/conflict | Two concurrent issuers against one remaining delivery quantity serialize by source ID; only one invoice/FI posting commits. | I |
| SD-INVOICE-010 | Duplicate/conflict | Repeated delivery line in one request cannot bypass aggregate balance; duplicate invoice number or FI source, including deleted history, cannot commit. | I |
| SD-INVOICE-011 | Partial result | Invalid middle line, FI error, mismatched FI result or audit failure rolls back every invoice line, number, posting and credit transfer. | I,E |
| SD-INVOICE-012 | Partial result | Partial success, all skipped, all failed batch results do not apply to one atomic ISSUE; failed/replayed requests report zero new rows, never a new-success toast. | I,E |
| SD-INVOICE-013 | Permission | Anonymous 401, missing exact ISSUE/VIEW 403; CREATE/ISSUE form options require ISSUE without unrelated VIEW permission. | I,E |
| SD-INVOICE-014 | Permission | Cross-company and out-of-SYS-action-scope invoice/delivery/order IDs give 404 without quantity, customer or FI detail leak. | I,E |
| SD-INVOICE-015 | State | Draft/Pending/Rejected/deleted delivery cannot invoice; Posted delivery is eligible; Posted invoice cannot edit/delete/reissue with different key. | U,I,E |
| SD-INVOICE-016 | Related | Mixed customer/order/currency/item source lines fail whole request; disabled/deleted current MD price/tax cannot rewrite eligible Posted historical snapshot. | U,I,E |
| SD-INVOICE-017 | Related | FI posting and SD Approved unbilled commitment release happen under one customer credit lock; AR increase equals release and total exposure has no gap. | I |
| SD-INVOICE-018 | Related | Missing FI/AR service or invalid FI source/currency/amount refuses ISSUE; SD does not query or write FI tables and retains original delivery. | I |
| SD-INVOICE-019 | Error | 400 field, 403 action, 404 hidden ID, 409 stale/replay conflict, 422 source/FI/tax, 500 traceId and timeout/network show translated specific cause and no false success. | I,E |
| SD-INVOICE-020 | Error | Unknown timeout then lookup by request key finds committed invoice or safely retries absent key; never creates two FI postings. | I,E |
| SD-INVOICE-021 | File | Empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable: this first invoice flow has no upload/import/export. | E |
| SD-INVOICE-022 | UI | View/Create normal, empty, filtered-empty, no-permission, validation, success, replay warning and FI failure are checked in three languages and light/dark; disabled action explains why. | E |
| SD-INVOICE-023 | Audit | Saved actor/IClock time, source payload fingerprint and FI posting reference are written once; forged actor/time/amount/state is ignored or rejected. | I,E |
| SD-INVOICE-024 | Snapshot | Posted invoice survives later order/delivery/MD changes and soft deletion as immutable history; correction requires a new credit-note flow, never in-place mutation. | U,I,E |

Traceability: BR-SD-037 → 001–005, 013–016; BR-SD-038 → 002, 005, 009–011; BR-SD-039 → 006–007, 016, 024; BR-SD-040 → 001, 008, 010–012, 020, 023; BR-SD-041 → 011, 017–018, 023; BR-SD-042 → 003, 012, 019–024. All cases are pending; 007d changes documents only.

## WU-SD-008d return scenarios (planned, not executed)

All cases trace to REQ_F4112.7.1; billed cases also trace to REQ_F4112.5.1 and credit cases to REQ_F4112.6.1. `U` = unit, `I` = real SQL and authorised HTTP, `E` = Playwright asserting both visible message and persisted SD return/INV movement/FI credit. 008d adds no executable test; 008a/b must write failing tests before implementation.

| ID | Class | Scenario and expected result | Level |
|---|---|---|---|
| SD-RETURN-001 | Normal | Draft from one Posted delivery saves 1–1000 lines with no INV/FI effect; VIEW shows version and source. | U,I,E |
| SD-RETURN-002 | Normal | Final SYS approval of a fully unbilled return creates one new INV inbound and releases only matching SD commitment. | I,E |
| SD-RETURN-003 | Normal | Final SYS approval of billed portions creates one INV inbound and a FI credit note linked to exact invoice lines. | I,E |
| SD-RETURN-004 | Normal | Mixed billed/unbilled portions post together; FI AR and SD commitment reduce by their distinct saved amounts. | U,I,E |
| SD-RETURN-005 | Normal | SYS rejection preserves history and posts nothing; Rejected revision returns Draft; zero-step approval uses the same posting path. | I,E |
| SD-RETURN-006 | Boundary | Empty lines, zero/negative quantity, >6 decimals, >1000 lines, duplicate line number and blank/oversize request key give field errors and zero writes. | U,I,E |
| SD-RETURN-007 | Boundary | Exact 0.000001 remaining unit succeeds; one unit above balance and zero remaining fail without truncation. | U,I |
| SD-RETURN-008 | Boundary | Month/year/leap-day return dates and original saved tax/currency survive changed current MD data. | U,I |
| SD-RETURN-009 | Boundary | Billed/unbilled partition must sum to requested quantity; a missing, negative or duplicate invoice portion fails atomically. | U,I,E |
| SD-RETURN-010 | Boundary | Repeated partial returns allocate cumulative original/base net/tax and exact final remainder, with no overflow or rounding drift. | U,I |
| SD-RETURN-011 | Duplicate/conflict | Two concurrent returns for the same final quantity serialize; one commits and the other gets 409 with no new INV/FI rows. | I |
| SD-RETURN-012 | Duplicate/conflict | Concurrent invoice issue and return share source-line lock and cannot double allocate billed/unbilled quantity. | I |
| SD-RETURN-013 | Duplicate/conflict | Same key and canonical payload replays saved result with warning and zero new postings; changed payload/key reuse gets 409. | I,E |
| SD-RETURN-014 | Duplicate/conflict | Stale edit/delete/submit/SYS decision version or duplicate permanent number/source (including soft-deleted history) cannot commit. | I,E |
| SD-RETURN-015 | Partial result | Invalid middle line, INV rejection, FI failure, mismatched response or audit failure rolls back SYS/SD/INV/FI and number. | I,E |
| SD-RETURN-016 | Partial result | Partial success/all skipped/all failed batch semantics do not apply to one atomic return; zero-change replay/failure never shows new-success toast. | I,E |
| SD-RETURN-017 | Permission | Anonymous 401, missing exact action 403; CREATE form options work without unrelated RETURN.VIEW grant. | I,E |
| SD-RETURN-018 | Permission | Other-company or SYS action-scope-excluded return/delivery/invoice IDs give 404 without balance, cost or FI leak. | I,E |
| SD-RETURN-019 | State | Draft edit/delete/submit, Pending decision only, Rejected revise and Posted read-only; invalid transitions give named 422. | U,I,E |
| SD-RETURN-020 | Related | Draft/Pending/Rejected/deleted delivery, wrong order/customer/item, invalid warehouse/location or lot/serial reject whole return. | U,I,E |
| SD-RETURN-021 | Related | Original outbound movement cost/tracking determines INV receipt; missing, mismatched or damaged source blocks posting, never uses sales price. | I |
| SD-RETURN-022 | Related | Billed credit cannot exceed original invoice net/tax/quantity; no billed portion is silently treated as unbilled commitment release. | U,I |
| SD-RETURN-023 | Error | 400 field, 403 action, 404 hidden ID, 409 conflict, 422 source/INV/FI, 500 traceId and timeout/network have translated specific messages and zero false success. | I,E |
| SD-RETURN-024 | Error | Unknown timeout lookup by request key resolves committed result or safe absent-key retry without duplicate INV/FI source. | I,E |
| SD-RETURN-025 | File | Empty/malformed/oversized/wrong-encoding files and columns are inapplicable: this return flow has no upload/import/export. | E |
| SD-RETURN-026 | UI/audit | Three-language light/dark normal/empty/filtered/no-permission/validation/success/rejection/replay/error states match persisted SD/INV/FI; actor/IClock and source links remain immutable after Posted. | I,E |

Traceability: BR-SD-043 → 001, 005, 017–020; BR-SD-044 → 006–012, 020, 022; BR-SD-045 → 002–004, 015, 021; BR-SD-046 → 003–004, 010, 022; BR-SD-047 → 002–004, 012, 015; BR-SD-048 → 013–014, 024, 026; BR-SD-049 → 015–026. All 26 cases are pending; 008d changes documents only.
## WU-SD-009d approved demand reader test-first plan

All cases trace to REQ_F4112.2.1, BR-SD-051–053 and SCM-OWN-001–003/009–012/014/016. `I` means real SQL with two independent connections. They are pending WU-SD-009a because SQL Server is stopped; this design slice adds no executable product test.

| ID | Category | Scenario and expected result | Level | Status |
|---|---|---|---|---|
| SD-DEMAND-001 | Normal | Approved committed lines across two months return ordered rows, owner cursor, exact total/digest and complete bucket list; no order or credit mutation. | I | Pending |
| SD-DEMAND-002 | Normal | Replay first and later pages with same cursor/filter returns byte-equivalent membership, totals and digest after another approval commits. | I | Pending |
| SD-DEMAND-003 | Boundary | Empty valid month range returns `Complete`, zero rows and explicit empty buckets; `fromMonth >= toMonth`, invalid month and missing company are rejected. | U,I | Pending |
| SD-DEMAND-004 | Boundary | Page sizes 1 and 500 work, 0/501 reject; month-end, leap day and year rollover assign each line to its saved business month. | U,I | Pending |
| SD-DEMAND-005 | Boundary | Six-decimal quantity and decimal overflow are handled exactly; unknown site and missing UOM evidence have named unavailable reasons, never guessed values. | U,I | Pending |
| SD-DEMAND-006 | Duplicate/conflict | Duplicate page token, stale filter/token pair and malformed cursor cannot change membership; concurrent approvals cannot enter a cursor issued before their commit. | I | Pending |
| SD-DEMAND-007 | Duplicate/conflict | Two connections approve/cancel the same order while paging: old cursor remains identical; later cursor shows the committed correction revision once. | I | Pending |
| SD-DEMAND-008 | Partial result | A crashed page or incomplete manifest returns `ReplayUnavailable`, never a partial `Complete`; retry with same cursor either reproduces all pages or stays unavailable. | I | Pending |
| SD-DEMAND-009 | Partial result | All buckets empty is a successful read with explicit zero counts; partial success/all skipped/all failed batch outcomes are inapplicable because this reader makes no batch mutation. | I | Pending |
| SD-DEMAND-010 | Permission | Missing reader entitlement, another company, out-of-scope order and narrowed scope on later page deny data without disclosing row existence; expanded scope needs a fresh cursor. | I | Pending |
| SD-DEMAND-011 | State | Draft, Pending, Rejected, Cancelled, soft-deleted and uncommitted lines do not contribute; Approved appears only after final SYS decision commits. | I | Pending |
| SD-DEMAND-012 | Related | Item/UOM/site references are read as saved identity; later MD changes do not rewrite SD cursor; missing conversion evidence is identified for SCM. | I | Pending |
| SD-DEMAND-013 | Error | Bad request maps to 400, stale/foreign token to 409, invalid state to 422, internal failure to 500 with traceId; timeout is unknown outcome and caller retries cursor, not zero. | I | Pending |
| SD-DEMAND-014 | File | Not applicable: internal reader has no import, export, upload or download. | I | N/A |
| SD-DEMAND-015 | Retention | Cursor replays through 90 days; after retention returns `CursorExpired`, without silently creating a new cursor. | I | Pending |

### WU-SD-004a2b6 saved-row lock plan

| Test Case ID | Category | Scenario / expected result | Level | Related REQ_F | Result |
|---|---|---|---|---|---|
| SD-DEL-LOCK-001 | Normal | Current Draft or Pending saved rows in arbitrary order, including repeated order-line references, produce each source ID once in ascending order. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-LOCK-002 | Boundary/conflict/state | Empty or over-1000 current rows, foreign delivery ID, deleted or mixed-status row, invalid source ID or six-place quantity, or a Posted lock request fails before any SQL lock. Exactly 1000 saved rows pass. | U | REQ_F4112.3.4 | PASS |

Duplicate codes, soft-deleted source eligibility, complete row selection, two-connection races and transaction rollback still require SD-DEL-009–011 in WU-SD-004a2b. Permission, company scope, 400/409/422/500/timeout and visible feedback remain SD-DEL-013/017/019. This pure plan has no user, SQL, HTTP, UI or file boundary; partial success and file cases do not apply to an atomic delivery command. E2E must compare the displayed decision with saved SD/SYS/INV data when the transaction and UI are implemented.

### WU-SD-004a2b7 selected saved-row completeness

| ID | Class | Scenario and expected result | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-SELECT-001 | Normal | Separately counted current Draft/Pending rows with distinct persisted IDs yield unique ascending order-line locks; repeated source order line is allowed. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-SELECT-002 | Boundary/conflict | Zero, over-1000 or mismatched count, missing/duplicate/nonpositive saved line ID, foreign delivery, deleted/mixed-status or malformed quantity fails before locking. | U | REQ_F4112.3.4 | PASS |

SQL must prove same-company count/selection in one serialized transaction, actual ordered locks, concurrency and rollback under SD-DEL-009–011. Permissions, state transitions, HTTP errors and translated UI remain SD-DEL-013/014/017/019. Duplicate codes and soft-deleted source eligibility require SQL. Partial success/skips and file inputs do not apply to this atomic, input-free pure guard.

### WU-SD-004a2b8 selected rows versus balance snapshot

| ID | Class | Scenario and expected result | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-MATCH-001 | Normal | Current Draft/Pending selected rows and balance snapshot have identical persisted line IDs, sources and six-place quantities regardless of order. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-MATCH-002 | Boundary/conflict/state | Empty, missing, extra, duplicate or nonpositive current snapshot ID, changed source/quantity/status, deleted or foreign row fails; exactly 1000 rows pass. | U | REQ_F4112.3.4 | PASS |

The SQL caller must still read both sets under one company-scoped serialized transaction and use the verified snapshot for the balance decision. Permission, 400/409/422/500/timeout, cross-company scope and two-connection races remain SD-DEL-009–011/013/017/019. Partial results and files do not apply to this atomic pure guard; no UI is added here.

### WU-SD-004a2b9 combined locked decision

| ID | Class | Scenario and expected result | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-DEL-DECIDE-001 | Normal | Draft and Pending decisions accept identical saved current line identity and sufficient full balance, including other Posted consumption. | U | REQ_F4112.3.4 | PASS |
| SD-DEL-DECIDE-002 | Boundary/conflict/state | Changed persisted line ID fails identity; one smallest unit above available fails balance; Posted is not a decision input. | U | REQ_F4112.3.4 | PASS |

The pure call has no user, API, file or batch boundary. Duplicate codes, soft-deleted source eligibility, permissions, company scope, 400/409/422/500/timeout, visible success or failure, actual ordered SQL locks, SYS/INV rollback and two-connection races remain SD-DEL-009–011/013/017/019 under WU-SD-004a2b. E2E must compare the message with saved SD/SYS/INV data when the transaction and UI exist.

WU-SD-003a manual persistence checkpoint (2026-10-27T16:01:48+08:00): SD-ORDER-001/002/005/011/012/014/015 gained three real-SQL tests. The first failed to compile before `SalesOrderDraftStore` existed. The store now requires a caller-owned transaction, saves a prepared manual Draft, replaces all lines under a same-company header lock and expected version without changing its permanent number, and soft-deletes with `IClock`. SQL checks cover saved lines and parent version, stale delete, rollback, other-company invisibility and Approved edit rejection. Focused tests passed 3/3. This is an internal persistence layer: MD commercial/reference snapshots, SYS number transaction, action scope, quotation conversion, authorised HTTP, two-connection race and full E2E remain open. Partial/all-skipped batch and file cases remain inapplicable as explained in SD-ORDER-013/019.

WU-SD-003a commercial snapshot checkpoint (2026-10-27T16:17:55+08:00): SD-ORDER-001/007/016 gained three unit tests. The first failed compilation because `SalesOrderSnapshotBuilder` was absent; the implementation resolves current-company approved customer, active item, active currency, base currency, dated exchange rate and output tax via MD public contracts, then retains identities and rounded per-line/header amounts. A leap-day case checks the copied evidence and a separate case checks company currency precision. Invalid input stops before MD calls. Architecture 230/230 and Debug build 0 warnings/errors. The builder creates a prepared snapshot only: caller-owned transaction, SYS number allocation, persisted SQL snapshot verification, concurrent reference edits, authorised HTTP and quote conversion remain open. SD-ORDER-002–020 retain the normal, boundary, conflict, partial, permission, status, related and error plan; batch/file cases remain inapplicable per 013/019.

WU-SD-003a transaction checkpoint (2026-10-27T16:30:27+08:00): SD-ORDER-001/012/014 gained a real-SQL command test. It first failed compilation because `SalesOrderCreateService` was absent. The command checks `SAL.ORDER.CREATE`, allocates a real SYS sequence number, builds the MD contract snapshot and saves a Draft in one transaction. A deliberately duplicated document number after sequence allocation makes the insert fail; the test confirms the saved order count and SYS current value remain unchanged. Missing CREATE permission also rejects the command. The MD contract responses are controlled fixtures here, so real MD concurrent edits, HTTP authorization and scope, two-connection number competition and quotation conversion remain pending. Focused order SQL tests pass 12/12; the full integration run had no summary after roughly two minutes and was stopped, so it is not counted as passed. Partial/all-skipped batch and file cases remain inapplicable per 013/019.

WU-SD-003a MD stability checkpoint (2026-10-27T16:37:47+08:00): SD-ORDER-001/016 gained a real SQL item concurrency test. It first failed because the order command used ReadCommitted. After moving create to Serializable, a second connection attempting to rename the MD item after its identity is read receives SQL lock timeout 1222; it can rename after commit while the saved order line retains the old name. The test also asserts the transaction isolation and real MD public lookup. Focused SalesOrder SQL 13/13, Architecture 230/230, Shared 381/381, DocGen 22/22 and Debug build 0 warnings/errors pass. Customer/currency/tax concurrent edits, quotation conversion, HTTP authorization, two-connection number allocation and full integration remain open. Atomic single-command and no-file inapplicability remain as SD-ORDER-013/019.

WU-SD-003a customer stability checkpoint (2026-10-27T20:28:06+08:00): SD-ORDER-001/016 now use the real approved MD customer lookup in the order transaction. A second SQL connection cannot rename that customer after lookup (lock timeout 1222); after commit it can rename, while the saved order retains the original name. The first run exposed a missing payment setup model in the focused test context; registering the existing model fixed the fixture, without changing product behavior. Focused SalesOrder SQL 14/14 and Debug build 0 warnings/errors pass. Currency/tax reference races, quote conversion, authorised HTTP, concurrent numbering and full integration remain open. Partial batch and file cases remain inapplicable per SD-ORDER-013/019.

WU-SD-003a currency/tax stability checkpoint (2026-10-27T20:34:30+08:00): SD-ORDER-001/007/016 now use real MD currency, dated exchange-rate and output-tax lookups. During the Serializable create transaction a second SQL connection cannot change the currency precision, dated exchange rate, tax percentage or tax-code active state (each gets SQL lock timeout 1222); after commit the exchange rate can change while the saved order still shows the original rate and tax percentage. The focused SalesOrder SQL suite passes 15/15 and Debug build has 0 warnings/errors. This verifies a single manual create's stable reference reads; quotation conversion, authorised HTTP, concurrent SYS numbering and full integration remain open. The normal/boundary/conflict/partial/permission/state/related/error/file categories remain SD-ORDER-001–020; batch partial and file cases remain inapplicable per 013/019.

WU-SD-003a SYS numbering race checkpoint (2026-10-27T20:40:31+08:00): SD-ORDER-009 now has a real two-connection integration test through manual order create and the SYS `NumberGenerator`. While the first create holds `SO001` before commit, the second connection's allocation receives SQL lock timeout 1222; after commit its retry saves `SO002`. The test verifies two distinct persisted orders and SYS current value 2. The first run failed because the test closed its second EF connection between `SET LOCK_TIMEOUT` and allocation, producing default command timeout -2; keeping that connection open corrected the fixture. Focused SalesOrder SQL 16/16, Shared 381/381, Architecture 230/230, DocGen 22/22, Debug build 0 warnings/errors. Quotation conversion, authorised HTTP/Swagger, scope/version races and full integration remain open. Single-command partial and file inapplicability remain SD-ORDER-013/019.

WU-SD-003a action-scope checkpoint: SD-ORDER-014 now checks that manual CREATE persists the SYS user's owner, branch and department. Real SQL then verifies Branch and Self include the saved order while a different Department excludes it. `SalesOrderCreateTransactionTests` 6/6 and Debug build with zero warnings/errors pass; migration applied to `myerp`. Read/edit/delete HTTP scope and version races remain open. The existing SD-ORDER-001–020 matrix covers all required scenario classes; batch-partial and file scenarios remain inapplicable per 013/019.

WU-SD-003a HTTP read/create checkpoint (2026-10-28T14:51:45+08:00): SD-ORDER-014 gained a failing real-SQL test before `SalesOrderQueryService` existed. It now verifies VIEW Branch and owner Self can read the saved line, while another Department or user Self receives 404. An HTTP test verifies GET detail and POST manual create require authentication and both routes appear in OpenAPI. Focused SalesOrder SQL/HTTP 19/19, Architecture 230/230, Shared 381/381 and Debug build with zero warnings/errors pass. Authorised HTTP POST using live MD/SYS data, cross-company HTTP, EDIT/DELETE scope and stale-version 409, list pagination, quotation conversion and full integration remain open. Normal/boundary/conflict/partial/permission/state/related/error/file scenarios remain SD-ORDER-001–020; batch-partial and file cases remain inapplicable per 013/019.

2026-10-27T16:05:03+08:00 follow-up: the full `SalesOrderSchemaTests` group passed 11/11 after the company guard was added. Debug solution build had zero warnings/errors; Shared, Architecture and DocGen projects passed in the full-solution attempt. Integration tests did not finish after about four minutes with `--max-threads 2`; the run was interrupted without a success summary, so full-suite acceptance remains open.
WU-SD-003a SQL integration gate checkpoint (2026-10-28T19:46:58+08:00): full `MyERP.IntegrationTests` with `--max-threads 2` produced SQL command timeouts in unrelated SYS and MD tests. A single SYS approval test with `--max-threads 1` failed inside `TestDatabase.InitializeAsync` migration before its test body (`INSERT __EFMigrationsHistory`, 30 seconds). This is an environment/test bootstrap gate; the current result does not invalidate the previously passing SalesOrder 29/29, but it is not a full-suite pass. Debug build and Architecture 230/230 pass. See optimization-log for SQL wait and memory evidence. No new SD scenario was marked passed; quotation conversion awaits real quotation persistence from WU-SD-002a.

WU-SD-003a SQL retry checkpoint (2026-10-28T19:59:48+08:00): the exact SYS approval test passed 1/1 and SalesOrder SQL/HTTP passed 29/29. A full integration diagnostic run showed repeated `WAIT_SLEEP_LOWMEM_GUARD`, SQL process low-memory flag and 0.5 GB host free memory; it was stopped without a summary, so full-suite status remains pending. No new SD scenario was marked passed. Debug build passed with zero warnings/errors. Quotation conversion still needs WU-SD-002a's real persisted quotation.
WU-SD-003a cross-order lock regression (2026-10-28T20:35:55+08:00): SD-ORDER-021 (I; REQ_F4112.2.3) holds a Serializable edit of one saved Draft while a separate connection inserts a line into another Draft in the same company. The independent insert must complete before the first transaction commits; after both commits, the edited version/remark/line and the other order's two lines must match SQL persistence. With the old clustered scan the test failed on SQL lock timeout 1222; with the company/order index FORCESEEK it passes. This covers the conflict/related-order boundary. Normal, input boundaries, permission, status and error paths remain mapped by SD-ORDER-001–020. A single atomic command has no partial-batch or file scenario (SD-ORDER-013/019). The test does not prove the full EF execution plan or quotation conversion.

WU-SD-003a full integration checkpoint (2026-10-28T21:00:30+08:00): `MyERP.IntegrationTests` Debug, one test thread, passed 659/659 in 5m 44s with no failures or skips. The diagnostic wrapper returned exit 1 afterwards because its background SQL sampler hit a transient file-sharing `IOException`; the saved MTP result is complete, but the wrapper needs repair and verification. No quotation conversion case is marked passed while WU-SD-002a remains BLOCKED.
