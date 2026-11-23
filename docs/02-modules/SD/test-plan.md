# SD test plan — WU-SD-000 baseline

WU-SD-003c5c order View/Create/Edit QA (2026-11-20T20:46:11+08:00): Traceability REQ_F4112.2.1/2.2/2.3 → ui-spec 4.4.1.2.4–2.6 → SD-ORDER-QA-001. Report: `docs/02-modules/SD/qa-report.md` (214 screenshots, P1 0 / P2 0 after fix, 3 P3 → WU-SD-003c7). Backend full 3666/3666 (`--max-threads 2`, SQL gate PASS 2.823/2.827/2.834 GiB); frontend 941/941; single-worker simulated E2E full 612 passed / 27 skipped (35.9 min).

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-ORDER-QA-001 | Normal / data consistency | Create draft (100 × 12.5, 5%) then Edit (120) against a stateful server: list row, detail header, detail line and Edit form all show the stored values (1,312.50 → 1,575.00, quantity, unit price, remark, version 1 → 2); exactly one POST and one PUT v1; en / zh-Hans light/dark success and normal screens captured without raw keys. | E | REQ_F4112.2.1, REQ_F4112.2.2, REQ_F4112.2.3 | Pass |

WU-SD-003c5b order export button (2026-11-20T19:52:55+08:00): Traceability REQ_F4112.2.1 → ui-spec 4.4.1.2.1 export action → ADR-141 → SD-ORDER-EXPORT-UI-001–009. Frontend 941/941 (csv-export 4, api-error blobJsonErrors 2, order-export 11, order.service export 2 new); order E2E 70/70 (export 7 new, View 7, Edit 32, Create 24); WebSecurityTests 16/16 with the new CORS assertion; Debug build 0 warnings/errors. 28 export screenshots `tests/qa/SD/screenshots/2.4-export-*` inspected. Backend/E2E full regression is WU-SD-003c5c.

WU-SD-003c5a order CSV export (2026-11-20T19:26:35+08:00): Traceability REQ_F4112.2.1 → API `GET /api/v1/sal/orders/export` (ADR-141) → SD-ORDER-EXPORT-001–010. Unit SalesOrderCsvTests 31/31; integration SalesOrderExportHttpTests 1/1, with all order HTTP classes 7/7. SQL memory gate DEFER 1.439/1.980/2.024 GiB, so backend full regression and E2E full were not run this round.

| ID | Category | Scenario | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-ORDER-EXPORT-001 | Normal | EXPORT user downloads UTF-8 BOM CSV, text/csv, attachment sales-orders.csv, X-Export-Row-Count 2; lines follow saved line number. | I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-002 | Language | en / zh-Hans / zh-Hant give localised headers and status text matching screen tags; unknown language falls back to zh-Hant; numbers and ISO dates stay invariant. | U/I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-003 | Boundary | Leap-day date, six-decimal rate/price, saved scale kept (0.1250), embedded comma/quote/newline quoted, null remark empty last cell. | U/I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-004 | Boundary / file | 10,000 lines allowed; 10,001 → 422 SAL.ORDER.EXPORT_LIMIT before output (pure guard; 10,001 SQL rows not seeded). | U | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-005 | Security / file | Text starting with = + - @ tab CR (also after spaces) is apostrophe-prefixed, inside quotes when needed. | U/I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-006 | Partial / empty | Filter with no matches → 200 header only, row count 0 (client must warn, 003c5b). | I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-007 | Permission | VIEW-only → 403 COMMON.PERMISSION.DENIED; unauthenticated → 401; foreign company → 0 rows; Branch EXPORT scope of another branch → 0 rows, of the order branch → 2. | I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-008 | Error | Numeric status and reversed date range → 400; undefined enum is never printed as a number. | U/I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-009 | Duplicate/deleted / state | Soft-deleted Draft in scope is not exported; every saved status is exported as saved (read-only, no state blocked). 409 not applicable to a read. | I | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-010 | Related / UI | Browser button, zero-row warning, 403/422/500/timeout messages and three-language light/dark screenshots (WU-SD-003c5b). | E | REQ_F4112.2.1 | Pass via SD-ORDER-EXPORT-UI-001–009 |
| SD-ORDER-EXPORT-UI-001 | Normal | EXPORT user exports the applied keyword/status/sort without page/pageSize; file `sales-orders.csv` downloads and a success alert shows the server line count. | U,E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-002 | Language | After switching to en and zh-Hans the request carries that Accept-Language and the downloaded CSV header/status are in that language; alert text follows the UI language. | E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-003 | Boundary / file | Row count uses RFC 4180 (BOM ignored, quoted comma/quote/CRLF is one row, last row without terminator); count missing, non-numeric, negative, mismatched, empty body or unterminated quote → error, no download. | U,E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-004 | Partial / zero | `X-Export-Row-Count: 0` (EXPORT scope narrower than VIEW) → warning alert “no file downloaded”, never success, no download. | U,E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-005 | Permission | VIEW without EXPORT: button and hint hidden, no export request; server 403 → translated “no permission to export”. Cross-company/scope rows are server-side (SD-ORDER-EXPORT-007). | U,E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-006 | State | Disabled with visible reason while list loads, after list failure, on unfiltered empty (“no orders yet”), filtered empty (“no orders match”) and while exporting; a second click sends no second request. Order status does not block export (read-only). | E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-007 | Error | 400 with field detail, 422 `SAL.ORDER.EXPORT_LIMIT`, 500 with trace ID (only 5xx shows trace), 15 s timeout, network; three languages light/dark; button re-enabled and retry succeeds. | U,E | REQ_F4112.2.1 | Pass |
| SD-ORDER-EXPORT-UI-008 | Duplicate / conflict | Not applicable: export is a read; 409 cannot occur. Stale results are discarded when filters, company or page change (export cancelled). | — | REQ_F4112.2.1 | N/A |
| SD-ORDER-EXPORT-UI-009 | Security / related | CORS exposes `X-Export-Row-Count` (with Retry-After, Content-Disposition) so a cross-origin frontend can verify counts. | I | REQ_F4112.2.1 | Pass |


WU-SD-003c4 completed 2026-11-20T19:11:09+08:00: SQL memory gate passed (2.913/3.098/3.095 GiB). Backend full regression including integration passed 3634/3634 with `--max-threads 2`; one-worker simulated E2E full passed 604 with 27 skipped (631 total), including the 32 Edit cases. No product code changed in this final regression round.

WU-SD-003c4 checkpoint 2026-11-19T22:01:20+08:00: One-worker simulated E2E full passed 603/603 with 27 skipped (630 total); the 31 Edit cases passed within it. Frontend full passed 922/922. SQL memory gate passed at 2.859/2.867/2.871 GiB, but backend full integration consumed free memory to 1.135 GiB after DocGen, Shared and Architecture passed; the run was stopped, so backend full remains unverified. Real authorised order HTTP 5/5 is inherited from the previous checkpoint. SQL-backed DocGen remains 13 Error/614 Warning under separate WU-P0-043. Edit stays IN_PROGRESS.

WU-SD-003c4 checkpoint 2026-11-19T21:26:25+08:00: Added browser checks for empty currency and incomplete added line (no PUT, saved version unchanged), plus malformed saved leap date and duplicate line number (no editable form or PUT). The commercial-field receipt now verifies a four-place discount token through PUT and saved GET. Edit suite passed 31/31 with one worker; the changed receipt passed again 1/1. Four new and two regenerated light/dark screenshots were opened individually; 66 distinct Edit screenshots have now been reviewed. Full simulated E2E is running with one worker. Backend full and SQL DocGen consistency remain unverified; the known 13 DocGen errors belong to WU-P0-043. SD completion count remains unchanged.

WU-SD-003c4 checkpoint 2026-11-19T16:58:45+08:00: Frontend regression passed 922/922 and the final Edit browser suite passed 25/25. Real authorised order HTTP passed 5/5. Backend full and simulated E2E full were both stopped on falling host memory; neither is a full pass. SQL-backed isolated DocGen generated artifacts but reported 13 Error/614 Warning: one duplicate FI/PUR data-structure ID, one SQL delivery-tracking table not documented on main, and eleven FI interface requirement shorthand references. `git diff` confirms none of those source files changed in this SD slice. Follow-up WU-P0-043 is in backlog; do not count consistency as passed.

WU-SD-003c4 checkpoint 2026-11-19T16:54:55+08:00: The complete Edit Playwright spec passed 25/25 with one worker, including saved customer/item/tax picker identities and a raw DECIMAL(19,6) boundary token preserved through GET, PUT and receipt. Four additional light/dark screenshots were opened individually; 58 Edit screenshots have been reviewed. The simulated E2E full run reached about 407 passing cases with no observed failure, then was stopped when two Chromium workers reduced host free memory to about 0.75 GiB; it is not a full pass. Frontend unit regression is running. The previously stopped SQL-backed backend full run remains unverified. Edit stays open for remaining client validation/date/action-scope cases, full regression and DocGen consistency.

WU-SD-003c4 checkpoint 2026-11-19T16:38:26+08:00: Added company-switch 404, edited commercial-field saved receipt, 1000-line Edit cap and item/tax/currency 422 browser acceptance. Company-switch assertion first failed on an extra new-company GET caused by authorisation refresh; it now verifies every post-switch request stays in company 2 and no PUT occurs. Edit 18/18 plus receipt 1/1, line limit 1/1 and reference 3/3 passed. Ten new light/dark screenshots were opened individually (54 total). Real authorised order HTTP 5/5 passed after a 2.654/2.648/2.655 GiB memory gate. A later backend full run started after 2.606/2.603/2.607 GiB and passed DocGen, Shared and Architecture, but SQL integration consumed free memory down to 1.2 GiB, so the run was stopped; backend full is unverified. Full simulated E2E, remaining Edit boundaries, action scope, picker identities and DocGen consistency remain pending.

WU-SD-003c4 checkpoint 2026-11-19T16:11:10+08:00: Recovered and committed the previous round's 19 pending files as `ede2fa33`. The unchanged Edit browser suite passed 14/14 and frontend full passed 922/922. Added two list-entry browser cases: Draft/Rejected offer Edit while Pending/Approved/Cancelled do not, and a VIEW-only user sees the Draft without an Edit link; both passed. SQL memory gate returned DEFER at 1.665/1.276/1.151 GiB, so real SQL, authorised HTTP, backend full and DocGen with SQL are unverified this round. The full simulated E2E suite and remaining Edit boundary, reference, saved-field and visual checks remain open; SD remains 53/69.

WU-SD-003c4 checkpoint 2026-11-19T12:06:16+08:00: The Edit browser spec passed 14/14 after the state-specific warning change; an additional focused 1/1 pass checked Pending warnings in English and Simplified Chinese. Pending, Cancelled, converted and delivered light/dark screenshots plus the two translated Pending screenshots were opened individually. The browser confirms no form or PUT for each blocked state and the saved mock stays unchanged. Frontend full passed 922/922. SQL memory gate passed 4.038/4.086/4.088 GiB immediately before a two-thread backend full run, which is still in progress. Remaining browser boundary, permission, saved-field identity and full simulated E2E checks keep 003c4 open.

WU-SD-003c4 checkpoint 2026-11-19T12:03:10+08:00: SD-ORDER-EDIT-008 browser assertion first failed because Pending displayed a generic noneditable warning. The Edit page now selects a translated reason from the saved status, source quotation and delivered or pending quantity, while retaining the generic warning for malformed saved data. The focused rerun reached all four reasons; its last assertion used different wording for the delivered reason and is being corrected before the full Edit spec rerun. No real SQL or full backend result is claimed at this checkpoint.

WU-SD-003c4 checkpoint 2026-11-19T11:44:56+08:00: Added browser GET/PUT disconnection cases and a hidden/deleted/scope-excluded 404 case. The network cases assert a retry or retained input, no duplicate PUT and unchanged saved data. The 404 case first failed because the decimal-safe text GET left its error body as a string, so the screen showed a generic unexpected error. `jsonTextErrors()` now restores the ProblemDetails error code before the Edit page translates it; the 404 case passed after the fix. Edit E2E was 12/12 before the fix and the focused 404 case passed after it; frontend full 922/922 passed. Six new light/dark screenshots were opened individually; the corrected 404 message is specific and does not expose saved content. SQL memory gate 2.910/2.896/2.915 GiB PASS; backend full rerun is in progress. No real SQL/HTTP or complete Edit acceptance is claimed here.

Checkpoint 2026-11-19T11:48:07+08:00: Final Edit spec 12/12 passed after the 404 fix; frontend full remains 922/922. Backend full rerun passed DocGen, Shared and Architecture but was interrupted during integration when free physical memory fell to 1.217 GiB; no backend full or real SQL/HTTP pass is claimed. Remaining Edit browser permissions, states, references, saved field identities, precision/1000-line boundaries, three-language visual matrix and full E2E regression are still open.

## WU-SD-003c4 Order Edit scenarios (2026-11-19T07:17:00+08:00)

Traceability: REQ_F4112.2.3 → Sales Order Edit Interface 2.6 → REQ_IO_A2.4.8 / REQ_IO_A2.6.1–16 → SD-ORDER-EDIT-001–014. U = frontend unit, I = real SQL/authorised HTTP, E = Playwright with visible result and saved GET. The service/mapping tests were red before implementation. The browser route, form and first E2E cases are now implemented; remaining cases and full visual/SQL acceptance remain open.

Verification 2026-11-19T07:21:36+08:00: frontend all 922/922, production build passed, DocGen 22/22. No new real SQL, backend HTTP or browser E2E result is claimed for this partial checkpoint.

Checkpoint 2026-11-19T11:12:23+08:00: Edit browser E2E 5/5 passed after red-to-green i18n and 422 refusal fixes. It covers Rejected → Draft, saved GET after PUT, 409 input retention, no-change, one invalid date, Approved block, 500 trace ID, 422 customer refusal and missing EDIT permission. The saved-result label now uses the verified GET. Eighteen light/dark and three-language screenshots were captured; visual review found and fixed a stale language-dependent tax label. SQL memory gate 1.629/1.563/1.554 GiB DEFER; no fresh real SQL or backend full claim. Frontend full and complete browser matrix are still pending.

Checkpoint 2026-11-19T11:26:46+08:00: Edit E2E 8/8 passed with added 400 field validation, timed-out GET/retry and mismatched saved receipt. The 400 and GET timeout light/dark screenshots were opened and checked for a visible reason, retry and absence of a misleading success or empty form. SQL memory gate 2.854/2.844/2.851 GiB PASS; backend full integration is in progress. PUT timeout test is written but not yet run. Other network, permission, status and browser precision boundaries remain open.

Checkpoint 2026-11-19T11:31:31+08:00: Edit E2E 9/9 passed, including timed-out PUT with retained input, disabled repeat save and no false success. Two additional light/dark PUT timeout screenshots were opened and checked. The Debug backend full run passed DocGen, Shared and Architecture projects but was interrupted during SQL integration after free physical memory fell to about 0.7 GiB; no full backend pass is claimed. Real SQL/authorised HTTP, full browser matrix and remaining network/permission/status/precision cases remain open.

Checkpoint 2026-11-19T11:32:42+08:00: Frontend full 922/922 passed. Edit E2E remains 9/9. WU-SD-003c4 stays IN_PROGRESS because real SQL/authorised HTTP and complete browser/full-suite regression have not passed this round.

| ID | Class | Scenario and required assertion | Level | Result |
|---|---|---|---|---|
| SD-ORDER-EDIT-001 | Normal | Load a saved manual Draft/Rejected with version and exact decimal strings; preserve number and line identities. Rejected revision becomes Draft after one PUT and saved GET. | U,I,E | U passed; E Rejected → Draft passed; real SQL/HTTP recheck pending |
| SD-ORDER-EDIT-002 | Boundary | Empty customer/date/currency/line, invalid leap day, 501-character remark, 0 or >1000 lines, duplicate line number, invalid six/four-place decimals prevent PUT with field reason. | U,E | Shared form U passed; E empty date/currency, incomplete added line, 501-character remark, seven-place quantity and 1000-line cap/no PUT passed; malformed saved leap day and duplicate line number hide the edit form. Empty customer and zero-line draft cannot be produced through the current picker/one-line-minimum UI and remain unit/API concerns. |
| SD-ORDER-EDIT-003 | Boundary | GET decimal values near DECIMAL(19,6) limit retain text before JSON parsing; PUT emits unchanged numeric tokens without JS Number conversion. | U,E | U passed; E six-place quantity/price and near-limit 9999999999999.999999 GET/PUT/receipt passed |
| SD-ORDER-EDIT-004 | Conflict | Stale version 409 leaves typed values visible, shows warning and disables repeat PUT until an explicit refresh; fresh GET replaces the baseline only after user action. | I,E | E passed; I prior server coverage, recheck pending |
| SD-ORDER-EDIT-005 | Duplicate / deleted | A soft-deleted order or lost EDIT scope returns 404 without disclosing saved content or changing another order. No code uniqueness rule applies to editing an existing numbered order. | I,E | I prior server coverage; E hidden 404 and no PUT passed; SQL recheck pending |
| SD-ORDER-EDIT-006 | Partial result | One atomic order PUT cannot partially succeed, skip rows or return a batch count. A zero-change/mismatched saved receipt must not show success. | I,E | E no-change and mismatched saved receipt passed |
| SD-ORDER-EDIT-007 | Permission | Anonymous 401; no EDIT 403 and no Edit link/deep link; GET additionally requires VIEW, and current-company/action-scope exclusion returns 404. | I,E | E deep link without EDIT, EDIT-only without VIEW (route forbidden before GET), hidden 404, list link/status/VIEW-only, company switch and action-scope PUT 404 with no repeat write passed. Authorised HTTP class 5/5 passed in prior checkpoint. |
| SD-ORDER-EDIT-008 | State | Pending, Approved, Cancelled, converted or delivered order shows a specific noneditable reason and sends no PUT; server remains authoritative if state changes after GET. | U,I,E | U passed; E all five states have no form or PUT; post-GET Pending 422 preserves input, names state, disables another PUT and refreshes into blocked view (17/17 Edit suite); real SQL recheck pending |
| SD-ORDER-EDIT-009 | Related | Disabled/deleted customer, item, currency or tax rate and changed business date are rechecked by server; rejection names field/reference and saves nothing. | I,E | E inactive customer (field detail and real `MD.CUSTOMER.NOT_APPROVED` shape without details, zh-Hant/zh-Hans/en light/dark), item/tax/currency and changed business date 422 field reasons passed; U snapshot builder stops after a rejected customer before currency/item/tax lookups; real SQL recheck pending |
| SD-ORDER-EDIT-010 | Error | 400 field validation, 409 conflict and 422 business refusal have localised messages and no success; 500 includes traceId and treats outcome as uncertain. | E,U | 400/409/422/500 and approved-status block passed in zh-Hant, zh-Hans and en, light and dark; field details re-translate on language switch and use a locale-appropriate colon; U zero-line Edit rejected before scope/DB access |
| SD-ORDER-EDIT-011 | Timeout / network | GET timeout/network offers retry without PUT; PUT timeout/network preserves input and blocks another PUT until saved status is checked. | E | GET timeout/network retry, PUT timeout/network retained input and no repeat PUT, 500 uncertain path passed |
| SD-ORDER-EDIT-012 | Success | After 200, fresh saved GET confirms same ID, number, version progression, edited customer/date/currency/line identity and text before success; server data and visible result agree. | E | Fresh GET success path passed for remark/date/currency/quantity/price/four-place discount and selected customer/item/tax IDs; mismatch rejects success |
| SD-ORDER-EDIT-013 | File | No upload/import/export in Edit; empty, malformed, oversized, over-row and bad-encoding file cases do not apply. | — | N/A |
| SD-ORDER-EDIT-014 | Visual | Capture zh-Hant normal, validation, 409, 422, 500, timeout, no permission, blocked status and success in light/dark; capture normal en/zh-Hans and inspect every screenshot. | E | 66 distinct screenshots captured and individually reviewed, including empty currency and incomplete added line in light/dark; changed commercial receipt screens regenerated and reviewed. Remaining three-language error states pending. |

WU-SD-003c3 completed 2026-11-19T07:09:18+08:00: REQ_F4112.2.2 / REQ_IO_A2.5.1–16 trace to SD-ORDER-UI-008–016. The final Create spec passes 24/24; it covers 1000 rendered browser lines with a disabled add button and no invalid POST, current-company MD selection after a company switch, hidden/invalidated quotation 404, status-changed and expired quotation 422 with specific translated reasons, and the three-language 400/409/422/500 error matrix in light/dark. Saved manual identity and quotation source must match the CREATE-scoped receipt before success. The last full simulated E2E run passed 570 with 27 skipped; the two later language tests passed 2/2 and the full Create spec passed 24/24. Frontend 917/917, Release backend 3632/3632 and zero build warnings/errors. No upload or batch partial-result path exists. UX screenshots are under `tests/qa/SD/screenshots/2.5-*` and were individually inspected.

Checkpoint 2026-11-19T06:21:32+08:00: SD-ORDER-UI-011 receipt identity tests first failed on the missing matcher, then passed 6/6 after checking saved manual customer/date/currency/line identities and converted quotation ID. Create E2E now checks a wrong-source receipt and an approved quotation hidden or invalidated before conversion; focused 4/4 and prior Create 14/14 pass, frontend full 917/917. The 404 screen's light/dark screenshots were inspected. The ongoing full E2E run, 1000-line browser boundary, cross-company MD picker, three-language error matrix and memory-gated SQL regression remain open; 003c3 stays IN_PROGRESS.

Checkpoint 2026-11-19T06:09:31+08:00: WU-SD-003c3 Create E2E 14/14 and frontend 915/915 pass. CREATE-only route, missing CREATE, empty approved-quotation picker, disabled reasons, mismatched/empty saved receipt and receipt-read 500 trace ID were exercised. The receipt-read 500 test failed before the UI exposed its trace and passed after the fix. Twenty-eight Create screenshots cover zh-Hant light/dark states and normal en/zh-Hans, each visually reviewed. Release backend build and Architecture 2171/2171, Shared 386/386, DocGen 22/22 pass. SQL memory gate twice deferred backend full integration (2.139/2.139/2.140 and 2.176/2.177/2.187 GiB versus 2.5 GiB). Cross-company/hidden-source and non-Approved source browser cases, 1000-line browser boundary, three-language error matrix, full E2E and SQL-backed backend regression remain open; WU-SD-003c3 stays IN_PROGRESS.

Checkpoint 2026-11-19T01:44:22+08:00: SD-ORDER-UI-008 and UI-014 were red for missing first-error focus and English field details in zh-Hant. Create E2E 7/7 and frontend 915/915 are green after focusing the first invalid control and localising recognised server reasons with a field-specific fallback. A follow-up Create E2E 7/7 also checked that the next invalid date gains focus after the customer is selected. The 422 zh-Hant light/dark screenshots were recaptured and visually inspected. CREATE-only browser role, empty source, disabled-action reasons, full three-language visual QA, interface IDs and full backend/E2E regression remain open; 003c3 is not complete.

Checkpoint 2026-11-19T01:36:24+08:00: SD-ORDER-UI-014 now checks 422 with a field reason, network loss and a six-second timeout. The 422 test first failed because the page called a business rejection an unexpected error; the new three-language rejection prompt made it green. Create E2E 7/7 passed, and the zh-Hant 422 light/dark screenshots were inspected at `tests/qa/SD/screenshots/2.5-rejected-*.png`. The mocked field detail remains English on a zh-Hant page, so translated server validation detail and the rest of Create QA remain open.

Checkpoint 2026-11-19T01:28:11+08:00: SD-ORDER-UI-009/010 conversion Playwright checks a saved CREATE receipt after 201, then verifies a second conversion returns 409 with the selected quote retained and no success message. The server emits `SAL.QUOTATION.ALREADY_CONVERTED`; its three translations now match the actual code. SD-ORDER-UI-014 checks 400 field details and 500 trace/uncertain-result feedback. Create E2E 4/4 and frontend units 915/915 passed. Other states and visual QA remain open.

## WU-SD-003c3 Create form scenarios (2026-11-18T21:03:05+08:00)

Verification checkpoint 2026-11-19T01:09:59+08:00: SQL memory gate passed at 2.883/2.874/2.916 GiB. The authorised HTTP class passed 5/5 in Release after a new conversion assertion: a CREATE-only user reads their own converted order through `/created`, while ordinary detail remains 403. The page and Playwright cases remain pending.

Checkpoint 2026-11-19T01:17:25+08:00: Create route and manual form now use current-company MD pickers and read the server's CREATE-scoped receipt before displaying success. SD-ORDER-UI-008/009 Playwright manual path passes, including invalid form preventing POST and exact decimal tokens. Quotation selection is wired to approved quotations. Conversion, errors, three-language visual review, screenshots and remaining E cases are pending; Create stays IN_PROGRESS.

Traceability: REQ_F4112.2.2 → REQ_IO_A2.5.1–16 → SD-ORDER-UI-008–016. U = form/service unit, I = real SQL/authorised HTTP, E = Playwright screen with visible message and saved data assertion. One order command is atomic: partial success and skipped rows are inapplicable; no import file is offered.

| ID | Category | Scenario and expected result | Layer | Status |
|---|---|---|---|---|
| SD-ORDER-UI-008 | Normal / boundary | Manual Draft accepts leap day, 1–1000 lines, six-place quantity/price, four-place discount and emits unrounded JSON decimal number tokens; invalid customer/date/currency/remark/line count and values show field errors without POST. | U,E | Passed: manual exact decimals, first-error focus and 1000-line browser limit; invalid draft sends no POST |
| SD-ORDER-UI-009 | Normal / permission | CREATE permission alone opens Create; manual POST and approved quotation conversion POST use distinct routes; after 201, GET saved order and display its server number/lines. VIEW absence needs a safe saved-result path. | U,I,E | U/I passed; E manual and conversion receipts and CREATE-only browser route passed |
| SD-ORDER-UI-010 | Duplicate / conflict | Reusing a converted quotation returns 409, preserves source selection and never shows another success; no second order/number is saved. | I,E | I covered by SD-ORDER-010/035; E passed |
| SD-ORDER-UI-011 | Partial result | Single atomic manual/convert command has no partial success or skipped rows; zero-change response must not show success. | I,E | E mismatched-ID, empty-lines and wrong-source receipts passed; manual saved identity checked in U/E; atomic SQL covered by SD-ORDER-013 |
| SD-ORDER-UI-012 | Permission / scope | Anonymous 401, missing CREATE 403, other-company/hidden quote 404; MD picker only shows authorised current-company options. | I,E | Passed: CREATE-only and missing CREATE browser roles, hidden quote 404, company-switched MD picker; SQL/HTTP covers anonymous and company scope |
| SD-ORDER-UI-013 | State / related | Pending, void, expired or already converted quote cannot convert; missing/disabled customer, item, currency or tax shows named reason and no saved order. | I,E | Passed: browser invalid status/expired 422, already converted 409, hidden 404, inactive customer 422; SQL/HTTP covers remaining source states/references |
| SD-ORDER-UI-014 | Error | 400 fields, 409 conflict, 422 business refusal and 500 with traceId display translated detail; timeout/network leaves outcome uncertain and blocks repeat POST until saved lookup. | E | Passed: three-language 400/409/422/500 bright/dark screens, receipt-read 500 trace, timeout/network and CREATE-only uncertainty guidance |
| SD-ORDER-UI-015 | File | Import/upload empty, format, size, row count and encoding cases do not apply: Create has no file input. | — | N/A |
| SD-ORDER-UI-016 | Visual / data | Manual and conversion forms show normal, empty/no quotation, validation, rejection, no permission, success and server error in three languages and light/dark; visible number/line values match saved GET. | E | Passed: Create 24/24; prior 28 plus 24 new screenshots inspected; saved customer/date/currency/line or source quotation identity checked before success |

## WU-SD-003c2 approval-history isolation scenarios

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-ORDER-VIEW-HIST-001 | Normal | A visible order returns only its saved SYS approval rounds and steps, with the current instance ID. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-002 | Boundary | An order without submission returns empty history and null instance; leap-day business date does not change history. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-003 | Permission | Other-company and Branch or Self VIEW scope outside the saved order return 404 without approval details. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-004 | State | Draft and completed approval rounds are read as saved; GET does not mutate them. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-005 | Duplicate/conflict | Repeated GET has no write or version conflict; another company's matching document ID cannot join history. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-006 | Partial result | Not applicable: one read has no batch write or skipped rows. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-007 | Related | A hidden order's SYS history is not exposed through the SD route. | I | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-008 | Error | Existing View E2E covers 400, 404, 500, timeout and network loss; 409/422 do not apply to a read. | E | REQ_F4112.2.1 |
| SD-ORDER-VIEW-HIST-009 | File | Not applicable: this View does not upload or download files. | I | REQ_F4112.2.1 |

2026-11-18T20:50:15+08:00 checkpoint: a real SYS approval row for a visible order and a second company's same document ID were seeded in SQL. Authorised HTTP returned only the home Approved instance; foreign company, other Branch and other Self VIEW requests returned `SAL.ORDER.NOT_FOUND` without history fields. A separate real draft GET asserts null instance and empty history. Existing browser E2E covers saved round, step/comment, empty state and errors. Credit has only an offline decision rule and FI snapshot interface today; WU-SD-006a–006c must create, persist and show the decision later.

