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
