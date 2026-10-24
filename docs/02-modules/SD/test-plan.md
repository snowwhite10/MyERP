# SD test plan — WU-SD-000 baseline

Status: planned; no product tests or SQL assertions are claimed as passing. `U` = unit, `I` = real SQL and authorised HTTP integration, `E` = Playwright UI/API agreement. Each implementation unit first writes failing automated tests for its allocated cases, then implements and records results. E2E checks both visible message/state and persisted API/list data.

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

WU-SD-003a partial result (2026-10-24T07:32:40+08:00): `SalesOrderDraftTests` first failed to compile because the order draft types did not exist, then passed 14/14. The pure `U` slice covers a leap-day date, 1000-line upper bound, per-field failures, duplicate/1001 lines, per-line rounding and header overflow in SD-ORDER-001, 006 and 007. It does not prove saved snapshots, reference validity, transactions, company/permission checks or HTTP behavior. The remaining `I/E` cases above retain their planned status. SD-ORDER-013 and 019 remain not applicable to a single atomic, file-free command as documented above.

WU-SD-003a source eligibility slice (2026-10-24T07:36:22+08:00): `SalesOrderQuotationSourceTests` first failed compilation because the source validator did not exist, then passed 9/9. The `U` portion of SD-ORDER-003, 010, 014 and 015 now checks Approved status, inclusive expiry date, prior conversion, same-company identity and invalid company. It delegates quotation state/expiry behavior to the existing quotation lifecycle. Real SQL locking/unique constraints, persisted snapshots, action scope, transaction rollback and authorised HTTP remain unverified `I` gates; no order conversion endpoint exists yet. File and batch cases remain inapplicable for the documented reasons.

WU-SD-003a lifecycle slice (2026-10-24T07:39:34+08:00): `SalesOrderLifecycleTests` first failed compilation because the lifecycle type did not exist, then passed 17/17. The `U` portion of SD-ORDER-002, 005, 008 and 015 covers Draft edit/delete/submit, Rejected revision to Draft, Pending SYS decision, Approved cancellation without active/posted delivery, immutable other states and named status/delivery errors. This pure rule does not perform credit checks, version checks, permission checks, deletion, SYS callbacks or SQL writes. Real SQL and HTTP cases remain pending; no frontend or file operation was introduced.

## WU-SD-004d sales outbound scenarios (planned, no product test claimed)

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

Traceability: BR-SD-021 → 002, 004–005, 010, 013–014, 019–020; BR-SD-022 → 001, 003, 007–010, 014; BR-SD-023 → 001, 006, 015–016, 020; BR-SD-024 → 003–005, 009–011, 016–017; BR-SD-025 → 011–012, 017–020. All cases are planned and unexecuted; WU-SD-004d adds no product test, schema or endpoint.

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