2026-11-18T20:57:43+08:00 final: focused authorised HTTP 5/5; Release solution build 0 warnings/errors and backend full 3632/3632. The prior release-only translation failure came from stale `--no-build` output; rebuilding the solution restored the current source/translation match. SD-ORDER-VIEW-HIST-001–003 and 007 have real SQL coverage, 004–005 are read assertions over saved rows, 006/009 are inapplicable, and 008 has existing View E2E coverage. WU-SD-003c2 is complete.

## WU-SD-003c2 order View checkpoint

Traceability: REQ_F4112.2.1 → REQ_IO_A2.4.1–7 → SD-ORDER-UI-001–009, SD-ORDER-036–037 and SD-ORDER-VIEW-HIST-001–009. Credit projection is owned by WU-SD-006a–006c after a saved decision contract exists.

| ID | Class | Scenario and applicability | Level | Requirement | Result |
|---|---|---|---|---|---|
| SD-ORDER-UI-001 | Normal / boundary | Client sends trimmed keyword, inclusive dates, named status, page/pageSize and sort; detail is a GET without mutation. | U | REQ_F4112.2.1 | 2/2 pass |
| SD-ORDER-UI-002 | Normal / status | List and saved line detail reflect API values; all labels render in three languages and both themes. | E | REQ_F4112.2.1 | Pass |
| SD-ORDER-UI-003 | Empty / error | Filtered empty differs from failed load; 500 shows trace ID and no stale rows. | E | REQ_F4112.2.1 | Pass |
| SD-ORDER-UI-004 | Permission | Missing VIEW blocks route and hides menu before API GET. Other-company and scope hiding remain covered by existing SQL tests. | E/I | REQ_F4112.2.1 | E pass; I prior |
| SD-ORDER-036 | Boundary / error | HTTP accepts `status=Draft`, rejects numeric status with 400; OpenAPI exposes the query. | U/I | REQ_F4112.2.1 | U 8/8 pass; I pass |
| SD-ORDER-UI-005 | Duplicate/conflict / partial / related / file | Read-only GET has no code creation, versioned write, batch, linked-record mutation or file; these scenarios are inapplicable. | — | REQ_F4112.2.1 | N/A |
| SD-ORDER-UI-006 | Validation / network | Impossible date prevents GET; plain empty list differs from filtered empty; detail 404 gives a localised reason; offline list clears stale data and retry restores saved data. | E | REQ_F4112.2.1 | Pass |
| SD-ORDER-UI-007 | Timeout / retry | A stalled list GET ends after six seconds with a localised timeout reason, clears stale rows, and retry restores saved rows without a write. | E | REQ_F4112.2.1 | Pass |
| SD-ORDER-UI-008 | Timeout / retry | A stalled detail GET ends after six seconds with a localised reason; retry reads the same saved order without reloading the list or writing data. | E | REQ_F4112.2.1 | Pass, View E2E 6/6 |
| SD-ORDER-UI-009 | Normal / empty / related | Detail shows server-saved approval round, step and comment; a draft without history shows an explicit empty state. No browser-side decision is invented. | E/I | REQ_F4112.2.1 | E pass; I real SYS history and scope pass |
| SD-ORDER-037 | Normal / boundary / status / permission | Real HTTP GET under VIEW scope shows posted 0.5, pending 0.25, remaining 1.25 of a saved quantity 2. Draft 0.75 does not reserve; a foreign-company GET is 404. Display labels distinguish posted from pending reservation. | I/E | REQ_F4112.2.1 | I red then pass; E red then pass |

Checkpoint 2026-11-18T20:35:02+08:00: SQL memory gate 2.693/2.688/2.589 GiB PASS; authorised HTTP 5/5. SD-ORDER-037 real SQL first failed because `deliveredQuantity` was absent and passed after the saved delivery projection. UI-002 first failed because the line balance was absent; revised View E2E 7/7 and light/dark screenshots passed. The existing order transaction fixture needed an empty SYS history reader after the prior constructor change, then a delivery EF model after the new balance query; the targeted test failed at the missing model and passed after correction. Final backend full 3632/3632, frontend 909/909, View E2E 7/7. A persisted credit decision and real SQL history isolation remain open.

Checkpoint 2026-11-18T20:05:32+08:00: UI-009 failed before the approval section existed and passed after the SYS history projection and page were added. Order View E2E 7/7, frontend units 909/909, Architecture 2171/2171 and Sales project Debug build passed. Light/dark approval screenshots were inspected. SQL gate 2.265/2.238/2.235 GiB deferred real HTTP/history isolation and backend full suite; full solution build was blocked by existing API/Visual Studio DLL locks. Delivery balance and saved credit decision remain open.

Checkpoint 2026-11-18T16:01:16+08:00: UI-008 first failed because detail timeout offered no retry control. A semantic button now retries the selected order ID; the visible reason and saved detail agree with the two GET responses. Six View E2E tests pass; the new light/dark detail timeout screenshots were inspected. The authorised HTTP regression exposed one old numeric-status assertion after ADR-140; it now expects `Draft`. Delivery balance, SYS approval history and credit result remain open.

Checkpoint 2026-11-18T15:52:18+08:00: UI-007 first failed because a stalled GET stayed in loading state; a six-second RxJS read timeout and three translations made it pass. Light/dark timeout screenshots were inspected. View E2E 5/5 and frontend 909/909 pass. SQL memory gate sampled 2.279/2.261/2.954 GiB, so authorised HTTP and full SQL remain deferred. Detail timeout, delivery balance, SYS approval history and credit result remain open; 003c2 is still in progress.

Checkpoint 2026-11-18T15:42:40+08:00: the new UI-006 E2E covers four further states in light/dark screenshots. Visual review found detail 404 rendering raw `errors.SAL.ORDER.NOT_FOUND`; a three-language translation and assertion fixed it, and revised screenshots were inspected. Sales order View E2E 4/4 passed. SQL memory gate measured 2.082/2.059/2.144 GiB and deferred authorised HTTP and full SQL tests. Saved delivery balance, approval history and credit projection, plus timeout acceptance, remain open; WU-SD-003c2 is still in progress.

Checkpoint 2026-11-18T15:29:35+08:00: the service test was red before the client existed and then 2/2 passed. Browser E2E 3/3 passed with 12 manually reviewed screenshots. The three-sample SQL memory gate gave 2.594/1.559/0.932 GiB and DEFER, so the changed authorised HTTP assertion and full SQL suite have no acceptance result. This is a partial checkpoint of 003c2; the next round completes remaining View projections and browser states before marking it DONE.

## WU-SD-003c1 order status JSON contract

| ID | Class | Scenario and applicability | Level | Requirement |
|---|---|---|---|---|
| SD-ORDER-035 | Normal / boundary / status | All five saved order states serialize as named JSON values; list/detail DTOs expose names, and numeric JSON state input fails. | U | REQ_F4112.2.1–3 |
| SD-ORDER-035-NA | Duplicate/conflict / partial result / permission / related / error / file | This pure response contract has no write, batch, permission decision, relation mutation, API request or file action. SD-ORDER-001–020 cover these classes for their respective flows. HTTP query/error checks remain for 003c2. | — | REQ_F4112.2.1–3 |

WU-SD-003c1 (2026-11-18T15:16:53+08:00): `SalesOrderStatusJsonTests` were red 6/6 with numeric serialization, then green 7/7 after ADR-140 implementation. DTO regression covers list/detail; creation, conversion and replace return the same detail DTO. Architecture full suite 2163/2163 and Debug build 0 warnings/errors. SQL gate sampled 1.398/1.417/1.332 GiB, below 2.5 GiB; SQL full suite deferred. HTTP query/OpenAPI validation remains for 003c2. No browser E2E is applicable until its screen exists.

## WU-SD-002b3 quotation draft form model (2026-11-04T16:01:17+08:00)

Traceability: REQ_F4112.1.2 → planned Create/Edit interfaces 2.2–2.3 → SD-QUOTE-FORM-014–022. This unit validates raw input only; no page, API mutation, saved data or screenshot is claimed.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-FORM-014 | Normal | A complete draft with leap-day business date, 1 valid line and exact six-place decimal text has no issues and leaves input unchanged. | U | REQ_F4112.1.2 |
| SD-QUOTE-FORM-015 | Boundary | Missing customer, impossible/reversed date, remark over 500 characters, zero or 1001 lines return field-specific issues. | U | REQ_F4112.1.2 |
| SD-QUOTE-FORM-016 | Boundary, related | Missing item/output-tax IDs, nonpositive or over-limit quantity, negative/over-limit price, discount over 100 or excess precision, and malformed currency return line-specific issues. Server MD status checks remain SD-QUOTE-016. | U,I | REQ_F4112.1.2 |
| SD-QUOTE-FORM-017 | Duplicate, conflict | Duplicate line numbers are flagged. Duplicate quotation number is inapplicable because SYS assigns it; concurrent edits and 409 remain SD-QUOTE-010/018. | U,I,E | REQ_F4112.1.2 |
| SD-QUOTE-FORM-018 | Partial result | Inapplicable: one quotation save is atomic, with no batch, skip or partial success. | N/A | REQ_F4112.1.2 |
| SD-QUOTE-FORM-019 | Permission, company | Inapplicable to this pure model: no request or rendered action exists. Later Create/Edit UI must hide writes without exact permission and use current-company MD choices; server scope remains SD-QUOTE-014. | I,E | REQ_F4112.1.2 |
| SD-QUOTE-FORM-020 | State, related | Inapplicable to an unsaved draft model: Draft/Rejected edit eligibility and disabled/deleted MD references remain SD-QUOTE-015/016 server and later UI gates. | I,E | REQ_F4112.1.2 |
| SD-QUOTE-FORM-021 | Error | Inapplicable locally: 400/409/422/500/timeout are API outcomes for the later save UI, covered by SD-QUOTE-018. | E | REQ_F4112.1.2 |
| SD-QUOTE-FORM-022 | File | Inapplicable: quotation draft input is JSON and has no upload action. | N/A | REQ_F4112.1.2 |

TDD: focused tests first failed because the model did not exist, then passed 6/6 after the boundary supplement. One intermediate assertion incorrectly treated a valid expiry date as invalid when the business date was impossible; the test was corrected to an impossible expiry and a separate reversed-date case. Frontend full suite passed 744/744 and production build passed. No product behavior was weakened.

## WU-SD-002b2 read-only regression (2026-11-04T15:41:08+08:00)

Traceability: REQ_F4112.1.1 → REQ_IO_A2.1.1–5 → SD-QUOTE-UI-009–012. These are simulated GET-only Playwright checks. No SQL or write acceptance is claimed.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-UI-009 | Permission, company, conflict | Switch company while an old-company GET is pending; old rows and detail disappear, the new company shows its own empty result, and a late old response cannot restore rows. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-010 | Normal, boundary | A URL with page 3, pageSize 50 and Approved status sends those values to GET and shows page 3 selected. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-011 | Boundary, error | An impossible calendar date in a deep link shows validation and sends no GET. Reversed dates remain covered by UI-002. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-012 | Layout, state | At 768px the page has no outer horizontal overflow in light/dark mode; the saved row remains visible and its wide table can scroll internally. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-013 | Duplicate, partial, related, file | Not applicable: this slice only reads saved quotations; duplicate writes, version 409, batch partial/all-skipped/all-failed and file errors require mutation, batch or file routes. | N/A | REQ_F4112.1.1 |

Red/green: UI-011 failed because `Date.parse` accepted February 30; UI-012 failed with 34px outer overflow at 768px. UI-009/010 passed initially. After strict calendar validation, internal table scroll and a narrower tablet company selector, quotation E2E 11/11 and frontend unit tests 738/738 passed. Screenshots: `tests/qa/SD/screenshots/2.1-company-switch-{light,dark}-zh-Hant.png` and `2.1-tablet-{light,dark}-zh-Hant.png`; all four were inspected. The existing UI-001–008 cases cover normal, empty, no permission, 404/500/network, sorting and three-language light/dark states.

WU-SD-002a closeout (2026-11-04T09:35:51+08:00): the SQL memory gate passed (about 9.2 GiB free in three samples). Debug build had 0 warnings and 0 errors. `dotnet test --solution src/backend/MyERP.sln --no-build --filter-method "*Quotation*" --max-threads 1` passed 95/95 across Integration and Architecture tests. The full backend suite with `--max-threads 1` passed 1337/1337: DocGen, Shared, Architecture and Integration (6m46s), with no failures or skips. `Sales_QuotationDraft` is listed in the applied migrations. This supplies the complete-suite evidence that was missing for SD-QUOTE-001–038, which are covered in the checkpoints below. WU-SD-002a is DONE. Quotation write UI (WU-SD-002b) and conversion (WU-SD-003a) remain separate units.

WU-SD-002a verification checkpoint (2026-10-29T12:50:02+08:00): `dotnet test --project tests/backend.integration/MyERP.IntegrationTests/MyERP.IntegrationTests.csproj --no-build --max-threads 1 --filter-class '*SalesQuotation*'` passed 29/29; the same command with `--filter-method '*QuotationUnexpectedFailure*'` passed 2/2. The whole-solution run passed Shared, Architecture and DocGen, but the integration project produced no success summary before host free memory fell from 1.44 GB to 0.70 GB and the run was interrupted with exit code 1. The full backend gate remains open, so WU-SD-002a stays in progress. Retry the complete solution only after free memory is stable at or above 2.5 GB, without closing user applications.

WU-SD-002a SYS HTTP decision checkpoint (2026-10-29T06:47:33+08:00): `SalesQuotationHttpTests` restores the real MD customer after a rejected submit, submits the saved version successfully, checks Pending plus one durable SYS instance, and verifies a repeat stale submit returns 409. A separate live SYS approver calls `POST /api/v1/sys/approvals/{id}/approve`; SQL confirms the same quotation becomes Approved with its instance ID and approval time, and SYS history lists one instance. HTTP 4/4 and real-SQL quotation suite 22/22 pass. This adds HTTP evidence for SD-QUOTE-021, 023, 030, 032 and 036. VOID, derived `isExpired`, quotation detail history and the full backend suite remain open; WU-SD-002a is not closed.

WU-SD-002a authorised create/edit HTTP checkpoint (2026-10-29T06:37:05+08:00): `SalesQuotationHttpTests` uses a live SYS writer and real MD customer, item, currency and output tax. POST saves a Draft with a permanent SYS number and gross 21; PUT replaces its saved line and gross with version 2 and 42; stale PUT returns 409 without changing SQL. An empty-line POST returns field-specific 400, and POST submit after the customer becomes Rejected returns `MD.CUSTOMER.NOT_APPROVED` 422 while the quote stays Draft with no approval instance. HTTP 4/4 and real-SQL quotation suite 22/22 pass. This adds HTTP evidence for SD-QUOTE-030–033, 036–037. Successful authorised submit, SYS HTTP final decision, VOID, history and derived expiry remain open; WU-SD-002a is not closed.

WU-SD-002a DELETE HTTP checkpoint (2026-10-29T06:30:18+08:00): a live SYS role with only VIEW and DELETE sends a stale version (409 `COMMON.CONCURRENCY.CONFLICT`), the current version (204), then repeats deletion (404). Real SQL confirms soft deletion retains the original number and owner. `SalesQuotationHttpTests` passes 3/3. This adds HTTP evidence for SD-QUOTE-030/032/034; authorised CREATE/EDIT/SUBMIT, invalid inputs, final SYS HTTP decision, VOID and UI remain pending. The command is atomic and has no file boundary, as already recorded under SD-QUOTE-033/038.

WU-SD-002a HTTP checkpoint (2026-10-29T06:23:01+08:00): `SalesQuotationHttpTests` first returned 404 for missing quotation routes, then passed 2/2 against real SQL. Six anonymous paths return 401 and appear in OpenAPI. A real SYS VIEW-only role reads its own-company saved quote, receives 404 and an empty list for another company, and gets 403 with `COMMON.PERMISSION.DENIED` for CREATE, EDIT, DELETE and SUBMIT. These are HTTP checks for REQ_F4112.1.1–1.3 and SD-QUOTE-001/014/025. The following HTTP scenarios remain for this same WU:

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-030 | Normal | Authorised create, detail/list, edit, delete and submit use saved MD/SYS data; status, version and line snapshots match SQL. | I | REQ_F4112.1.1–1.3 |
| SD-QUOTE-031 | Boundary | Empty/oversized lines, invalid dates, quantity precision and invalid paging return field-specific 400; expiry today can submit. | I | REQ_F4112.1.1–1.3 |
| SD-QUOTE-032 | Duplicate/conflict | Stale edit/delete/submit returns 409; concurrent same-version commands leave one persisted winner. | I | REQ_F4112.1.2–1.3 |
| SD-QUOTE-033 | Partial result | Single-document writes are atomic; MD/SYS failure leaves no partial quote, number or approval. Batch partial/all-skipped is not applicable because no batch route exists. | I | REQ_F4112.1.2–1.3 |
| SD-QUOTE-034 | Permission | Anonymous 401, missing action 403, foreign company and hidden action-scope ID 404; independent permissions for each route. | I | REQ_F4112.1.1–1.3 |
| SD-QUOTE-035 | State | Pending/Approved cannot edit or delete; only Draft submits; Rejected revision returns Draft; void only Approved without order. | I | REQ_F4112.1.2–1.3 |
| SD-QUOTE-036 | Related | Submit rechecks saved MD references; SYS final decision and history agree with SD; converted/voided source rules hold. | I | REQ_F4112.1.3 |
| SD-QUOTE-037 | Error | Bad request 400, stale 409, state/reference 422 and unexpected 500 use ProblemDetails; future UI shows localised error and traceId for 500/timeout. | I,E | REQ_F4112.1.1–1.3 |
| SD-QUOTE-038 | File | Not applicable: quotation HTTP accepts JSON only and has no file operation. | I | REQ_F4112.1.1–1.3 |

WU-SD-002a live-SYS checkpoint (2026-10-29T06:16:31+08:00): `SalesQuotationActionScopeTests.LiveSysSubmissionAndFinalDecisionShareQuotationStateAndKeepHistory` uses actual SYS users, company membership, a configured approval flow and the SD result handler over real SQL. Submission saves Pending in both SD and SYS; a final SYS rejection changes the same saved quotation to Rejected and retains the instance history. Two independent connections submitting one saved version yield one Pending and one state refusal with exactly one instance. Disabling the flow yields a zero-step SYS instance and immediate SD Approved in the submission transaction. This adds live evidence for SD-QUOTE-003/004/011/021/023. Quotation SQL 18/18, Architecture 230/230 and Debug build 0 warnings/errors pass. Authorised quotation HTTP/Swagger, SYS HTTP decision, VOID, UI and the complete backend suite remain open; partial batch and file cases remain inapplicable under SD-QUOTE-013/019.

WU-SD-002a real-MD submission checkpoint (2026-10-29T06:08:59+08:00): the real-SQL quotation fixture now changes a saved draft's actual customer to Rejected and deactivates its item, currency and output tax code, one at a time. Each submit fails before SYS is called and leaves the saved quotation Draft at version 1 without an approval instance; after restoring MD rows, submit succeeds as Pending. This verifies SD-QUOTE-024/027 against live MD rows. The focused case passes 1/1 and quotation integration passes 17/17. Live SYS history, two-connection submit race, authorised HTTP/Swagger and VOID remain open. Batch partial and file cases remain inapplicable under SD-QUOTE-013/019.

WU-SD-002a submit checkpoint (2026-10-29T05:50:51+08:00): the new service test was red at missing `SalesQuotationSubmissionService`, then real SQL `SalesQuotationActionScopeTests` passed 9/9 and all quotation integration tests passed 17/17 after a company-wide fixture assertion was narrowed to its unique number prefix. Mocked MD contracts and SYS workflow over a real saved quotation verify Pending and zero-step Approved, amount/document identity, 200-character SYS summary, SUBMIT permission, stale version, invalid customer, SYS exception rollback and repeated-submit state refusal. This covers parts of SD-QUOTE-021/023–027; real MD state changes, live SYS approval history and HTTP/UI error handling remain open. The cases below remain the acceptance plan, not all completed results.

WU-SD-002a submit scenarios (2026-10-29T05:46:06+08:00), REQ_F4112.1.3 / BR-SD-011,014:

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-021 | Normal | Draft submits through SYS in the same transaction; Pending saves instance and time, zero-step returns Approved with approval time; prior rounds remain in SYS. | I | REQ_F4112.1.3 |
| SD-QUOTE-022 | Boundary | Expiry today is accepted; the previous day is rejected; an empty saved line set is rejected before SYS. | I | REQ_F4112.1.3 |
| SD-QUOTE-023 | Duplicate/conflict | A stale version, concurrent same-version submits or repeat Pending submit cannot create a second active approval round. | I | REQ_F4112.1.3 |
| SD-QUOTE-024 | Partial result | SYS or MD failure rolls back the entire single-document submit; batch partial/all-skipped cases do not apply because there is no batch operation. | I | REQ_F4112.1.3 |
| SD-QUOTE-025 | Permission | Missing SUBMIT permission returns 403; other-company and action-scope-excluded IDs return 404. | I | REQ_F4112.1.3 |
| SD-QUOTE-026 | State | Only Draft can submit; Rejected must be revised first; Pending, Approved, Voided and deleted cannot submit. | I | REQ_F4112.1.3 |
| SD-QUOTE-027 | Related | Recheck approved customer and active item, currency and effective output tax from MD contracts; reject invalid references without changing saved commercial snapshots. | I | REQ_F4112.1.3 |
| SD-QUOTE-028 | Error | Invalid input 400, stale version 409 and business state 422 have named errors; 500, timeout and network UI handling waits for the quotation HTTP/UI slice. | I,E | REQ_F4112.1.3 |
| SD-QUOTE-029 | File | Not applicable: quotation submission has no file input. | U | REQ_F4112.1.3 |

WU-SD-002a approval-handler checkpoint (2026-10-29T02:10:41+08:00): `SalesQuotationApprovalResultTests` first failed to compile without the handler and then passed real SQL 2/2. It covers the final-decision portion of SD-QUOTE-003/012/015: Approved and Rejected update the saved Pending quote only in the caller-owned transaction; rollback leaves Pending; a mismatched approval instance and repeated decision return 409; only approval records `approved_at`. The handler is registered for SYS dispatch. Submission, live MD revalidation, zero-step approval, SYS HTTP decision, authorised quotation HTTP and VOID remain pending. SD-QUOTE-013/019 remain inapplicable because this is one atomic document and has no file input.

WU-SD-002a list/race checkpoint (2026-10-29T01:59:27+08:00): `SalesQuotationActionScopeTests` adds real SQL I coverage for SD-QUOTE-010/014 with two independent connections and the same document version: exactly one EDIT succeeds and the other returns 409; exactly one DELETE succeeds and the other returns 404, retaining the permanent number. The list test was red at the absent query method, then green after scoped count, filters and stable paging. It covers SD-QUOTE-001/014/020 server-side pieces: owner versus company visibility, stable two-page count, trimmed keyword, inclusive date, status/customer filters, invalid page/date/customer/status/sort, and missing VIEW 403. Focused quotation action tests 6/6 and full backend 1304/1304 pass; Debug build has 0 warnings/errors. Authorised HTTP, SYS approval and E2E remain pending. Partial-result and file categories remain inapplicable per SD-QUOTE-013/019.

WU-SD-002a EDIT checkpoint (2026-10-29T01:48:11+08:00): `SalesQuotationActionScopeTests.EditUsesItsOwnScopeAndRebuildsSnapshotWithoutChangingNumberOrOwner` was red at the absent command and now passes against real SQL. It checks SD-QUOTE-002/010/014/015: missing EDIT permission 403, other-owner Self scope 404, stale version 409, full line/amount/MD snapshot replacement, retained document number and owner, and Rejected revision returning to Draft. Focused real SQL 3/3; final full backend 1301/1301; Debug build 0 warnings/errors. Two-connection races, list, SYS approval, authorised HTTP and E2E remain pending; SD-QUOTE-013/019 remain inapplicable.

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
| SD-ORDER-022 | Normal, boundary, conflict, state, related | Pure conversion preparation copies all saved header/line commercial values and source IDs without repricing; rejects absent/duplicate/mismatched saved lines, deleted/foreign, expired, already converted or non-Approved quote. | U | REQ_F4112.2.2 |
| SD-ORDER-023 | Normal, boundary, conflict, state | Create-store source guard accepts a manual Draft with no source IDs or a converted Draft with positive, unique saved quote/line IDs; rejects mixed/missing/zero/repeated IDs, deleted or non-Draft input before persistence. | U | REQ_F4112.2.2 |
| SD-ORDER-024 | Normal, boundary, related | Conversion accepts six matching saved header sums (original/base net, tax, gross) and rejects each mismatched or overflowing sum before issuing an order snapshot; no repricing or source mutation. | U | REQ_F4112.2.2 |
| SD-ORDER-025 | Normal, boundary, related | Conversion requires each saved line's original and base gross to equal its own net plus tax, even when all header totals still match the saved line sums; overflow rejects the source. | U | REQ_F4112.2.2 |
| SD-ORDER-026 | Normal, boundary, related | Conversion copies a valid saved business identity and rejects a saved source with invalid customer/date/currency/rate or line item/quantity/price/discount/tax identity, while leaving the original quote untouched. | U | REQ_F4112.2.2 |
| SD-ORDER-027 | Normal, boundary, related | Conversion accepts valid saved amounts and rejects each of six line monetary components when negative, outside DECIMAL(19,4), or above four decimal places, even with matching line arithmetic and header sums; reject invalid header money by the same rule. | U | REQ_F4112.2.2 |
| SD-ORDER-028 | Normal, boundary, related | Conversion retains valid saved customer/item names and remark; rejects missing or overlength customer/item code or name, overlength remark and missing expiry date before building an order. An already expired quotation keeps its named expiry error. | U | REQ_F4112.2.2 |
| SD-ORDER-029 | Boundary, state | A saved Approved quote with expiry before its business date is incomplete even when expiry includes today; reject before allocating an order. An actually expired quote retains the named expiry error. | U | REQ_F4112.2.2 |
| SD-ORDER-030 | Normal, boundary, related | Accept the maximum saved DECIMAL(19,8) exchange rate, reject a twelve-integral-digit rate or more than eight fractional places before copying the quotation to an order; do not reprice saved amounts. | U | REQ_F4112.2.2 |
| SD-ORDER-031 | Normal, boundary | Accept a generated order number of 30 characters; reject 31 characters before building the converted Draft, leaving the saved quotation unchanged. SQL transaction rollback remains SD-ORDER-012. | U,I | REQ_F4112.2.2 |
| SD-ORDER-032 | Conflict, concurrency | Serializable line writes of one order do not take locks on unrelated order headers: with X held on another company's order header, a Draft line replacement completes under a 2 s lock timeout (no 1222/1205); concurrent HTTP edits of different orders never return 500. | I | REQ_F4112.2.3 |
| SD-ORDER-033 | Conflict, concurrency | Two connections convert the same Approved quotation at once: exactly one Draft order, the other gets 409 `COMMON.CONCURRENCY.CONFLICT`, and the SYS sequence advances once (the loser's number is rolled back). | I | REQ_F4112.2.2 |
| SD-ORDER-034 | State, conflict | Soft-deleting the converted Draft does not release its quotation: converting again returns 409, the deleted order keeps the source link and number, and no number is used up. A 31-character generated number fails after allocation and rolls the sequence back to 0, and a retry gets the first number (SD-ORDER-031 SQL half). | I | REQ_F4112.2.2 |
| SD-ORDER-035 | Permission, normal, conflict, error | `POST /api/v1/sal/orders/from-quotation/{id}`: 401 unauthenticated, 403 without SAL.ORDER.CREATE, 404 `SAL.DOCUMENT.NOT_FOUND` for an unknown quotation, 201 Draft with the saved snapshot and the first number, then 409 on a repeat; the route appears in OpenAPI. | I | REQ_F4112.2.2 |

WU-SD-003a8 scenario scope (2026-11-05T06:03:00+08:00): SD-ORDER-029 covers the valid business/expiry boundary and invalid future-business-date source (U); the new case failed before the guard and passes after it. The existing expired-source test caught an error-code precedence regression, fixed by running lifecycle eligibility before date-order integrity. Duplicate/concurrent conversion, rollback, permission, related records and HTTP 400/409/422/500/timeout remain SD-ORDER-010/012/014–020 (I/E). Partial/all-skipped batch and file inputs do not apply to one atomic conversion (SD-ORDER-013/019). Real SQL and HTTP acceptance remain WU-SD-003a.

WU-SD-003a9 scenario scope (2026-11-05T06:08:42+08:00): SD-ORDER-030 covers the accepted upper bound, rejected integral overflow and rejected scale overflow (U). The twelve-integral-digit case was red before the guard fix, then Architecture 301/301 passed. Duplicate/concurrent conversion, rollback, permission, status, related records and HTTP errors remain SD-ORDER-010/012/014–020 (I/E). Partial/all-skipped batch and file inputs do not apply to one atomic conversion (SD-ORDER-013/019). Real SQL and HTTP acceptance remain WU-SD-003a.

WU-SD-003a10 scenario scope (2026-11-05T06:14:13+08:00): SD-ORDER-031 covers the normal 30-character number and rejected 31-character boundary (U). The 31-character case failed before the guard and passes after it. Duplicate/concurrent conversion, source state, permission, related records, HTTP 400/409/422/500/timeout and transactional number rollback remain SD-ORDER-010/012/014–020 (I/E). Partial/all-skipped batch and file input do not apply to one atomic conversion (SD-ORDER-013/019). Real SQL and HTTP acceptance remain WU-SD-003a.

WU-SD-003a7 scenario scope (2026-11-05T02:10:38+08:00): SD-ORDER-028 covers the valid copied text and seven invalid text/date boundaries (U). Duplicate/concurrent conversion and rollback remain SD-ORDER-010/012 (I/E); permission, status, linked records, HTTP errors and visible messages remain SD-ORDER-014–020 (I/E). One atomic conversion has no partial batch or file input (SD-ORDER-013/019). Six new cases first failed, while the whitespace case already passed; this offline guard does not establish SQL locks or HTTP conversion.

WU-SD-003a conversion checkpoint (2026-11-05T05:55:11+08:00): `ApprovedQuotationConvertsOnceAndKeepsSavedSnapshot` is the first real-SQL case for SD-ORDER-003/010/012: an Approved saved quote retains its commercial values and source line ID in one order, a repeat returns 409, and the losing attempt does not advance the SYS sequence. The test was written before the service (red compile: missing `SalesOrderConversionService`), and the solution now builds. Its first execution failed before the test body: `TestDatabase.InitializeAsync` timed out during migration and could not release `__EFMigrationsLock`; free memory fell from 2.5 GiB at gate to 1.25 GiB during setup and then below 1 GiB. SQL, migration, concurrent two-connection competition, authorised HTTP, full backend and Playwright evidence remain pending. Existing SD-ORDER-006–020 retain their normal/boundary/conflict/partial/permission/state/related/error/file mappings; batch partial success and upload are inapplicable to this atomic endpoint.

WU-SD-003a6 scenario scope (2026-11-05T02:03:58+08:00): SD-ORDER-027 covers normal and 18 boundary cases at unit level. Duplicate conversion/conflict, transaction rollback, company/role scope, status, linked records and HTTP 400/409/422/500/timeout remain SD-ORDER-010/012/014–020 (I/E) for WU-SD-003a. Partial/all-skipped batch and file inputs are inapplicable to this single-document pure guard (SD-ORDER-013/019). Red: 18/18 new cases failed because invalid amounts were copied; green: snapshot suite 46/46. No SQL or HTTP acceptance is claimed.

WU-SD-003a5 scenario scope: normal saved identity and numeric boundaries use SD-ORDER-026 (U). Duplicate conversion and source conflict remain SD-ORDER-010 (I/E); no partial batch or file input exists (SD-ORDER-013/019). Permission, status, linked MD records and HTTP 400/409/422/500/timeout remain SD-ORDER-014–018 (I/E). This pure guard never re-reads current MD data or reprices a historical quote.

WU-SD-003a5 result (2026-11-05T01:58:52+08:00): eleven invalid source examples first failed 11/11, then passed 11/11; full snapshot suite passed 28/28. The valid source and nonmutating assertions also pass. Real SQL and HTTP conversion are still pending WU-SD-003a.

WU-SD-003a4 scenario scope: SD-ORDER-025 covers a valid saved line, inconsistent original or base gross hidden by matching header sums, and checked arithmetic overflow. Duplicate conversion, whole-transaction rollback, permissions, state and API/UI errors remain SD-ORDER-010/012–020 for SQL and HTTP work. Partial/all-skipped batches and files do not apply to this pure whole-document guard (SD-ORDER-013/019).

WU-SD-003a4 result (2026-11-05T01:50:48+08:00): two original/base assertions first failed, then the focused snapshot suite passed 17/17. Existing valid and overflow cases also passed. No real SQL conversion or HTTP acceptance is claimed.

WU-SD-003a3 scenario scope: SD-ORDER-024 covers normal exact sums, each of six mismatch boundaries and overflow. Existing SD-ORDER-010/012–020 cover duplicate conversion, transaction rollback, permission, status, API errors and UI. Partial/all-skipped batches and files do not apply to this pure whole-document guard (SD-ORDER-013/019); there is no API or user context in this slice.

WU-SD-003a3 result (2026-11-05T01:44:41+08:00): SD-ORDER-024 first ran red (7/15 failures: six mismatched saved sums and overflow), then green (15/15). The existing valid two-line snapshot proves exact saved sums pass. Source SQL consistency and authorised conversion HTTP remain in WU-SD-003a.

WU-SD-003a2 checkpoint (2026-11-05T01:36:37+08:00): SD-ORDER-023 first failed compilation because the guard was absent, then passed 8/8. The pure guard covers normal manual and converted shapes, missing/zero/mixed/repeated IDs and invalid Draft state. Existing SD-ORDER-010/012–020 retain real SQL source uniqueness, whole-transaction rollback, permission, API error and UI evidence; one atomic order has no partial batch result and this slice has no file input. The store now accepts prepared converted snapshots, but no conversion transaction or HTTP endpoint is claimed.

WU-SD-003a1 checkpoint (2026-11-05T01:27:55+08:00): SD-ORDER-022 was red before the pure builder existed and passes 8/8 after implementation and review. Normal copying, 0/mismatched line counts, duplicate IDs/numbers, wrong parent/company, reversed load order, expiry, deletion, cross-company, converted and Voided source are covered. Partial batch, permissions, HTTP errors, file and E2E scenarios remain in SD-ORDER-010/012–020 for later transaction/API/UI slices; this pure builder has no user identity, API or file input. Its caller must provide an independent SQL count under the source lock; no real SQL or HTTP conversion has been claimed.

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

### WU-SD-004b1 Delivery posting through INV at final approval / zero-step submit (2026-11-20, ADR-144)

Real-SQL tests in `SalesDeliveryCreateTransactionTests.Posting.cs` (2 new) and `.Submission.cs` (2 updated from the ADR-139 `POSTING_UNAVAILABLE` expectation to BR-SD-024 posting). INV is a recording fake implementing the public `IExternalInventoryPostingService` contract, except in the real-INV case, which composes `ExternalInventoryPostingService` over real MD items and stock. The tests were written together with the shared `SalesDeliveryPosting` step; the old 422 assertions were the red baseline being replaced. Movement-ID storage on the SD side and HTTP decision acceptance remain in WU-SD-004b2.

| Case | Category | Assertion | Layer | Result |
|---|---|---|---|---|
| SD-DEL-003 (posting part) | Normal | Real SYS flow: final approval calls INV once with source = delivery ID, saved business date and saved lines (line no, item, warehouse, location, quantity; no price or cost) → Posted, `PostedAt` = clock, SYS Approved; Rejected sibling untouched. | I | pass |
| SD-DEL-005 (zero-step part) | Normal | No active flow: submit posts in the same transaction → Posted version 2, SYS Approved instance linked; Posted then refuses submit and withdraw with 422 `INVALID_STATUS` and no second INV call. | I | pass |
| SD-DEL-007 (final part) | Boundary | Final recheck excludes only the current Pending: a Posted 2.000001 added after submit leaves 2.999999 < 3 → 422 `ORDER_BALANCE_EXCEEDED`, INV never called. | I | pass |
| SD-DEL-010 (replay part) | Conflict | INV reports a replay of an existing posting for the delivery → 409 `SAL.DELIVERY.POSTING_CONFLICT` at both final approval and zero-step submit; nothing saved. | I | pass |
| SD-DEL-011 (posting part) | Partial result | INV refusal (`INV.STOCK.INSUFFICIENT`) at final approval keeps SYS Pending and the delivery Pending version 2; at zero-step submit it leaves Draft version 1 with no SYS instance. Real INV: 3.000001 against stock 3 is refused with no movement; a two-line delivery of 1 + 2 posts two Out movements and stock nets to 0. | I | pass |
| SD-DEL-014 (final part) | State | Source order Cancelled while the delivery is Pending → final approval 422 `ORDER_NOT_APPROVED` without INV; rejection still succeeds and releases the reservation without INV. | I | pass |
| SD-DEL-016 (price part) | Related | `SalesOutboundLine` carries no unit cost or sales price; the real INV Out movements take their cost from INV. | I | pass |

### WU-SD-004a2b13 Delivery submit / withdraw / SYS decisions (2026-11-16T06:02:04+08:00)

Four real-SQL tests in `SalesDeliveryCreateTransactionTests.Submission.cs` (ADR-139), one HTTP test in `SalesDeliveryAuthorizedHttpTests`, and lifecycle unit cases for `Withdraw`. The first run of the service tests failed (the submit advanced the version twice because SYS saved the tracked header); after the single-save fix all 21 delivery integration tests pass.

| Case | Category | Assertion | Layer | Result |
|---|---|---|---|---|
| SD-DEL-003 (submit part) | Normal | Real SYS flow with one approver: Draft (line A 1 + line B 1.5) → Pending, version 2, `ApprovalInstanceId` = SYS instance; one MD check per distinct reference. Posting part stays 004b. | I | pass |
| SD-DEL-004 (reject part) | Normal | Approver rejects → delivery Rejected with its SYS link; the released balance lets another Draft submit. | I | pass |
| SD-DEL-005 (zero-step part) | State | Superseded by WU-SD-004b1 (ADR-144): ~~No active flow → 422 `POSTING_UNAVAILABLE`, delivery stays Draft version 1 and SYS has no instance (ADR-139 until 004b). Final approval → 422 `POSTING_UNAVAILABLE`, SYS instance and delivery stay Pending.~~ | I | pass |
| SD-DEL-007 (submit part) | Boundary | Pending 4 + submitted 1 on line ordered 5: another Draft of 0.000001 → 422 `ORDER_BALANCE_EXCEEDED`, no SYS instance; after withdrawal it submits. | I | pass |
| SD-DEL-009 (submit part) | Concurrency | Winner holds its uncommitted Pending save: submits in another company and for another order finish within a 2 s lock timeout; the same-order contender waits (still running after 1.5 s) and then gets `ORDER_BALANCE_EXCEEDED`. When the winner fails after its save, the waiting contender takes the full balance and the failed delivery stays Draft version 1 without SYS link. | I | pass |
| SD-DEL-010 (submit part) | Conflict | Stale submit (version 2) and stale withdraw (version 1) → 409; repeated submit of Pending and withdraw of Draft/withdrawn → 422 `INVALID_STATUS`. | I, HTTP | pass |
| SD-DEL-011 (submit part) | Partial result | Tracked item at submit → `TRACKING_UNSUPPORTED`, version 1; every refused submit leaves the delivery and other-company rows unchanged. | I | pass |
| SD-DEL-013 (submit part) | Permission | EDIT only → 403; Self scope for user 8 (order owner 7) → 404; other-company and missing delivery → 404; HTTP: CREATE-only user → 403 on submit. | I, HTTP | pass |
| SD-DEL-014 (submit part) | State | Cancelled order → submit 422 `ORDER_NOT_APPROVED`, but a Pending delivery of that order can still be withdrawn. | I | pass |
| SD-DEL-HTTP-005 | HTTP | 401 on submit/withdraw; OpenAPI documents both with 422. SUBMIT-only user: 409 stale, 404 unknown, 200 Pending version 2, 422 `INVALID_STATUS` on repeat, 422 `ORDER_BALANCE_EXCEEDED` for the micro-unit Draft, withdraw 200 Draft version 3, then the other Draft submits; viewer reads it as Pending. | HTTP | pass |

Not applicable: duplicate codes (no code input), file import, batch partial success. Not covered: INV posting on final/zero-step approval (004b), lot/serial selections (004a2b14), UI (004c).

### WU-SD-004a2b12 Authorised delivery HTTP (2026-11-16T05:39:47+08:00)

`SalesDeliveryAuthorizedHttpTests` (real API, SYS login/roles/data scope, MD UOM/item/warehouse services, real SQL). Before the controller existed all 4 failed (404 routes); after implementation 4/4 pass.

| ID | Category | Assertion | Layer | REQ_F | Result |
|---|---|---|---|---|---|
| SD-DEL-HTTP-001 | Permission/doc | Anonymous GET list/detail, POST, PUT, DELETE → 401; OpenAPI has `get`/`post` on the collection (POST documents 422) and `get`/`put`/`delete` on `{id}` (PUT documents 409). | I | REQ_F4112.3.1, REQ_F4112.3.2, REQ_F4112.3.3, REQ_F4112.3.6 | pass |
| SD-DEL-HTTP-002 | Normal/permission/conflict | Writer with CREATE/EDIT/DELETE only: POST 201 + Location, number DH001, order number, customer, order-line number, item code, exact quantity; GET with that writer 403. Viewer: list filtered by orderId+status+keyword+sort → 1 row with lineCount/totalQuantity; status=Posted → 0; bad sort / from>to → 400; POST/PUT/DELETE → 403 `COMMON.PERMISSION.DENIED`. PUT version 1 → 200 version 2 with 2 lines (0.000001 exact); repeat → 409; DELETE version 1 → 409, version 2 → 204, again → 404; detail and list then 404/0; SQL row is soft-deleted with the edited date and lines. | I | REQ_F4112.3.1, REQ_F4112.3.2, REQ_F4112.3.3, REQ_F4112.3.6 | pass |
| SD-DEL-HTTP-003 | Boundary/state/related/error | Empty lines → 400 `errors.lines`; 7-decimal quantity → 400 `errors["lines[0].quantity"]`; unknown order → 404 `SAL.ORDER.NOT_FOUND`; Draft order → 422 `ORDER_NOT_APPROVED`; line of another order → 422 `ORDER_LINE_INVALID` with `lineNo` 7 and `traceId`; 5.000001 on ordered 5 → 422 `ORDER_BALANCE_EXCEEDED`; unknown warehouse → 404 `MD.WAREHOUSE.NOT_FOUND`; afterwards no delivery row and no number sequence. | I | REQ_F4112.3.2 | pass |
| SD-DEL-HTTP-004 | Permission/scope | VIEW with Self scope hides a delivery whose order belongs to another owner (detail 404 `SAL.DELIVERY.NOT_FOUND`, list 0); a viewer of another company gets 404 and an empty list. | I | REQ_F4112.3.1 | pass |

Not applicable here: partial success/all skipped (one atomic command, no batch), file import (none). Concurrency and lock order were proven at service level in 004a2b10/11; this slice adds the HTTP mapping only. Submit, tracking, INV posting and UI/E2E remain 004a2b13/14, 004b, 004c.

### WU-SD-004a2b11 Draft edit and soft delete on real SQL (2026-11-16T05:21:16+08:00)

Five tests added to `SalesDeliveryCreateTransactionTests` (partial file `SalesDeliveryCreateTransactionTests.DraftCommands.cs`, real SQL, ADR-138). Executed parts of the SD-DEL list for edit/delete only:

| Case | Category | Assertion | Result |
|---|---|---|---|
| SD-DEL-002 | Normal | Draft with 2 lines edited to new line numbers 5/6, new date, another warehouse → version 2, same number, every old line ID gone, item from order line, one MD check per distinct reference. Rejected with SYS instance 55 → Draft, version 2, instance kept. | I pass |
| SD-DEL-005 (delete part) | Normal | Draft delete → `is_deleted`, `deleted_at` = clock, version 2, still Draft, lines kept; hidden from normal queries. | I pass |
| SD-DEL-006 | Boundary | Zero quantity on edit → 400 before any lock. | I pass |
| SD-DEL-007 | Boundary | Pending 4 on line ordered 5: edit to 1.000001 → 422 ORDER_BALANCE_EXCEEDED, exactly 1 accepted (the edited Draft itself does not consume). | I pass |
| SD-DEL-010 | Conflict | Stale edit (version 2) and stale delete (version 0) → 409; second delete or edit of a deleted Draft → 404. | I pass |
| SD-DEL-011 | Partial result | Every rejected edit (state, source, line, tracking, MD, balance, cancelled order) leaves version 1, original line ID and date; other deliveries keep version 1. | I pass |
| SD-DEL-013 | Permission | Edit with DELETE only and delete with EDIT only → 403; Self scope for user 8 (order owner 7) → 404 on both; other-company and missing delivery → 404; Self scope for the owner edits. | I pass |
| SD-DEL-014 | State | Pending/Posted edit and Pending/Posted/Rejected delete → 422 INVALID_STATUS; edit after order cancelled → 422 ORDER_NOT_APPROVED, but the Draft can still be deleted; changing the source order → 422 SOURCE_IMMUTABLE. | I pass |
| SD-DEL-015 | Related | Line of another order → ORDER_LINE_INVALID `lineNo` 3; tracked item → TRACKING_UNSUPPORTED `lineNo` 2; inactive warehouse → MD.WAREHOUSE.INACTIVE. | I pass |
| SD-DEL-009 (edit part) | Concurrency | While a first edit is uncommitted (lines replaced), an edit in another company and an edit of another order's Draft finish within a 2 s lock timeout; delete of the same Draft and a create for the same order are blocked (1222) on the source-order lock. | I pass |

Mutation proof: removing the forced header `IsModified` makes the lines-only owner edit keep version 1 (test red). Not covered: HTTP bodies/OpenAPI (004a2b12), submit (004a2b13), lot/serial (004a2b14), INV (004b), UI (004c). Partial success/all skipped and file cases are inapplicable to one atomic document command.

### WU-SD-004a2b10 Draft create on real SQL (2026-11-16T05:04:19+08:00)

`SalesDeliveryCreateTransactionTests` (5 tests, real SQL, ADR-138). Executed parts of the SD-DEL list for create only:

| Case | Category | Assertion | Result |
|---|---|---|---|
| SD-DEL-001 | Normal | Approved order → Draft `DA001`, saved order/line link, item copied from the order line (not the client), leap-day date, no PostedAt; one MD reference check per distinct item/warehouse/location. | I pass |
| SD-DEL-007 | Boundary | Line A ordered 5, Posted 1, Pending 1.5 → two lines 1.000001 + 1.499999 on line A accepted (exact remaining 2.5); Draft, Rejected and deleted deliveries do not consume. 2.500001 over remaining Pending 2.5 → 422 ORDER_BALANCE_EXCEEDED. | I pass |
| SD-DEL-006 | Boundary | Zero quantity → 400 before any read; nothing saved. | I pass |
| SD-DEL-011 | Partial result | Duplicate document number after SYS allocation → whole insert rolls back, sequence unchanged (1); inactive warehouse on line 2 and tracked item → no delivery and no sequence row. | I pass |
| SD-DEL-013 | Permission | No `SAL.DELIVERY.CREATE` → 403; CREATE scope Self for another user, other-company order, missing order → 404. | I pass |
| SD-DEL-014 | State | Draft and Cancelled source orders → 422 ORDER_NOT_APPROVED. | I pass |
| SD-DEL-015 | Related | Order line of another order → 422 ORDER_LINE_INVALID with `lineNo` 7; MD inactive warehouse propagates 422; tracked item → 422 TRACKING_UNSUPPORTED with `lineNo` (ADR-138 until 004a2b14). | I pass |
| SD-DEL-009 (create part) | Concurrency | While a first create is uncommitted, a create in another company and for another order of the same company finish within a 2 s lock timeout; a create for the same order is blocked (1222) by the source-order update lock. Red with the previous Serializable design. | I pass |

Not covered here: edit/delete (004a2b11), HTTP 400/401/403/404/409/422 bodies, OpenAPI and list/detail (004a2b12), submit reservation and last-balance race (004a2b13), lot/serial (004a2b14), INV posting (004b), UI/E2E (004c). Partial success/all skipped and file cases are inapplicable to a single atomic create. The SYS number test probe uses a fixed number because SYS numbering blocks across companies (WU-SYS-NUMSEQ-LOCK).
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
WU-SD-002a action-scope checkpoint (2026-10-29T01:36:07+08:00): `SalesQuotationActionScopeTests` covers VIEW detail visibility for owner, other owner, other company and missing VIEW permission; DELETE requires its own action, hides another owner's document, rejects stale version, soft-deletes the current Draft and retains its number. Real SQL 2/2 verifies portions of SD-QUOTE-010/014/015. EDIT, two-connection races, list, approval, authorised HTTP and E2E remain open; SD-QUOTE-013/019 remain inapplicable for the reasons below.
# WU-SD-002a VOID and detail acceptance (2026-10-29)

| ID | Requirement | Category | Layer | Scenario |
|---|---|---|---|---|
| SD-QUOTE-039 | REQ_F4112.1.3 | Normal | I | A user with the distinct VOID permission voids an Approved quotation at its current version; timestamp and audit version persist. |
| SD-QUOTE-040 | REQ_F4112.1.3 | Conflict/status | I | Stale version returns 409; Draft, Pending, Rejected and already Voided refuse the transition without changing data. |
| SD-QUOTE-041 | REQ_F4112.1.3 | Permission/scope | I/E | Missing VOID permission returns 403; another company or a quote outside the exact VOID data scope returns 404. |
| SD-QUOTE-042 | REQ_F4112.1.3 | Related record | I | An Approved quote already linked to an order cannot be voided. |
| SD-QUOTE-043 | REQ_F4112.1.2 | Date boundary | U/I | Detail derives isExpired from IClock.Today: expiry day is valid, next day expired, saved Approved status stays unchanged. |
| SD-QUOTE-044 | REQ_F4112.1.3 | History | I/E | VIEW detail exposes the current SYS approval instance and its decision history to an authorized reader. |
| SD-QUOTE-045 | REQ_F4112.1.3 | Batch/file/error | I | One atomic document has no partial batch or file input; invalid version is 409, missing resource 404, invalid status 422, unexpected 500 and timeout use the shared ProblemDetails handler. |
| SD-QUOTE-046 | REQ_F4112.1.1 | Read client contract | U | Typed client calls only GET list/detail; list sends page/pageSize, supported filters and trimmed keyword, omits blank filters, and does not send company or ownership fields. |

WU-SD-002b0 scenario review: normal list/detail and empty page are covered by SD-QUOTE-046 (2/2 focused U tests). Date endpoints, invalid paging/status/customer and company/data-scope restrictions remain server checks under SD-QUOTE-020/030/031/034; the client does not reinterpret or bypass them. A stale detail, denied VIEW, 400/404/500 and network error remain visible UI cases under SD-QUOTE-014/018 and WU-SD-002b. Partial/all-skipped results, write conflicts, document state transitions, references and file cases do not apply to this GET-only contract; their existing SD-QUOTE-010–019 cases remain with the write/UI units. This unit has no E2E or SQL result claim.

WU-SD-002a VOID/detail checkpoint (2026-10-29T10:53:26+08:00): focused real SQL or authorized HTTP evidence covers Approved VOID, stale version, missing permission, another owner's Self scope, a soft-deleted source order, inclusive expiry date, and SYS history. Quotation tests passed 25/25 in that run. Draft/Pending/Rejected VOID, cross-company VOID, and unexpected-error/timeout response checks remain open; do not mark SD-QUOTE-040/041/045 complete. Full backend regression remains open.

2026-10-29T11:51:05+08:00 rerun: Debug build passed with zero warnings/errors; SalesModule architecture tests passed 8/8. Focused SQL test execution was stopped after `AuthorizedCreateAndEditPersistRealMdSnapshotsAndRejectStaleVersion` received HTTP 500 at quotation detail after 40 seconds and `DeleteRequiresCurrentVersionAndRetainsPermanentNumber` timed out in SYS company creation after 35 seconds. Host free physical memory reached about 0.3 GB; neither focused nor full SQL regression is counted as passed in this run. Recover host memory and repeat the exact quotation group, then run full backend with one test thread.

2026-10-29T12:06:25+08:00 checkpoint: `SalesQuotationActionScopeTests` now covers Draft, Pending, Rejected and foreign-company VOID refusals with unchanged saved status, version and void timestamp (SD-QUOTE-040/041). A Draft fixture initially used version 2 when its saved version was 1, yielding the expected 409 before state validation; corrected the fixture without changing product behavior. The action-scope group passes 17/17, and the focused quotation integration group passes 29/29 at one thread. Debug integration project build has zero warnings/errors. Full backend run completed Shared, Architecture and DocGen projects but integration remained without a summary after several minutes while free memory fell to 0.85 GB; it was stopped, so the full gate is still open. SD-QUOTE-045 unexpected 500 and timeout response checks remain open. Atomic single-document VOID has no partial batch or file case.

2026-10-29T12:11:13+08:00 checkpoint: SD-QUOTE-045 now has two synthetic middleware integration checks of the shared error handler: an unexpected exception and a server-side `TimeoutException` both return English 500 ProblemDetails with `COMMON.SERVER.INTERNAL_ERROR`, traceId and request instance, without exposing exception text. Focused `QuotationUnexpectedFailure` passes 2/2. This proves server error serialization, not a real SQL timeout or future browser-side request timeout. The prior quotation SQL/HTTP 29/29 remains the latest focused business result. Full backend regression is still open because host free memory dropped to 0.24 GB during the targeted run; no full-suite pass is claimed. Partial batch/file cases remain inapplicable to this atomic JSON document.

2026-10-29T12:20:14+08:00 checkpoint: confirmed SD-QUOTE-045 traces to REQ_F4112.1.3 and the shared ProblemDetails handler checks remain 2/2; the quotation SQL/HTTP group remains 29/29 from the preceding run. After shutting down idle .NET build servers, host free memory rose from 1.43 GB to 2.35 GB. A one-thread full backend run completed Shared, Architecture and DocGen, then the integration project ran for about six minutes without a completed summary while free memory fell to 0.83 GB. The test host was stopped; the final aggregate showed 1301 passed and zero failed, but exit code -1 and no integration success summary. This is not a full-suite pass. Retry when host memory is stable at or above 2.5 GB.

## WU-SD-002b1 read-only screen scenarios (2026-10-29T20:56:45+08:00)

Traceability: REQ_F4112.1.1/3 → REQ_IO_A2.1.1–8 → SD-QUOTE-UI-001–008. `E` means simulated Playwright GET responses; no real SQL or mutation is claimed. Existing SD-QUOTE-001–020 still govern the later write-capable page.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-UI-001 | Normal, state, related | List and detail show persisted summary, lines, status, separate derived expiry and translated approval rounds/steps in three languages and light/dark; GET requests make no data change. | E | REQ_F4112.1.1, REQ_F4112.1.3 |
| SD-QUOTE-UI-002 | Boundary, validation, empty | Invalid or reversed date range sends no GET; saved URL filters reach GET; unfiltered empty and filtered no-results have distinct text. Maximum keyword is 100 characters. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-003 | Error | A 500 response shows a translated error and traceId, leaves no stale rows and supports retry. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-004 | Permission | Missing VIEW hides the menu, redirects to 403 and sends no quotation GET. Backend company and data-scope isolation remain proven by the existing SQL/HTTP tests, not by mocked UI data. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-005 | Conflict, partial result, file | No write, batch or upload control exists in this slice; duplicate code, deleted references, 409 update conflicts, partial success, all skipped/failed and file format cases do not apply to GET-only viewing. | N/A | REQ_F4112.1.1 |
| SD-QUOTE-UI-006 | Error, deleted record | Detail 404 gives a translated reason, leaves the saved list intact and does not show a false detail. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-007 | Error, network | A failed GET shows the shared translated network error and a retry which restores visible saved rows. A server-side timeout serialized as 500 follows SD-QUOTE-UI-003. | E | REQ_F4112.1.1 |
| SD-QUOTE-UI-008 | Pagination, sort | Number sorting and selected page size reach the server; the supported sort is retained in the URL. | E | REQ_F4112.1.1 |

## WU-SD-002b4 Create page scenarios (2026-11-04T19:47:16+08:00)

Traceability: REQ_F4112.1.2 → Sales Quotation Create Interface 2.2 → SD-QUOTE-UI-014–023. Mocked E2E responses verify displayed results against the simulated API store; the existing real SQL tests remain the persistence authority. No edit, approval, batch, or file operation is in this slice.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-UI-014 | Normal | User selects a current-company customer and item, enters one or more lines, saves once, and sees the server-issued number; POST contains all fields and exact decimal digits. | U,E | REQ_F4112.1.2 |
| SD-QUOTE-UI-015 | Boundary | Missing references, empty lines, over 1000 lines, duplicate/invalid line numbers, impossible or reversed dates, 500-character remark, currency and six/four-place decimal bounds show translated field errors and send no POST. | U,E | REQ_F4112.1.2 |
| SD-QUOTE-UI-016 | Duplicate/conflict | A server 409 or stale reference decision shows a translated reason, keeps entered data and reports no save. Client has no code field; a deleted MD reference is server-validated. | E | REQ_F4112.1.2 |
| SD-QUOTE-UI-017 | Partial result | One JSON quotation is atomic; batch partial success and all-skipped are inapplicable. Zero changed rows never generate a success notice. | U,E | REQ_F4112.1.2 |
| SD-QUOTE-UI-018 | Permission | CREATE action is hidden without its permission; 403 from the server shows a translated error. Picker and server enforce company and data scope. | E,I | REQ_F4112.1.2 |
| SD-QUOTE-UI-019 | State/related | The page creates Draft only; approval and editing states are in later units. Inactive/deleted/unapproved MD references are rejected by the server with no saved document. | I,E | REQ_F4112.1.2 |
| SD-QUOTE-UI-020 | Error | 400, 409, 422 show localized refusal and preserve input; 500 with traceId, network failure and timeout show an unknown-result warning, preserve input and block repeat POST until list verification. | E | REQ_F4112.1.2 |
| SD-QUOTE-UI-021 | File | No file upload or import exists for quotation creation; empty, invalid, oversized and mis-encoded files are inapplicable. | N/A | REQ_F4112.1.2 |
| SD-QUOTE-UI-022 | UX | Create form and errors remain readable in three languages, light/dark and tablet width; saved result text matches the returned record. | E | REQ_F4112.1.2 |
| SD-QUOTE-UI-023 | Concurrent company change | A late save response from a previous company never displays success in the new company; the form resets before another save. | U,E | REQ_F4112.1.2 |

2026-11-04T20:44:51+08:00 evidence: Create service transport was red before implementation and green afterward (3 focused tests); full frontend 745/745, production build and mocked Playwright 465 passed／5 skipped. The four Create E2E cases cover validation, exact POST and saved result, 400/422/409/403/500, permissions, company switch, three languages, themes and tablet width. Network loss and browser timeout use the same unknown-result branch as 500 but were not separately simulated; SQL persistence and reference checks remain backed by earlier integration tests, with FI-wide SQL retest gated by memory. Atomic JSON creation has no partial batch or file path.

## WU-SD-002b5 quotation Edit browser (2026-11-04T21:07:06+08:00)

Trace: REQ_F4112.1.2 → REQ_IO_A2.3.1–15 → SD-QUOTE-EDIT-001–013. Browser routes use simulated HTTP; the earlier WU-SD-002a real SQL/HTTP tests remain the server persistence evidence.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-QUOTE-EDIT-001 | Normal, boundary | PUT carries saved positive version and full validated draft, with six-place decimal JSON numbers unchanged. | U | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-002 | Normal | A Draft loads its saved customer, line and version; one PUT changes the persisted remark and a follow-up GET returns the new version before success appears. | E | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-003 | Conflict | Stale 409 preserves input, shows a warning, prevents another PUT, and explicit refresh loads the other editor's version. | E | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-004 | Status | Pending disables all edit controls and sends no PUT. Approved/Voided use the same non-Draft/Rejected gate; server status remains authoritative. | E,I | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-005 | Status, related | Rejected revision returns to Draft with the same saved number. Existing SYS history is retained by the server. | E,I | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-006 | Permission, company, error | A missing or hidden current-company detail shows a retryable error and cannot send PUT; the server enforces action scope on every request. | E,I | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-007 | Permission | Without EDIT, the list hides Edit and the deep link returns 403 before PUT. | E | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-008 | Boundary, duplicate | The shared form covers empty/long fields, 1–1000 lines, decimal/date limits and duplicate line numbers (SD-QUOTE-FORM-014–017); SYS owns the permanent number, so duplicate user number is inapplicable. | U | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-009 | Partial result | Inapplicable: one quotation PUT is atomic, with no batch, skipped rows or partial result. | N/A | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-010 | Error | Existing shared form reports 400 field errors, 422 reference refusal, 500 trace ID and unknown network outcome; Edit 409 has separate preserved-input behavior. | E | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-011 | File | Inapplicable: the Edit form submits JSON, with no file input. | N/A | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-012 | Boundary, permission | An invalid Edit URL stays in Edit mode, shows a failed detail load, and sends neither PUT nor Create POST. | E | REQ_F4112.1.2 |
| SD-QUOTE-EDIT-013 | Company, concurrency | A detail response from the previous company arriving after a company switch cannot replace the newly loaded company data or trigger a write. | E | REQ_F4112.1.2 |

TDD: the versioned transport test first failed because `replace` did not exist, then passed. Browser tests verify rendered messages and request/persisted data together; full frontend and E2E outcomes are recorded in progress.md.

2026-11-05T01:20:59+08:00 acceptance: frontend 747/747; Edit browser 9/9 including invalid URL and late old-company detail; full simulated E2E 473 passed / 5 skipped (the ninth Edit case was added after full discovery and passed in the focused rerun). DocGen tests 22/22 and isolated consistency 0 Error / 580 Warning; backend Debug build 0 warnings / 0 errors. SQL full suite was not started: memory gate DEFER at 1.889 / 1.889 / 1.889 GiB; WU-SD-002a's previously passed real SQL/HTTP evidence remains the backend authority. This UI work changes no schema or backend code.

## WU-SD-005a0 offline pricing rules (executed 2026-11-06, U layer only)

`SalesPricingRules` in `MyERP.Modules.Sales.Application`; tests `tests/backend.unit/MyERP.ArchitectureTests/SalesPricingRulesTests.cs` (red: missing types; green 17/17). These pure cases refine the U layer of SD-PRICE-002/005/006/007/008/010/014; every I and E part of those scenarios stays planned for WU-SD-005a/b.

### WU-SD-005a1 — read-only dated price quote (offline slice)

| ID | 類別 | 情境與預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| SD-PRICE-U15 | 正常 | 客戶規則勝出，唯讀回應含查詢業務日、規則 ID／版本、單價、折扣上限及有效截止日；不修改輸入。 | U | REQ_F4112.4.1、BR-SD-027 | PASS（U） |
| SD-PRICE-U16 | 邊界／狀態 | 有效截止日當天仍可報價；隔日或停用／軟刪除規則回 `RULE_NOT_FOUND`，不返回過期價格。 | U | REQ_F4112.4.1、BR-SD-027 | PASS（U） |
| SD-PRICE-U17 | 重複與衝突 | 決定層多規則時回 `DUPLICATE_ACTIVE_RULE`、排序衝突 ID，絕不任選價格。 | U | REQ_F4112.4.1、BR-SD-027 | PASS（U） |
| SD-PRICE-U18 | 錯誤／權限／關聯 | 無效查詢欄位先回 400；跨公司、其他客戶、幣別及物料的規則不可出現在報價。此純函式無登入、HTTP 或持久化，受權 401／403／404、500／逾時、並行寫入留待 005a 真實 SQL／HTTP。 | U,I | REQ_F4112.4.1、BR-SD-026～027 | PASS（U），I 待驗 |

部分結果／檔案不適用：單次唯讀報價沒有批次或上傳；0 筆匹配以 `RULE_NOT_FOUND` 表示。草稿／審批中／已核准文件狀態不參與報價選擇，保存重驗與不可變快照留待 005a。前端三語提示及畫面／資料雙斷言留待 005b。

### WU-SD-005a2 — quoted rule hint shape (offline slice)

| ID | 類別 | 情境與預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| SD-PRICE-U19 | 正常／邊界 | 無提示可進入手動定價；正數 ID／版本成對可用；0 與負數任一成員均按 `pricingRuleId`／`pricingRuleVersion` 回欄位 400。 | U | REQ_F4112.4.1、BR-SD-028 | PASS（U；正常／成對值由 U11／U12） |
| SD-PRICE-U20 | 重複與衝突／權限／錯誤 | 無效成對提示在重複規則 409、過時報價 409 或缺覆核權限 403 前拒絕；兩欄同時無效均有欄位錯誤。有效但過時提示仍為 409。 | U | REQ_F4112.4.1、BR-SD-028～030 | PASS（U；有效過時由 U14） |

狀態／關聯：純函式只處理已選規則快照及提示形狀；停用／刪除規則選擇已由 U04 測，保存文件狀態與 MD 引用由 005a 的 SQL／HTTP 驗。部分結果不適用：單行原子決策，無批次或 0 筆寫入。檔案不適用：沒有匯入或上傳。HTTP 400／409／422／500／逾時、多語提示、跨公司及資料範圍由 005a／005b 的 I／E 情境驗，本切片不宣稱受權或持久化。

執行記錄（2026-11-11T14:58:09+08:00）：先以 `NonpositiveQuoteHintsAreFieldErrorsBeforeConflictsAndOverride` 見到 409 `STALE_QUOTE` 紅燈，後定價規則類別 22/22、Architecture 1582/1582 通過；Sales Debug 建置零警告／錯誤。SQL 守衛 DEFER，I／E 未執行。

### WU-SD-005a3 — save-context pricing decision (offline slice)

| ID | 類別 | 情境與預期 | 層級 | 需求／規則 | 結果 |
|---|---|---|---|---|---|
| SD-PRICE-U21 | 正常／日期邊界／狀態／關聯 | 同一業務日有效的規則可存檔；隔日已到期的同 ID／版本提示回 409 `STALE_QUOTE`，不得沿用舊報價。 | U | REQ_F4112.4.1、BR-SD-027～030 | PASS（U） |
| SD-PRICE-U22 | 公司／客戶／幣別範圍／權限 | 另一客戶或幣別的規則提示回 409；沒有提示時只在具覆核授權且有理由時形成無規則的手動定價快照。公司與物料隔離沿用 U02、U18。 | U | REQ_F4112.4.1、BR-SD-026～030 | PASS（U） |

### WU-SD-005a4 — 存檔欄位驗證順序（離線）

| ID | 類別 | 情境／預期 | 層級 | 追溯 | 結果 |
|---|---|---|---|---|---|
| SD-PRICE-U23 | 正常／邊界／衝突／錯誤 | 規則快照重複 ID 且單價、折扣或提示欄位非法時，存檔入口先回對應欄位 400；合法輸入仍檢查快照並拒絕重複 ID；有效快照的正常定價結果不變。 | U | REQ_F4112.4.1、BR-SD-028a | PASS（U） |

### WU-SD-005a5 — 載入規則快照完整性（離線）

| ID | 類別 | 情境／預期 | 層級 | 追溯 | 結果 |
|---|---|---|---|---|---|
| SD-PRICE-U24 | 正常／邊界／狀態 | 有效規則在有效首尾日可選取、報價、存檔；零價／零折扣、價格六位和上限四位仍有效。 | U | REQ_F4112.4.1、BR-SD-028b | PASS（U） |
| SD-PRICE-U25 | 邊界／錯誤 | 快照版本、公司、物料、客戶、幣別、日期、價格或折扣上限無效時，選取／報價／存檔皆以 `rules` 欄位拒絕，沒有價格輸出。 | U | REQ_F4112.4.1、BR-SD-028b | PASS（U） |
| SD-PRICE-U26 | 重複與衝突／權限／關聯 | 無效快照即使屬其他公司／客戶、停用或已刪除仍拒絕；不能以另一有效規則或覆核權限掩蓋。存檔輸入欄位錯誤仍先拒絕。 | U | REQ_F4112.4.1、BR-SD-028a～028b | PASS（U） |

執行記錄（2026-11-11T15:17:16+08:00）：U25 先見到無效版本可通過的紅燈；修正後定價 28/28、Architecture 1588/1588、Sales Debug 建置 0 警告／錯誤。記憶體守衛 1.136／1.126／1.146 GiB DEFER，沒有啟動 SQL fixture、HTTP 或全量整合測試。

部分結果：純決策沒有批次或資料寫入，故部分成功、全部略過及 0 筆變更提示不適用。狀態與關聯：本層只處理停用／刪除的快照完整性，單據審批狀態與 MD 參照由 005a 真實交易測試驗。權限：本層只驗無效快照不能因覆核授權通過，HTTP 401／403／404 與公司資料範圍仍屬 005a。錯誤：欄位 400 與既有 409／422 決策在 U 層測試；500、逾時與畫面三語提示留 005a／005b。檔案：沒有檔案端點，空檔／錯誤格式／大小／編碼／欄位情境不適用。

部分結果、權限、狀態、關聯、檔案：本切片只調整純函式欄位驗證順序，無批次、資料寫入、登入、狀態轉移、外部參照或檔案；這些情境由 SD-PRICE-001～020 的 I／E 驗收。500、逾時及前端提示也留在 005a 真實 SQL／HTTP／UI 測試。

重複與衝突、欄位 400、無覆核 403、折扣邊界沿用 U05／U12～U14／U19～U20，單一入口呼叫既有選擇與決定函式。部分結果不適用：單行純決策，沒有批次寫入；檔案不適用：沒有上傳。HTTP 401／403／404／422／500／逾時、並發、軟刪除規則寫入、SYS 資料範圍及畫面與資料雙斷言仍由 005a／005b 的 I／E 情境驗收。本切片不宣稱交易鎖或保存資料。

執行記錄（2026-11-11T15:04:09+08:00）：缺少 `DecideForSave` 得 CS0117 紅燈；完成後定向 24/24、Architecture 1584/1584、Sales Debug 0 警告／錯誤。記憶體守衛 DEFER 1.902／1.879／1.526 GiB，未跑 SQL／HTTP。

| ID | Category | Scenario and expected result | Layer | Result |
|---|---|---|---|---|
| SD-PRICE-U01 | Normal | Customer-specific active rule beats the default rule on the business date. | U | PASS |
| SD-PRICE-U02 | Normal | Rules of another customer, currency, company or item are ignored; default selected. | U | PASS |
| SD-PRICE-U03 | Boundary | `valid_from`/`valid_to` inclusive across leap day 2028-02-29 and next day; one day outside → `RULE_NOT_FOUND`. | U | PASS |
| SD-PRICE-U04 | State | Inactive, soft-deleted, expired and future rules are never selected; result NotFound with no guessed rule. | U | PASS |
| SD-PRICE-U05 | Duplicate/conflict | Two candidates on the deciding tier → `DUPLICATE_ACTIVE_RULE` with ascending conflicting IDs, no rule. | U | PASS |
| SD-PRICE-U06 | Duplicate/conflict | Duplicate default tier does not block a single customer rule; it does block a customer that falls back to it. | U | PASS |
| SD-PRICE-U07 | Boundary | Missing company/item/customer, blank currency, null or duplicate-ID snapshots → 400 before selection. | U | PASS |
| SD-PRICE-U08 | Duplicate/conflict | Equal-scope intervals sharing a boundary, enclosed or enclosing overlap; adjacent-day intervals do not. | U | PASS |
| SD-PRICE-U09 | Duplicate/conflict | Default and customer scopes coexist; inactive, deleted, self, other currency/item/company ignored; inactive proposal overlaps nothing. | U | PASS |
| SD-PRICE-U10 | Boundary | Rule input: item, customer (null or >0), 3 uppercase currency, reversed dates, price ≥0 / ≤6 places / <10^13, ceiling 0–100 / ≤4 places; errors keyed in form order. | U | PASS |
| SD-PRICE-U11 | Normal | Rule price with discount exactly at ceiling is ordinary; supplied reason is discarded; provenance ID/version/price/ceiling kept. | U | PASS |
| SD-PRICE-U12 | Permission | 0.0001% above ceiling, manual price or no rule → 403 `OVERRIDE_REQUIRED` without grant; with grant blank or >200-char reason → 400; trimmed reason saved; no-rule override has null provenance. | U | PASS |
| SD-PRICE-U13 | Error | Negative, >6/4-place, ≥10^13 price or discount outside 0–100 is 400 even with override; duplicate rules → 409, never overridable. | U | PASS |
| SD-PRICE-U14 | Duplicate/conflict | Quote hint with other ID/version or no current rule → 409 `STALE_QUOTE`; half hint → 400 on the missing field. | U | PASS |

Partial result, file and UI categories: not applicable to these pure functions (single-line decision, no batch, upload or screen). Decision: deciding tier = customer tier when it has any candidate, else default tier; an override reason is limited to 200 characters (revisable in 005a with the column type).

## WU-SD-006b0 offline credit decision rules (executed 2026-11-06, U layer only)

`SalesCreditRules` in `MyERP.Modules.Sales.Application` (uses the FI public `CreditSnapshot` contract, ADR-104); tests `tests/backend.unit/MyERP.ArchitectureTests/SalesCreditRulesTests.cs` and `SalesCreditErrorTranslationTests.cs` (red: missing types CS0246; green 21 + 5). These pure cases refine the U layer of SD-CREDIT-001/004/005/006/007/009/015/016/019/020/021; every I and E part (credit lock, concurrency, SYS round, audit row, HTTP, UI) stays planned for WU-SD-006b/c.

| ID | Category | Scenario and expected result | Layer | Result |
|---|---|---|---|---|
| SD-CREDIT-U01 | Normal | Limit 1000, AR 300, two other commitments 150, candidate 200 → WithinLimit, headroom 350, excess 0, components, limit version and evaluation date returned. | U | PASS |
| SD-CREDIT-U02 | Boundary | Exact zero headroom passes; 0.0001 beyond → OverLimit `LIMIT_EXCEEDED`, headroom −0.0001, excess 0.0001. | U | PASS |
| SD-CREDIT-U03 | Boundary | Zero limit is valid: zero exposure passes, 0.0001 refuses. | U | PASS |
| SD-CREDIT-U04 | Duplicate/conflict | Candidate order ID present among commitments is excluded and counted once. | U | PASS |
| SD-CREDIT-U05 | State | Inactive/unapproved MD customer → Blocked `CUSTOMER_INACTIVE` with no credit figures. | U | PASS |
| SD-CREDIT-U06 | Error | No FI snapshot → `UNAVAILABLE`; MissingLimit → `LIMIT_NOT_SET`; AmbiguousLimit → `INVALID_SNAPSHOT`; MissingBaseCurrency/CustomerUnavailable/ExposureUnavailable → `UNAVAILABLE`. | U | PASS |
| SD-CREDIT-U07 | Boundary | Snapshot of another company, customer or evaluation date (backdated) → `INVALID_SNAPSHOT`, even when unavailable. | U | PASS |
| SD-CREDIT-U08 | Boundary | FI currency ≠ company base currency → `INVALID_SNAPSHOT`; missing or malformed company base currency → `UNAVAILABLE`. | U | PASS |
| SD-CREDIT-U09 | Boundary | Candidate negative, 5 decimal places or above decimal(19,4) → `INVALID_AMOUNT`. | U | PASS |
| SD-CREDIT-U10 | Boundary | Commitment negative, 5 places, non-positive or duplicate order ID, sum overflow, or AR + candidate overflow → `INVALID_AMOUNT`; maximum limit and candidate exactly at decimal(19,4) max pass. | U | PASS |
| SD-CREDIT-U11 | Normal | Within limit: supplied reason ignored, no override recorded, no grant needed. | U | PASS |
| SD-CREDIT-U12 | Error | Over limit without reason → 422 `LIMIT_EXCEEDED` with `excess` and `currency` extensions. | U | PASS |
| SD-CREDIT-U13 | Permission | Over limit with reason but no `SAL.CREDIT.OVERRIDE` → 403 `OVERRIDE_FORBIDDEN`; with grant empty/blank/201-char reason → 400 `creditOverrideReason`; trimmed 1–200-char reason → override decision keeping excess. | U | PASS |
| SD-CREDIT-U14 | State | Blocked results (inactive customer, missing snapshot, currency mismatch) throw 422 with their own code even with grant and reason. | U | PASS |
| SD-CREDIT-U15 | Error | Every `SalesCreditErrors` code has a non-empty, translated `SAL.CREDIT.*` message in en / zh-Hans / zh-Hant, with no extra keys. | U | PASS |

Partial result, file and UI categories: not applicable (single-order decision, no batch, upload or screen). Stale order version / SYS round (SD-CREDIT-009) and the credit lock (SD-CREDIT-008) are transaction concerns of 006b.

## WU-SD-005a6 — overlap snapshot integrity (REQ_F4112.4.1, BR-SD-028c)

| ID | Category | Scenario and expected result | Layer | Result |
|---|---|---|---|---|
| SD-PRICE-U27 | Normal/boundary/conflict | New proposed ID 0 detects an equal-scope overlap including an interval endpoint; valid other scope, inactive, deleted and self rows remain excluded. | U | PASS (existing U08/U09 and new U27) |
| SD-PRICE-U28 | Boundary/state/error | Invalid version, currency, reversed dates or negative price in proposed or any existing row, including inactive/out-of-scope history, gives `rules` 400 before returning overlaps; null and duplicate existing IDs also fail. | U | PASS |

Partial result, permission, related record, file and UI categories do not apply to this pure calculation. Concurrent 409, MD identity, HTTP errors and saved rows remain covered by SD-PRICE-008–020 in WU-SD-005a; this unit makes no SQL or UI claim.

### WU-SD-005a7 — public pricing decision selection integrity

| ID | Category | Scenario | Level | Trace | Result |
|---|---|---|---|---|---|
| SD-PRICE-U29 | Normal/boundary | A valid selected rule produces the same ordinary decision; an invalid rule ID, version, range or price fails before a saved decision. | U | REQ_F4112.4.1, BR-SD-028d | PASS |
| SD-PRICE-U30 | Duplicate/conflict/state/error | Selected, NotFound and DuplicateActiveRule must have coherent rule, error code and conflict IDs; malformed combinations fail closed before override permission or stale-hint handling. | U | REQ_F4112.4.1, BR-SD-028d | PASS |

Partial result is inapplicable because one decision writes no records. Permission is exercised only as an input flag here; HTTP actor scope, cross-company access and 401/403 remain SD-PRICE-013. Related MD records, file import, UI messages, 500/timeout and SQL rollback remain SD-PRICE-001–020 under WU-SD-005a/b. No E2E claim is made for this pure function.

## WU-SD-004a2b14 — saved delivery tracking (ADR-145; written as ADR-140 on the feature branch)

| ID | Category | Scenario and expected result | Level | Requirement |
|---|---|---|---|---|
| SD-DEL-TRACK-001 | Normal | Draft saves trimmed lot/serial selections; detail returns entry order; edit replaces all old selections. | I | REQ_F4112.3.1–3 |
| SD-DEL-TRACK-002 | Boundary | Empty choices are allowed on Draft, but submit requires exact line total; serial quantity is exactly one and serial-tracked line quantity is integral. Invalid number length, quantity scale, >1000 choices and over-selection return field-level 400. | U,I | REQ_F4112.3.2–4 |
| SD-DEL-TRACK-003 | Duplicate/conflict | Case-insensitive duplicate identifier in one line is rejected; same item's serial on another line is 422 with lineNo. Stale edit version remains 409 under existing command tests. | U,I | REQ_F4112.3.2–3 |
| SD-DEL-TRACK-004 | Partial result | Invalid middle line or failed submit leaves no partial selections, delivery state or SYS number. Batch partial success and all-skipped do not apply: one delivery is atomic. | I | REQ_F4112.3.2–4 |
| SD-DEL-TRACK-005 | Permission | Existing CREATE/EDIT/SUBMIT action scope and company checks also protect tracking input; hidden source stays 404, missing action 403. | I | REQ_F4112.3.1–4 |
| SD-DEL-TRACK-006 | State | Only Draft/Rejected may replace choices; Pending/Posted cannot edit; incomplete Draft stays Draft after rejected submit. | I | REQ_F4112.3.3–4 |
| SD-DEL-TRACK-007 | Related | MD tracking mode is checked under reference lock: untracked item with choices, lot item with serials and serial item with lots reject with lineNo; INV stock and serial availability belong to 004b. | I | REQ_F4112.3.2–4 |
| SD-DEL-TRACK-008 | Error | 400 format, 409 version and 422 tracking carry precise reasons; unexpected 500/timeout and browser messages belong to the 004c UI work unit. | U,I | REQ_F4112.3.1–4 |
| SD-DEL-TRACK-009 | File | Not applicable: this endpoint accepts JSON only and has no file import. | — | REQ_F4112.3.2–3 |
| SD-DEL-TRACK-010 | Related/normal | Posting (zero-step submit and SYS final approval share `SalesDeliveryPosting`) sends each line's saved lots (with quantity) and serials (one unit) to INV in seqNo order, without manufacture/expiry dates; an untracked line sends no tracking. INV lot shortage or unavailable serial rolls back SYS and SD (ADR-144). | I | REQ_F4112.3.4, BR-SD-024 |

Tests were written in the interrupted prior round; this round validates them against real SQL. No tracking UI exists yet, so E2E message/data assertions remain WU-SD-004c.

Verification, 2026-11-18: focused real-SQL delivery suite 17/17 passed. The full-suite run with two test threads encountered SQL command timeouts in unrelated PUR and INV tests; each failed test passed alone with one thread. The one-thread full-suite rerun also hit FI SQL timeout and an MD company-lock failure. SQL DMV showed `WAIT_SLEEP_LOWMEM_GUARD`, `process_physical_memory_low=1` and approximately 1.1 GiB host physical memory available. Full regression remains open on `feature/sd-delivery-line-tracking`; do not merge before it passes.
WU-SD-003b1 evidence (2026-11-20T20:59:51+08:00, ADR-142): `tests/backend.integration/MyERP.IntegrationTests/Database/SalesOrderSubmissionTransactionTests.cs` 6/6 on real SQL, written before the services existed (compile red). Covers SD-ORDER-004 (submit Pending, SYS approve rechecks then Approved, zero-step Approved), SD-ORDER-011/SD-CREDIT-009 (stale version 409), SD-ORDER-012/SD-CREDIT-011 (failure after the credit check rolls back; the waiting order then takes the headroom), SD-ORDER-014/SD-CREDIT-014 (missing SUBMIT 403, Self scope / other company / missing ID 404), SD-ORDER-015/SD-CREDIT-015 (Rejected and Pending cannot submit), SD-ORDER-017 and SD-CREDIT-001/002/004/005/007/008/016 (headroom = limit − open AR − other Approved, with Pending/Cancelled excluded; exact zero headroom passes, 0.0001 excess refused; missing limit; inactive customer; base-currency change; second approval refused once the first is Approved, SYS decision rolled back; concurrent same-customer approvals serialize with one LIMIT_EXCEEDED loser while another customer is not blocked). HTTP, cancel and override cases remain in 003b2/003b3/006b.

### WU-SD-003b2 order submit/cancel HTTP (2026-11-20T22:26:11+08:00, ADR-143)

| ID | Category | Scenario | Level | Related |
|---|---|---|---|---|
| SD-ORDER-HTTP-001 | Permission/error | Anonymous submit/cancel return 401; OpenAPI documents 200/403/404/409/422 for both routes. | I | REQ_F4112.2.4, REQ_F4112.2.6 |
| SD-ORDER-HTTP-002 | Normal/permission/conflict/state/error | Submit without SUBMIT 403; missing, Self-scope hidden and other-company IDs 404; stale version 409; over limit by 0.0001 → 422 LIMIT_EXCEEDED with excess/currency/traceId and the order stays Draft v1 with no SYS round; no flow → 200 Approved with credit figures (limit 1000, candidate 400, headroom 600, limit version 3); resubmit 422 INVALID_STATUS; with a one-step flow → 200 Pending and the Approved 400 counted as commitment (headroom 0, exact boundary). | I | REQ_F4112.2.4, BR-SD-016, BR-SD-018 |
| SD-ORDER-HTTP-003 | Normal/permission/state/relation/conflict | Cancel without CANCEL 403; Self-scope hidden 404; Draft and Pending 422 INVALID_STATUS; Pending or Posted delivery 422 HAS_DELIVERY; Draft, Rejected and soft-deleted deliveries do not block; stale 409; success 200 Cancelled v2 with lines and unchanged number; second cancel 422. Database confirms only the cancellable order changed. | I | REQ_F4112.2.6, BR-SD-016, BR-SD-019 |
| SD-ORDER-HTTP-004 | Relation | An Approved 700 makes a 400 submit exceed the 1000 limit by 100; after cancelling it the same submit passes with zero other commitments. | I | BR-SD-018, BR-SD-019 |

Not applicable: partial results (single-document commands), files, boundary dates (server evaluation date only). HTTP races (cancel vs. delivery submit, submit vs. SYS approve) are WU-SD-003b3. Evidence: tests written first; with the controller change stashed all 4 failed (404 routes / missing OpenAPI), with it 4/4 pass; Sales order/delivery integration classes 69/69, ArchitectureTests 2205/2205. Full backend run deferred: memory guard 1.79～1.96 GiB DEFER.

### WU-SD-003b3 order HTTP races and resubmission (planned before tests)

| ID | Category | Scenario | Level | Related |
|---|---|---|---|---|
| SD-ORDER-HTTP-005 | Conflict | Two concurrent `submit` calls with version 1 on the same Draft while a second connection holds the order row UPDLOCK: exactly one 200 Pending; the other 409 COMMON.CONCURRENCY.CONFLICT or 422 SAL.ORDER.INVALID_STATUS. Database: one SYS instance for the order, order Pending v2. | I | REQ_F4112.2.4, BR-SD-016 |
| SD-ORDER-HTTP-006 | State/normal | One-step SYS flow via HTTP: submit → Pending; approver rejects (comment) → order Rejected, credit not reserved; submit on Rejected 422 INVALID_STATUS; PUT replacement with real MD references → Draft with new amounts; resubmit → Pending with a new SYS instance; concurrent approve and reject of that instance → exactly one 200, the loser 409/422 and the order follows the winner (Approved with approvedAt, or Rejected); when Approved, cancel → Cancelled. | I | REQ_F4112.2.3, REQ_F4112.2.4, REQ_F4112.2.5, REQ_F4112.2.6, BR-SD-016 |
| SD-ORDER-HTTP-007 | Relation/conflict | Cancel of an Approved order and submit of its Draft delivery race while a second connection holds the order UPDLOCK: exactly one 200; loser is cancel 422 SAL.ORDER.HAS_DELIVERY or delivery 422 SAL.DELIVERY.ORDER_NOT_APPROVED. Database is consistent with the winner (Cancelled + Draft delivery, or Approved + Pending delivery); never Cancelled + Pending. | I | REQ_F4112.2.6, REQ_F4112.3.4, BR-SD-019, BR-SD-022 |

Not applicable: permission/cross-company (SD-ORDER-HTTP-001–003), files, partial results (single-document commands), boundary amounts (SD-ORDER-HTTP-002/004). UI messages belong to WU-SD-006c.

Evidence (2026-11-20T23:28:09+08:00): `SalesOrderCommandHttpTests` 7/7 on the real API and SQL (SD-ORDER-HTTP-005～007 added). These are characterization tests of the 003b1/003b2 locking, so they passed on first run without product changes; each race asserts that both requests are still waiting while the second connection holds the order row, so the outcome is decided after the lock. SQL memory guard 2.85 GiB PASS; full backend 3680/3680 (`--max-threads 2`), covering the run deferred from 003b2.

### WU-SD-004a2b14 merge into main (2026-11-20T23:58:44+08:00, ADR-145)

The feature branch was merged with `main` after WU-SD-004b1. SD-DEL-TRACK-010 is covered by `SalesDeliveryCreateTransactionTests.PostingSendsSavedLotAndSerialSelectionsToInventoryInSequence` on real SQL. The test was red (null tracking) when the INV mapping was disabled and green after it was restored. The delivery transaction class passed 20/20. INV lot balance and serial availability are already covered by the INV posting tests. Partial results do not apply because a delivery posts as one transaction. Permission and cross-company checks are unchanged from SD-DEL-TRACK-005. Files do not apply.

### WU-SD-004b2 movement-ID storage and SYS approval HTTP (2026-11-21, ADR-146)

| ID | Category | Scenario | Level | Related |
|---|---|---|---|---|
| SD-DEL-020 (movement part) | Traceability | Real INV zero-step posting of a two-line delivery stores each line's Out movement ID by line number; the refused delivery's lines stay null. | I | REQ_F4112.3.6, BR-SD-025 |
| SD-DEL-MODEL-002 | Data | `inv_movement_id` nullable, positive CHECK, filtered unique `(company_id, inv_movement_id)`, no FK to INV. | U | REQ_DS43.171 |
| SD-DEL-HTTP-006 | Error/normal/duplicate/conflict | One-step SYS flow over the real API: approve with stock 2 < 3 → 422 `INV.STOCK.INSUFFICIENT`, delivery Pending v2, `postedAt` and `invMovementId` null, no movement; after a receipt the same round approves → Posted with `postedAt` and the stored movement ID; a repeated approve is 409/422 with no new movement; concurrent approve and withdraw of another Pending delivery → exactly one 200, the loser 409/422, and the delivery is either Posted with its movement or Draft v3 with none. | I | REQ_F4112.3.4, REQ_F4112.3.5, REQ_F4112.3.6, BR-SD-024 |

Not applicable: files, partial results (single-document transactions), permissions (unchanged from SD-DEL-HTTP-001～005). Evidence: `SalesDeliveryCreateTransactionTests.RealInventoryPostsTheOutbound…` and `SalesDeliveryAuthorizedHttpTests.SysApprovalEndpointPostsThroughInventory…` both red with the movement assignment disabled (collection differs / null `invMovementId`) and green with it; the HTTP race passed in four consecutive runs; `SalesDeliveryPersistenceModelTests` 2/2.

## WU-SD-004c1 Sales Outbound View Interface 2.7 (2026-11-21T00:37:54+08:00)

| ID | Category | Scenario | Level | Trace |
|---|---|---|---|---|
| SD-DEL-HTTP-007 | Normal/state | The VIEW detail of a resubmitted Pending delivery has one Pending round with its step name; the withdrawn delivery keeps one non-Pending round; the posted delivery's round is Approved with the approver comment. List and detail status are named strings (ADR-147). | HTTP | REQ_F4112.3.1 |
| SD-DEL-HTTP-008 | Normal/state | POST and PUT receipts and the VIEW detail name each line's current MD tracking mode (None, Lot, Serial), with an incomplete Draft lot pick allowed (red without the mapping: `[null]`). | HTTP | REQ_F4112.3.3 |
| SD-DEL-UI-001 | Normal/boundary | The service trims filters, sends the named status, orderId and paging; list and detail keep exact six-decimal quantities, including those in tracking; remark text that looks like a property is not rewritten; an invalid id sends nothing; 404 ProblemDetails reach the caller. | U | REQ_F4112.3.1 |
| SD-DEL-UI-002～010 | Normal/empty/validation/state/error/company/permission | List row content and grouped exact quantity; different empty and filtered no-result text; reversed dates refused without a GET; posted lines show lot/serial picks, INV movement IDs, posting time and SYS history; Pending explains its reservation and shows "Not posted"; 404 shows the specific message, and 500 shows the trace ID with a retry; a list failure is never shown as an empty list; a company switch drops the old reply; route and menu require VIEW. | U | REQ_IO_A2.7.1–8 |
| SD-DEL-UI-011～013 | E2E/QA | Three languages and light/dark normal and posted detail; Pending detail; empty, filtered no-result, invalid date (no request), 404 detail, 500 with trace ID, then a successful retry. | E2E | REQ_IO_A2.7.1–8 |

Not applicable here: 409/422 (this interface has no mutation; covered by 2.8/2.9 in WU-SD-004c2), files, and partial results. Timeout uses the same handler as 500; the timeout and offline screenshots are planned for WU-SD-004c3.

## WU-SD-004c2a Sales Outbound Create Interface 2.8 (2026-11-21T01:04:45+08:00, ADR-148)

| ID | Category | Scenario | Layer | Requirement |
|---|---|---|---|---|
| SD-DEL-CR-001～007 | Normal/boundary/error | Prefill skips exhausted order lines and keeps exact text. Quantity is accepted at 0.000001 and at 13+6 digits, and refused at 0, below 0, 7 decimals, 14 digits or exponent form. Leap-day and 29-Feb-2027 dates are checked. A split line totalling one millionth above the balance is refused. JSON uses exact numbers and draft fields only. `lineNo` is read only from a positive integer on a 422. The receipt must match every line. | U | REQ_F4112.3.2 |
| SD-DEL-CR-010～020 | Normal/validation/conflict/error/state/permission/company | Order pick and balances. Save posts the exact body; success only on a matching Draft, and a mismatch is "not confirmed". Local errors send no request. A line 422 marks the line and keeps the input; `ORDER_BALANCE_EXCEEDED` reload keeps the input. MD 422 and 400 field paths are shown in the user's language. 500 shows the trace ID and locks against a duplicate. Cancelled, fully used or hidden (404) orders cannot be saved. Without order or warehouse VIEW the picker is disabled with a reason; CREATE-only uncertainty names a colleague. A warehouse change clears the location. A company switch drops the order and ignores a late reply. The route needs CREATE and guards unsaved input. | U | REQ_IO_A2.8.1–7 |
| SD-DEL-CR-021～022 | E2E/QA | From the list button: create in three languages and two themes, with the exact POST body and the list showing the new Draft. Local validation (no request), line 422, balance reload with input kept, and 500 with trace ID and no success. Screenshots 2.8-*. | E2E | REQ_IO_A2.8.1–7 |
| — | Duplicate/soft-deleted code; file; partial batch | Not applicable: a Draft gets a server number, and there is no import or batch on this page. A 409 version conflict applies only to edit (WU-SD-004c2b). | — | — |

## WU-SD-004c2b1 Sales Outbound Edit Interface 2.9 (2026-11-21T04:28:32+08:00, ADR-149)

| ID | Category | Scenario | Layer | Requirement |
|---|---|---|---|---|
| SD-DEL-ED-001 | Normal/boundary | Service: PUT `{version, draft}` with exact 13+6-digit tokens and the saved tracking; DELETE with `?version=`; invalid id/version (0, negative, fractional) refused before any request. | U | REQ_IO_A2.9.6–7 |
| SD-DEL-ED-002～005 | Normal/state/boundary | Saved Draft/Rejected maps to the form in line and tracking order with trailing zeros trimmed; Pending, Posted, no lines, non-decimal text or null → no form. A quantity below the saved lot/serial total is refused, equal is accepted. A reply that loses or changes tracking is not a match. | U | REQ_IO_A2.9.1–4 |
| SD-DEL-ED-010～025 | Normal/validation/conflict/error/state/permission | Route needs VIEW+EDIT and is ordered before the list. Load with saved references, tracking and balance. Exact PUT, success only for a newer matching reply, labels kept. Lost tracking → not confirmed. No change → warning, no request. Below-tracking, empty date and above-balance refused locally. 409 keeps input, pauses saving with a reason, refresh asks first. Line 422 balance error marks the line; reload clears it and keeps input. INVALID_STATUS, 404 and 500 (trace ID) are stale, never success. Rejected: resubmit notice, delete disabled with reason. Pending/Posted: explained, no form. Delete: danger confirm with the version, then the deleted notice. Cancel sends nothing; the dirty prompt names lost input; delete 409/503 are not success. Without DELETE there is no button; without order VIEW there is a hint and no balance. Load 404, invalid id and a Cancelled order are explained. | U | REQ_IO_A2.9.1–8 |
| SD-DEL-ED-026～027 | Permission/state | The list detail shows Edit only for Draft/Rejected and only with `SAL.DELIVERY.EDIT`. | U | REQ_IO_A2.9.1 |
| SD-DEL-ED-030～032 | E2E/QA | From the list detail, a Rejected outbound: no-change warning, exact PUT body with kept lot, success in three languages and two themes, back to the list showing Draft. Below-tracking validation; 409 keeps input with the stale hint; refresh; line 422; 500 with trace ID and no success; delete confirm with the version, then the deleted notice. Posted by URL is explained. Screenshots 2.9-*. | E2E | REQ_IO_A2.9.1–8 |
| SD-DEL-ED-006～007 | Validation/boundary | Pick checks: blank, 51-character lot, 101-character serial, both numbers, duplicate (case and trailing space), zero, 7 decimals, serial quantity 2, empty list. Serial text parsing (CRLF, blank lines, trimming) and exact progress (0.000001, invalid quantity skipped, no picks). | U | REQ_IO_A2.9.9–10 |
| SD-DEL-ED-040～043 | Normal/validation/state | Lot editor: saved lot prefilled, exact progress, Add focuses the new row, trimmed PUT, removing a row still saves an incomplete Draft. Blank/zero/duplicate lot refused without a request, focus on the first pick. Serial editor: raw text with newline kept, duplicate refused, PUT with quantity 1 each, success clears the raw text. Unknown mode is read-only; editing is locked after a 409. | U | REQ_IO_A2.9.9–10 |
| SD-DEL-ED-034 | E2E/QA | Lot + Serial lines: blank-row refusal and focus, fixed row clears its issue and the error count (UX P2), complete/incomplete progress, exact PUT body, success; zh-Hant/zh-Hans/en, light/dark (screenshots `2.9-tracking-*`). | E2E | REQ_IO_A2.9.9–10 |
| — | Duplicate code; file; partial batch; timeout | Not applicable: no code input, import or batch. Timeout uses the same handler as 5xx (stale "not confirmed"), covered by the 500/503 cases. | — | — |

## WU-SD-004c2b2b2b Pick Lots/Serials from Stock (2026-11-21T09:17:35+08:00, ADR-152)

| ID | Category | Scenario | Layer | Requirement |
|---|---|---|---|---|
| SD-DEL-ED-044 | Normal/boundary/duplicate | `addStockPicks`: lots earliest expiry first (none last), each min(on hand, still needed), exact six-place text for 13-digit quantities, fully blank lot rows replaced; serials by number, quantity 1, until full; case-insensitive duplicates skipped; an invalid or already-reached quantity changes nothing and counts every choice as full. | U | REQ_IO_A2.9.11–12 |
| SD-DEL-ED-045 | Normal/boundary | Service GET with orderLineId, warehouseId, optional locationId and trimmed keyword; exact quantity text; invalid ids refused before any request. | U | REQ_IO_A2.9.11 |
| SD-DEL-ED-046～047 | Normal/partial | Lot line at its current location: on-hand columns, truncated notice, one request per keyword (pages sliced), capped picks and warning summary. Serial line without location adds whole serials up to the quantity and refreshes the serial text. | U | REQ_IO_A2.9.11–12 |
| SD-DEL-ED-048 | Validation/state | Disabled with a reason without a warehouse or with an invalid quantity; tracking mode changed and over-long keyword are explained with no picks and no request. | U | REQ_IO_A2.9.11 |
| SD-DEL-ED-049 | Conflict | Changing/clearing the location after picking shows the warning; a dialog result for a location changed meanwhile is ignored. | U | REQ_IO_A2.9.13 |
| SD-DEL-CR-023 | Normal/permission | Create receipt with Lot/Serial lines shows the hint and edit link with VIEW+EDIT; without EDIT the hint names a colleague and there is no link; plain lines show no hint. | U | REQ_IO_A2.8.7 |
| shared-ui select dialog | Error | Fixed description, load-dependent notice, page timeout key; without a timeout key a timeout shows the generic error. | U | REQ_IO_A2.9.11 |
| SD-DEL-ED-035 | E2E/QA | Lot and Serial lines pick from stock at the line location; exact GET query; duplicate and full counts; PUT body with picks; 500 with trace ID in the dialog; zh-Hant/zh-Hans/en, light/dark (screenshots `2.9-stock-*`). | E2E | REQ_IO_A2.9.11–13 |
| — | Permission 403/404 | Mapped by the dialog's error codes (`COMMON.PERMISSION.DENIED`, `SAL.ORDER.NOT_FOUND`), shared with other pickers; server behaviour covered by SD-DEL-HTTP-009. | U | REQ_IO_A2.9.11 |
| — | File; batch import; version 409 | Not applicable: the picker only changes the unsaved form; saving keeps the existing 409/422 handling (SD-DEL-ED-010～025). | — | — |

## WU-SD-004c2b2a Sales Outbound Detail Actions (2026-11-21T04:54:37+08:00, ADR-150)

| ID | Category | Scenario | Layer | Requirement |
|---|---|---|---|---|
| SD-DEL-ACT-001～002 | Normal/boundary | Service: submit and withdraw POST exactly `{"version":n}` (JSON) and read 13+6-digit quantities as text; id/version 0, negative or fractional refused before any request. | U | REQ_IO_A2.7.9–10 |
| SD-DEL-ACT-010～011 | Normal | Draft submit after confirmation with the loaded version; Pending reply → "pending approval, stock unchanged", row and detail show Pending, Withdraw replaces Submit/Delete; a Posted reply (zero-step) → "posted, stock issued" and movement IDs shown. | U | REQ_IO_A2.7.9 |
| SD-DEL-ACT-012 | Normal | Cancelled confirmation sends no request and shows no notice. | U | REQ_IO_A2.7.9 |
| SD-DEL-ACT-013 | Conflict/state | Withdraw sends the Pending version; a reply that is still Pending or not newer → "could not confirm", actions paused; refresh re-reads; then a newer Draft → "withdrawn to Draft". | U | REQ_IO_A2.7.10, REQ_IO_A2.7.12 |
| SD-DEL-ACT-014～015 | Error (422) | TRACKING_REQUIRED with `lineNo` marks only that saved line, says "still Draft" and keeps Submit enabled; INV.STOCK.INSUFFICIENT (no lineNo) shows the cause plus the stock hint; ORDER_BALANCE_EXCEEDED shows its cause without the stock hint. | U | REQ_IO_A2.7.12 |
| SD-DEL-ACT-016 | Conflict/state | 409, 404 and INVALID_STATUS each pause Submit and Delete with their own reason, focus Refresh, and refresh restores the actions. | U | REQ_IO_A2.7.12 |
| SD-DEL-ACT-017 | Error (500/network) | 500 shows the trace ID and "could not confirm"; status 0 likewise; never a success notice. | U | REQ_IO_A2.7.12 |
| SD-DEL-ACT-018 | Permission/normal | DELETE without EDIT: Delete shown, Edit hidden; danger confirmation with the number; DELETE `?version=3`; the list reloads without the row and the notice says deleted. | U | REQ_IO_A2.7.11 |
| SD-DEL-ACT-019 | Permission/state | VIEW only: no action buttons. Rejected: no Submit/Delete, hint to edit and save first. Posted: no actions. | U | REQ_IO_A2.7.9–11 |
| SD-DEL-ACT-020 | Concurrency | Closing the detail while a submit is in flight ignores the late reply (no notice, no detail). | U | REQ_IO_A2.7.12 |
| SD-DEL-ACT-030 | E2E/QA | From the list, submit a Draft (confirm dialog, exact body), Pending notice in three languages and two themes, row and history updated; withdraw back to Draft with the next version. | E | REQ_IO_A2.7.9–10 |
| SD-DEL-ACT-031 | E2E/QA | Line 422 marks line 2 and the line's tracking reads "None selected"; INV stock 422 with the hint; 500 with trace ID pauses actions, refresh; zero-step Posted with movement ID; Draft delete with `?version=5` and the row gone. | E | REQ_IO_A2.7.11–12 |
| — | Duplicate code; file; partial batch | Not applicable: single-document actions, no code input, import or batch. Cross-company/data scope: the server returns 404, covered by SD-DEL-ACT-016. | — | — |

## WU-SD-004c2b2b2a — tracking options endpoint (ADR-152)

| ID | Category | Scenario | Level | Trace |
|---|---|---|---|---|
| SD-DEL-HTTP-009 | Normal | CREATE-only user lists Lot line options at warehouse W1 without location: LOT-A 2.5 (expiry 2028-03-01), LOT-B 5 (2028-06-01), LOT-N 1 (no expiry) in that order with exact quantities and expiry date. | I | REQ_F4112.3.3 |
| SD-DEL-HTTP-009 | Boundary/related | Location L1 lists only LOT-C; warehouse W2 only LOT-D; a lot received then fully issued (LOT-E) and an issued serial (SN-3) are absent; serials sorted SN-1, SN-2 with quantity 1; trimmed lower-case keyword " lot-b " finds LOT-B. | I | REQ_F4112.3.3 |
| SD-DEL-HTTP-009 | Normal | Untracked line returns `None` and no rows. EDIT-only user gets the same results. | I | REQ_F4112.3.3 |
| SD-DEL-HTTP-009 | Permission | Anonymous 401; VIEW + INV.TRACKING.VIEW without CREATE/EDIT 403; Self-scope CREATE user on another owner's order 404 `SAL.ORDER.NOT_FOUND`; another company's order line 404. | I | REQ_F4112.3.3 |
| SD-DEL-HTTP-009 | Validation | 400 for warehouseId 0, orderLineId 0, locationId 0 and a 101-character keyword. | I | REQ_F4112.3.3 |
| SD-DEL-HTTP-001 | Docs | OpenAPI lists `GET /tracking-options` with 404. | I | REQ_F4112.3.3 |
| — | Duplicate code; partial batch; file; state; 409/422/500 | Not applicable: read-only query without code input, batch, import, document state or version. Truncation (> 100 rows) is a one-line `Take(limit + 1)` and is exercised by the UI unit in 004c2b2b2b with a mocked reply. | — | — |

Mutation check: removing the warehouse/location filter from the INV lot query made SD-DEL-HTTP-009 fail (LOT-C/LOT-D appeared), restoring it passed.

## WU-SD-004c2b2c — delivery line warehouse/location labels (2026-11-21T09:50:04+08:00, ADR-153)

| ID | Category | Scenario | Level | Trace |
|---|---|---|---|---|
| SD-DEL-HTTP-010 | Normal | POST and PUT receipts (CREATE+EDIT user) and the VIEW detail (VIEW only, no MD.WAREHOUSE.VIEW) return `warehouseCode/Name` and `locationCode/Name` per line; `warehouseActive`/`locationActive` true. | I | REQ_F4112.3.1, REQ_IO_A2.7.13 |
| SD-DEL-HTTP-010 | Boundary | A line without location returns null location labels and null `locationActive`. | I | REQ_IO_A2.7.13 |
| SD-DEL-HTTP-010 | Related | After MD deactivates the warehouse it is still labelled with `warehouseActive=false`; after the location is soft-deleted its labels and `locationActive` are null. | I | REQ_IO_A2.7.13 |
| SD-DEL-UI-014 | Normal/related | List detail shows "W1 · Main", "—" for no location, "B6 · Bin 6 (inactive)"; a reply without labels shows "Deleted warehouse (ID 2)" / "Deleted location (ID 6)". | U | REQ_IO_A2.7.13 |
| SD-DEL-UI-015 | Normal/related | E2E: zh-Hant and en labels, inactive mark, deleted fallback, headers no longer say "ID"; light/dark screenshots `2.7-references-*`. Data: read-only display of the mocked detail (no change to assert). | E | REQ_IO_A2.7.13 |
| SD-DEL-ED-050 | Normal/related | Edit page seeds labels from the saved reply, marks an inactive saved warehouse; picking another warehouse replaces the label, clears the mark and the location, and the page is dirty. | U | REQ_IO_A2.9.14 |
| SD-DEL-ED-011 | Boundary | A reply without labels shows the deleted-ID fallback. | U | REQ_IO_A2.9.14 |
| SD-DEL-ED-012 | Normal | After save, a just-picked label stays when the receipt has none. | U | REQ_IO_A2.9.14 |
| — | Duplicate/conflict; partial result; file | Not applicable: read-only fields, no code input, batch or file. |
| — | Permission | Labels need no MD permission; the delivery itself keeps VIEW/CREATE/EDIT checks and 404 for other company/out of scope (existing SD-DEL-HTTP tests). | I | REQ_F4112.3.1 |
| — | State | Same labels for Draft/Pending/Posted/Rejected; posted lines keep the current MD label (not a snapshot, ADR-153). | — | REQ_IO_A2.7.13 |
| — | Errors 400/409/422/500/timeout | No new request; detail load errors unchanged (SD-DEL-UI-013). | — | — |

Mutation check: without the `SavedAsync` labels SD-DEL-HTTP-010 failed (missing `warehouseCode`), with them it passed.

## WU-SD-004c2b2d — INV refusals name the delivery lines (2026-11-21T10:27:50+08:00, ADR-154)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-DEL-HTTP-011 | Normal | Zero-step submit (no approval flow): line 1 (no location) has stock, line 2 at BIN9 has none → 422 `INV.STOCK.INSUFFICIENT`, `lineNo` 2, `lineNos` [2]. | I | REQ_F4112.3.4, REQ_IO_A2.7.14 |
| SD-DEL-HTTP-011 | Boundary | Two lines of one item/warehouse each covered but together short (1.5 + 1 > 2) → `lineNo` 1, `lineNos` [1, 2]. | I | REQ_IO_A2.7.14 |
| SD-DEL-HTTP-011 | Related | Both refused deliveries stay Draft v1 with no SYS round; no `SALES_OUTBOUND` movement exists. | I | BR-SD-024 |
| SD-DEL-HTTP-006 (extended) | Related | The SYS approve endpoint passes INV's `lineNo` 1 / `lineNos` [1] through; the delivery stays Pending. | I | REQ_IO_A2.7.14 |
| INV-LOT-016 | Normal/boundary | INV names Out lines of the short key only, all lines of a combined lot shortage, the line of a missing lot/serial, incomplete tracking, and In lines for a serial already in stock (409). | I | BR-INV-069 |
| SD-DEL-CR-024 | Validation | `deliveryErrorLineNos` keeps positive integers only, ascending and unique, needs a valid `lineNo` on a 422. | U | REQ_IO_A2.7.14 |
| SD-DEL-ACT-021 | Normal | One named line (`INV.LOT.INSUFFICIENT`, [2]): only line 2 marked and an alert, "line 2", stock hint, still Draft. | U | REQ_IO_A2.7.14 |
| SD-DEL-ACT-022 | Boundary/error | [1, 2, 9, 0, 'x']: lines 1 and 2 marked with the reason, only line 1 is an alert, "lines 1, 2"; a later refusal naming only an unknown line clears the marks and shows the general message. | U | REQ_IO_A2.7.14 |
| SD-DEL-ACT-032 | Normal/i18n | E2E: two-line stock refusal in zh-Hant (第 1、2 行), en (lines 1, 2), zh-Hans; light/dark; then a one-line serial refusal moves the mark to line 2. Screenshots `2.7-action-stock-lines-*`, `2.7-action-serial-line-*`. Data: mocked 422, the detail stays Draft (no change to assert). | E | REQ_IO_A2.7.14 |
| — | Not applicable | Permission/scope: unchanged endpoints and permissions (covered by SD-DEL-HTTP-005/006). Import/batch: none. | — | — |

## WU-SD-004c4 — delivery form options by action (2026-11-21T11:03:49+08:00, ADR-155)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-DEL-HTTP-012 | Normal | CREATE-only lists the Approved order only (no Draft order, no other company), keyword on lower-case number, no `originalGross`; balance after a Pending 2 and a Draft 1: pending [2, 0], remaining [3, 1.5], no `unitPrice`; CREATE and EDIT both read it. | I | REQ_IO_A2.8.8, REQ_IO_A2.9.15 |
| SD-DEL-HTTP-012 | Permission/scope | Anonymous 401 on all four routes; VIEW + SAL.ORDER.VIEW + MD.WAREHOUSE.VIEW without CREATE/EDIT 403; EDIT-only cannot list orders (403); Self-scope CREATE sees an empty list and 404 `SAL.ORDER.NOT_FOUND`; other company and missing IDs 404. | I | ADR-155 |
| SD-DEL-HTTP-012 | Boundary/error | Unknown sort 400; a Draft order is returned with its status; warehouse options exclude other companies; locations of another company's warehouse 404 `MD.WAREHOUSE.NOT_FOUND`. | I | ADR-155 |
| SD-DEL-HTTP-001 (extended) | Docs | OpenAPI lists the four routes with 403/404 responses. | I | ADR-155 |
| SD-DEL-CR-018 (rewritten) | Permission | CREATE-only: pickers enabled, picker loads call `order-options` (page, pageSize, trimmed keyword, no status), `warehouse-options`, `warehouse-options/4/locations`; location picker knows `MD.WAREHOUSE.NOT_FOUND`; no permission hints. | U | REQ_IO_A2.8.8 |
| SD-DEL-ED-024 (rewritten) | Permission | VIEW + EDIT only: balance shown, no hints, warehouse picker enabled and sends the keyword to `warehouse-options`. | U | REQ_IO_A2.9.15 |
| SD-DEL-CR-025 | Normal/permission | E2E: CREATE-only user picks order, warehouse and location, sees the balance and saves; no request goes to `/sal/orders` or `/md/warehouses`. Screenshots `2.8-create-only-*` (zh-Hant light/dark). Data: mocked 201 receipt matched line by line. | E | REQ_IO_A2.8.8 |
| — | Not applicable | Import/batch/approval: none. Concurrency: read-only routes; the balance stays advisory and is rechecked on submit (SD-DEL-HTTP-005). | — | — |

### WU-SD-004c3 Sales outbound 2.7–2.9 error and three-language QA round (2026-11-21, ADR-156)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| apiErrorKey (extended) | Error/permission | 403 with `COMMON.PERMISSION.DENIED` that the page did not list, and 403 without a body, map to `errors.COMMON.PERMISSION.DENIED` (not "unexpected"). | U | ADR-156 |
| SD-DEL-QA-001 | Normal/consistency | E2E stateful mock: create (5.25 + 2) → list row 7.25 Draft and detail → edit line 1 to 6 (PUT v1) → list 8 → submit (v2) → Pending v3 on the row and the detail. Data: stored lines, version and status equal what each screen shows; writes exactly POST, PUT v1, SUBMIT v2. Screenshots `2.8-qa-success-*`, `2.7-qa-detail-*`, `2.9-qa-success-*`, `2.7-qa-submitted-*` (3 languages × light/dark, no untranslated key). | E | REQ_IO_A2.7–2.9 |
| SD-DEL-QA-002 | Error | 2.7 list: server 403 says "no permission" (ADR-156); offline says the server can't be reached; timeout has its own message; none of them shows the empty-list text; retry recovers. Detail: offline, timeout and 403 keep the list and offer retry. Screenshots `2.7-qa-forbidden-*`, `2.7-qa-offline-*` (3 languages × 2 themes), `2.7-qa-timeout-*`, `2.7-qa-detail-{offline,timeout,forbidden}-*`. | E | REQ_IO_A2.7, ADR-156 |
| SD-DEL-QA-003 | Error/concurrency | 2.7 submit: 403 shows the reason and "nothing changed"; 409 pauses actions and focuses Refresh; offline and timeout say the result is unconfirmed and pause; no success notice; the row stays Draft; 4 submits sent. Screenshots `2.7-qa-action-*`. | E | REQ_IO_A2.7.9–12 |
| SD-DEL-QA-004 | Error | 2.8: order-option 403 and timeout show an error with Retry; save 403 shows the reason and keeps input; save offline is "unconfirmed" and locks Save (no duplicate). Screenshots `2.8-qa-order-*`, `2.8-qa-save-{forbidden,offline}-*`. | E | REQ_IO_A2.8 |
| SD-DEL-QA-005 | Error | 2.8 save timeout is unconfirmed, success is not shown, Save is disabled; exactly 1 POST. | E | REQ_IO_A2.8 |
| SD-DEL-QA-006 | Error | 2.9: load 403/timeout show an error, no form, Retry; save 403 keeps input; save offline/timeout are unconfirmed, keep input and require Refresh; 3 PUTs. Screenshots `2.9-qa-*`. | E | REQ_IO_A2.9 |
| SD-DEL-QA-007 | Permission | No SAL.DELIVERY.* permission: `/sal/deliveries`, `/new` and `/61/edit` go to `/forbidden` without any delivery request. | E | REQ_IO_A2.7–2.9 |
| — | Not applicable | 422 / 500 / 409 on save were already covered by SD-DEL-CR-022, SD-DEL-ED-031 and SD-DEL-ACT-031/032 and are not repeated. Create has no version, so it has no 409. Import, file and batch: none on these screens. | — | — |

### WU-SD-005a8 Price rule table and create/list/detail API (2026-11-21T14:21:56+08:00, ADR-157)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-PRICE-HTTP-001 | Permission/docs | Anonymous 401 on list, detail and create; OpenAPI documents GET 400, POST 201/400/403/404/409/422, detail 404. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-002 | Normal | CREATE-only user saves a default rule (12.345678, ceiling 5.5), a customer rule and a USD rule; 201 + Location, version 1, active. SQL row matches, `created_by` is set. VIEW lists them by default sort (`-validFrom`, then ID), by `sort=id`, and with customer / defaultOnly / currency / asOf / item+asOf+active / active=false filters; page 2 of size 2 holds the last row, totalCount 3; detail returns the customer rule. | I | REQ_F4112.4.1, REQ_DS43.204 |
| SD-PRICE-HTTP-002 | Permission | CREATE without VIEW gets 403 on the list; VIEW without CREATE gets 403 on create. Another company's viewer gets 404 `SAL.PRICE_RULE.NOT_FOUND` for a rule ID, as for an unknown ID; another company's item filter returns nothing. | I | ADR-157 |
| SD-PRICE-HTTP-002 | Boundary/error | List 400: unknown sort, itemId 0, customerId −1, lowercase currency, customerId with defaultOnly, pageSize 201. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-003 | Duplicate/conflict | Inclusive dates: adjacent periods are allowed. Customer and default rules, other currencies and other items are separate keys. An overlap across two rules returns 409 `SAL.PRICING.RULE_OVERLAP` with `conflictingRuleIds` [first, second]; a customer key also conflicts. A rule that is inactive or soft-deleted no longer blocks the same dates. | I | BR-SD-026, BR-SD-028 |
| SD-PRICE-HTTP-004 | Boundary/error | 400 lists every bad field at once (item, customer, currency, validTo < validFrom, negative price, ceiling > 100); price with 7 decimal places is 400; price at 10^13 is 400. Allowed boundaries: price 0, ceiling 100, a one-day rule, and 9,999,999,999,999.999999 saved exactly. | I | SD-PRICE-006 |
| SD-PRICE-HTTP-004 | Relation | Another company's item or customer is 404 `MD.ITEM.NOT_FOUND`; an unknown currency is 404 `MD.CURRENCY.NOT_FOUND`. Inactive currency, inactive item or rejected customer is 422 (`MD.CURRENCY.INACTIVE`, `MD.ITEM.INACTIVE`, `MD.CUSTOMER.NOT_APPROVED`). Only the two boundary rules are saved. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-005 | Concurrency | Four parallel POSTs with overlapping dates on one default key, three rounds: each round 1 × 201 and 3 × 409; 3 rows in SQL. | I | SD-PRICE-007, ADR-157 |
| — | Not applicable | Partial results, files: none (no batch or import). State: rules have no workflow; edit, deactivate and stale version (409) are left in WU-SD-005a. Front end and 500/timeout display: WU-SD-005b. | — | — |

### WU-SD-005a9 Price rule edit and deactivate (2026-11-21T14:40:28+08:00, ADR-158)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-PRICE-HTTP-001 | Permission/docs | Anonymous 401 on PUT and deactivate; OpenAPI documents PUT 200/400/403/404/409/422 and deactivate 200/403/404/409/422. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-006 | Normal | EDIT-only user (Self scope) shrinks and reprices a rule within its own dates: 200, version 2, saved exactly; one `sys_audit_log` UPDATE row with `unit_price` old 10 / new 11.5. | I | REQ_F4112.4.1, ADR-158 |
| SD-PRICE-HTTP-006 | Concurrency | Stale version 1 and version 0 are 409 `COMMON.CONCURRENCY.CONFLICT`. | I | ADR-158 |
| SD-PRICE-HTTP-006 | Duplicate/conflict | Extending into the neighbour is 409 `SAL.PRICING.RULE_OVERLAP` listing only the neighbour (self excluded). Moving the neighbour to the customer key over the customer's rule is 409 listing that rule. A clean key change (default → customer) is 200 and frees the default dates for a new rule. | I | BR-SD-026 |
| SD-PRICE-HTTP-006 | Permission/error | 400 lists item/customer/currency/validTo/price; VIEW-only and CREATE-only users get 403; unknown and other-company IDs 404 `SAL.PRICE_RULE.NOT_FOUND`; other-company item 404 `MD.ITEM.NOT_FOUND`; inactive currency 422 `MD.CURRENCY.INACTIVE`; inactive rule 422 `SAL.PRICING.RULE_INACTIVE`. None of these change the rule. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-007 | Normal/state | DEACTIVATE-only user retires a rule: 200, `isActive=false`, version 2, price unchanged; row kept (not deleted); one UPDATE audit row with `is_active`; the dates accept a new rule. A second deactivate is 422 `SAL.PRICING.RULE_INACTIVE`. | I | ADR-158 |
| SD-PRICE-HTTP-007 | Permission/error | EDIT-only and CREATE-only users 403; unknown ID 404; stale version 409. | I | ADR-158 |
| SD-PRICE-HTTP-008 | Concurrency | Two edits swap keys in opposite directions (default ↔ customer) at the same time, four rounds: both 200 every round (no deadlock), versions end at 5. | I | ADR-158 |
| SD-PRICE-HTTP-009 | Concurrency/error | Another transaction holds the key lock; with a 300 ms timeout create, edit and deactivate each return 409 `SAL.PRICING.RULE_LOCK_TIMEOUT` (not 500); the rule is unchanged. | I | ADR-158 |
| — | Not applicable | Partial results, import, files: none. Reactivation is not offered (create a new rule). Front end: WU-SD-005b. | — | — |

### WU-SD-005a10 Price quote endpoint (2026-11-21T14:52:23+08:00, ADR-159)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-PRICE-HTTP-010 | Permission/docs | Anonymous quote is 401; OpenAPI documents 200/400/403/404/409/422. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-010 | Normal | VIEW-only user (Self scope): customer rule (90.123456, ceiling 12.5) wins on 01-10 and 01-20 (inclusive); default rule on 01-09 and 01-21 with `customerId` null; response carries rule ID, version 1, item, currency, price, ceiling, valid dates and `asOf`; USD quote uses the USD rule. | I | BR-SD-027 |
| SD-PRICE-HTTP-010 | Boundary/state | Leap day 2028-02-29 closes the February default rule; an inactive customer rule (02-15) and a soft-deleted one (02-22) never take over from the default. | I | BR-SD-026 |
| SD-PRICE-HTTP-010 | Permission | Each of `SAL.QUOTATION.CREATE`, `SAL.QUOTATION.EDIT`, `SAL.ORDER.CREATE`, `SAL.ORDER.EDIT` alone may quote; `SAL.PRICING.CREATE`, `SAL.ORDER.VIEW`, `SAL.DELIVERY.CREATE` alone are 403 `COMMON.PERMISSION.DENIED`. No rule row (version, status, price, updated time) changes. | I | ADR-159, BR-SD-030 |
| SD-PRICE-HTTP-011 | Duplicate/conflict | Two active default rows overlapping (second written outside the API): 409 `SAL.PRICING.DUPLICATE_ACTIVE_RULE`, `conflictingRuleIds` [first, second], no price; a clean customer rule on 01-05 still wins (200). | I | BR-SD-027 |
| SD-PRICE-HTTP-011 | Error | 422 `SAL.PRICING.RULE_NOT_FOUND` before the first rule, in the gap after the last, for an item without rules and for an unknown currency (EUR); another company quoting its own IDs never sees this company's rules (422). | I | BR-SD-027 |
| SD-PRICE-HTTP-011 | Boundary/error | No parameters: 400 lists itemId, customerId, currency, businessDate; itemId 0, customerId −1, lowercase or 4-letter currency, 2028-02-30, "tomorrow", itemId=abc are 400. | I | REQ_F4112.4.1 |
| SD-PRICE-HTTP-011 | Relation | Other-company item 404 `MD.ITEM.NOT_FOUND`, other-company customer and unknown item 404; rejected customer 422 `MD.CUSTOMER.NOT_APPROVED`; inactive item 422 `MD.ITEM.INACTIVE`. Nothing written. | I | ADR-159 |
| — | Not applicable | Partial results, import, files: none (single lookup). State: rules have no workflow; inactive/deleted covered above. Concurrency: read-only, no lock (advisory; save rechecks in WU-SD-005a11). Front end and 500/timeout display: WU-SD-005b. | — | — |

### WU-SD-005a11a Quotation line pricing on save (2026-11-21T15:15:49+08:00, ADR-160)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-QPRICE-HTTP-001 | Normal/boundary | Writer without override: line 1 hint = customer rule (default rule ignored), discount equal to ceiling 10, business date = rule `valid_to` (inclusive); line 2 without hint selects the default rule of item 2. 201; response and SQL show `Rule`, rule ID/version/price/ceiling, no override data. | I | BR-SD-027, BR-SD-029a |
| SD-QPRICE-HTTP-002 | Permission/error | Without `SAL.PRICING.OVERRIDE`: manual price, discount 10.0001 > 10 and an item with no rule are each 403 `SAL.PRICING.OVERRIDE_REQUIRED`; no quotation saved. With it: missing, blank and 201-character reason are 400 `lines[0].overrideReason`, nothing saved; then a save is 201 with number 001 (refusals consumed no number), Override lines keep rule price 100 vs chosen 95, trimmed reason, actor = the user, time; the no-rule line has no rule snapshot. | I | BR-SD-028 |
| SD-QPRICE-HTTP-003 | Conflict/error | Hint with rule ID but no version and rule ID 0 are 400 (before 403). After the rule is edited (version 2), hint version 1 is 409 `SAL.PRICING.STALE_QUOTE` with `lineNo` 1. Inactive, USD and expired rules are never selected: line 2 is 403 with `lineNo` 2 although line 1 is valid; no quotation saved. | I | BR-SD-027, BR-SD-030 |
| SD-QPRICE-HTTP-004 | State/permission | Editing the rule price, and later deactivating it, leaves the saved line (price, rule price, version) unchanged. Edit with the old hint is 409 and changes nothing. A user with Company EDIT but Self-scope override cannot override another owner's quotation (403). Owner edits with the new hint: Rule, version 2, price 120. | I | BR-SD-029, ADR-160 |
| SD-QPRICE-HTTP-005 | Data integrity | SQL CHECK rejects Rule without rule ID/version, Rule with price ≠ rule price, Override without reason/actor/time, unknown mode, override reason on a Rule line, and another company's rule ID (FK); Legacy without provenance and a complete Override are accepted. | I | ADR-160 |
| SD-QPRICE-HTTP-006 | Read model | After the rule is deactivated, GET detail still returns `Override`, rule ID, ceiling 5, discount 6, reason, actor and time. | I | BR-SD-029 |
| SD-QUO-* (existing) | Regression | Existing quotation create/edit/scope/concurrency tests now grant override and send a reason (no rules exist in them) and additionally assert Override provenance; their original assertions are unchanged. | I | ADR-160 |
| — | Not applicable | Partial results, import, files: none (whole-document save). Duplicate active rule on save: same `Select` as the quote (SD-PRICE-HTTP-011, unit SD-PRICE-*) and cannot be created through the API; the save path adds only `lineNo`. Manual order and conversion: WU-SD-005a11b. Front end: WU-SD-005b. | — | — |
| SD-QPRICE-HTTP-007 | Relation/state | Approved quotation with a Rule line and an Override line converts after its rule was repriced and deactivated; the converter has only `SAL.ORDER.CREATE/VIEW`. Order lines copy mode, rule ID/version/price/ceiling, reason, actor, time and price unchanged. Unit `SalesOrderQuotationSnapshotTests` asserts every quotation line member (now including provenance) is copied. | I, U | BR-SD-028, ADR-160 |

### WU-SD-005a11b Manual sales order line pricing on save (2026-11-21T15:46:14+08:00, ADR-160)

| ID | Type | Scenario / expected | Level | Trace |
|---|---|---|---|---|
| SD-OPRICE-HTTP-001 | Normal/boundary | Writer without override creates an order: line 1 hint = customer rule (default ignored), discount = ceiling 10, business date = rule `valid_to`; line 2 without hint selects item 2 default rule. 201; response, GET detail and SQL show `Rule` with rule ID/version/price/ceiling and no override data. | I | BR-SD-029b |
| SD-OPRICE-HTTP-002 | Permission/error | Without `SAL.PRICING.OVERRIDE`: manual price, discount 10.0001 and no-rule item are each 403 `SAL.PRICING.OVERRIDE_REQUIRED` with `lineNo`; nothing saved. With it: missing/blank/201-char reason are 400 `lines[0].overrideReason`; then 201 with number 001; Override lines keep rule price 100 vs 95, trimmed reason, actor, time; no-rule line has no snapshot; response carries the provenance. | I | BR-SD-028, BR-SD-029b |
| SD-OPRICE-HTTP-003 | Conflict/error | Rule ID without version, version without rule ID and rule ID 0 are 400 (before 403). After a rule edit, old version hint is 409 `SAL.PRICING.STALE_QUOTE` with `lineNo` 1. Inactive/USD/expired rules never selected → line 2 403 `lineNo` 2, no order saved; then a valid save gets number 001. | I | BR-SD-027, BR-SD-030 |
| SD-OPRICE-HTTP-004 | State/permission | Rule repricing/deactivation never rewrites the saved order line. Edit with old hint 409, unchanged. Company EDIT + Self override cannot override another owner's order (403); same user can re-save at the new rule price (Rule v2). Owner with override edits to Override (reason, actor, rule price 120 kept); GET detail returns it after deactivation. | I | BR-SD-029, BR-SD-029b |
| SD-OPRICE-HTTP-005 | Data integrity | `sal_order_line` CHECK rejects incoherent provenance (as SD-QPRICE-HTTP-005) and another company's rule (FK); Legacy and complete Override accepted. | I | ADR-160 |
| SD-ORDER-* (existing) | Regression | Existing manual order create/edit/scope/concurrency/HTTP tests have no price rules, so they now grant `SAL.PRICING.OVERRIDE` and send a reason; assertions unchanged. Converted/non-Draft edits keep their 422 status errors (status checked before pricing). | I, U | ADR-160 |
| — | Not applicable | Partial results/import/files: whole-document save. Soft-deleted duplicates: no codes. Duplicate active rule on save: same path as quotation (`Select`), cannot be created via API. Front end: WU-SD-005b. | — | — |

## WU-SD-005b1a Sales Price Rule View Interface 2.11 (2026-11-21T19:20:19+08:00, ADR-161)

| Test ID | Category | Scenario and expected result | Type | Requirement |
|---|---|---|---|---|
| SD-PRICE-HTTP-012 | Normal/relation | Create, list (VIEW, Self scope), detail return current MD `itemCode/Name`, `customerCode/Name`; an inactive item keeps its label; a company default rule has null customer labels; a soft-deleted item has null item labels but keeps `itemId`. | I (real SQL/HTTP) | REQ_F4112.4.1, ADR-161 |
| price-rule.service spec (4) | Contract | Only applied filters are sent; `defaultOnly` never with a customer; exact price digits kept; deactivate sends the read version and refuses invalid IDs before sending; each refusal code maps to a named message. | U | REQ_IO_A2.11.2, 2.11.7 |
| SD-PRULE-UI-002～003 | Normal/empty | Labels, "Company default" tag, exact price/ceiling; deleted item by ID; empty list vs filtered no-result differ. | U | REQ_IO_A2.11.6 |
| SD-PRULE-UI-004～005 | Validation/URL | Lowercase currency upper-cased; impossible date, 2-letter currency, customer scope without customer: inline message, no GET; URL restores filters/page/sort. | U | REQ_IO_A2.11.1–5 |
| SD-PRULE-UI-006～007 | Action/conflict | Deactivate after danger confirmation with row version then reload; cancel sends nothing; 409 stale explained and list reloads. | U | REQ_IO_A2.11.7 |
| SD-PRULE-UI-008～011 | Error/permission | 500 keeps rows and shows trace ID; no DEACTIVATE or inactive row → no button; list failure → Retry; company switch reloads page 1 without old customer; route and menu need `SAL.PRICING.VIEW`. | U | REQ_IO_A2.11.6–7 |
| SD-PRULE-UI-101 | E2E (simulated) | Three languages, light/dark, 768 px tablet without horizontal overflow, dates and amounts on one line; screenshots `2.11-normal-*`, `2.11-tablet-*`. | E2E | REQ_IO_A2.11.6 |
| SD-PRULE-UI-102 | E2E | Filters go to the server and URL; invalid input sends nothing; empty vs no-result; screenshots `2.11-empty-*`, `2.11-no-results-*`, `2.11-validation-*`. | E2E | REQ_IO_A2.11.1–6 |
| SD-PRULE-UI-103 | E2E | Deactivate confirmation shows consequence; request body carries shown version; success only after reply; 409 explained; screenshots `2.11-deactivate-*`, `2.11-deactivated-*`. | E2E | REQ_IO_A2.11.7 |
| SD-PRULE-UI-104 | E2E | List 500 shows trace ID and Retry; screenshot `2.11-error-*`. | E2E | REQ_IO_A2.11.6 |
| SD-PRULE-UI-012 | Permission/options | User with only `SAL.PRICING.VIEW`: item and customer pickers load `item-options` / `customer-options` (trimmed keyword, no MD request), dialog states active-only, chosen IDs go to the list query. | U | REQ_IO_A2.11.1–2 |
| SD-PRULE-UI-013 | Contract | Option calls send page, size and trimmed keyword only; currency options keep decimal places. | U | REQ_IO_A2.11.1–2 |
| SD-PRULE-UI-105 | E2E | Pricing viewer without MD permission filters by item and customer through the option dialogs; URL and list query carry the IDs; zero `/api/v1/md/` requests; screenshots `2.11-item-picker-*`, `2.11-filtered-by-options-*`. | E2E | REQ_IO_A2.11.1–2 |
| SD-PRICE-HTTP-013 | Real SQL/HTTP | VIEW, CREATE or EDIT alone lists active items, active approved customers, active currencies; inactive item, pending or soft-deleted customer, inactive currency and other company never offered; keyword and paging. | I | REQ_IO_A2.11.1–2 |
| SD-PRICE-HTTP-014 | Real SQL/HTTP | Anonymous 401; quotation writer with MD view but no pricing permission 403; `sort=name` and `pageSize=0` 400; routes in OpenAPI with 200/401/403. | I | REQ_IO_A2.11.1–2 |
| — | Not applicable | Import, batch, partial success, approval: none on this page (single-row deactivate). Create/Edit (2.12/2.13) in WU-SD-005b1b; document pricing controls in 005b2. | — | — |

## WU-SD-005b1b2 Sales Price Rule Create / Edit Interfaces 2.12 / 2.13 (2026-11-21T19:54:22+08:00, ADR-163)

Tests written first: the form helper and page specs failed to compile/run before `price-rule-form.ts` and the page existed. Backend unchanged (SD-PRICE-HTTP-001～014 already cover the write API).

| ID | Category | Scenario | Level | Requirement |
|---|---|---|---|---|
| SD-PRULE-FORM-001～002 | Normal/boundary | Default and customer rule valid; empty draft lists every field at once; default ceiling 0 is valid. | U | REQ_IO_A2.12.1–6 |
| SD-PRULE-FORM-003～004 | Boundary | Price 0 and 9999999999999.999999 accepted, 10^13 / 7 decimals / minus / exponent refused; ceiling 0 and 100.0000 accepted, 100.0001 / 5 decimals refused; 2028-02-29 accepted, 2027-02-29 refused; same-day period allowed, end before start refused. | U | REQ_IO_A2.12.4–6 |
| SD-PRULE-FORM-005～006 | Contract | JSON keeps exact digits, strips leading zeros, null customer for default, version only on PUT; invalid draft/version throws before sending; saved rule → draft keeps digits and scope. | U | REQ_IO_A2.12.7, 2.13.1 |
| SD-PRULE-FORM-007 | Error mapping | Overlap (IDs, malformed IDs ignored), lock timeout, stale, inactive, not found, MD item/customer/currency refusals per field, 400 field names, 403, other 4xx refused, 5xx/0/timeout unknown. | U | REQ_IO_A2.12.8, 2.13.4 |
| SD-PRULE-FORM-008 | Contract | Service POST/PUT bodies and JSON header; exact digits in reply; invalid ID/version refused before sending. | U | REQ_IO_A2.12.7, 2.13.3 |
| SD-PRULE-FORM-UI-001 | Normal | Create from option dialogs (active-only description), currency hint, success only after reply, Save locked with reason, Create another resets. | U | REQ_IO_A2.12.1–8 |
| SD-PRULE-FORM-UI-002 | Validation | Six invalid fields counted and shown per field; no POST; customer scope sends the customer. | U | REQ_IO_A2.12.2, 2.12.7 |
| SD-PRULE-FORM-UI-003～004 | Conflict | Overlap names #12, #3 and links to the same-key active list; editing clears it; lock timeout keeps Save for a retry. | U | REQ_IO_A2.12.8 |
| SD-PRULE-FORM-UI-005 | Error 500 | Unknown outcome with trace ID; Save locked with reason; second save sends nothing. | U | REQ_IO_A2.12.8 |
| SD-PRULE-FORM-UI-006 | Error 422/400/403 | MD inactive item / currency on their fields; server 400 fields; 403 message. | U | REQ_IO_A2.12.8 |
| SD-PRULE-FORM-UI-007～008 | Edit/409 | Prefill exact digits, PUT with version read, adopt new version; stale version locks the form, Reload restores and the next PUT carries the new version. | U | REQ_IO_A2.13.1–4 |
| SD-PRULE-FORM-UI-009～010 | State/permission | Inactive rule read-only and no PUT; 422 became inactive explained; another company's rule (404) and non-numeric ID show "not found" without a form (the latter sends nothing). | U | REQ_IO_A2.13.1–2 |
| SD-PRULE-FORM-UI-011～012 | Relation/state | Deleted item by ID; saved inactive currency shown as unavailable; currency load failure with Retry; unsaved-changes confirm; company switch starts fresh. | U | REQ_IO_A2.12.3, 2.13.1 |
| SD-PRULE-UI-014～015 | Permission | New needs CREATE, Edit needs EDIT and an active row; routes guarded (CREATE; VIEW + EDIT) with leave guard. | U | REQ_IO_A2.11.6 |
| SD-PRULE-UI-106 | E2E (stateful) | Pricing maintainer without MD permission: validation sends nothing; overlap with #31 saves nothing (store still 2 rules); after changing dates 201 "rule 42 created"; list shows the new price/date; zero MD requests. Screenshots `2.12-validation/filled/overlap/created-*`. | E2E | REQ_IO_A2.12.1–8 |
| SD-PRULE-UI-107 | E2E (stateful) | Inactive row has no Edit; edit prefill; stale version (server at v3) explained and locked; Reload then save → version 4 and the list shows 1,288. Screenshots `2.13-normal-*` (3 languages), `2.13-stale-*`, `2.13-saved-*`. | E2E | REQ_IO_A2.13.1–4 |
| SD-PRULE-UI-108 | E2E | Inactive rule read-only and dimmed; customer rule 500 → unknown with trace ID, Back to list, store unchanged; 768 px tablet without overflow. Screenshots `2.13-inactive-*`, `2.12-unknown-*`, `2.12-tablet-*`. | E2E | REQ_IO_A2.12.8, 2.13.2 |
| — | Not applicable | Import/file, batch and partial results: single-rule form. Approval: rules are active on save (ADR-157). Timeout is covered as "unknown" (same path as 0/5xx) in SD-PRULE-FORM-007. Cross-company write: server returns 404 (SD-PRICE-HTTP), page shows "not found". | — | — |

Results: frontend 1072/1072 (2 known unhandled TimeoutError from MD specs, WU-P0-FE-TIMEOUT-LEAK); price-rule E2E 8/8 (includes production build); UX review P2 (disabled inputs looked editable) and P3 (no list link after unknown result) fixed and asserted in SD-PRULE-UI-108.

## WU-SD-005b2a Quotation pricing controls 2.2 / 2.3 (2026-11-21T20:12:34+08:00, ADR-164)

U = Vitest unit, E = Playwright with mocked API (screenshots `2.2-pricing-*`).

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-QPRICE-UI-U01～U06 | Rules | Exact decimal compare/trim; override need mirrors `Decide` (unknown/legacy/stale/failed leave it to the server); pricing 403/409 code + lineNo + IDs, other codes ignored; quote 422/409/timeout/network states; saved source labels. | U | REQ_IO_A2.2.14–17, 2.1.10 |
| SD-QPRICE-UI-001 | Contract | Quote sends the four lookup fields, keeps exact digits. | U | REQ_IO_A2.2.14 |
| SD-QPRICE-UI-002～003 | Contract / Validation | Hint pair only together, reason trimmed only when entered; saved rule price exact text; reason required only where flagged, > 200 refused. | U | REQ_IO_A2.2.16 |
| SD-QPRICE-UI-004 | Normal | Rule price applied after date; POST carries pair, no reason. | U | REQ_IO_A2.2.14 |
| SD-QPRICE-UI-005 | Validation | Changed price / discount above ceiling → reason required, no POST; Use rule price clears it. | U | REQ_IO_A2.2.15–16 |
| SD-QPRICE-UI-006 | Permission 403 | No rule + no OVERRIDE: notice up front; reason sent trimmed; 403 line message, no success. | U | REQ_IO_A2.2.15, 2.2.17 |
| SD-QPRICE-UI-007 | Conflict | Duplicate rules: no price applied, IDs listed. | U | REQ_IO_A2.2.14 |
| SD-QPRICE-UI-008 | Race / Network | Late older reply ignored; network failure → retry works. | U | REQ_IO_A2.2.14–15 |
| SD-QPRICE-UI-009 | Edge | Removing a line keeps the other line's source. | U | REQ_IO_A2.2.14 |
| SD-QPRICE-UI-010 | Stale 409 | Edit resends saved pair; STALE_QUOTE is not a version conflict; line re-quoted (new version, price). | U | REQ_IO_A2.3.16, 2.2.17 |
| SD-QPRICE-UI-011 | Edit states | Legacy line: optional reason; Override line keeps reason, no pair; version 409 still a conflict. | U | REQ_IO_A2.3.16 |
| SD-QPRICE-UI-012 | Validation 400 | Server `lines[0].overrideReason` opens and requires the reason, detail names line 1. | U | REQ_IO_A2.2.16 |
| SD-QPRICE-UI-101 | E2E normal + validation | Rule applied, deviation blocked with reason error (3 languages), Use rule price, saved body = price 100.5, discount 10, pair, no reason. | E | REQ_IO_A2.2.14–16 |
| SD-QPRICE-UI-102 | E2E 422 + 403 | No rule → reason sent; 403 shows line message, no success; 3 languages. | E | REQ_IO_A2.2.17 |
| SD-QPRICE-UI-103 | E2E permission | Without OVERRIDE, deviation says it cannot be saved. | E | REQ_IO_A2.2.15 |
| SD-QPRICE-UI-104 | E2E 409 + detail | Edit 409 STALE_QUOTE re-quotes line 1 only, second PUT sends v4 pair and kept reason; detail shows sources (3 languages). | E | REQ_IO_A2.3.16, 2.1.10 |

Existing SD-QUOTE-UI-014/016/023 and SD-QUOTE-EDIT-010 now mock the quote lookup (the form reads it after a date change).

## WU-SD-005b2b Manual order pricing controls 2.5 / 2.6 (2026-11-21T20:34:38+08:00, ADR-165)

U = Vitest unit, E = Playwright with mocked API (screenshots `2.5-pricing-*`, `2.6-pricing-*`, `2.4-pricing-*`).

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-OPRICE-UI-U01 | Contract / Validation / Edit mapping | Pair only together, reason trimmed only when entered (JSON escaping); reason required only where flagged, 200 ok, 201 tooLong; saved Rule/Override/Legacy map to pair/reason correctly. | U | REQ_IO_A2.5.19, 2.6.17 |
| SD-OPRICE-UI-001 | Normal | Lookup sends item, customer, header currency, date; rule price applied; POST carries pair, no reason. | U | REQ_IO_A2.5.17 |
| SD-OPRICE-UI-002 | Boundary | Header currency change re-quotes every line and clears the old pair; invalid currency sends no lookup. | U | REQ_IO_A2.5.17 |
| SD-OPRICE-UI-003 | Validation | Deviation → reason required, no POST; Use rule price clears it. | U | REQ_IO_A2.5.18–19 |
| SD-OPRICE-UI-004 | Permission 403 | No rule + no OVERRIDE said up front; reason sent trimmed; 403 line message and "Line 1" alert; not uncertain, no success. | U | REQ_IO_A2.5.18, 2.5.20 |
| SD-OPRICE-UI-005 | Conflict / Race | Late older reply ignored; duplicate rules list IDs, no price applied. | U | REQ_IO_A2.5.17 |
| SD-OPRICE-UI-006 | Edge | Removing a line keeps the other line's source. | U | REQ_IO_A2.5.17 |
| SD-OPRICE-UI-007 | Stale 409 | Edit resends saved pair; STALE_QUOTE not a version conflict; line re-quoted to v4 / 101. | U | REQ_IO_A2.6.17 |
| SD-OPRICE-UI-008 | Edit states | Legacy optional reason; Override keeps reason (unchanged edit = no change, no PUT); version 409 still a conflict. | U | REQ_IO_A2.6.17 |
| SD-OPRICE-UI-009 | Validation 400 | Server `lines[0].overrideReason` opens and requires the reason; detail "Line 1: …". | U | REQ_IO_A2.5.19 |
| SD-OPRICE-UI-010 | Status | Quotation conversion never looks up prices. | U | REQ_IO_A2.5.17 |
| SD-OPRICE-UI-101 | E2E normal + validation | Rule applied, deviation blocked with reason error and focus (3 languages, light/dark), Use rule price, body = 100.5 / 10 / pair, no reason, one lookup. | E | REQ_IO_A2.5.17–19 |
| SD-OPRICE-UI-102 | E2E 422 + 403 | No rule → reason sent; 403 line message, alert names line 1 with generic (not "quotation") wording, no success/unknown, save still enabled, one write; 3 languages. | E | REQ_IO_A2.5.20 |
| SD-OPRICE-UI-103 | E2E permission | Without OVERRIDE, deviation says it cannot be saved. | E | REQ_IO_A2.5.18 |
| SD-OPRICE-UI-104 | E2E 409 + detail | Edit STALE_QUOTE re-quotes line 1 only (no conflict), second PUT v1 sends v4 pair and kept reason; list detail shows sources (3 languages). | E | REQ_IO_A2.6.17, 2.4.9 |
| — | Not applicable | Import/file, batch, partial results: single-document form. Status limits unchanged (edit only Draft/Rejected; converted orders not editable). Cross-company: server 404 uses the existing edit-unavailable path. Lookup timeout: same `failed` state as ADR-164 U-tests. | — | — |

Existing SD-ORDER-UI-008/009, SD-ORDER-EDIT-002/003/009/012 and SD-ORDER-QA-001 now mock the quote lookup with the price they keep; the create and QA specs also assert the sent pair.

Results: frontend 1109/1109 (2 known unhandled TimeoutError, WU-P0-FE-TIMEOUT-LEAK); order E2E create/edit/QA/view/pricing and quotation pricing/create/edit all passed; production build passed; no backend change.

## WU-SD-005b3a Pricing real-API E2E: rule → quotation → conversion → manual order (2026-11-22, ADR-166)

R = Playwright against the running API and SQL Server (`Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-pricing.spec`); every screen value is checked against the row read back with sqlcmd. Fresh unit, item, output tax (5 %) and approved customer per run (MD API, admin). Screenshots `tests/qa/SD/screenshots/real-pricing-*`.

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-PRICE-REAL-001 | Normal + permission | `sales` (seeded SAL.PRICING.* asserted first) creates a customer rule on 2.12 through the SD option endpoints; success names the rule ID; SQL row = item, customer, TWD, dates, 1234.500000, 10.0000, active, version 1; list row shows it. | R | REQ_F4112.11.x, REQ_IO_A2.12 |
| SD-PRICE-REAL-002 | Normal | Quotation form applies the rule (text with rule ID/version, price 1234.5, no reason field); SQL line `Rule` with rule ID/version, rule price/ceiling, no override, net 2469.0000 (qty 2); detail source text. | R | REQ_IO_A2.2.14, 2.1.10 |
| SD-PRICE-REAL-003 | Status + relation | Zero-step submit approves (SQL Approved; flow restored afterwards); conversion on screen; SQL order `Draft`, source quotation, line copies `Rule` provenance and price; order detail source text. | R | REQ_IO_A2.4.9, 2.5.17 |
| SD-PRICE-REAL-004 | Validation + normal | Manual order below rule price: Save without reason shows reason error and SQL still has only the converted order; with reason (spaces trimmed) SQL line `Override`, rule snapshot, reason, override_by = admin, override_at set; detail shows override text. | R | REQ_IO_A2.5.17–19 |
| — | Guard | Each test: no console error, no 5xx, no raw i18n key. | R | MASTER_PROMPT 20.3 |
| SD-PRICE-NRSEED-001 | Seed | SD provider registered; seed creates SAL.QUOTATION SQ / SAL.ORDER SO / SAL.DELIVERY DN (year-month, 4 digits, monthly, active) per seeded company; numbers issue as SQ/SO/DN202611nnnn (rolled back). Compile-red before the provider. | I | ADR-166 |
| SD-QUOTE-HTTP (submit/void) | Contract | Quotation `status` is the name (Pending, Voided), not an integer; red 1/4 before the converter. | I | ADR-166 |
| — | Not applicable here | Boundary/decimal limits, 409 STALE/DUPLICATE, 403 OVERRIDE, timeout/500/network: covered by U/E tests of 005a11/005b2 and the 005b3b QA round; files, batch and partial results: single-document forms; cross-company: SD-PRICE-HTTP tests. `sales` on quotation/order forms: blocked by missing MD view (WU-SD-FORM-OPTIONS). | — | — |

Results: real-API SD-PRICE-REAL-001～004 4/4 (after fixing two product defects, see optimization-log); NumberRuleSeedTests 5/5; SalesQuotation* 36/36.

## WU-SD-FORM-OPTIONS Quotation / order form options without MD permission (2026-11-22T01:08:11+08:00, ADR-167)

Requirements: REQ_F4112.1.2, REQ_F4112.2.2, REQ_F4112.2.3; REQ_IO_A2.2.1/2.2.5/2.2.10, REQ_IO_A2.5.1/2.5.5/2.5.9; BR-SD-054. I = real SQL + HTTP (`SalesDocumentOptionsHttpTests`, red 404 before the controller); U = Vitest; E = mocked Playwright (`sales-document-options.spec.ts`); R = real API.

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-DOCOPT-HTTP-001 | Permission | Anonymous 401 on the three routes; a user with SAL.QUOTATION/ORDER/PRICING.VIEW and MD.CUSTOMER/ITEM.VIEW but no writer permission gets 403; OpenAPI documents 200/400/401/403. | I | ADR-167 |
| SD-DOCOPT-HTTP-002 | Normal + relation + cross-company | Each of the four writer permissions alone lists exactly: the active approved customer (not pending, inactive, soft-deleted, other company); active items (not inactive, other company); Both/Output rates of active codes (not Input, not inactive code). | I | BR-SD-054 |
| SD-DOCOPT-HTTP-003 | Boundary + validation | Paging totals; keyword on code/name (items, customers, tax); `sort=name` 400; tax list on 2028-12-31 (effective-to inclusive) vs 2029-01-01 (next rate) vs 2027-12-31 (empty); missing / 2028-02-30 / `abc` businessDate → 400 `businessDate`. | I | ADR-167 |
| SD-DOCOPT-UI-001 | Contract | Service calls `/sal/document-options/*` with trimmed keyword, paging and businessDate. | U | ADR-167 |
| SD-DOCOPT-UI-002 / 003 | Normal + disabled | Quotation / order page: tax button disabled with hint until a date is typed; pickers load SD endpoints (never `/md/`); tax dialog columns code/name/rate, request carries the date; choice sets taxRateId and label `OUT · 5%`. | U | REQ_IO_A2.2.10, REQ_IO_A2.5.9 |
| SD-DOCOPT-UI-101 | Normal + i18n | Quotation: hint visible (zh-Hant, zh-Hans, en, light/dark); customer/item/tax dialogs state their scope; tax request businessDate = typed date; chosen label on screen; zero `/md/` requests. | E | MASTER_PROMPT 20.1 |
| SD-DOCOPT-UI-102 | Empty | Order: no rate on the date → dialog empty state, confirm disabled, cancel keeps "no tax rate chosen". | E | MASTER_PROMPT 20.1 |
| SD-DOCOPT-UI-103 | Error 500 | Order: tax option list 500 → dialog error with trace ID (marked expected 5xx). | E | MASTER_PROMPT 20.1 |
| SD-PRICE-REAL-002～004 | Permission (real) | Quotation, conversion and manual order now run as `sales` (no MD view) and still save Rule/Override provenance read back from SQL; override actor = sales user. | R | ADR-167 |
| — | Not applicable | 409/422 and state: read-only lookups, no writes or document status; timeout: shared select dialog behaviour (covered by its own tests); files, batch, partial results: none; data scope: MD records are company-wide; save-time revalidation of a later-deactivated record: unchanged and covered by existing quotation/order HTTP tests. | — | — |

Results: SD-DOCOPT-HTTP 3/3 (red 404 first); SD frontend 207/207, full frontend 1112/1112; mocked SD E2E 69/69 + 3/3; real-API pricing 4/4 as `sales`.

## WU-SD-005b3b Pricing error-state UX QA 2.2 / 2.5 / 2.11 / 2.12 (2026-11-22T01:31:26+08:00, ADR-168)

| ID | Category | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-PRICE-QA-U01 | Error 5xx / 4xx | `quoteFailureState` keeps the trace ID of a 500, never of a 403; timeout/network/422 have `traceId: null`. | U | MASTER_PROMPT 20.1 |
| SD-PRICE-QA-001 | Error 500 / network / 409 / 403 / timeout + 422 | Quotation 2.2 line quote: each failure has its own text (500 with `Trace ID`), exactly one quote call per retry, retry stays enabled; save 422 `SYS.NUMBER_RULE.NOT_CONFIGURED` shows the number-rule text (not "references rejected"), one POST with the rule pair, no success; zh-Hant light/dark, en, zh-Hans. | E2E | REQ_IO_A2.2.14, ADR-166 P2 |
| SD-PRICE-QA-002 | Same as 001 | Manual order 2.5: same five quote states; number-rule text instead of "server refused to create the order"; one POST, no success. | E2E | REQ_IO_A2.5.17 |
| SD-PRICE-QA-003 | Error timeout / network / 403 | Rule list 2.11: timeout, network and 403 each show their own message, the table says it could not load, 403 has no trace ID; three list calls. | E2E | REQ_IO_A2.11 |
| SD-PRICE-QA-004 | Error 403 / network / timeout | Rule create 2.12: 403 says no permission and Save stays enabled; network and timeout show "cannot confirm whether the rule was created", Save locked, no success; POST count matches. | E2E | REQ_IO_A2.12 |

Not applicable: partial / all-skipped / all-failed (no batch), files (no import), cross-company (covered by HTTP tests of ADR-159/167).

## WU-SD-006b1 Credit override on order submit (2026-11-22, ADR-169)

Requirements: REQ_F4112.6.1 v1.1; BR-SD-034, BR-SD-036; REQ_DS43.205. I = real SQL (`Database/SalesOrderSubmissionTransactionTests`, red on the pending-model check before the migration) and authorised HTTP (`SalesOrderCommandHttpTests`).

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-CREDIT-OVR-001 | Normal + override | Over-limit zero-step submit with full grant and padded reason → Approved v2; the one `sal_credit_override` row has the order, version 1, SYS round, stage Submit, trimmed reason, actor 7, `IClock` time, evaluation date, limit version 3, TWD and limit/open AR/other/candidate/excess 500/100/50/400.5/50.5. A Pending (multi-step) submit also records one row with its own round; the zero-step order just approved counts in its other commitments. | I | SD-CREDIT-019 |
| SD-CREDIT-OVR-002 | Permission + validation + state | No reason → 422 `LIMIT_EXCEEDED` excess 50; reason with only SUBMIT → 403 `OVERRIDE_FORBIDDEN`; OVERRIDE with Self scope on another owner's order → 403; `"   "` → 400 `creditOverrideReason`; 201 characters → 400; 200 characters accepted; no limit → 422 `LIMIT_NOT_SET`; inactive customer → 422 `CUSTOMER_INACTIVE` even with reason and grant; within limit with reason → no row. Refused orders stay Draft v1 and only the accepted order has a row. | I | SD-CREDIT-013/016/020 |
| SD-CREDIT-OVR-003 | Partial result + duplicate + audit | SYS failure after the decision → no row, order unchanged; retry saves one row; replay of version 1 → 409 and still one row; UPDATE and DELETE → SQL error 51031; a second row with the same order/round/stage → unique violation 2601. | I | SD-CREDIT-010/011/024 |
| SD-ORDER-HTTP-007 | Permission + error + normal (HTTP) | SUBMIT-only user with reason → 403 `SAL.CREDIT.OVERRIDE_FORBIDDEN`; blank reason → 400 with `errors.creditOverrideReason`; no reason → 422 excess 30; with SUBMIT + OVERRIDE and forged `creditOverrideBy` → 200, `credit.status` OverLimit, `credit.excess` 30, `creditOverride` {Submit, trimmed reason, 30, TWD, orderVersion 1, same round as the order, server actor}; one SQL row. | I | SD-CREDIT-013/019/020/021 |

## WU-SD-006b3 — read-only credit check (2026-11-22, ADR-170)

| ID | Category | Scenario and expected result | Layer | Result |
|---|---|---|---|---|
| SD-CREDIT-U16 | Boundary | Candidate order ID 0 is a customer-level check: every commitment counts (150), candidate 0, headroom 550; ID −1 still throws. | U | PASS |
| SD-CREDIT-CHECK-HTTP-001 | Permission + doc | Anonymous → 401; OpenAPI documents the GET with 200/400/403/404/409/422. | I | PASS |
| SD-CREDIT-CHECK-HTTP-002 | Normal + no write | Limit 1000, other Approved 300: customer level, Draft 500 (within) and Draft 800 (over, `LIMIT_EXCEEDED`, excess) return the figures with `requiresRecheck=true`; an Approved order as candidate is counted once; orders, versions and audit rows are unchanged. | I | PASS |
| SD-CREDIT-CHECK-HTTP-003 | Error + rollback | No limit → 422 `LIMIT_NOT_SET` and no `ar_credit_key` row survives; unapproved customer → 422 `CUSTOMER_INACTIVE`; unknown customer → 404 `MD.CUSTOMER.NOT_FOUND`. | I | PASS |
| SD-CREDIT-CHECK-HTTP-004 | Permission + scope + related record | SUBMIT holder sees its order but gets 403 at customer level; Self-scope EDIT / CREDIT.VIEW on another owner's order → 404; ORDER.VIEW only → 403; other company's customer/order and another customer's order → 404; non-positive IDs → 400. | I | PASS |

Partial result, file, batch and UI categories do not apply (single read, no write). The 409 `FI.CREDIT.LOCK_TIMEOUT` lock wait relies on the FI lock contract covered by FI credit tests. UI is WU-SD-006c.
| — | Not applicable | Files, batch and partial success (single-order command); UI (WU-SD-006c); SYS final-decision override (WU-SD-006b2); read-only check endpoint (WU-SD-006b3). | — | ADR-169 |

## WU-SD-006b2 SYS final-approval credit override (2026-11-23, ADR-171)

| ID | Category | Scenario | Level | Status |
|---|---|---|---|---|
| SYS-APPROVAL-OVR-001 | Normal + state (HTTP) | Two-step flow: `overrideReason` on step 1 is not forwarded (no handler call); the final approve forwards the raw untrimmed text in `ApprovalCompleted.OverrideReason`; a rejection carrying `overrideReason` forwards null. | I | PASS |
| SD-CREDIT-OVR-004 | Normal + duplicate + related | Submit with an override (excess 50, Submit row). Another order is approved later, so the excess at the final decision is 80. The approver's approve without a reason → 422 `LIMIT_EXCEEDED` excess 80, and the order stays Pending. Approve with a padded reason → order Approved v3 `approvedAt`=IClock, and a second row with stage `FinalApproval`: order version 2, same SYS round, trimmed reason, IClock time, limit/AR/other/candidate/excess 500/100/30/450/80. A replay is refused by SYS with `NOT_PENDING`, and no row is added. | I | PASS |
| SD-CREDIT-OVR-005 | Permission + boundary + error + state | Within limit with a reason and only `SYS.APPROVAL.PROCESS` → Approved, with no row. Over limit (excess 25): no reason → 422. A reason without OVERRIDE → 403 `OVERRIDE_FORBIDDEN`. OVERRIDE with Self scope on another owner's order → 403. `"   "` → 400 `overrideReason`. 201 characters → 400 `overrideReason`. Inactive customer with a reason and the grant → 422 `CUSTOMER_INACTIVE`. Rejection with a reason → Rejected, with no row. The over-limit order stays Pending in SD and SYS, and the company has no override rows. | I | PASS |

Not applicable:

- Files, batch and partial success: one decision per call.
- The 200-character maximum: it uses the shared `SalesCreditRules.Decide` and is already tested by SD-CREDIT-U13 and OVR-002.
- Concurrency: the FI lock order is unchanged, see SD-CREDIT ADR-142 tests.
- UI: WU-SD-006c.

## WU-SD-006c1 Order submit and credit override UI (2026-11-23, ADR-172)

| ID | Category | Scenario | Level | Status |
|---|---|---|---|---|
| SD-ORDER-SUBMIT-UI-001 | Normal | Within limit: preview figures and "preview only" notice, no reason input; POST body `{"version":2}`; Pending notice only after reply v3; list row and re-read detail Pending; submit button gone. | U | PASS |
| SD-ORDER-SUBMIT-UI-002 | State | Zero-step reply Approved → "needed no approval step and is Approved". | U | PASS |
| SD-ORDER-SUBMIT-UI-003 | Normal + boundary | Over limit with OVERRIDE: empty and blank reason refused locally (no request); padded reason sent trimmed; saved override restated with excess and reason. | U | PASS |
| SD-ORDER-SUBMIT-UI-004 | Permission | Over limit without OVERRIDE: no input, confirm disabled with the reason, no request. | U | PASS |
| SD-ORDER-SUBMIT-UI-005 | Conflict (stale preview) | Preview within limit, server 422 LIMIT_EXCEEDED excess 12.5 → named error, "still Draft", excess shown, reason input revealed; the next submit carries the reason. | U | PASS |
| SD-ORDER-SUBMIT-UI-006 | Error 403 / 400 | 403 OVERRIDE_FORBIDDEN named; 400 `creditOverrideReason` shown under the input. | U | PASS |
| SD-ORDER-SUBMIT-UI-007 | Error 422 blocked | Preview 422 LIMIT_NOT_SET: named reason, "cannot be submitted even with an override", confirm disabled; recheck within limit re-enables it. | U | PASS |
| SD-ORDER-SUBMIT-UI-008 | Error 500 (preview) | Preview 500 shows the trace ID; submit stays available. | U | PASS |
| SD-ORDER-SUBMIT-UI-009 | Conflict 409 | Version conflict pauses (confirm disabled); Reload re-reads the order and clears the pause. | U | PASS |
| SD-ORDER-SUBMIT-UI-010 | Conflict 409 lock | `FI.CREDIT.LOCK_TIMEOUT` is named and retryable (not paused). | U | PASS |
| SD-ORDER-SUBMIT-UI-011 | Error 500 (submit) | Uncertain: paused, trace ID, no success, no "nothing was saved" text. | U | PASS |
| SD-ORDER-SUBMIT-UI-012 | Error (inconsistent reply) | 200 with the same version is not success; paused. | U | PASS |
| SD-ORDER-SUBMIT-UI-013～015 | State + permission | Rejected → edit-first hint, no submit; Pending → nothing; Draft without SUBMIT → nothing. | U | PASS |
| SD-ORDER-SUBMIT-UI-016 | State | 422 INVALID_STATUS pauses with "status has changed"; Cancel closes the area. | U | PASS |
| SD-ORDER-SUBMIT-E2E-001 | Normal + data | Over-limit preview, local reason refusal (0 submits), padded reason → body `{version:2, creditOverrideReason:"Key account; CFO agreed"}`; notice, override text, detail and list row all Pending v3; three languages, light/dark. | E | PASS |
| SYS-APPROVAL-OVR-UI-001 | Normal + boundary | 8.70 final approve → 422 LIMIT_EXCEEDED shows excess 1,234.5 TWD "not saved" and the reason field, no top error; blank / 201-character reason not sent; padded reason sent trimmed in `overrideReason`; Approved → override notice, task reloaded. | U | PASS |
| SYS-APPROVAL-OVR-UI-002 | Permission | Without SAL.CREDIT.OVERRIDE: no reason field, Approve disabled with the reason, no request; reopening as reject clears state and sends `{ comment, version }`. | U | PASS |
| SYS-APPROVAL-OVR-UI-003 | Error 403/400/409/500/422 | OVERRIDE_FORBIDDEN in dialog; 400 overrideReason under the field; FI.CREDIT.LOCK_TIMEOUT named; 500 trace ID and "result could not be confirmed" (no "not saved"); LIMIT_NOT_SET removes the reason field. | U | PASS |
| SD-APPROVAL-OVR-E2E-001 | Normal + data | Inbox approve SO → excess shown, reason focused; blank reason refused (1 request); 400 overrideReason under field; approve → notice equals the sent excess, body `{ comment, version, overrideReason }` trimmed, task gone. Screenshots 8.70-credit-override-*. | E | PASS |
| SD-APPROVAL-OVR-E2E-002 | Error | 500 trace ID, reason kept, uncertain wording; LIMIT_NOT_SET removes field; cancel keeps the task, no notice. | E | PASS |
| SD-APPROVAL-OVR-E2E-003 | Permission | SYS.APPROVAL.PROCESS only: Approve disabled with reason; reject still works with `{ comment, version }`. | E | PASS |
| SD-APPROVAL-OVR-E2E-004 | i18n + theme | en / zh-Hans excess and label, light and dark. | E | PASS |
| SD-ORDER-SUBMIT-E2E-002 | Error + data | LIMIT_NOT_SET disables confirm; recheck within limit; submit 500 → paused with trace ID, no notice, no "nothing saved" text, exactly 1 submit; reload shows Draft. | E | PASS |

Not applicable: files, batch and partial success (one order per submit). Timeout and network share the uncertain 5xx path (U-011). Cross-company and data scope are server checks (SD-CREDIT-CHECK-HTTP-004, SD-ORDER-HTTP-007); the UI shows their 404 as "no longer exists" and pauses. SYS final-approval reason field: WU-SD-006c2. UX review: 12 screenshots `2.4-submit-*` checked; P1 fixed (5xx said both "cannot confirm" and "nothing was saved"); P3 figures width fixed; P3 blocked state repeats the reason in two lines (kept).

## WU-SD-006c3a Order Create/Edit saved credit panel (2026-11-23, ADR-174)

Read-only credit preview shown on Sales Order Create (2.5) and Edit (2.6) only after a confirmed save, for the saved order (`GET sal/credit-checks/{customerId}?orderId=`). Figures are shared with the 2.4 submit area (`erp-sales-credit-figures`).

| ID | Category | Scenario | Level | Result |
|---|---|---|---|---|
| SD-ORDER-CREDIT-UI-001 | Normal | Saved order within limit: one request with saved customerId and orderId; figures, Within limit tag, "saved version, nothing submitted" notice; no success wording about credit. | U | PASS |
| SD-ORDER-CREDIT-UI-002 | Normal + boundary | Over limit: Over limit tag and excess warning; with SAL.ORDER.SUBMIT the hint says submit needs an override reason; without the panel still shows (preview only). Excess 0 / headroom 0 shows Within limit. | U | PASS |
| SD-ORDER-CREDIT-UI-003 | Error | 422 LIMIT_NOT_SET / UNAVAILABLE → named reason and "cannot be submitted until fixed"; 500 → generic message + trace ID; timeout → timeout text; recheck repeats one request and replaces the error with figures. | U | PASS |
| SD-ORDER-CREDIT-UI-004 | State | Panel absent before save, while saving, on uncertain/failed save, on conflict; a new saved order (another / reload) reloads the preview and cancels the stale request. | U | PASS |
| SD-ORDER-CREDIT-UI-004b | Error | Receipt without a valid customer id: no request, failure text says the order is saved (no exception). | U | PASS |
| SD-ORDER-CREDIT-UI-005 | Permission | 403 from the preview → "no permission to view credit" without trace ID; panel does not offer recheck loops beyond the button. | U | PASS |
| SD-ORDER-CREDIT-E2E-001 | Normal + data | Create manual order → success → credit panel figures equal the mocked credit-check reply for that saved id; en and zh-Hant, light and dark screenshots `2.5-credit-*`. | E | PASS |
| SD-ORDER-CREDIT-E2E-002 | Edit + error | Edit save → over-limit preview with excess; recheck 500 shows trace ID; screenshots `2.6-credit-*`. | E | PASS |

Not applicable: files, batch, partial success (single order, read-only preview). Duplicate/soft-deleted codes: no input. Cross-company / data scope are server 404 checks (SD-CREDIT-CHECK-HTTP-004); the panel shows the generic not-found reason. Real API flow (sales over-limit submit with override → manager final approval, SQL `sal_credit_override` Submit/FinalApproval) is WU-SD-006c3b.

Results 2026-11-23: unit 6/6 (frontend total 1138/1138, existing TIMEOUT-LEAK 2 unhandled errors), E2E 2/2 plus order create/edit/submit/pricing/QA/approval-override/view regressions. First E2E run found the panel threw on a conversion mock receipt without `customerId` (mock completed; panel now guarded, UI-004b) and one known theme-menu flake (WU-P0-E2E-FLAKE-MENU, passed on rerun). UX review of 10 screenshots: no P1/P2; P3 fixed (saved-version notice repeated next to the failure hint; now shown only with figures, E2E-002 asserts).

## WU-SD-006c3b Credit override real API E2E (2026-11-23T06:02:27+08:00, ADR-170–175)

`tests/e2e/specs/real-api-sales-credit-override.spec.ts`, run with `tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-credit-override.spec` (migration → seed → API; SQL Server + Redis). Arrange through MD / FI / SYS APIs only: fresh unit, item, 5% output tax, approved customer, FI credit limit 1,500 TWD; SAL.ORDER flow set to one step (user `manager`) and restored in afterAll. Every screen value is compared with the rows read back with sqlcmd.

| ID | Category | Scenario | Level | Result |
|---|---|---|---|---|
| SYS-SEED-ROLE (RoleRulesCoverFutureModulePermissions, extended) | Permission | MANAGER gets SAL.CREDIT.OVERRIDE but not SAL.PRICING.OVERRIDE; SALES gets both (ADR-175). Precondition also asserted in SQL by the real spec. | I | PASS |
| SD-CREDIT-REAL-001 | Normal + data | `sales` saves a manual order (2 × 800 + 5% = 1,680) over the 1,500 limit; SQL `sal_order` Draft, base gross 1,680, no approval instance, no override row; saved-credit panel shows Over limit, limit 1,500, this order 1,680, remaining −180, excess 180.00 TWD and the override hint. | E (real) | PASS |
| SD-CREDIT-REAL-002 | Validation + normal + state | 2.4 submit without reason shows the reason error and SQL stays Draft with the same version; with reason (spaces trimmed) SQL order Pending with approval instance and higher version; exactly one `sal_credit_override` Submit row (limit 1,500, posted AR 0, other 0, candidate 1,680, excess 180, reason, created_by sales, evaluation date today); `sys_approval_instance` Pending for the order; notice shows the saved version, excess and reason; detail and list show 審批中. | E (real) | PASS |
| SD-CREDIT-REAL-003 | Status + permission + normal | `manager` approves on 8.70: first approve refused (422) with excess 180 TWD, reason field focused; SQL still Pending, one override row, instance Pending. With reason: notice names the order and excess; task leaves the inbox; SQL order Approved with approved_at, instance Approved, second row FinalApproval with the Pending order version, same instance and credit limit version, reason trimmed, created_by manager. | E (real) | PASS |

Not applicable here (covered by mocked E2E / HTTP tests in 006b1–006c3a): 400/403/409/500/timeout, blocked credit, no override permission, other languages. Files, batch, partial success: not applicable (single order).

Results 2026-11-23: real API 3/3 (first run failed in the spec itself: a fresh item has no price rule, so the order line needs a pricing override reason — spec fixed, product unchanged); SystemSeedTests 7/7. UX review of the 9 real screenshots: no P1/P2; P3 → WU-SD-006c4a, WU-SD-006c4b, WU-SYS-APPROVAL-FMT.

## WU-SD-007a1 Invoice tables and FI source port (2026-11-23T09:12:38+08:00, ADR-176)

Requirements: REQ_F4112.5.1 v1.1; BR-SD-037～042; REQ_DS43.206/207. I = real SQL (`Database/SalesInvoiceSchemaTests`, red on the pending-model check and missing types before the migration).

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-INVOICE-SCHEMA-001 | Normal + state + audit | Issuing → Posted with FI references (version 1 → 2); then header UPDATE (content or back to Issuing) → 51032, line UPDATE/INSERT/DELETE → 51033, header DELETE refused; rows intact. | I | SD-INVOICE-001/020 |
| SD-INVOICE-SCHEMA-002 | State + rollback | While Issuing, saved content changes → 51032 and line UPDATE → 51033; deleting Issuing lines then header (in-transaction cleanup) is allowed. | I | SD-INVOICE-011 |
| SD-INVOICE-SCHEMA-003 | Duplicate + company | Same company: duplicate number or request key → unique violation; second invoice Posted with an already-used FI source ID → violation on the Issuing → Posted update and rollback. Another company reuses number, key and FI source ID. | I | SD-INVOICE-009/010 |
| SD-INVOICE-SCHEMA-004 | Validation (15 shapes) | Gross ≠ net + tax, negative tax, same-currency rate ≠ 1 or base ≠ original, Posted without FI IDs, Issuing with FI IDs, blank key/fingerprint, unknown status, zero quantity, line gross, tax rate > 100, line_no 0, duplicate line_no, zero references → CHECK/unique error and zero rows. | I | SD-INVOICE-004/005 |
| SD-INVOICE-SCHEMA-005 | Company + delete protection | Source order/delivery/line from another company → FK error; an invoiced delivery line or order line cannot be deleted. | I | SD-INVOICE-014/024 |
| SD-INVOICE-SCHEMA-006 | Precision | Foreign-currency invoice keeps decimal(19,8) rate and independent decimal(19,4) base amounts. | I | SD-INVOICE-006 |
| SD-INVOICE-SOURCE-001 | Contract + company + misuse | `SalesInvoiceSource` inside the caller's transaction returns Issuing then Posted snapshots with saved fingerprint/totals and SourceVersion 1; missing or foreign-company IDs → null; outside the shared transaction → InvalidOperationException. | I | BR-SD-040, FI-SALES-001 |

## WU-SD-007a2 Invoice ISSUE service (2026-11-23T09:46:03+08:00, ADR-177)

| ID | Type | Scenario | Level | Covers |
|---|---|---|---|---|
| SD-INVOICE-U01 | Normal + boundary | Three one-third invoices of a USD/TWD line: 33.33/33.34/33.33 net, base 1033/1034/1033; sums equal saved net/tax/gross in both currencies. | U | SD-INVOICE-007 |
| SD-INVOICE-U02 | Normal | Two portions of one order line in one invoice accumulate in line order; second takes remainder. | U | SD-INVOICE-002 |
| SD-INVOICE-U03 | Boundary | 0.000001-unit portion of a TWD zero-tax line allocates 0; the completing portion takes all. | U | SD-INVOICE-005 |
| SD-INVOICE-U04 | Boundary | decimal(19,4)/(19,6) maxima allocate without overflow or drift. | U | SD-INVOICE-005/007 |
| SD-INVOICE-U05 | Validation (7 shapes) | Gross ≠ net + tax, base gross mismatch, negative tax, zero ordered, above ordered (with/without prior), prior above saved → SOURCE_INVALID with lineNo. | U | SD-INVOICE-016 |
| SD-INVOICE-U06 | Validation | Portion whose order line was not loaded → SOURCE_INVALID. | U | BR-SD-039 |
| SD-INVOICE-U07 | Validation (17 shapes) | Key null/blank/oversize/bad char, no date, null/empty/>1000 lines, null line, line no 0, source 0, quantity 0/negative/7 decimals/≥10^13, duplicate line no, duplicate delivery line → 400 by field. | U | SD-INVOICE-004/010 |
| SD-INVOICE-U08 | Duplicate | Fingerprint ignores scale, request order and key; changes with date, quantity or line mapping. | U | SD-INVOICE-008 |
| SD-INVOICE-ISSUE-001 | Normal + snapshot | Two Posted deliveries, two partial invoices: Issuing, number IA001/IA002, customer/currency/rate/tax snapshot, per-line amounts, header = line sum; final invoice reconciles every order line exactly. | I | SD-INVOICE-001/002/006/007 |
| SD-INVOICE-ISSUE-002 | Boundary | 0.4 then 0.600001 → BALANCE_EXCEEDED (lineNo, remaining 0.6); exact 0.6 succeeds; zero balance fails; only two numbers used. | I | SD-INVOICE-005 |
| SD-INVOICE-ISSUE-003 | State + permission + company | Draft/Pending/Rejected/deleted delivery → DELIVERY_NOT_POSTED (lineNo); two orders → SOURCE_MIXED; date before delivery → DATE_BEFORE_DELIVERY; foreign company / unknown / Self scope → 404; no ISSUE → 403; zero quantity → 400; no invoice or number left. | I | SD-INVOICE-013/014/015/016 |
| SD-INVOICE-ISSUE-004 | Duplicate/conflict | Same key + same payload (1 vs 1.000000) replays same invoice, no hook, no number; changed quantity/date/line no → 409; out-of-scope user → 409; new key → new invoice. | I | SD-INVOICE-008/020 |
| SD-INVOICE-ISSUE-005 | Concurrency | First issuer holds its transaction: same delivery line is blocked (lock timeout), another order in the same company issues; after commit the loser gets BALANCE_EXCEEDED; billed quantity = 1. | I | SD-INVOICE-009 |
| SD-INVOICE-ISSUE-006 | Rollback + lock order | No caller transaction → InvalidOperationException; hook receives customer 21 before SD locks (another connection can still lock the order); hook failure leaves no invoice or number. | I | SD-INVOICE-011, BR-SD-041 |

## WU-SD-007a3a Invoice ISSUE command with FI posting (2026-11-23, ADR-178)

Category check (myerp-test-scenarios): normal 001; boundary 001 (full invoicing releases the commitment to exactly 0); duplicate/conflict 002, 005; partial result — not applicable (one invoice is all-or-nothing, proven by 003/004); permission/company/scope — same pre-FI path as SD-INVOICE-ISSUE-003, HTTP 401/403 in 007a3b; state — Issuing never committed (003/004/006), Posted immutability in SD-INVOICE-SCHEMA; relation — held FI credit lock 005; errors 400/409/422/500/timeout — U10, 002, 003, 004, 005; file — not applicable. UI wording of each error belongs to 007a3b/007b.

| ID | Type | Scenario | Level | Covers |
|---|---|---|---|---|
| SD-INVOICE-U09 | Validation (12 shapes) | FI receipt accepted only when company, invoice, customer, version, fingerprint, currency, original gross, base currency, base gross match and FI source/receivable/journal IDs are positive. | U | BR-SD-041, BR-FI-010 |
| SD-INVOICE-U10 | Error mapping | Rejected 404/409/422 keep FI's code (422 with `journalLineNo`); Rejected 400/500, Rejected without code, Fault and Unchanged become `SalesInvoicePostingException` (500). | U | SD-INVOICE-011 |
| SD-INVOICE-POST-001 | Normal + boundary | Real FI: invoice Posted, PostedAt = clock, FI source/receivable/journal IDs equal FI rows; AR gross USD 35.00, base 1,085 open; journal Dr 1131 1,085 / Cr 2171 52 / Cr 4101 1,033; credit commitment 3,743 → 2,658 while open AR 0 → 1,085 (total unchanged); invoicing the rest leaves commitment 0 and AR 3,743. | I | SD-INVOICE-010/012, FI-SALES-001/003 |
| SD-INVOICE-POST-002 | Duplicate | Same key + payload replays the Posted invoice with the same FI journal and no new FI/AR/GL row; changed payload → 409 REQUEST_CONFLICT, still one invoice. | I | SD-INVOICE-008/020, BR-SD-040 |
| SD-INVOICE-POST-003 | Error 422 | FI mapping missing → 422 `FI.SALES_POSTING.MAPPING_MISSING`; no invoice, line, FI source, receivable or journal. | I | SD-INVOICE-011 |
| SD-INVOICE-POST-004 | Error 500 + rollback | FI wrote rows but receipt gross differs → `SAL.INVOICE.FI_RECEIPT_MISMATCH`, everything rolled back; FI Fault (transaction already rolled back) → `FI.SALES_POSTING.WRITE_FAILED`, nothing saved; the same key then succeeds. | I | BR-SD-041, BR-FI-010 |
| SD-INVOICE-POST-005 | Concurrency/timeout | Another transaction holds the FI credit lock → 409 `FI.CREDIT.LOCK_TIMEOUT` before any SD lock (order row still lockable), no rows; succeeds after release. | I | BR-SD-041 |
| SD-INVOICE-POST-006 | Guard | Outer transaction → InvalidOperationException; SD 422 BALANCE_EXCEEDED never calls FI; nothing saved. | I | SD-INVOICE-011 |

## WU-SD-007a3b1 Invoice ISSUE/detail HTTP (2026-11-23, ADR-179)

Category check (myerp-test-scenarios): normal HTTP-002; duplicate/replay HTTP-002 (200, no FI rows) and 409; validation
400 HTTP-003; not found / state 404 and 422 with lineNo HTTP-003; FI refusal 422 and FI fault/receipt mismatch 500
HTTP-003 with nothing saved and the same key succeeding afterwards; permission 401 HTTP-001, 403 both ways HTTP-002;
data scope and company isolation HTTP-004; i18n completeness U11; partial result — not applicable (all-or-nothing).

| ID | Type | Scenario | Level | Covers |
|---|---|---|---|---|
| SD-INVOICE-U11 | i18n | Every SD `SAL.INVOICE.*` code (11) has non-empty zh-Hant/zh-Hans/en text, no extra keys, Chinese not an English copy (red before translations). | U | ADR-179 |
| SHARED-ERR-INTERNAL | Error contract | `InternalErrorException` → 500 /internal, generic title, its errorCode, no detail; handler writes it. | U | ADR-179 |
| SD-INVOICE-HTTP-001 | Permission | Anonymous POST/GET 401; OpenAPI documents POST 200/201/400/403/404/409/422/500 and GET {id} 404; no list yet. | I | ADR-179 |
| SD-INVOICE-HTTP-002 | Normal + replay | ISSUE-only user: 201 + Location, number HA001, Posted, order/customer/currency/base amounts 1,033/52/1,085, line refs; FI IDs equal SQL AR source/receivable/journal, open AR 1,085, journal Dr 1131 / Cr 2171 / Cr 4101; replay 200 same ID/journal, no FI rows; changed payload 409 REQUEST_CONFLICT; ISSUE user GET 403; VIEW user POST 403; VIEW GET body equals the POST body (red before the commit read-back: PostedAt 7 vs 6 decimals); unknown id 404 NOT_FOUND. | I | SD-INVOICE-008/010/012/020 |
| SD-INVOICE-HTTP-003 | Errors | 400 with requestKey/lines fields; unknown delivery line 404 SOURCE_NOT_FOUND; over balance 422 BALANCE_EXCEEDED lineNo 3; FI map missing 422 FI.SALES_POSTING.MAPPING_MISSING; FI fault 500 FI.SALES_POSTING.WRITE_FAILED (no detail, traceId); receipt tamper 500 SAL.INVOICE.FI_RECEIPT_MISMATCH; no invoice, number sequence, AR or journal; then the same key 201 HB001. | I | SD-INVOICE-011, BR-SD-041 |
| SD-INVOICE-HTTP-004 | Scope | Self ISSUE scope on another owner's order 404 SOURCE_NOT_FOUND, no FI rows; Self VIEW 404 NOT_FOUND; other company VIEW 404. | I | BR-SD-038 |

## WU-SD-007a3b2 Invoice list HTTP (2026-11-23, ADR-180)

Category check (myerp-test-scenarios): normal list HTTP-005; filters (keyword on invoice/order number with spaces,
from/to inclusive, orderId, customerId, empty result) HTTP-005; sort both fields/directions and same-date ID
tie-break HTTP-005; paging without overlap HTTP-005; validation 400 HTTP-005; permission 401 HTTP-001, 403 HTTP-005;
data scope and company isolation HTTP-006; duplicate / partial / state changes — not applicable (read only).
Implementation was written before these tests in the same round; mutation check: removing the scope join turned
HTTP-005/006 red, restoring it green.

| ID | Type | Scenario | Level | Covers |
|---|---|---|---|---|
| SD-INVOICE-HTTP-001 (updated) | Permission + OpenAPI | Anonymous list 401; OpenAPI GET list documents 200/400/401/403 and parameters keyword/from/to/orderId/customerId/sort/page/pageSize. | I | ADR-180 |
| SD-INVOICE-HTTP-005 | List | Three invoices HE001 (11-06, order A), HE002 (11-07, order A), HE003 (11-07, order B customer 22): default order HE003, HE002, HE001 (same-date ID tie-break); first row fields equal GET detail, lineCount = detail lines, FI journal present; documentNo asc/desc; businessDate asc; pageSize 2 pages 1+2 cover all without overlap, totalPages 2; keyword = padded order B number / `E002`; from, to, from+to; orderId A; customerId 22 and 23 (empty); ISSUE-only user 403; 400 for from > to, orderId 0, customerId −1, sort=status, two sort fields, pageSize 201. | I | ADR-180 |
| SD-INVOICE-HTTP-006 | Scope | Self VIEW lists and counts only the invoice of its own order (total 1); other company's viewer sees only its own HG001. | I | ADR-180, BR-SD-038 |

## WU-SD-007b0 Invoice source reads HTTP (2026-11-23, ADR-181)

Category check (myerp-test-scenarios): normal HTTP-007/008; filters (keyword on order number with spaces, customer
name, customer code; customerId; empty result) HTTP-007; sort both fields/directions and ID tie-break, paging
without overlap HTTP-007; validation 400 HTTP-007; permission 401 HTTP-001, 403 (VIEW only) HTTP-007/008; data scope
(Self) and company isolation HTTP-007/008; state limits (Pending delivery, Cancelled order, fully invoiced line and
order, partially invoiced line) HTTP-007/008; boundary (remaining + 0.000001 refused by ISSUE with the same
remaining) HTTP-008; not found HTTP-008; duplicate / partial success — not applicable (read only). Tests were red
first (EF could not translate the open-line projection, 500); mutation check: removing the scope filter turned
HTTP-007 red, restoring it green.

| ID | Type | Scenario | Level | Covers |
|---|---|---|---|---|
| SD-INVOICE-HTTP-001 (updated) | Permission + OpenAPI | Anonymous `source-orders` and `source-orders/1` 401; OpenAPI list documents 200/400/401/403 and keyword/customerId/sort/page/pageSize; single documents 200/401/403/404/422. | I | ADR-181 |
| SD-INVOICE-HTTP-007 | Source orders | Order A (Posted 1 + Posted 1.5, Pending 0.5), B (customer 22), fully invoiced order (23), Cancelled order (24), own order of a Self issuer (25), order with only a Pending delivery (26): Company issuer sees own, B, A (total 3, default -businessDate ID tie-break); A row id/customer/code/name/date/USD/openLineCount 2/version; documentNo asc/desc; businessDate asc; pageSize 2 pages 1+2 no overlap, totalPages 2; keyword padded A number, `Second`, `C25`; customerId 22 / 23 (empty); 400 customerId 0, sort=status, two sort fields, pageSize 201; VIEW-only 403; Self issuer total 1 (own); other company sees only its own order. | I | ADR-181, BR-SD-037/038 |
| SD-INVOICE-HTTP-008 | Source lines | A before any invoice: (line A 1/0/1), (second 1.5/0/1.5); after invoicing 0.25: header number/customer/code/name/date/USD/TWD/31; lines (1/0.25/0.75), (1.5/0/1.5) ordered by delivery date with delivery number/date/line no/order line/item/tax 5; Pending line absent; ISSUE 0.750001 → 422 BALANCE_EXCEEDED lineNo 1 remaining 0.75; ISSUE 0.75 + 1.5 → 201; then 200 with no lines and the picker total 0; 404 SOURCE_NOT_FOUND for unknown id, Self issuer on another owner's order, other company; 422 ORDER_NOT_APPROVED for Cancelled order; VIEW-only 403. | I | ADR-181, BR-SD-038/055 |

## WU-SD-007b1 Sales invoice view page (Interface 2.14, 2026-11-23T14:09:56+08:00)

| ID | Category | Scenario | Level | Trace |
|---|---|---|---|---|
| SD-INV-UI-001 | Service | list sends trimmed keyword/from/to/sort/paging plus customerId/orderId as one GET, omits empty filters; detail keeps exact rate, tax %, quantities and amounts as text; invalid id rejected before any request; JSON ProblemDetails surfaced; money formatting keeps every saved digit. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-002 | Normal | List row shows number, order, customer, date, USD 1,234.50, TWD 38,459.00 and journal 77; default sort -businessDate; immutability notice. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-003 | Empty | Empty list says "No invoices yet."; filtered no-result says "No invoices match these filters." | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-004 | Validation | 2028-02-30 or From after To shows an inline message and sends no request. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-005 | Normal | Detail: Posted tag, customer code · name, exact rate 31.154321, totals 1,175.7143 / 58.7857 / USD 1,234.50 / TWD 38,459.00, FI journal/receivable/source 77/42/41, no input controls, lines with 1,234.000001, 5%, 1,050.00, 32,712.00 and 0.5. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-006 | Boundary | Null FI journal shows "Not recorded". | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-007 | Error | Detail 404 SAL.INVOICE.NOT_FOUND shows its text; retry then 500 shows trace ID and no detail. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-008 | Error | List 500 shows trace ID and Retry, never the empty text; Retry reloads rows. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-009 | Company switch | Switching company clears rows, open detail and customer filter; the stale reply is ignored and the new GET omits customerId. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-010 | Permission | Route `invoices` has one guard and the Sales menu entry `/sal/invoices` requires SAL.INVOICE.VIEW. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-011 | URL state | Keyword, valid from, page 3, size 50, sort documentNo restored; invalid to, orderId x, customerId -4 dropped. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-012 | URL state | URL customer filter is named and clearable; clearing sends a GET without customerId. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-013 | Normal | Row customer link filters by customerId and names it; Reset clears every filter. | U | REQ_IO_A2.14, REQ_F4112.5.1 |
| SD-INV-UI-014 | Timeout | A stalled list read is cancelled after 6 s and shows "Request timed out" with Retry. | U | REQ_IO_A2.14, REQ_F4112.5.1 |

Result: frontend 1156/1156 (18 new: 5 service + 13 page); Playwright and myerp-ux-review screenshots belong to WU-SD-007b3.

## WU-SD-007b2 Sales invoice issue form (Interface 2.15, 2026-11-23T14:25:34+08:00)

| ID | Category | Scenario | Level | Trace |
|---|---|---|---|---|
| SD-INV-ISSUE-UI-001 | Normal | Picker columns/keys, customer label; chosen order, exact rate 31.154321, lines with invoiced 0.249999, remaining 0.750001 and 9,999,999,999,999.999999; all lines preselected with full remaining text; "2 of 2 lines selected"; posting notice. | U | REQ_IO_A2.15.1–3 |
| SD-INV-ISSUE-UI-002 | Normal | Unselect a line, quantity " 0.5 ", date → one POST body with UUID key, lineNo 1, deliveryLineId 31, quantity 0.5 literal; 201 → toast, navigate `/sal/invoices?open=5`, no leave prompt. | U | REQ_IO_A2.15.4–6 |
| SD-INV-ISSUE-UI-003 | Validation | Empty date; date before latest selected outbound (2028-02-20) and the floor moving when that line is unselected; 7 decimals; above remaining; zero; no line selected; problem count; no POST. | U | REQ_IO_A2.15.4–5, BR-SD-055 |
| SD-INV-ISSUE-UI-004 | Timeout | 20 s timeout cancels the POST, locks the form, leaving asks; Resend sends the identical body; 200 replay toast "nothing was posted again". | U | REQ_IO_A2.15.7 |
| SD-INV-ISSUE-UI-005 | Error | Network failure and uncoded 500 (trace ID) stay unconfirmed with the same key; coded 500 FI_POSTING_FAILED clears the warning, shows text + trace, re-enables submit; next POST uses a new key. | U | REQ_IO_A2.15.7–8 |
| SD-INV-ISSUE-UI-006 | Concurrency | 422 BALANCE_EXCEEDED lineNo 1 maps to the first selected line (33), "Only 12.5 …", line highlighted; Reload keeps the unselected line and revalidates against the new remaining. | U | REQ_IO_A2.15.8 |
| SD-INV-ISSUE-UI-007 | Error | DATE_BEFORE_DELIVERY (no line mark), REQUEST_CONFLICT, FI.CREDIT.LOCK_TIMEOUT, 403, unknown 422 → "Nothing was issued". | U | REQ_IO_A2.15.8 |
| SD-INV-ISSUE-UI-008 | Error | Source read 422 ORDER_NOT_APPROVED, 500 with trace and Retry, order with no open lines → warning and no form. | U | REQ_IO_A2.15.9 |
| SD-INV-ISSUE-UI-009 | Integrity | 201 receipt with another request key → unconfirmed, no toast. | U | REQ_IO_A2.15.6 |
| SD-INV-ISSUE-UI-010 | Permission | ISSUE without VIEW: no back link, no navigation, receipt USD 105.00 on the page, "Issue another" resets. | U | REQ_IO_A2.15.6 |
| SD-INV-ISSUE-UI-011 | Company switch | Leave prompt once an order is chosen; company switch clears the form and ignores the stale source reply. | U | REQ_IO_A2.15.2 |
| SD-INV-ISSUE-UI-012 | Permission | Route `invoices/new` has one guard, a deactivate guard and precedes `invoices`. | U | REQ_IO_A2.15 |
| SD-INV-ISSUE-UI-013 | Permission | List shows "Issue invoice" (`/sal/invoices/new`) only with SAL.INVOICE.ISSUE. | U | REQ_IO_A2.15 |
| SD-INV-ISSUE-UI-014/014b | Navigation | `?open=5` loads the detail once with the list and focuses its title; an invalid `open` is ignored. | U | REQ_IO_A2.14.9 |
| SD-INV-ISSUE-UI-015 | Service | source-orders params; exact remaining/invoiced/rate text; invalid id throws; ISSUE body with exact literal and JSON content type; 201 new / 200 replay; non-decimal quantity rejected; UUID key; 422 lineNo/remaining parsed. | U | REQ_IO_A2.15 |

Result: frontend 1174/1174 (18 new: 3 service + 15 page; Vitest still reports the 2 known FE-TIMEOUT-LEAK unhandled errors); ng build passed. Tests were written in the same round as the page; the first run caught a real defect (a coded refusal after an unconfirmed attempt left the warning and lock on), fixed.

### WU-SD-007b3a invoice Playwright with mocked API (ADR-182)

| Case | Category | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-INV-E2E-001 | Normal | List row with USD/TWD gross and GL journal ID; detail status, customer, exact rate, FI journal/receivable/source IDs, totals, exact line quantity, posted time; zh-Hant/zh-Hans/en, light/dark screenshots; customer chip filter written to URL and query. | E2E | REQ_IO_A2.14.1～9 |
| SD-INV-E2E-002 | Empty / Error | Empty list, keyword without result (URL), From after To blocks the read, detail 404 SAL.INVOICE.NOT_FOUND, list 500 with trace ID and retry. | E2E | REQ_IO_A2.14 |
| SD-INV-E2E-003 | Normal / Boundary | From the list "Issue invoice" → pick order → all open lines preselected with exact remaining; date before latest outbound and quantity above remaining blocked client-side with focus on the date (no POST); one POST with exact `10.000001` literal, only requestKey/businessDate/lines, UUID key; toast, detail opened and focused, list shows the invoice. | E2E | REQ_IO_A2.15.1～9 |
| SD-INV-E2E-004 | Conflict | 422 BALANCE_EXCEEDED lineNo 1 remaining 4.5: line message (zh-Hant/en), focus on that quantity; reload shows new remaining, clears the error, client blocks the old quantity; retry with 4.5 uses a new request key. | E2E | REQ_IO_A2.15.7 |
| SD-INV-E2E-005 | Error / Idempotency | Network failure: "may have been issued" warning, form locked, invoice list link; "Resend safely" sends the identical body and key; 200 replay message "nothing was posted again". | E2E | REQ_IO_A2.15.6 |
| SD-INV-E2E-006 | Permission | ISSUE without VIEW: no back link, no list read, receipt `SI-0005 · SO-0081 · USD 1,050.0001 · TWD 32,550.0031` stays on the page; "Issue another" resets. | E2E | REQ_IO_A2.15.6 |
| SD-INV-E2E-007 | Boundary / Layout | Long real-length item codes and names: 2.15 line headers stay one line (equal heights), outbound number and date cells nowrap (`DN2026110004 #1`, `2026-11-20`); screenshot 2.15-long-names (WU-SD-007b3c). Also E2E-005 asserts locked inputs look disabled (opacity < 1) and E2E-002 asserts aria-invalid + error colour on refused dates. | E2E | REQ_IO_A2.15.3, REQ_IO_A2.15.7, REQ_IO_A2.14.2 |

Result: 6/6 (chromium). First run found a product defect: after a coded refusal the focus target (quantity or date)
was still disabled — ngModel applies `[disabled]` in a microtask after the render — so focus was lost
(SD-INV-E2E-004 red). Fixed with `afterNextRender` + microtask; jsdom does not reproduce it, so the E2E is the
regression test. Frontend 1174/1174 (2 known FE-TIMEOUT-LEAK unhandled errors). Real-API run is 007b3b.

### WU-SD-007b3b invoice Playwright against the real API (ADR-183)

`tests/e2e/specs/real-api-sales-invoice.spec.ts`; run `& ./tools/scripts/Invoke-RealApiE2E.ps1 -Specs specs/real-api-sales-invoice.spec`.
Arrange through APIs only (fresh item, warehouse, 5% tax, approved customer, TWD credit limit, opening stock 10, Approved
TWD order 10 × 100, Posted delivery 10); active SAL.ORDER / SAL.DELIVERY flows switched off during arrange and restored.

| ID | Type | Scenario | Level | Trace |
|---|---|---|---|---|
| SD-INV-REAL-001 | Normal / Partial | sales: list → Issue → pick the order by keyword; line shows remaining 10 and the input `10` (server `10.000000`); issue 4 dated today → 2.14 detail opened and focused. SQL: one `sal_invoice` Posted (TWD, rate 1, net 400 / tax 20 / gross 420, created by sales), one line (delivery line, order line, item, qty 4.000000, 5%); `ar_posted_receivable` (Invoice, invoice id, 420 open, today); `ar_sales_source` (journal id, amounts); `gl_journal` Posted 420/420 with Dr receivable 420, Cr revenue 400, Cr output tax 20 on the TW01 account map. Screen: status, customer, rate, FI journal/receivable/source IDs, totals, line quantity / rate / net / tax / gross equal SQL. | E2E real | REQ_IO_A2.14, REQ_IO_A2.15, BR-SD invoice posting |
| SD-INV-REAL-002 | Idempotency / Conflict | API with the saved `request_key` and identical body → 200 same invoice and FI IDs, no new `ar_sales_source` / `ar_posted_receivable`; same key with quantity 3 → 409 `SAL.INVOICE.REQUEST_CONFLICT`; still one invoice, receivable open 420. | E2E real (API) | REQ_IO_A2.15.6 |
| SD-INV-REAL-003 | Normal | 2.15 for the same order: invoiced 4, remaining 6, input prefilled `6`; 2.14 keyword search lists exactly the invoice with order number and gross; dark screenshot. | E2E real | REQ_IO_A2.14, REQ_IO_A2.15 |
| SD-INV-ISSUE-UI-013 | Boundary | Source remaining `6.000000` → balance column `6`, input `6`; `9999999999999.999999` unchanged. | U | REQ_IO_A2.15 |

Result: 3/3 real (first run: `LINENO` is a T-SQL reserved word — alias bracketed; SD-INV-REAL-003 found the
`6.000000` prefill, fixed with `trimDecimalText`). Frontend 1176/1176.

## WU-SD-008a1 Return tables and state triggers (2026-11-23T15:29:44+08:00, ADR-184)

Requirements: REQ_F4112.7.1 v1.1; BR-SD-043～049; REQ_DS43.208/209/210. I = real SQL (`Database/SalesReturnSchemaTests`, written before the migration: red on missing types).

| ID | Kind | Scenario | Type | Trace |
|---|---|---|---|---|
| SD-RETURN-SCHEMA-001 | Normal + state + audit | Draft (2 billed + 1 unbilled) → Pending → lines stamped → Posted with FI credit (version 3); then header UPDATE/back to Draft → 51035, line UPDATE/INSERT → 51036, allocation UPDATE/DELETE → 51037, header DELETE refused; rows intact. | I | SD-RETURN-001/020 |
| SD-RETURN-SCHEMA-002 | State + edit | Draft lines/allocations replaced (EF orphan delete); number/key/order/customer changes → 51035; under Pending and Rejected line UPDATE/DELETE → 51036, allocation INSERT → 51037; Rejected → Posted 51035, Rejected → Pending with posted_at CHECK 547; Rejected → Draft makes lines editable; Pending stamps inbound once, restamp → 51036. | I | SD-RETURN-002/003 |
| SD-RETURN-SCHEMA-003 | Validation (5 shapes) | Pending → Posted refused (51035) when an inbound is missing, billed + unbilled ≠ quantity, billed portion without FI credit, FI credit without billed portion, or no lines; return stays Pending. | I | SD-RETURN-008/011/015 |
| SD-RETURN-SCHEMA-004 | Normal (unbilled only) | Wholly unbilled return posts with inbound and no FI credit. | I | SD-RETURN-012 |
| SD-RETURN-SCHEMA-005 | Duplicate + soft delete + company | Soft-deleted Draft keeps number and request key (duplicates refused); undelete → 51035; Posted FI credit ID or inbound movement reused in the same company → unique violation and rollback; another company reuses number, key, credit and inbound. | I | SD-RETURN-009/010 |
| SD-RETURN-SCHEMA-006 | Validation (20 shapes) | Header gross, negative tax, same-currency rate ≠ 1, blank key/fingerprint, Pending with credit, Posted without time, deleted non-Draft, deleted without time, zero quantity, negative/over unbilled, line gross, line_no 0, duplicate line_no, zero outbound, zero location, zero allocation quantity, allocation gross, duplicate invoice-line allocation → CHECK/unique error, zero rows. | I | SD-RETURN-004/005 |
| SD-RETURN-SCHEMA-007 | Company + delete protection | Order, delivery, delivery line, order line, invoice or invoice line from another company → FK error; returned delivery line and source order cannot be deleted. | I | SD-RETURN-014 |
| SD-RETURN-SCHEMA-008 | Precision + Draft delete | USD rate decimal(19,8), base decimal(19,4), quantities 2.999999/0.999999 on 2028-02-29 round-trip; physical Draft delete removes lines and allocations. | I | SD-RETURN-006 |

Not applicable in 008a1: permissions, HTTP 400/403/409/500, concurrency/lock order, replay and FI/INV rollback (no service or endpoint yet — 008a2～a6); files and batch (single document). Result: real SQL 31/31.
