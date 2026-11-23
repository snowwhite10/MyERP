# SD optimization log

## 2026-11-18T14:49:20+08:00 — WU-SD-004a2b14 tracking review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Saved delivery choices had no durable child table or detail projection, so tracked items were refused at Draft save. | The delivery schema predated lot/serial selection. | Add same-company child rows, field validation, MD mode guard, ordered detail loading and submit completeness check. | Draft can retain incomplete selections; invalid or duplicate serials fail atomically with a line reason. Real SQL delivery tests 17/17 passed. |

Review: raw SQL submission loads only this delivery's tracking rows using the parent lock and indexed line ID. The new table has a same-company FK and a unique ordered lookup index. INV stock/serial availability and final decision revalidation remain WU-SD-004b.

Full regression risk: two-thread run timed out in unrelated PUR and INV tests, both of which passed alone. A one-thread run timed out in FI and failed an MD lock test. SQL DMV showed `WAIT_SLEEP_LOWMEM_GUARD` and process low-memory signal; host physical memory available was about 1.1 GiB. No product code was changed to mask this. The work unit remains on a feature branch for a later full-suite run after memory recovers.
## WU-SD-003c5b order export button review — 2026-11-20T19:52:55+08:00

- Shared over per-page code: Blob error bodies are now parsed by a shared `blobJsonErrors()` operator (core/http/api-error.ts) and CSV row counting/saving by `shared/export/csv-export.ts`, instead of the INV pattern of parsing Blob errors inside each page. INV pages were not migrated in this unit (no behaviour change requested); candidates for a later clean-up.
- Correct counting: the first draft compared `X-Export-Row-Count` with a plain CRLF split, which miscounts remarks with embedded newlines; an RFC 4180 counter is used and covered by unit and E2E (quoted CRLF in every mocked row).
- UX review (28 screenshots, three languages, light/dark): P3 — unfiltered empty list said “no orders match the filters” while the table said “no orders yet”; added `exportNoOrders` and an E2E assertion for both wordings. Guideline review: result alerts were created inside new nodes; wrapped in a persistent `aria-live="polite"` region (E2E asserts it).
- Accepted: the 400 field detail shows the server text as sent (mock was English); real server ProblemDetails text and localisation are owned by the shared error display and were not changed here. The 400 path is rare because the page validates dates before searching.
- Process note: the backend CORS assertion was added before the implementation but its red run was not executed separately; it passed 16/16 after the change.

## WU-SD-003c5a order CSV export review — 2026-11-20T19:27:52+08:00

- Duplicate list/export filtering: list validation, sort whitelist and filters moved into one private `FilterAsync` used by both, so a filter that is 400 on the list is 400 on export too (existing order HTTP tests 7/7 still pass). Ordering stays per caller because export orders joined lines and adds line number.
- Count/write consistency: count and rows are read in one SNAPSHOT transaction (database already allows snapshot isolation, as INV exports do), so `X-Export-Row-Count` equals written rows even with concurrent edits.
- Remaining risk, accepted: a database failure after headers and some rows were streamed cannot turn into ProblemDetails, and the client gets a truncated file. Same trade-off as INV tracking export; 003c5b compares received line count with `X-Export-Row-Count` and warns on mismatch.
- Security: text cells are apostrophe-prefixed against formula injection, including leading spaces, tab and CR; numbers are emitted unguarded because they are invariant decimals, never user text. No further code change was needed after review.

## WU-SD-003c4 full-regression resource check — 2026-11-19T22:03:24+08:00

- One-worker simulated Playwright full run completed: 603 passed, 27 skipped, 0 failed; all 31 Edit cases passed inside it. Frontend unit regression passed 922/922. The SQL memory gate initially passed with 2.859/2.867/2.871 GiB available. During the one-thread backend full run, DocGen, Shared and Architecture test projects passed, but SQL integration reduced available memory to 1.135 GiB; the run was stopped. Backend full remains unverified and is a completion gate for this work unit.
- The full E2E run rewrote many unrelated screenshot artifacts. Only four new Edit validation captures and two regenerated Edit receipt captures were kept; all unrelated screenshot differences were restored. No product code was changed in this slice.

## WU-SD-003c4 Edit boundary review — 2026-11-19T21:27:46+08:00

- The Edit form kept its current customer picker and one-line minimum. Added browser checks for empty currency and an incomplete newly added line; each presents an inline reason, keeps the saved order at version 1 and sends no PUT. A malformed saved leap date or duplicate line number now has explicit browser regression coverage: the existing mapping refuses to open an editable form. The commercial receipt test also checks a four-place discount through the raw PUT and saved GET.
- Reviewed the Edit template against `ui-conventions.md` and the current Web Interface Guidelines. Inputs have visible labels, errors appear by their fields, and the first invalid field receives focus. Buttons use button elements and blocked states explain their reason. The four new and two regenerated light/dark screenshots were each inspected; no P1/P2 finding in these states.
- No product code change was required by these checks. Full simulated E2E and backend regression results remain pending at this checkpoint; SQL-backed DocGen's 13 unrelated source errors are tracked in WU-P0-043.

## WU-SD-003c4 action scope and boundary review — 2026-11-19T21:17:10+08:00

- A readable order can leave the user's EDIT action scope between GET and PUT. The Edit page now treats PUT 404 as an unavailable saved record, preserves entered values and disables another write until an explicit reload. The first inherited screenshot contained both an unavailable warning and a stale API error; the current E2E run regenerated it with only the warning. Light and dark versions of the 404 and changed-date 422 screenshots were opened individually, and the message, disabled controls and field reason were checked against the saved mock state.
- Added browser checks for empty business date, 501-character remark and seven-place quantity. All three show field errors and leave the saved version and remark unchanged; no PUT occurs. Edit 27/27 plus the added boundary case 1/1, frontend 922/922.
- Reviewed the changed template against the current Web Interface Guidelines and `ui-conventions.md`: action uses a button, the warning has a reload step, and the saved input is visible while the form is locked. No new P1/P2 issue in these states. The three free-memory samples were 2.082/2.144/2.378 GiB, so backend and full simulated E2E regressions remain unverified this turn.

## WU-SD-003c4 consistency diagnosis — 2026-11-19T16:58:45+08:00

- Feedback loop: the SQL-backed isolated DocGen command generated a report with 13 Error/614 Warning. The report names one duplicate `REQ_DS43.189` in FI/PUR, eleven undefined shorthand FI interface references, and one saved `sal_delivery_line_tracking` table absent from main's SD data-structure source. The source rows confirm the duplicate and shorthand; `git diff --name-only` confirms this Edit slice changed none of those files. The tracking table belongs to the blocked unmerged SD branch. These are source consistency defects, not a new Edit parser regression.
- No out-of-scope FI/PUR/feature-branch source was changed in this work unit. WU-P0-043 records the independent repair and requires a fresh SQL-backed consistency run. The generated output remains isolated under ignored `.tools/`.
- Full simulated E2E stopped around 407 passing tests when two workers left about 0.75 GiB free. Tracked FI/INV/MD/PUR screenshots regenerated during the partial run were restored; only the new Edit screenshots remain for commit.

## WU-SD-003c4 saved identity and decimal review — 2026-11-19T16:54:55+08:00

- Review: the Edit page should claim success only after a fresh saved GET agrees with selected customer, item and tax identities, edited commercial fields and the new version. Added a browser path through all three pickers and checked the PUT body plus saved receipt. A separate raw JSON response carries `9999999999999.999999` without JavaScript Number conversion; the screen, outgoing numeric token and receipt agree.
- The complete Edit browser suite passed 25/25 with one worker. Four new light/dark screenshots were opened individually; the selected labels and long decimal remain visible without overflow. Current Web Interface Guidelines and project UI conventions show no new P1/P2 issue in these states.
- Simulated E2E full regression was stopped after about 407 passing tests because two Chromium workers reduced host free memory to about 0.75 GiB. SQL-backed backend full regression was also stopped earlier at 1.2 GiB free. Neither incomplete run is counted as a pass.

## WU-SD-003c4 related-reference review — 2026-11-19T16:38:26+08:00

- Added Playwright rejection cases for inactive or deleted item, tax-rate and currency references. Each 422 names its field in the user's language, retains the unsaved remark, does not show success and leaves the mocked saved order at its original version. All 3 focused cases passed.
- Opened six new light/dark screenshots individually. Error title, field reason, edit controls and contrast are readable; no new P1/P2 UX finding. The current Web Interface Guidelines review found no new blocking issue in the Edit screen.
- The SQL-backed authorised order HTTP class passed 5/5. A subsequent Release backend full run passed DocGen, Shared and Architecture but was stopped after free host memory fell to 1.2 GiB during integration; no full-pass claim is made.

## WU-SD-003c4 company and receipt acceptance — 2026-11-19T16:34:54+08:00

- Review: switching to another authorised company must discard the previously loaded Draft before the new-company GET resolves. The first browser assertion assumed exactly one new-company GET and failed; the permission refresh also triggers a second GET. The corrected assertion checks that every post-switch GET carries company 2, the old form disappears, the 404 is specific, and no PUT occurs.
- Added a successful Edit browser path for date, currency, remark, six-place quantity and price, discount and saved version. The fresh saved GET agrees with the outgoing draft before the success alert appears. The 1000-line browser boundary prevents line 1001 and refuses an incomplete replacement without PUT.
- UX review: opened four new light/dark screenshots individually. The cross-company 404 has a clear return/retry path and no stale form; the receipt shows the stored number, version, customer and line count. No P1/P2 issue found. Current Web Interface Guidelines review of the Edit page found no new blocking issue; `ui-conventions.md` remains authoritative.
- Verification: Edit E2E 18/18 after company case; focused receipt 1/1 and line-limit 1/1; authorised SQL-backed HTTP 5/5. Full backend regression was started after a 2.606/2.603/2.607 GiB memory gate; its final result belongs in the next checkpoint.

## WU-SD-003c4 post-read status change — 2026-11-19T16:21:57+08:00

- Problem: a 422 `SAL.ORDER.INVALID_STATUS` after an editable GET displayed the status field as a generic line error. The edited draft stayed on screen with Save enabled, inviting another rejected PUT.
- Change: identify a Pending transition from the server's field detail, show the existing translated Pending reason, and require a fresh read before another write. Other invalid statuses use the generic noneditable reason. The new browser test first failed on the generic line error, then passed after the correction; it checks the unchanged saved remark, one PUT, disabled Save, and the blocked form after refresh.
- UX review: opened the two new light/dark screenshots individually. The Pending reason and refresh action are readable in both themes, the unsaved remark remains visible until refresh, and Save is disabled. Web Interface Guidelines review found no new blocking issue in the changed page; project UI conventions remain authoritative.
- Verification: Edit E2E 17/17; frontend full result recorded in progress after completion. Real SQL/HTTP and backend full remain pending.

## WU-SD-003c4 list-entry check — 2026-11-19T16:11:10+08:00

- Review: the order list shows an Edit link on visible Draft/Rejected rows under `SAL.ORDER.EDIT`. A VIEW-only user can still read a Draft but has no Edit link; immutable statuses have no entry. Added two browser assertions and both passed. The saved detail remains the authority for converted/delivered Drafts, since those facts are absent from the list summary.
- Verification: previous-round state-warning changes passed Edit E2E 14/14, frontend 922/922 and staged diff checks before recovery commit `ede2fa33`. New list cases passed 2/2. SQL memory gate 1.665/1.276/1.151 GiB DEFER; no backend full or real SQL result is claimed.

## WU-SD-003c4 immutable reason review — 2026-11-19T12:03:10+08:00

- Problem: the Edit page's noneditable warning did not identify whether the saved order was Pending, Approved, Cancelled, sourced from a quotation or already delivered/reserved. The first strengthened browser assertion failed on the generic Pending message.
- Change: map the saved status and source/delivery evidence to five translated warning keys. Keep the generic warning for malformed saved data. The edit form remains absent and the browser test counts zero PUT requests.
- UX review: opened the Approved, Pending, Cancelled, converted and delivered light/dark captures individually, plus Pending in English and Simplified Chinese. Each warning names the saved blocker, omits the form and remains readable in both themes; no P1/P2 issue remains for these states. The warning uses the existing semantic alert component. Current Web Interface Guidelines were reviewed against the changed page; no new blocking issue was found. The project UI conventions take precedence.
- Verification: frontend suite 922/922; Edit browser suite 14/14 and focused three-language case 1/1. SQL-gated backend regression is still in progress at this checkpoint.

## WU-SD-003c4 permission and immutable-state review — 2026-11-19T11:55:30+08:00

- Added browser assertions for Pending, Cancelled, quotation-converted and delivered saved orders: each loads a warning without an edit form or PUT. An EDIT-only role without VIEW is redirected to the no-permission page before any order GET or PUT. The mocked persisted order is checked through request counts and unchanged values.
- The two new tests passed 2/2 after correcting test assumptions about the existing translated warning and route guard; the previous twelve Edit cases passed 12/12 in the initial run. Ten new light/dark screenshots were opened individually; warning and forbidden states remain legible, with no success claim.
- UX finding (P2, same work unit pending): the immutable warning states allowed conditions but does not name the saved order's actual blocking status or delivery/source reason. Add status-specific translated guidance and browser assertions before closing 003c4.
- Web Interface Guidelines review used the current published rules. No production UI code changed in this slice; the reviewed warning and forbidden pages use readable text, semantic links and buttons, and dark-theme contrast. Project `ui-conventions.md` remains authoritative. Real SQL/HTTP and full regression remain unverified in this slice.

## WU-SD-003c4 network and hidden-order review — 2026-11-19T11:44:56+08:00

- Problem: Edit reads a saved order as text to preserve exact decimal tokens. On HTTP 404, Angular therefore left the ProblemDetails body as a string; `apiErrorKey` could not see `SAL.ORDER.NOT_FOUND`, and the screen incorrectly displayed an unexpected error. A Playwright assertion for the specific translated reason failed first, then passed after `getEditable` applied the shared `jsonTextErrors()` operator before parsing successful responses.
- Regression: simulated GET disconnection offers a retry with no form or PUT; simulated PUT disconnection keeps unsaved text, disables repeat save and leaves the mock persisted order unchanged. The hidden-order 404 has no form or PUT and now names the missing/out-of-scope order. The E2E guard permits only the exact console message caused by each intentional disconnection.
- UX review: opened six new light/dark screenshots for GET network, PUT network and 404, including replacement 404 images after the fix. The messages are readable in both themes, the uncertain PUT uses a warning and retains its form, and no state claims success. No new P1/P2 issue remains in these states.
- Web Interface Guidelines review: fetched the current published rules; reviewed the changed service and Edit page. The retry/refresh actions are buttons, the 404 message gives a next step, the form keeps labels and visible focus, and the uncertain state explains the disabled save. No new blocking finding in the touched UI. Project `ui-conventions.md` remains authoritative.
- Verification: Edit E2E 12/12 after the 404 translation fix; frontend full 922/922. SQL memory gate 2.910/2.896/2.915 GiB PASS. DocGen, Shared and Architecture backend projects passed; the integration/full run was stopped as free physical memory fell to 1.217 GiB. Real authorised HTTP and complete backend/E2E regression remain pending.

## WU-SD-003c4 error and receipt review — 2026-11-19T11:31:31+08:00

- Risk: the Edit browser contract lacked direct checks for a server 400 field error, an unresponsive saved GET/PUT and a PUT acknowledged by a stale saved receipt. A misleading success or duplicate write would be costly. Added Playwright cases that compare visible feedback with the mock persisted order and count writes; no product code change was needed because the existing guards passed.
- UX review: opened all six new light/dark screenshots for 400, GET timeout and PUT timeout. Field reason, retry, retained unsaved text and disabled duplicate save are readable; neither timeout shows success. The remaining visual states stay in the test plan.
- Web Interface Guidelines review: fetched the current published rules and checked the touched Edit screen and test assertions. The existing error display supplies a visible reason, form controls keep labels and focus styles, and refresh is a button. No new blocking UI finding in this slice; `ui-conventions.md` takes precedence for project layout.
- Verification: Edit E2E 9/9 and frontend full 922/922. SQL memory gate passed at 2.854/2.844/2.851 GiB, but the Debug backend full integration run was stopped when free physical memory fell to about 0.7 GiB. DocGen, Shared and Architecture projects passed before interruption; full backend and complete browser regression remain required before closing the work unit.

## WU-SD-003c4 browser review — 2026-11-19T11:11:13+08:00

- Problem: the initial Edit E2E run found a missing tax-rate fallback translation; all three browser cases failed the i18n guard. Replaced the fallback with an existing translated label plus the saved tax-rate ID, then the 3/3 baseline passed.
- Problem: a 422 reference refusal showed an “unexpected error” banner above its correctly localised field reason. The new red-capable browser/visual check found it; the Edit-specific refusal text now gives an actionable message.
- Problem: immutable orders showed an empty, disabled Draft form. The editor now hides that form and keeps the reason visible; the state E2E asserts no form and no PUT.
- Problem: success feedback used the PUT response after saved GET validation. The banner now uses the verified saved GET identity and fields.
- Verification: focused Edit E2E and screenshots; full frontend, real SQL/HTTP and full E2E remain open. SQL memory gate 1.629/1.563/1.554 GiB was DEFER below 2.5 GiB.
- Web Interface Guidelines review (fetched on 2026-11-19T11:15:10+08:00) of the touched Edit/Create and list code: added `autocomplete="off"` to the reused non-auth form inputs. The Edit navigation remains a link and the form controls have labels, names and visible focus. No further blocking finding in the touched lines; project `ui-conventions.md` remains authoritative.
- UX review: all 18 captured 2.6 screenshots were opened individually. The English normal view exposed a Chinese tax fallback cached across language changes; the fallback now shows the language-neutral saved tax-rate ID. The first validation “light” image was actually dark because the theme survived reload; the test now explicitly selects light before that capture. Timeout and other pending states remain in the test plan.

## WU-SD-003c4 decimal-safe Edit contract review — 2026-11-19T07:17:00+08:00

- Problem: ordinary Angular JSON parsing converts persisted order decimals to JavaScript numbers. Opening an Edit form and sending those numbers could round `DECIMAL(19,6)` values. The existing order line frontend type also omitted `taxRateId` required for a full replacement.
- Change: read the saved edit detail as text and preserve `quantity`, `unitPrice` and `discountRate` JSON tokens as strings, add `taxRateId` to the line type, map only valid manual Draft/Rejected snapshots, and send versioned PUT with the existing exact-decimal serializer. A saved receipt read and UI remain for the next checkpoint.
- Test protection: service and mapper tests failed before implementation, then focused suites passed. Checked that an escaped property-like phrase in `remark` remains intact. No visual UI changed, so screenshot and Web Interface Guidelines review are deferred until the Edit page exists.
- Verification at 2026-11-19T07:21:36+08:00: frontend 922/922, production build passed, DocGen 22/22 and `git diff --check` passed. No backend or route change, so the previous full backend result remains 3632/3632; Edit UI, E2E and real SQL acceptance are still pending.

## WU-SD-003c3 completion review — 2026-11-19T07:09:18+08:00

- Problem: an expired or no-longer-approved quotation returned 422 without field details, so Create displayed only a generic refusal. The first 1000-line E2E attempt also read the button before Angular finished navigation; a cross-company modal capture attempted to change theme while the modal overlay was open.
- Change: show the server's known `SAL.QUOTATION.INVALID_STATUS` and `SAL.QUOTATION.EXPIRED` reasons in all three languages, retaining the generic translated fallback for unrecognised 422. The two status tests were red before the change and green afterward. The browser tests now wait for the Create button and close the selection modal before theme switching; no product defect was inferred from those test setup errors. The 1000-line case has a 60-second test allowance because rendering 1000 line controls took 20–26 seconds, with the same assertions intact.
- UX review: individually inspected 24 new light/dark screenshots for company picker, hidden/status-changed/expired source, and English/Simplified Chinese 400/409/422/500; no untranslated key, misleading success, clipped text or dark-mode contrast issue found. The already recorded 28 Create screenshots cover normal, empty, validation, success, CREATE-only and network/timeout states. Web Interface Guidelines review found visible focus, inline errors, named disabled reasons and preserved input; the existing full-page modal screenshot includes undimmed content below the viewport, a P3 capture artifact rather than a product overlay defect.
- Verification: Release backend build 0 warnings/errors and 3632/3632 tests after SQL memory gate 2.882/2.873/2.810 GiB; frontend 917/917; final full simulated E2E 570 passed/27 skipped, followed by the two added language tests 2/2 and complete Create E2E 24/24. `git diff --check` passed. No migration or backend product change in this final slice.

## WU-SD-003c3 saved identity review — 2026-11-19T06:21:32+08:00

- Problem: the browser treated any nonempty Draft receipt with the returned ID as a successful create. It could show success for the wrong customer, source quotation or line identity.
- Change: compare manual customer, business date, currency and ordered line/item identities, or the selected quotation ID, with the CREATE-scoped saved receipt before showing success. A mismatch keeps the unknown-result warning and blocks repeat submission. Unit tests were red before the matcher existed and passed 6/6 afterward.
- Coverage: focused Create E2E 4/4 for wrong-source and hidden/invalidated quotation, prior Create 14/14, frontend 917/917. The hidden-quotation light/dark screenshots were visually reviewed: specific translated cause, legible contrast and retained selection. Web Interface Guidelines review found the changed feedback and controls have visible focus and a clear next step; no new P1/P2 finding. Full E2E and SQL regression remain pending.

## WU-SD-003c3 Create permission and receipt review — 2026-11-19T06:09:31+08:00

- Problem: a CREATE-only user saw a VIEW-only return link and an uncertain result message instructing them to use a forbidden order list. The 201 receipt path also trusted a mismatched or empty receipt and hid the trace ID when the receipt GET returned 500.
- Change: gate the return link by `SAL.ORDER.VIEW`, provide CREATE-only verification guidance, require the receipt's ID, Draft state, document number and nonempty saved lines before success, and display the receipt-read 500 trace. Show explicit minimum/maximum line and quotation-selection disabled reasons. Tests for mismatched/empty receipt and receipt-read 500 were red before the UI fixes and green after them.
- UX review: personally inspected all 28 Create screenshots in `tests/qa/SD/screenshots/2.5-*.png`. zh-Hant light/dark normal, validation, empty quotation, 400/409/422/500, network, timeout, success and permission states have readable contrast and specific messages; en/zh-Hans normal screens have no untranslated keys or clipping. Web Interface Guidelines review found no P1/P2 issue in the changed UI; the form retains visible focus and first-error focus. Screenshot capture of the empty selector includes extra page height under the modal, a P3 test-artifact issue to tidy with the remaining QA matrix.
- Verification: Create E2E 14/14 and frontend 915/915 after the receipt-read fix; Release backend build 0 warnings/errors, Architecture 2171/2171, Shared 386/386 and DocGen 22/22. SQL memory gate deferred full integration twice below 2.5 GiB. Full E2E was started and stopped after 42 passing cases to prioritize a red/green receipt-read 500 regression; full E2E must run before completion.

## WU-SD-003c3 Create field feedback review — 2026-11-19T01:44:22+08:00

- Problem: on invalid submit, focus stayed on Save; the 400/422 field list copied English server messages into a zh-Hant screen. Web Interface Guidelines form review identified the missing first-error focus and untranslated detail.
- Change: focus the first invalid control, show translated field names and known inactive-customer/unavailable-quotation reasons, and give a translated field-specific fallback for other server messages. Recaptured and inspected the 422 light/dark screenshots; both show the reason and readable contrast.
- Verification: Create E2E 7/7 and frontend 915/915. Remaining Create QA includes disabled reasons, empty source, CREATE-only browser scope, three-language states and complete regression; this is a partial checkpoint.

## WU-SD-003c3 conversion error review — 2026-11-19T01:27:48+08:00

- Problem: Create handled a quotation conflict under `SAL.ORDER.QUOTATION_ALREADY_CONVERTED`, but the backend emits `SAL.QUOTATION.ALREADY_CONVERTED`; the screen would show an untranslated key. The 400 field list was not passed to the common error component, and a 500 trace ID was hidden while repeat submission was locked.
- Change: Use the actual server error code with three translations, pass parsed 400/422 field details to the shared error display, and show the 500 error with trace ID beside the unknown-result warning. Conversion E2E checks the saved receipt and 409 no-save state; 400/500 E2E passed. Frontend units 915/915 and Create E2E 4/4 passed.
- Review: The form uses semantic buttons and visible focus styles. Follow-up under this same work unit: first invalid field focus, disabled action reasons, source empty state, and all light/dark screenshots required by the UX checklist and Web Interface Guidelines.

## WU-SD-003c3 saved receipt review — 2026-11-19T01:01:53+08:00

- Problem: CREATE-only users could receive a 201 response but the existing detail GET requires VIEW, so the browser could not independently confirm saved data before showing success.
- Change: added a creator-only receipt query that applies the CREATE action's company and data scope, saved `created_by` and soft-delete filters. The normal VIEW query keeps its own permission. The frontend service exposes this separate GET.
- Protection: a new front-end service test failed before implementation and passed after. HTTP integration assertions now cover own receipt, a VIEW-only 403 and another creator's 404. Release build passed with zero warnings/errors, frontend 915/915 and Architecture 2171/2171. SQL memory guard measured 2.594/2.474/2.682 GiB and deferred running the new HTTP assertions. No page controls changed, so screenshot review remains for the next checkpoint.


## WU-SD-003c3 form contract review — 2026-11-18T21:05:31+08:00

- Reviewed the new form validator and HTTP service against the server Draft input. The bounds match `decimal(19,6)`, discount 0–100 with four places, 1–1000 lines, date and reference requirements. No client monetary calculation or JavaScript Number conversion is used at the write boundary.
- Reviewed changed front-end files against the current Web Interface Guidelines and project UI conventions: these files have no rendered controls, so no screen findings apply yet. Create page focus, feedback, dark mode and responsive review remain part of this same work unit.
- Focused tests 7/7, full front-end 914/914 and production build pass. The route and screen remain pending; no SQL migration or back-end behaviour changed.

## WU-SD-003c2 history isolation review — 2026-11-18T20:50:15+08:00

- The existing query orders the scoped SD lookup before SYS history access. Real SQL/HTTP now exercises a saved home round, a foreign-company matching document ID, and Branch/Self VIEW exclusions. No product-code change was needed; the test protects this ordering.
- The first test run used substring matching for a one-digit approval ID and incorrectly matched the order ID in a legitimate 404 detail. The assertion now checks the absence of `approvalHistory` and `approvalInstanceId` fields in the structured response. The focused SQL test passes.
- Credit has an offline `SalesCreditDecision` and FI snapshot contract, but no persisted SD decision or HTTP projection. Displaying the assessment in this View would misrepresent a current calculation as saved approval evidence. WU-SD-006a–006c owns the persisted decision and display.

WU-SD-003c2 SQL fixture regression (2026-11-18T20:30:06+08:00): The first modified backend full run failed at `OrderDetailUsesViewActionScopeBeforeRevealingSavedLines`: its hand-built `MyErpDbContext` included the order model but omitted `SalesDeliveryDbModel`, so the new saved-balance query could not create `DbSet<SalesDeliveryLine>`. The exact test failed with that exception; adding the delivery model to the fixture made the same test pass. The production host already registers both models, as the authorised HTTP balance test demonstrated. No production query change was needed.

WU-SD-003c2 delivery balance review (2026-11-18T20:24:00+08:00): The real SQL regression initially found no delivery fields in the order response. A single grouped, same-company SQL read now supplies Posted and Pending totals only after the order passes `SAL.ORDER.VIEW` scope, avoiding one query per line; creation/replace mapping uses zero consumed quantity. The first browser copy called Pending quantity merely "待交數量", which was too close to "剩餘可交". The labels now say "審批中預留" and "已過帳交貨"; light/dark detail screenshots show separate quantities without overflow. Web Interface Guidelines review of the changed order table found semantic table cells and no new keyboard or focus issue. Existing hard-coded ISO dates and raw amount formatting remain follow-ups outside this slice. The prior order transaction tests also failed source compilation after `IApprovalHistoryReader` became mandatory; their test fixture now supplies an explicit empty reader instead of relying on an old binary.

WU-SD-003c2 detail retry review (2026-11-18T16:02:40+08:00): Detail reads already had a six-second timeout, but their error state gave no retry action. SD-ORDER-UI-008 failed at the missing control, then passed with a button that reuses the selected order ID; it leaves the list query and saved rows alone. The new light/dark timeout screenshots were inspected against UX checklist B3, C2, D1 and E1–E3: the reason and retry remain legible and no stale detail is shown. Web Interface Guidelines review of the changed template found a semantic, keyboard-focusable button with translated text. The existing ISO date and URL-state follow-ups remain. An authorised HTTP run also exposed a stale numeric status assertion after ADR-140; the test now checks the string contract. No production backend behavior changed.

WU-SD-003c2 review (2026-11-18T15:29:35+08:00): The first read-only page uses the existing shared pagination, API error display, permission guard and company epoch pattern. Screenshots show legible light/dark tables and explicit filtered-empty/500 states with trace ID. Review found the detail response has no delivery balance or approval history even though the interface specification requires both; leaving them off the screen avoids fabricated figures. The status query previously used enum model binding, which can accept numeric values despite the named JSON contract; a named-string HTTP adapter now rejects numeric input. Pending work: run the authorised HTTP test when SQL memory gate passes and add the remaining detail projections. Web Interface Guidelines review found ISO date display and detail panel URL state as follow-up UX improvements; the page's server ISO date is currently shown verbatim for audit clarity. No mutation or zero-change success path exists.

## 2026-11-11T15:30:30+08:00 — WU-SD-005a7 decision selection integrity

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Public `Decide` accepted contradictory selection metadata and could use a malformed selected rule. | It validated only client line fields; callers could construct `SalesPriceSelection` without using `Select`. | Validate selected snapshot, active state and coherent status/error/conflict metadata before pricing decision; retain field-error priority. | Two targeted tests first failed, then pricing 31/31 and Architecture 1591/1591 passed; Sales Debug build has zero warnings/errors. |

Review: the validation is pure and does not read SQL or change permission policy. Additional unknown-status, nonpositive conflict ID and malformed-line precedence cases passed. SQL transaction, HTTP and persisted document acceptance remain WU-SD-005a.

## 2026-11-11T15:24:41+08:00 — WU-SD-005a6 overlap snapshot integrity

| Finding | Cause | Change | Effect |
|---|---|---|---|
| An invalid existing rule could be silently filtered out of the overlap check. | `FindOverlaps` filtered scope/status before checking candidate integrity. | Validate proposed and all existing snapshots, then reject duplicate existing IDs; reuse selection's snapshot validation. | New test failed first and now passes; 29/29 pricing, 1589/1589 Architecture, Sales Release build has zero warnings. SQL locking and HTTP remain WU-SD-005a. |

Review: ID 0 is accepted only for the proposed unsaved rule; inactive/deleted valid rows remain excluded from overlap results. No migration, API or UI changed. The memory gate returned DEFER, so SQL and full backend tests were not run.

## 2026-11-05T06:14:13+08:00 — WU-SD-003a10 generated number length

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Conversion accepted a generated number longer than the 30-character order column. | The pure builder checked only for blank text. | Check the 30-character maximum before building the Draft; test the 30/31 boundary. | The 31-character test was red then green; Architecture 303/303. Real SQL number rollback and HTTP remain WU-SD-003a. |

The memory gate returned DEFER at 1.624/1.662/1.603 GiB; SQL was not run. Review found no changes to saved source values, status or permission rules.

## 2026-11-05T06:08:42+08:00 — WU-SD-003a9 saved rate SQL range

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A saved exchange rate with twelve integral digits passed pure conversion but cannot fit the order DECIMAL(19,8) column. | The guard used the DECIMAL(19,6) unit-price limit for an eight-scale rate. | Set the exclusive rate bound to 100,000,000,000 while retaining the eight-place scale check. | The over-limit case failed first; the maximum valid value and nine-place invalid value passed. Architecture 301/301 after the fix. Real SQL conversion remains unverified. |

## 2026-11-05T06:03:00+08:00 — WU-SD-003a8 saved date consistency

| Finding | Cause | Change | Effect |
|---|---|---|---|
| An Approved saved quotation with expiry before business date could produce a Draft when today's date was still within expiry. | The conversion builder checked only that both dates existed and that expiry included today. | Reject the reversed saved date range after lifecycle validation. | The focused test failed before the change and passed after it; full Architecture 298/298. The first placement masked the existing expired-source error, so moving the new check after lifecycle validation restored that error. No SQL acceptance is claimed. |

## 2026-11-05T02:10:38+08:00 — WU-SD-003a7 saved text and expiry guard

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Valid monetary totals could still produce an order snapshot with overlength saved names, codes or remark, or a missing expiry date. | The pure builder trusted persisted text widths and checked only business date. | Check the order column limits and missing expiry before copying; preserve the existing expired-source error path. | Six boundary tests failed first and then passed; the existing whitespace check passed both times. No source repricing or mutation. |

## 2026-11-05T01:58:52+08:00 — WU-SD-003a5 saved commercial source validity

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A corrupted Approved quote with valid totals could copy a zero quantity, negative unit price, missing identity or invalid rate into an order. | The pure snapshot checked source linkage and sums but assumed all saved commercial fields were valid. | Validate saved header and line identity/numeric ranges before copying, with one named source-integrity error; retain the original saved amounts. | Eleven red source cases turn green; valid snapshot remains unchanged. No SQL or HTTP conversion is claimed. |

## 2026-11-05T01:50:48+08:00 — WU-SD-003a4 saved line arithmetic

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Header totals matched all saved lines while one line's gross differed from net plus tax. | The six-component sum check compared only header to line totals. | Check both original and base gross against each line's net plus tax with checked arithmetic before copying. | The pure snapshot builder rejects a corrupted source with `SAL.ORDER.SOURCE_INCOMPLETE`; valid saved snapshots still copy unchanged. |

The two currency cases failed before the change and the focused suite passed 17/17 afterwards. Debug build had zero warnings/errors; Architecture 261/261, Shared 381/381, DocGen 22/22 and isolated consistency 0 Error/580 Warning passed. SQL source locking, SYS number rollback and authorised HTTP remain in WU-SD-003a.

## 2026-11-05T01:44:41+08:00 — WU-SD-003a3 saved amount integrity

Review found that line count and persisted IDs alone allowed a quotation with a mismatched saved header total to become an order. The conversion builder now compares each of the six saved monetary header components with its saved-line sum and maps overflow to `SAL.ORDER.SOURCE_INCOMPLETE`. It retains the saved values and does not consult MD or recalculate prices. The seven new assertions failed before the change and the focused snapshot suite passed 15/15 afterwards. Debug build had zero warnings/errors, Architecture 259/259, Shared 381/381, DocGen 22/22 and isolated consistency 0 Error/580 Warning passed. SQL locking, source uniqueness and HTTP are still unverified by this pure test.

## 2026-11-05T01:36:37+08:00 — WU-SD-003a2 create-source review

The manual-only store rejected every quotation snapshot before EF could persist it. A pure source-shape guard now accepts manual and quotation Drafts under the existing transaction and company checks. Review kept source eligibility, independently counted lines, quote locking, SYS numbering and database uniqueness in WU-SD-003a, where real SQL can verify them. The first focused run exposed xUnit `InlineData` integers binding to nullable `long`; changing literals to `long` made the targeted 8/8 pass. Debug build had zero warnings/errors; Architecture 252/252, Shared 381/381, DocGen 22/22 and isolated consistency 0 Error/580 Warning passed. No SQL acceptance is inferred from the pure tests.

## 2026-11-05T01:29:56+08:00 — WU-SD-003a1 conversion snapshot review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A complete line count alone cannot detect a partial or duplicated saved-line load. | Navigation collections can be incomplete or contain the wrong parent while matching the expected size. | Require an independent saved-line count, positive unique persisted line IDs and line numbers, and matching quotation/company on every line. | The pure builder rejects inconsistent input with `SAL.ORDER.SOURCE_INCOMPLETE` before creating an order. The future SQL caller must obtain both reads under its source lock. |
| SQL retrieval order is not a business line order. | A navigation collection can arrive in a different order from `line_no`. | Copy in `LineNo` order; add a reversed-load regression. | The Draft keeps the saved line sequence without changing the source collection. |

Review found no direct MD/FI table access, monetary recomputation, DateTime system clock use, or persistence side effect. The new code is preparation only; SQL transaction and HTTP conversion remain open in WU-SD-003a. Debug build, Architecture 244/244, Shared 381/381, DocGen 22/22 and isolated consistency 0 Error/580 Warning passed. SQL memory gate was DEFER at 2.470/2.390/2.436 GiB, so no SQL integration or smoke acceptance is claimed.

## 2026-11-04T21:07:06+08:00 — WU-SD-002b5 Edit review

2026-11-05T01:19:33+08:00 DocGen diagnosis: isolated consistency check returned 34 `UNKNOWN_REQ_F` errors, all for SD requirements after REQ_F4112.1.2. An inserted blank line split the requirements Markdown table, so the parser stopped there. Removed the line; the same isolated command now reports 0 Error / 580 Warning. No parser change or weaker check was needed.

2026-11-05T00:50:58+08:00 follow-up: a nonnumeric Edit URL produced `editId = 0`, which selected the Create branch. Keep the route's Edit identity even when its ID is invalid; detail loading now fails closed and SD-QUOTE-EDIT-012 asserts that neither PUT nor Create POST occurs.

2026-11-05T01:04:46+08:00 review: added SD-QUOTE-EDIT-013 to exercise a late old-company detail response after company switching; the epoch check is the product guard. The browser test asserts the new company's customer remains visible and no write occurs.

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A stale-version edit displayed both a warning and a generic red conflict. | The Create error path and Edit warning both handled HTTP 409. | Keep one Edit warning with an explicit refresh action; retain the entered text and block repeat PUT. | The user sees one recovery path and cannot silently overwrite a newer version. |
| Opening a saved high-precision decimal through JSON parsing could round it before a user edits anything. | `HttpClient` parsed JSON numbers into JavaScript Number. | Edit GET parses only quantity, unit price and discount tokens as text before `JSON.parse`; the existing PUT emits validated numeric tokens. | A `9999999999999.999999` saved quantity remains exact in the Edit model and PUT. |
| The first full E2E run failed in the new permission scenario with `toLocaleString` on undefined. | Its mock list row omitted required `originalGross`; the page correctly assumed the API contract. | Complete the mock row and retain the same permission assertions. | The focused test now exercises edit visibility and deep-link denial instead of an invalid fixture. |

Code review against web interface guidelines and `ui-conventions.md`: fields have labels, inputs show focus and validation, navigation uses links, ineligible states explain disabled actions, and the editor fits 1280px light/dark captures. Screenshot review found no additional P1/P2 issue after the duplicate-conflict fix.

## 2026-11-04T20:07:30+08:00 — WU-SD-002b4 Create page review

2026-11-04T20:30:47+08:00 regression review: the first full Playwright run finished 464 passed, 5 skipped, 1 failed because its new 768px assertion sampled before `BreakpointObserver` collapsed the 220px sider. A targeted DOM width probe found `erp-layout__actions` ending at x=918 while the viewport was 768; the existing settled-layout list test starts at 768 and passes. The Create test now waits for the sider to reach its 64px collapsed width, then retains the strict no-outer-overflow assertion. The exact test went red and green. Visual inspection also found two stale date errors still displayed after valid input; a new ready-state assertion failed before the form revalidated on every input change, then passed. The resulting light/dark ready screenshots show no stale warning. The `diagnosing-bugs` loop used the single Create Playwright case; temporary `[DEBUG-sd-create-overflow]` instrumentation was removed. No product layout change was needed.

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A raw output-tax ID would force users to guess an internal database key | The first Create form rendered `taxRateId` as a numeric input | Replaced it with two MD selection dialogs: output tax code, then a dated rate; the form shows code and percentage | The chosen rate ID is visible by business meaning and remains server-validated |
| An unknown 5xx/network POST result could be retried into a duplicate quotation | The first error path treated every failed response as safe to retry | Unknown results now show a warning with trace ID, disable Save, and direct the user to the saved list | No false success or blind repeat POST; the server's result must be checked |
| A 400 response with field errors was initially shown as a generic validation failure | The Create page did not map server field paths | Map up to 20 field paths to translated, line-specific details; retain user entries | The user can identify the rejected line/field without English server text |
| Form navigation and invalid-field focus were unclear | No leave guard or first-error focus in the first draft | Added route/beforeunload unsaved guard, explicit labels, visible focus, first invalid field focus, and Save spinner | Keyboard and error recovery are clearer; no auto-save is implied |

Reviewed `quotation-create-page.ts`, `quotation-page.ts`, `quotation.service.ts`, route and translations against the current Web Interface Guidelines and `ui-conventions.md`. The standard uses business-date `yyyy-MM-dd` input and ng-zorro controls. Playwright screenshots under `tests/qa/SD/screenshots/2.2-*` were inspected in light/dark and three languages; no remaining P1/P2 visual issue was found. The UI tests use mocked MD/SD endpoints, while existing real SQL/HTTP tests remain the authority for persistence and company scope.

## 2026-11-04T16:04:38+08:00 — WU-SD-002b3 draft form model review

- Problem: converting discount text through `Number` solely to compare with 100 introduced an unnecessary floating-point path. Cause: the first validator draft used numeric comparison. Fix: compare the bounded decimal integer part lexically; quantity, price and discount remain raw strings. Effect: six-place boundary test and full frontend 744/744 pass.
- Reviewed `quotation-draft-form.ts` and its focused tests against the current Web Interface Guidelines and `ui-conventions.md`: the unit renders no control, so label, focus, dark mode, responsive and screenshot checks are deferred to the actual Create/Edit page. `myerp-ux-review` states and screenshots are not applicable for this model-only slice. No P1/P2 visual finding is claimed.

## 2026-11-04T15:41:08+08:00 — WU-SD-002b2 read-only page review

- Problem: an impossible calendar date could pass `Date.parse` and issue a misleading GET. Cause: JavaScript normalized overflow dates. Fix: compare month/day against the actual UTC month length before applying filters. Effect: UI-011 displays validation and sends zero GET requests.
- Problem: at 768px the quotation table and global company selector produced outer horizontal overflow. Fix: scroll the quotation table inside its own container and narrow the selector from 200px to 160px at 768–900px. Effect: UI-012 measures at most 1px outer overflow in light and dark mode; the row stays visible.
- Review: company switch already invalidated list/detail epochs, and URL page/pageSize already restored correctly; UI-009/010 protect these behaviors. Current Web Interface Guidelines review found labelled controls, keyboard buttons and visible focus, but the saved-number action uses a button rather than a deep link; that navigation enhancement belongs to the later WU-SD-002b write/view page. UI conventions take precedence for the fixed yyyy-MM-dd business date display. Four new screenshots were inspected; no P1/P2 UX finding remains in this slice.

## 2026-10-29T13:08:15+08:00 — WU-SD-002a full-suite resource review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Three full-suite attempts lack an integration completion summary although focused quotation SQL/HTTP cases pass | Available physical memory fell below 1 GB during integration; the current host has only 1.85 GB available after build-server shutdown | Kept the full-suite gate open, recorded the resource threshold and retry command, and marked the unit blocked after three failed attempts | No product or test behavior changed; quotation acceptance remains unverified by the full suite |

The diagnostic loop is the existing full-suite command plus available-memory monitoring. It cannot be safely rerun at the current 1.85 GB, so no product-code hypothesis or fix is claimed.

## 2026-10-29T12:11:36+08:00 — WU-SD-002a error response review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| SD-QUOTE-045 lacked direct evidence for shared 500 and server-side timeout serialization | Existing ProblemDetails test covered a generic unexpected failure only | Added two synthetic middleware cases asserting the 500 title, error code, traceId, instance and hidden exception text | Focused 2/2 pass; no product change needed; actual SQL timeout and browser timeout behavior are outside this server-handler check |

The full backend gate remains open. Host free memory fell to 0.24 GB during the focused run, and interactive IDE, DevHub and browser processes account for most use; no user processes were stopped.

## 2026-10-29T06:47:33+08:00 — WU-SD-002a SYS HTTP checkpoint review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Successful submit and final SYS HTTP decision lacked end-to-end evidence | Earlier tests covered the service layer or rejected MD input | Extended the authorised HTTP test with a distinct approver, durable instance, stale resubmit, final approval and history checks | HTTP 4/4 and quotation SQL 22/22 pass; no product change was needed |

The quotation detail DTO still lacks its approval instance ID and derived expiry; VOID has no route. These remain in WU-SD-002a.

## 2026-10-29T06:37:05+08:00 — WU-SD-002a authorised HTTP review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The six routed actions had no real-MD authorised create/edit HTTP regression and no HTTP proof that rejected MD blocks submission. | Earlier HTTP tests covered anonymous, VIEW-only and DELETE paths. | Added one live SYS writer fixture with real MD rows, HTTP 400/201/200/409/422 checks and SQL snapshot assertions. | HTTP 4/4 and quotation SQL 22/22 pass; stale edit and rejected-customer submit leave the saved Draft consistent. No product code change was needed. |

Full backend validation remains outstanding; free physical memory was approximately 1.49 GB during this checkpoint.

## 2026-10-29T06:30:18+08:00 — WU-SD-002a DELETE HTTP review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| An HTTP integration case initially expected a quotation-specific conflict code and a version 1 seed that was actually version 0. | `ConflictException` uses the shared concurrency code; direct EF test seeding does not initialize `BusinessEntity.Version`. | Corrected the expected shared code and explicitly seeded version 1. | The test now exercises stale 409, successful 204 and repeated 404 against the intended saved version; 3/3 quotation HTTP tests pass. No product change was needed. |

## 2026-10-29T06:23:01+08:00 — WU-SD-002a quotation HTTP review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The internal quotation services had no routed authentication boundary or API document. | The prior slice stopped at service integration. | Added six action-specific controller routes with permission attributes and OpenAPI response types; tested anonymous routing and a real SYS VIEW-only role against SQL. | 401/403/404 company and permission boundaries now have HTTP evidence; authorised mutation and final decision paths still need integration coverage. |


## 2026-10-29T06:16:31+08:00 — WU-SD-002a live SYS review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The prior submission tests used a mocked SYS result, so they could not prove persisted approval history, final callback atomicity or same-version submit serialization. | The saved quote and workflow were exercised in separate fixtures. | Added one real-SQL integration fixture with actual SYS users and flow, final SD callback, two independent submission connections and a disabled-flow zero-step branch. | Quotation SQL 18/18 passes; one concurrent winner has exactly one SYS instance, final rejection updates SD, and zero-step approval persists both states. No product code change was needed. |

Complete backend validation is still outstanding because the host had approximately 1.34 GB free physical memory at verification time.

## 2026-10-29T06:08:59+08:00 — WU-SD-002a real-MD submission review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Saved MD snapshots alone cannot prove submit eligibility | The prior submission test used mocked MD lookups | Extended the real-MD SQL fixture to change four persisted reference states and check rollback before SYS invocation | All four invalid states reject, saved Draft/version remain unchanged, and restored references submit; quotation integration 17/17 |

## 2026-10-29T05:50:51+08:00 — WU-SD-002a submission review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A submission could leave SD Pending if the SYS call or an MD recheck failed outside a common transaction. | Quotation had no submission command. | Add a Serializable, action-scoped command with a header update lock, fresh MD reference checks and a single commit after SYS returns; reject invalid SYS result shape. | Real SQL tests verify pending and immediate approval plus unchanged Draft/version after MD or SYS failure. |
| A valid 500-character quotation remark could be rejected by SYS's 200-character approval summary limit. | The two fields have different documented maxima. | Copy at most 200 characters into the SYS summary; keep the saved quotation remark intact. | The real SQL submit test checks the summary length while retaining a 250-character saved remark. |
| A create test intermittently reported a quotation after its intentionally failed create. | Its company-wide `AnyAsync` also matched another test's quotation when the shared SQL fixture ran tests in parallel. | Scope the absence/count assertions to this test's unique SYS number prefix. | The original test passed alone; the quotation class suite passed 17/17 with two threads after the assertion became source-specific. |

The review found no safe reason to recalculate saved prices or tax amounts at submission: those commercial snapshots belong to the saved Draft. Real MD changes, live SYS history and two-connection submit races still require acceptance tests.

## 2026-10-29T02:10:41+08:00 — WU-SD-002a final-decision handler review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A late or repeated SYS result could otherwise mutate a quotation from a different approval round. | The quotation had no final-result handler yet. | Require a caller-owned transaction, Pending state and exact approval instance ID before changing status; register the handler for SYS dispatch. | Real SQL 2/2 verifies approval, rejection, rollback, wrong instance and repeated decision; full backend 1306/1306 passes. |

The handler uses the shared DbContext and leaves SaveChanges and commit to SYS. The workflow submit service and live MD checks are still needed before a quotation can enter Pending through the product path.

## 2026-10-29T01:59:27+08:00 — WU-SD-002a list and concurrent-command review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The saved-detail query did not provide a scoped quotation list, and command locking had no two-connection regression. | The prior slice covered only one document at a time. | Add VIEW-scoped filtered count and stable ID tie-breaker; exercise two simultaneous EDIT and DELETE commands against separate SQL connections. | Focused real SQL 6/6 confirms one edit plus 409, one delete plus 404, persistent snapshot/number, and scoped list results. |

The service-layer list has no HTTP/Swagger exposure yet. The concurrent test verifies the existing header lock without changing its implementation. SYS approval remains open.

## 2026-10-29T01:48:11+08:00 — WU-SD-002a quotation EDIT review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The Draft store could replace saved lines without an application-level EDIT scope check or fresh MD snapshot. | It was an internal persistence primitive awaiting a command boundary. | Add a Serializable command that locks the active company header, checks exact EDIT scope and version before MD lookup, then rebuilds and atomically replaces the entire commercial snapshot. | Real SQL verifies missing permission, hidden owner, stale version, new customer/item/amounts, permanent number/owner and Rejected-to-Draft revision; focused 3/3 and Debug build pass. |

Concurrent commands, quotation list, SYS approval and authorised HTTP remain open.

## 2026-10-29T01:36:07+08:00 — WU-SD-002a action scope review

- The first cross-company VIEW test accidentally reused the company-1 DbContext while constructing a company-2 authorization. The focused test went red because the original row remained visible; replacing the DbContext with company 2 made the 404 assertion pass. The query service itself was unchanged.
- The DELETE command obtains a company-constrained update lock before its scoped read, matching the order command's lock order and avoiding a shared-to-update upgrade cycle. A real SQL test checks independent DELETE permission, other-owner 404, stale 409, soft deletion and number retention. Two-connection races still require a dedicated test.
- Both services are internal. There is no quotation HTTP route yet, so this does not establish Swagger or user-facing acceptance.


## 2026-10-29T01:26:30+08:00 — WU-SD-002a real-MD checkpoint

- Review: the first create-service test used fixed MD contract responses, so it could not detect company filtering or missing live reference rows. Added a real SQL test with two companies and real MD services.
- Result: five failed create attempts roll back the quotation and SYS sequence; the following valid create receives 001 and persists customer/item/tax snapshots. No product change was needed.
- Remaining: real MD stability under concurrent edits, action-scoped CRUD, SYS approval and authorised HTTP.

## 2026-10-29T01:17:22+08:00 — WU-SD-002a create-service checkpoint

- Review: the quotation input carries currency per line while the saved header has one currency. The builder rejects mixed line currencies with a field error before saving, so the header never misrepresents line amounts.
- Review: SYS number allocation precedes MD reference lookup inside one Serializable transaction. An unknown currency causes rollback; the real SQL test proves the next successful quote still receives sequence 001.
- Remaining: the integration fixture stubs MD contracts; live MD, action-scoped edit/read/delete, SYS approval and authorised HTTP need separate tests before this work unit can close.

## 2026-10-29T01:04:58+08:00 — WU-SD-002a quotation store checkpoint

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A quotation mutation could save an incomplete document without a caller-owned transaction. | Only schema and pure business rules existed. | Added a transaction-required store for Draft create, versioned whole-line replacement and soft delete, with current-company checks and per-header update locks. | Real SQL 3/3 checks commit, rollback, version and company isolation. The store is internal and awaits authorised service composition. |
| Domain quotation referenced an Application status enum. | The initial pure lifecycle slice placed the status enum beside the transition helper. | Moved the enum into Domain and kept the transition helper in Application. | The domain-boundary architecture test passes. |


## 2026-10-29T00:51:18+08:00 — WU-SD-002a quotation schema checkpoint

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The quotation schema test failed because neither saved quotation table existed. | WU-SD-002a had stopped while SQL Server was unavailable; only pure quotation rules had been committed. | Added the quotation EF model and reversible migration, including company-scoped parent and unique keys, range checks and lookup indexes. Extended the test to inspect SQL catalog constraints. | The focused real SQL test passed 1/1 and the migration applied to `myerp`. Commands, approval and HTTP still need their own red-to-green tests. |


## 2026-10-28T21:10:26+08:00 — WU-SD-003a diagnostic sampler recovery

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Full SQL suite passed but the diagnostic wrapper returned 1 while collecting a sampler `IOException`. | The background sampler wrote with one unguarded `Add-Content`; `Receive-Job` surfaced its write exception after the tests finished. | Added a bounded retry for transient file sharing failures and captured background job failure as an explicit sampler exit 2. The `sqlcmd` exit code is saved immediately after invocation. | Deterministic file lock checks pass 5/5, including a held lock that must propagate failure. The complete real SQL suite passed 659/659 in 5m 41s, sampler had no `SQLCMD_ERROR`, and the wrapper returned 0. |

The contending process in the original run is still unidentified; the bounded retry addresses its observed transient effect. Quotation conversion remains dependent on BLOCKED WU-SD-002a, so WU-SD-003a remains open.

## 2026-10-28T21:00:30+08:00 — WU-SD-003a full integration result

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Earlier full SQL runs lacked a completion summary under host memory pressure. | The 3 GB diagnostic preflight was unmet at 2.83 GB after idle build servers were shut down. | Explicitly ran the existing diagnostic with `-Configuration Debug -MaxThreads 1 -MinimumFreeMemoryGB 2.5`, monitoring host memory, SQL low-memory flags and thread-pool activity. | The saved test output reports 659/659 integration tests passed in 5m 44s, with zero failed or skipped. Host free memory reached about 1.38 GB during the run and recovered afterwards. |
| The diagnostic command returned exit 1 after the test suite passed. | The SQL sampler's `Add-Content` encountered a transient file-sharing `IOException` when `Receive-Job` collected its output. The contending process is not identified. | Preserved the test and SQL sample files under ignored `logs/sql-timeouts/`; no product code or test assertion changed. | Test success is established by the complete MTP summary, but the diagnostic wrapper itself has not passed and needs a focused retry/write failure check. |

Quotation conversion still requires the BLOCKED WU-SD-002a real quotation store. WU-SD-003a remains open for that path and diagnostic wrapper verification.

## 2026-10-28T20:51:35+08:00 — WU-SD-003a idle build-server resource recovery

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Seven MSBuild worker processes, all started at 19:45 with a parent process that no longer exists, and one VBCSCompiler process remained resident while the host had about 1.25 GB free. | Reusable .NET build servers outlived the earlier build command; their working sets contributed roughly 1.4 GB of pressure. | Ran `dotnet build-server shutdown` after the focused test completed; it closed the orphaned MSBuild workers and compiler server. | Free physical memory rose to about 2.84 GB. Focused SalesOrder real-SQL/HTTP tests passed 31/31. The full diagnostic script still correctly returned exit 3 at 2.83 GB, below its 3 GB preflight. No full integration result is claimed. |

The remaining largest consumers are interactive IDE, DevHub and browser processes; they were left running. On the next attempt, remeasure free memory after closing idle build servers. Run the full diagnostics only when the 3 GB preflight passes, then inspect any actual test failures. Quotation conversion still depends on BLOCKED WU-SD-002a.

## 2026-10-28T20:47:28+08:00 — WU-SD-003a integration resource preflight

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Repeated full integration attempts had stalled while the host had under 2 GB free and SQL reported low memory. | The diagnostic script started the full suite without checking host headroom. The underlying SQL timeout mechanism remains unproven. | Added a 3 GB free physical memory preflight to `Invoke-IntegrationDiagnostics.ps1`, with an explicit override for intentional runs. | At 1.42 GB free, the command exits promptly with code 3 and a clear reason, without starting tests. Debug build has zero warnings/errors and focused SalesOrder SQL tests pass 31/31. Full integration acceptance remains open. |

The highest working-set processes were user IDE, DevHub and browser processes; none were terminated. SQL Server's `process_physical_memory_low` flag was 1. This is an environment guard, not a claim that the SQL timeout root cause is fixed. Rerun the full diagnostics after free memory reaches 3 GB and inspect any real failures before closing WU-SD-003a.

## 2026-10-28T20:43:05+08:00 — WU-SD-003a full EF line-plan review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The earlier SHOWPLAN projected only representative line columns, so it did not prove the full EF tracked-entity query seeks the company/order index. | EF composes its company filter and full column projection around the raw query. | Exposed the store's actual line query internally for the integration test and submitted its `ToQueryString()` SQL to real `myerp_test` SHOWPLAN XML. Asserted an Index Seek on `ix_sal_order_line_company_id_order_id_line_no`. | The complete query plan passes 1/1; the SalesOrder group passes 31/31, Architecture 230/230, and Debug solution build has zero warnings/errors. |

EF's diagnostic `ToQueryString()` omits `@` in raw interpolation parameter declarations while using `@p0`/`@p1` in the SELECT. The test corrects those declarations only for SHOWPLAN execution; the product query is unchanged. Free host memory remains about 1.3 GB, so full integration has no new completion summary. Quotation conversion still depends on BLOCKED WU-SD-002a.

## 2026-10-28T20:35:55+08:00 — WU-SD-003a cross-order lock regression

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A representative seek plan and ordinary two-thread order tests did not prove that editing one order permits another order's line insert. | The original clustered scan retains a Serializable range lock over unrelated order keys on this small table. | Added a real-SQL two-connection test that holds the edit transaction open during the other order's insert, then reads both documents from a fresh context. | With FORCESEEK removed temporarily, the insert failed with SQL lock timeout 1222; restored FORCESEEK passes 1/1 and the SalesOrder group passes 30/30. Debug solution build has zero warnings/errors. |

The test uses a bounded 3-second lock wait and separately verifies persisted version, remark and lines. It checks lock independence without needing a nondeterministic deadlock. The exact full-entity EF SHOWPLAN and full integration summary remain open; host free physical memory was about 1.3 GB.

## 2026-10-28T20:28:17+08:00 — WU-SD-003a order-line seek review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Splitting the header and line loads did not ensure a narrow lock: real `myerp_test` SHOWPLAN still selected `pk_sal_order_line` Clustered Index Scan for the company/order predicate on a small table. | SQL Server chose the cheaper clustered scan and Serializable retained a line-key range lock across orders; the earlier deadlock graph showed `RangeS-S` versus another order's line insert. | Load tracked lines with a parameterised `FromSqlInterpolated` query and `INDEX(ix_sal_order_line_company_id_order_id_line_no), FORCESEEK` after locking and version-checking the header. Keep the existing company/order predicate and EF tracking fixup. | SHOWPLAN now uses an index seek on company/order plus a clustered key lookup. SalesOrder SQL/HTTP passed 29/29 four consecutive two-thread runs; Architecture 230/230 and Debug build with zero warnings/errors. A dedicated cross-order insert race and full integration summary are still needed. |

The forced index is part of the current migration and model. A later index change must update this query in the same migration. The representative SHOWPLAN query projected a subset of line columns; the product query loads the full entity and passed the persisted line assertions. Full integration was deferred while the host had about 1.2 GB of available memory, matching the earlier low-memory stall conditions.

## 2026-10-28T19:46:58+08:00 — WU-SD-003a full SQL integration gate diagnosis

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Full integration with `--max-threads 2` produced 30-second SQL command timeouts in unrelated SYS approval and MD unit-of-measure tests. An isolated SYS approval test with `--max-threads 1` also failed during the assembly fixture's database migration, before its test body. | Root cause is not established. SQL DMV showed migration waiting at `ALTER DATABASE myerp_test SET READ_COMMITTED_SNAPSHOT ON`; the isolated run later timed out inserting `20261012150752_MasterData_ItemAttributes` into `__EFMigrationsHistory`. SQL reported only 767 MB free physical memory and `Available physical memory is running low`. Competing hypotheses: host memory pressure stalls SQL I/O; migration DDL waits for database connections; or another SQL workload competes for resources. The isolated result rules out test-method concurrency as a sufficient explanation. | Captured the exact failing commands and migration point. No product or migration change was made without a reliable focused reproduction. | Debug solution build succeeds with zero warnings/errors; Architecture 230/230. Full SQL integration remains failed, so WU-SD-003a cannot close. |

Next check: record free memory and SQL wait/blocking sessions before and during a single isolated test; identify any external connection or host workload without terminating user processes; then rerun the same test and the SalesOrder suite. The fixture rebuilds `myerp_test` on every run, so its startup is part of the repro. Quotation conversion still depends on blocked WU-SD-002a: no sales quotation table, repository, or transactional service exists yet.

## 2026-10-28T16:01:39+08:00 — WU-SD-003a authorised command lock order

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Concurrent authenticated PUT commands returned HTTP 200/500, with SQL deadlock 1205. | Both Serializable commands read the action-scoped header with shared locks before the store requested an update lock, allowing a shared-to-update conversion deadlock. | Lock the company-scoped, active header with UPDLOCK/HOLDLOCK before the scoped read for EDIT and DELETE, keeping the scope check inside the same transaction. | Concurrent PUT returns 200/409 and concurrent DELETE returns 204/404; no partial saved order. The focused SQL/HTTP suite passes 29/29. |


## 2026-10-28T15:52:20+08:00 — WU-SD-003a concurrent mutation review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Sequential version tests did not show whether concurrent edit and delete preserve a whole document. | Earlier tests issued commands after the previous transaction had ended. | Added four real-SQL two-connection cases for both command orders and commit/rollback outcomes; verified lock timeout, retry result, saved number, version, deletion flag and lines. | Store-level locking and rollback behave consistently; SalesOrder 29/29 pass without production changes. |

The authorised HTTP service path may have a different lock sequence because it reads action scope before the store lock. That race and quotation conversion are still unverified; the focused tests do not establish full integration acceptance.

## 2026-10-28T15:44:56+08:00 — WU-SD-003a authorised CRUD review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| HTTP create and edit had no proof against persisted MD references and a signed-in actor. | Earlier SQL command tests used controlled authorization or controlled MD responses. | Added a real SYS role/login and persisted MD reference fixture, then exercised POST, GET, PUT, stale DELETE and current DELETE through HTTP; checked the saved row independently. | The commercial snapshots, permanent number, action permissions, version 409 and soft deletion agree with SQL; SalesOrder 25/25 pass. |

The test uses one company and one actor, so it does not replace the separate action-scope or cross-company cases. The fixture builds only required MD references and avoids product code changes.

## 2026-10-28T15:22:24+08:00 — WU-SD-003a HTTP permission review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Anonymous order HTTP coverage did not prove that action-specific SYS permissions reject a signed-in viewer. | The existing HTTP test stopped at 401 and the SQL command tests used controlled authorization. | Added a real SYS VIEW-only user and company assignment; exercised list plus POST, PUT and DELETE through HTTP. | The list succeeds with zero scoped rows; all three mutations return 403 and the shared permission error code. |

No production change was needed for this permission slice. Saved-row authorised HTTP, cross-company hiding and concurrent command outcomes still require evidence.

## 2026-10-28T15:17:16+08:00 — WU-SD-003a list coverage review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Date, status, customer and keyword filters had no saved-row SQL evidence. | The first list test covered only default sorting and Branch scope. | Extended that regression with distinct saved dates, customers and states, plus an out-of-scope Branch row. | Real SQL confirms each filter and scoped total count; no production change was needed. |
| Invalid pagination and filter combinations were implemented but unverified for orders. | Shared paging and query validation were reused without order-specific assertions. | Added eight invalid query cases, including skip overflow and reversed date bounds. | All are rejected before a list result is returned. |

Authorised HTTP and simultaneous write outcomes remain outside this review's evidence.

## 2026-10-28T15:06:01+08:00 — WU-SD-003a list review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Paging equal-date rows without a unique tie breaker can repeat or omit orders. | Business date is shared by many orders. | Every allowed sort adds immutable ID as the final sort key. | A real-SQL two-order test proves stable page 1/page 2 selection. |
| A count performed before SYS scope could expose hidden order volume. | The endpoint was previously unimplemented. | Apply company and VIEW scope to the base query before counting or projecting. | A foreign Branch sees an empty page and scoped zero total. |


## 2026-10-28T14:51:45+08:00 — WU-SD-003a HTTP read/create review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A detail lookup could reveal an order outside the actor's VIEW scope if the controller loaded by ID first. | The earlier store checked only current company; HTTP and read query did not exist. | Added `SalesOrderQueryService` that requires `SAL.ORDER.VIEW` and applies `DataScopeFilter` to the EF query before the ID lookup. | Real SQL verifies Branch/Self inclusion and Department/other-owner 404; no hidden header or line is materialised. |
| Manual create had no HTTP route or OpenAPI contract. | The command existed only as an internal service. | Added distinct GET/POST controller actions with action permissions, response types and XML descriptions. | Anonymous routes return 401 and both appear in OpenAPI; authorised HTTP acceptance remains open. |

The detail response deliberately returns saved commercial fields only. Approval history and delivered balance are deferred to their owning work units; this checkpoint does not claim the full REQ_F4112.2.1 view acceptance.

## 2026-10-28T14:42:58+08:00 — WU-SD-003a action-scope and integration review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Manual order headers lacked SYS owner and organization attributes, so later action-scope filtering could not distinguish Branch, Department and Self. | The initial order schema only recorded company and audit actor. | Added scalar branch, department and owner columns, backfilled existing owner from `created_by`, indexed the scope fields and saved CREATE authorization identity. | Real SQL create and scope-filter test passes 6/6; Debug build has zero warnings/errors. This does not yet enforce read/edit/delete scope. |
| Full integration run timed out in unrelated SYS approval and MD unit tests; both passed when run alone. | The full test load produced SQL command timeout, with no deterministic single-test reproduction. | Stopped the failing full run and reran each failed test alone with one thread. | Isolated tests passed 1/1 each. Full-suite acceptance remains open; next run should use bounded groups and inspect SQL waits during a repeatable failure. No product code was changed for this finding. |

## 2026-10-27T16:37:47+08:00 — WU-SD-003a MD snapshot transaction review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A read MD identity could change while the order command was still resolving other references and saving its snapshot. | The default SQL transaction was ReadCommitted, which releases the shared row lock after each lookup. | Raised only the manual-create transaction to Serializable and added a real-MD, second-connection lock probe after the item read. | The SQL writer receives lock timeout 1222 before commit; after commit it succeeds, while the saved order retains its earlier item name. SalesOrder 13/13 and Debug build pass. |

The SYS number is still allocated before MD reads. This serializes competing number allocations and may increase contention; keep the transaction short and verify two-connection numbering before closing WU-SD-003a. The test uses a one-second lock timeout to observe the held read lock rather than waiting for a concurrent request indefinitely.

Full integration diagnosis (2026-10-27T16:41:11+08:00): `dotnet test --project tests/backend.integration/MyERP.IntegrationTests/MyERP.IntegrationTests.csproj --no-build --max-threads 2` ran for about four minutes without a completion summary and was interrupted. The narrower `--filter-method "*SalesOrder*"` loop completed 13/13 in about 26 seconds. The full-run symptom does not yet have a fast, deterministic failing filter, so no root cause or full-suite pass is claimed. Next diagnosis should bisect integration test groups with a bounded timeout and record the first group that reproduces the stall before changing production code.

## 2026-10-27T15:48:59+08:00 — WU-SD-003a physical-delete review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could physically delete an order, releasing its unique number and quotation source and cascading to saved lines. | Existing constraints and triggers guarded updates and line writes but did not reject a header DELETE. | Added a reversible, set-based `AFTER DELETE` guard and a failing real-SQL regression before implementation; expanded it to active Draft, soft-deleted Draft and Approved. | The focused order schema suite passes 8/8; rejected deletes retain the header identity, source, state and line. |

The guard is a persistence backstop for BR-SD-016/017/019. It does not replace the future authorised soft-delete command, which must still check company, permission, state and version.

The migration was applied to `myerp` and the Debug solution build finished with zero warnings/errors. Shared, Architecture and DocGen projects passed in the full backend run; integration remained without a summary under host load even with `--max-threads 2`, so that run was stopped and is not counted as a full-suite pass.

## 2026-10-27T15:37:16+08:00 — WU-SD-003a saved-header content review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could change an Approved order's customer, monetary totals or remark without changing its status. | The existing header trigger checked transitions and deletion only; line triggers protected child rows, not header content. | Added a reversible set-based UPDATE trigger with a null-safe commercial-field comparison for Pending, Approved, Cancelled and deleted headers. Added a failing real-SQL regression before implementation, then verified Draft remains editable. | The focused order schema suite passes 7/7; failed writes leave the commercial snapshot unchanged. |

Audit/version columns and state transitions remain available to their existing lifecycle. The trigger checks every affected row in a statement and rejects the whole statement if one frozen header changes. Authorised service decisions, quote conversion and MD/SYS transaction checks remain WU-SD-003a work.

## 2026-10-27T15:30:38+08:00 — WU-SD-003a saved-header state review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could move an Approved order back to Draft or revive a soft-deleted Draft. | Header CHECK constraints allowed each resulting row and did not compare old and new states. | Added a reversible set-based UPDATE trigger with the BR-SD-016 transition matrix and a persistence regression that verifies rejected writes leave the saved rows intact. | The focused real-SQL order suite passes 6/6; migration is applied to `myerp` and Debug build has zero warnings/errors. |

The trigger enforces persisted state shape, but only the future authorised service can validate SYS decisions, versions, action scope and business references. The all-solution unit/architecture/docgen projects passed; integration remained active without a final summary under host load, so the run was stopped and is not counted as a full-suite pass.

## 2026-10-27T11:40:10+08:00 — WU-SD-003a saved-line state review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could add, change or remove an Approved or soft-deleted order line. | The application lifecycle guarded status changes, but the existing SQL source triggers did not guard line writes by parent state. | Added an INSERT/UPDATE/DELETE trigger in a reversible migration and tested both frozen states with all three write kinds. | The focused real-SQL schema suite passes 5/5; Draft order insertion and the existing source integrity regressions remain green. |

The trigger checks affected parent identities in one set operation, so a multi-row statement is rejected atomically. It does not validate MD references or quotation eligibility; those remain service-level work within WU-SD-003a.

The full backend suite again exposed unrelated INV valuation SQL load failures: `HundredThousandMovementsUseValuationIndex` lost its connection after about three minutes and `NegativePoolAndOverflowReturnBusinessErrors` timed out while creating MD setup data. A focused low-load rerun of the latter passed 1/1 in about 25 seconds, so the full-suite failure is load-sensitive rather than a deterministic order-schema regression. The 100,000-movement case has no fast isolated feedback loop yet; no speculative INV code change was made. Its isolated runtime and SQL resource contention remain for a later quality unit.

## 2026-10-27T11:24:14+08:00 — WU-SD-003a source-pair review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A saved order could mix a manual header with quoted lines, or a quoted header with manual lines. | Unique source indexes and immutable UPDATE triggers did not compare header and line source presence on INSERT. | Added a reversible line INSERT/UPDATE consistency trigger and real-SQL rollback test. Configured both trigger-bearing order tables to use EF's trigger-compatible save path after the first green attempt exposed SQL Server's OUTPUT restriction. | Four order schema tests pass, including both invalid source pairs and existing valid inserts. Full quotation identity and complete-line conversion remain service-level gates. |

## 2026-10-27T11:09:39+08:00 — WU-SD-003a source identity review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A saved order or line could replace its quotation source, freeing the original unique source key for a second conversion. | The filtered unique indexes prevent duplicates but do not make an existing source link immutable. | Added SQL update triggers with a reversible migration, and tested clearing, reassignment, ordinary update and soft deletion against the real database. | Source identity stays permanent across later updates and deletion. Authorised conversion, MD/SYS snapshots and two-connection competition still need implementation. |
| Full-suite architecture test found a Domain entity using an Application enum. | `SalesOrderStatus` had been declared with its lifecycle service. | Moved the enum to Domain without changing persisted values or transition logic. | Restores the Domain dependency boundary; rerun of the full suite is required before a completion claim. |
| The seed role regression expected SALES to have only inventory VIEW and SYS self-service permissions. | SD permissions were subsequently added to the catalog, while the test expectation stayed at the earlier foundation stage. | Extended the expected set from the catalog's SD/CRM modules, matching the existing SALES seed policy. | Preserves the role-policy assertion, including all SD actions; focused SQL regression passes. |


## 2026-10-27T10:50:16+08:00 — WU-SD-003a schema checkpoint review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Order source links needed durable uniqueness and same-company line ownership before delivery could lock approved source lines. | WU-SD-003a had only pure input/state helpers while SQL Server was stopped. | Added order/line model, permanent filtered source indexes and same-company FK; generated and applied a reversible migration. A real SQL regression tests duplicate source and invalid-line rollback. | The two source tables now exist. CRUD, conversion, approved source enforcement and two-connection concurrency remain unverified. |

The generated migration's constant column arrays triggered CA1861 under `TreatWarningsAsErrors`; the same narrow generated-code pragma used by existing migrations fixes compilation without changing runtime behavior. The failing `dotnet test` command was the feedback loop, and the focused SQL tests passed after correction.

## 2026-10-27T10:39:15+08:00 — WU-SD-004a2b saved-line selection review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The pure completeness guard had no SQL caller and could accidentally be invoked from a read outside a transaction. | The order persistence prerequisite WU-SD-003a is still blocked, and delivery persistence only had its schema. | Added a company-filtered saved-line selector requiring an existing serializable transaction, with an independent COUNT and ordered lock plan; covered it with real SQL tests. | The selected-line part is executable and rejects missing or cross-company documents. Source locks, full balance reread, SYS/INV rollback, and HTTP remain open. |

The `sal_order` and `sal_order_line` tables now exist after the WU-SD-003a schema checkpoint, but no authorised order command has proven an Approved source. A complete delivery submit or approve implementation still needs WU-SD-003a order persistence and must not trust client-supplied balances.

## 2026-10-27T10:16:42+08:00 — WU-SD-004a2b schema checkpoint review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The interrupted delivery model and migration were present but had no verified SQL deployment or source document IDs. | The previous turn stopped after generating the migration. | Restored the unrelated protected Runner file, applied the migration, inspected the actual tables, constraints, indexes and quantity precision, then allocated REQ_DS43.170–171. | Schema changes are reviewable and traceable; draft CRUD, transaction locks and posting are still open. |

The parent FK includes company ID; source order, MD, SYS and INV IDs have no cross-module FK. The current schema does not yet store tracking or posted INV movement identities, so those design requirements remain for the next implementation step. No claim of outbound command completion is made.

## 2026-10-27T05:49:46+08:00 — WU-SD-004a2b7 selected-row guard review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A correct ordered lock plan could still receive a truncated saved-row query. | The prior pure plan knew each selected row but had no independently read persisted row count or line identity. | Added a count comparison and unique positive persisted line-ID check before reusing the existing ordered lock plan; the SQL caller must read both under one serialized, company-scoped transaction. | A missing, repeated or foreign selected row is rejected before lock planning; SQL isolation and actual locks remain for WU-SD-004a2b. |

The helper allocates only one ID set and one projection array for at most 1000 lines. It introduces no table, API, UI or cross-module access. Focused tests first failed at compile time and then passed; full offline verification is recorded in progress.md. SQL concurrency and smoke cannot run while the service remains stopped.

## 2026-10-24T11:53:43+08:00 — WU-SD-005d pricing design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A unique interval start would allow overlapping prices. | Different start dates can cover the same business day, and nullable default customers need one logical key. | BR-SD-026 and schema plan require serialized same-key writes plus overlap check; SD-PRICE-008/009 cover boundary and two-writer cases. | A quote selects at most one rule per specificity. |
| A displayed price can become stale before document save. | Quote lookup and save are separate requests. | BR-SD-027/030 make quotes advisory and require transactional re-resolution/version check; SD-PRICE-010 covers 409. | Obsolete prices cannot silently persist. |
| A manual price could bypass the ordinary discount ceiling. | A document CREATE grant alone does not authorise commercial override. | BR-SD-028 requires an additional OVERRIDE action and reason, with actor/time audit. | Price exceptions are explicit and traceable. |
| Rule edits could recalculate an Approved document. | A live rule lookup would discard the commercial decision made at save. | BR-SD-029 and schema plan retain rule provenance and rounded line/header snapshots; SD-PRICE-020 covers historical stability. | Existing documents remain reproducible. |

This design unit changed no product code, table, endpoint or UI. DocGen tests passed 7/7 and `git diff --check` passed. SQL service is stopped; real SQL/HTTP and Playwright acceptance belong to 005a/b.

## 2026-10-24T07:43:04+08:00 — WU-SD-004d outbound design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Concurrent deliveries could overreserve one order line. | A balance shown by the UI is stale as soon as another delivery submits. | BR-SD-022 requires ascending order-line locks and aggregate decimal balance checks at submit and final approval, counting the current reservation once. | Concurrent writes serialize; no negative order balance. |
| A zero-step workflow could leave a Posted SD document without an INV issue. | SYS does not call the result handler for immediate approval. | BR-SD-024 requires the submit service to execute the same saved-line posting path in its transaction. | Immediate and final approval have the same atomic effects. |
| INV's replay protection could be mistaken for business authorisation. | INV accepts identical saved-source replay. | BR-SD-024 rejects a second SD decision on Posted/Rejected before invoking INV. | Replays remain a safety guard, not a route to duplicate approval. |
| Tracking or stock failure could leave an apparent success. | SYS, SD and INV share one decision transaction. | SD-DEL-011/017 assert complete rollback and a specific visible failure, including 500 traceId. | No partial movement or false success message. |

This design unit changed no product code, schema, API or UI. DocGen tests passed 7/7 and `git diff --check` passed. Real SQL, authorised HTTP and Playwright acceptance remain with WU-SD-004a/b/c while SQL is stopped.

## 2026-10-24T07:36:22+08:00 — WU-SD-003a quotation source slice review

The source validator checks company identity before quotation status so a cross-company source does not expose its state or expiry. It reuses the existing quotation lifecycle for Approved, converted and inclusive expiry decisions, avoiding a second transition matrix. The red test failed on the absent validator; nine focused assertions and all 143 architecture tests now pass. This pure check does not lock a quote, enforce source uniqueness or prove a saved order. Debug build has zero warnings/errors; Shared 381/381 and DocGen 7/7 pass. Real SQL and authorised HTTP remain pending because the SQL service is stopped.

## 2026-10-24T07:29:06+08:00 — WU-SD-003d order design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A zero-step approval could bypass unavailable credit data. | SYS may return immediate approval without a final callback. | BR-SD-016/018 and API detail require the same FI/AR credit check before any submit/zero-step approval; missing contract returns a named block. | No assumed zero exposure or accidental approval. |
| Deleting or cancelling an order could free a quotation for reuse. | Source uniqueness might otherwise follow active status. | BR-SD-017 and schema plan keep unique quote/line links across soft deletion and cancellation. | One quote converts once under concurrency. |
| A delivery could be ignored by order cancellation. | Order status alone does not describe pending or posted outbound. | BR-SD-019 requires zero active/past delivery and an explicit current-version cancel guard. | Original order and delivery remain traceable. |

No product code, table, endpoint or test was changed by this design unit. DocGen unit tests pass 7/7; `git diff --check` is clean. Real SQL and authorised HTTP remain implementation gates while the SQL service is stopped.

## 2026-10-24T07:26:10+08:00 — WU-SD-002a transition slice review

The transition rule is pure and reads business date only through `IClock`; it does not mutate an Approved quote when expiry passes. Review found that a converted quote needs a separate source-order guard even when its stored state remains Approved, so both void and repeat conversion return a named conflict. Invalid status is checked before expiry to keep the reported reason tied to the attempted action. The test fixture initially triggered analyzer CA1859; its concrete clock type now satisfies the build without weakening a test. No persistence, SYS callback or HTTP behavior is claimed. Debug build has 0 warnings/errors; architecture 120/120, Shared 381/381 and DocGen 7/7 pass.

## 2026-10-24T07:18:32+08:00 — WU-SD-002d quotation design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A proposed direct quotation approval path could diverge from SYS decisions. | The baseline named submit but did not pin the zero-step and final-callback behavior. | BR-SD-011 and API detail use `IApprovalWorkflowService` plus `IApprovalResultHandler`, including immediate approval in the submit transaction. | One decision owner and explicit rollback tests. |
| Expiry could be mistaken for a mutable approval state. | Approved status and commercial validity serve different purposes. | BR-SD-012 keeps stored status and computes `isExpired` from `IClock.Today`. | Approval history remains stable while conversion expires on time. |
| Soft deletion could reopen a quotation for duplicate conversion. | Application-only source checks are racy. | BR-SD-013 and schema plan require permanent unique order source links, including deleted orders, and concurrency tests. | One quotation produces at most one order. |
| Planned actions could appear complete before permission and UI IDs exist. | The design baseline listed future routes and interfaces. | Added VOID permission as a future 002a gate and fixed REQ_DS43/REQ_IO allocation at migration/UI delivery. | The current catalog and documents state implementation status accurately. |

No product code, schema or test was changed by this design unit. DocGen unit tests pass 7/7 and `git diff --check` is clean; real SQL, authorised HTTP and Playwright remain delivery gates for 002a/b.

## 2026-10-24T07:15:06+08:00 — WU-SD-001 MD option slice review

The selection contract sits in MD and returns only minimal choice data; SD has no direct MD table dependency. Reviewed filtering for company, soft deletion, active state, customer approval and inclusive tax dates. The query uses stable code/ID ordering and bounded paging; it does not fetch full entities or perform per-row lookups. The red integration test first failed compilation because the service did not exist. Debug build, architecture 83/83, Shared 381/381 and DocGen 7/7 pass. The SQL fixture cannot run while `MSSQL$MSSQLSERVER2022` is stopped, so no data filtering result or HTTP behavior is claimed. A future edit form must apply `SalesActionAccess` before calling MD.

## 2026-10-24T07:11:25+08:00 — WU-SD-001 action scope slice

Reviewed the new access service: it resolves the current company first, then requires the exact requested SAL action from the SYS authorization contract. This prevents a VIEW grant from authorizing EDIT and rejects a company mismatch before a document query. Unit tests cover each grant, missing permission and anonymous context. Future SD queries still need to apply the returned filter before lookup; no SQL or HTTP validation was possible while SQL Server was stopped.

## 2026-10-24T07:07:26+08:00 — WU-SD-001 foundation slice review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Draft API permissions used `SD.*`, conflicting with `ErpModuleCatalog` prefix `sal`. | Module code and database/API/permission prefix differ for Sales. | Corrected the API plan and permission catalog to `SAL.*`; an architecture test now checks registration and uniqueness. | PermissionCatalog accepts the module; future endpoints can use declared actions. |
| A company boundary error used the module code in its error key. | The new helper and test initially used different prefixes. | Aligned the error with `SAL.DOCUMENT.NOT_FOUND` and reran the focused then full architecture suite. | Cross-company document checks fail closed with a stable code. |

The test feedback loop was `dotnet test --project tests/backend.unit/MyERP.ArchitectureTests/MyERP.ArchitectureTests.csproj`: it first reported the permission prefix conflict, then the error-code mismatch, and finally passed 80/80. No debug instrumentation remains. SQL service is stopped, so no migration or authorised HTTP result is claimed.

## 2026-10-24T07:01:29+08:00 — WU-SD-000 design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A delivery might accidentally send sales price as stock cost. | Commercial and inventory lines have different meanings. | BR-SD-006 explicitly uses saved basic-unit quantity/tracking and the existing `SalesOutboundLine` contract, which has no sales price. | INV remains sole owner of outbound valuation. |
| A future order approval might silently proceed without credit exposure. | FI/AR contract is not yet available. | BR-SD-004 and REQ_F4112.6.1 require a specific blocked result until the contract exists; SD-BASE-017 covers it. | No fictitious credit approval. |
| Broad sales Epic cannot be verified in one round. | Quotation, order, delivery, FI and UI have different dependencies. | Backlog splits design, persistence, approval/INV and UI slices; later large slices must split again before coding. | Each next action has a named contract and acceptance gate. |

## WU-SD-002a partial code review (2026-10-24)

| Finding | Cause | Change | Result |
|---|---|---|---|
| The first rounding test expected cents in TWD base amounts. | The test fixture ignored the shared TWD zero-decimal rounding policy. | Use USD base currency for the cent-precision assertion; retain a separate TWD tax/discount assertion. | Architecture suite passes 99/99 and the assertion now matches `Money.Round`. |
| A future quote save could persist over-range numbers or partially valid lines. | Pure calculation and request validation had no sales implementation. | Added field-specific whole-input validation and checked per-line/base-header arithmetic, returning a named overflow error. | The pure slice is independently testable; persistence and transaction tests remain required. |

No product code or schema changed. Review of the new documentation found no secret, generated artifact or cross-module table access requirement. `git diff --check` and DocGen unit tests 7/7 passed; real SQL tests are not applicable to this design-only unit and remain required for implementation units.

## 2026-10-24T07:32:40+08:00 — WU-SD-003a partial code review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Order amount code could drift from quotation rounding. | The same commercial snapshot policy applies to manual orders and quotations. | Reuse the tested quotation calculation with order-specific inputs/results and translate overflow to `SAL.ORDER.AMOUNT_OVERFLOW`. | Line and header rounding remain aligned while callers receive an order error code. |
| Invalid draft data might reach reference lookup. | No order input boundary existed. | Added field-specific validation for date, customer, currency, lines, precision and remark. | Pure validation is testable before SQL transaction work. |

The helper does not make a document persistent. SQL Server is stopped and this account cannot start it; migration, real SQL, authorised HTTP and full integration regression remain WU-SD-003a gates.

## 2026-10-24T07:39:34+08:00 — WU-SD-003a lifecycle slice review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Cancellation could be admitted after a delivery exists. | A simple status-only matrix omits delivery balance. | Require the caller to supply whether any active or posted delivery exists; reject cancellation with `SAL.ORDER.HAS_DELIVERY`. | The pure gate covers this invariant; SQL caller must derive the flag under a lock. |
| Rejected orders might be resubmitted without revision. | The business rule requires an edit to return Rejected to Draft. | Permit submit only from Draft and test Rejected-to-Draft edit separately. | The lifecycle cannot skip the revision step. |

Review found no new cross-module access or date/time source. The helper cannot prove persistence, locking, credit, SYS transaction or authorised HTTP. Debug build, architecture 160/160, Shared 381/381 and DocGen 7/7 passed offline; SQL service remains stopped.

## 2026-10-24T11:58:05+08:00 — WU-SD-006d credit design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A backdated order could select an obsolete credit limit. | Initial contract used the order business date for a current approval decision. | FI evaluation date is server `IClock.Today` on each submit/final check; saved order date remains solely for commercial amounts. | Backdated documents cannot borrow historical headroom. |
| A Pending order appeared to carry a prior credit reservation. | The first acceptance wording conflated a submitted candidate with Approved commitments. | State Pending orders do not reserve credit; exclude candidate order ID from commitment query and add its saved gross once at decision. | No double count or silent reservation. |
| FI AR and SD uninvoiced commitment could overlap during invoicing. | Ownership shifts at FI posting. | Require the SD commitment release and FI AR increase in the same credit-key transaction, with a saved invoice/order link. | The exposure sum has no transient gap or duplicate. |

This is a design-only unit. No product code, migration, API or UI was changed. DocGen tests 7/7 and `git diff --check` passed; SQL/HTTP and E2E cases remain pending for 006a/b/c because SQL Server is stopped.

## 2026-10-24T12:02:12+08:00 — WU-SD-007d invoice design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Partial invoices could drift from the saved order tax after repeated rounding. | Rounding each partial independently loses residual units. | BR-SD-039 allocates cumulative saved net/tax by quantity and gives the final portion the exact remainder; gross is their sum. | All posted portions reconcile to the original commercial snapshot. |
| A retry after timeout could post a second AR item. | Client does not know whether the first transaction committed. | BR-SD-040 requires a unique request key plus payload fingerprint and FI source uniqueness, with a lookup path before retry. | Identical retry reads the existing invoice; changed payload is a conflict. |
| Credit exposure could disappear between SD and FI updates. | Separate transactions could release SD commitment before AR exists. | BR-SD-041 uses the FI credit lock and one SQL transaction for FI posting and SD commitment transfer. | One committed transfer keeps the combined exposure consistent. |

No product code, schema, API or UI changed. DocGen tests 7/7 and `git diff --check` pass. SQL/authorised HTTP and Playwright remain 007a/b gates because SQL Server is stopped.

## 2026-10-24T12:06:00+08:00 — WU-SD-008d return design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Return and invoice could both consume the same unbilled delivery quantity. | Their balances are interdependent. | BR-SD-044 requires the same delivery-line lock, separate billed/unbilled allocations and final recheck. | Concurrent operations cannot double allocate quantity. |
| A return receipt could adopt sales price as inventory cost. | SD commercial snapshots do not represent stock cost. | BR-SD-045 requires original outbound movement references and INV-derived reversal cost. | Inventory valuation remains INV-owned and traceable. |
| A mixed return could reduce credit twice or leave stock without a credit adjustment. | Billed and unbilled portions have different exposure owners. | BR-SD-046–047 separate FI credit from SD commitment release and post both with INV/SYS in one transaction. | Failure rolls back the entire return and each exposure component changes once. |
| Partial credits could lose cents through repeated independent rounding. | Each portion would otherwise round separately. | BR-SD-046 uses cumulative quantity with an exact final remainder from saved invoice amounts. | Credits reconcile to the original posted invoice. |

This is a design-only unit: no product code, migration, API, UI or executable feature test was added. DocGen tests 7/7 and `git diff --check` pass. Real SQL, authorised HTTP and Playwright remain 008a/b gates while SQL Server is stopped.
## 2026-10-24T22:43:43+08:00 WU-SD-009d design review

Reviewed the proposed SD demand reader against SCM's frozen-source contract. Risk: a current-state query can change membership between pages, and permission changes can silently leak or trim a manifest. The design now requires committed revision membership, an owner-issued replayable cursor with digest and complete empty buckets, plus permission rechecks on every page. No production code was changed. DocGen unit tests passed 7/7 and `git diff --check` passed; direct document generation remains blocked by another process holding `MyERP_SRS.docx`.

## 2026-10-27T00:44:56+08:00 WU-SD-004a1 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Repeated delivery lines could each pass separately while their total exceeds an order line. | Comparing each line to the same remaining balance ignores the aggregate. | Aggregate six-place decimals by saved order-line ID before comparison; add exact and one-micro-unit-over tests. | One balance decision per order line without rounding away excess. |
| Final approval could count its own Pending reservation twice. | A generic Pending sum includes the current delivery as well as the requested saved lines. | Name the snapshot field `OtherPending`, explicitly excluding the current delivery; unit test the final-decision boundary. | The current saved quantity is counted once. |

The guard has no transaction or authority of its own. WU-SD-004a2 must assemble trusted saved snapshots under ordered SQL locks and prove concurrent reservations, state, permissions and rollback before exposing delivery commands.
## 2026-10-27T00:55:46+08:00 WU-SD-004a2a review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Invalid quantity or repeated line number could reach a later database operation. | Draft input previously had no field guard. | Validate all structural fields and six-place `decimal(19,6)` range before reference checks. | Named field errors without partial persistence. |
| Multiple lines can legitimately refer to one order line. | A uniqueness check on source ID would reject a valid split. | Enforce unique delivery line numbers only; leave source aggregation to `SalesDeliveryBalance`. | Split warehouse/location lines remain valid while the later balance guard checks their total. |

The guard deliberately does not trust a client-supplied balance or validate saved state. WU-SD-004a2b must load sources under SQL locks and prove company isolation, SYS/INV rollback and dual-connection concurrency.

## 2026-10-27T01:08:23+08:00 WU-SD-004a2b1 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A Rejected delivery might be resubmitted without revising saved lines, or a Posted delivery might accept a repeat decision. | The transaction handler has no shared state decision yet. | Add one pure transition table and tests for each valid, invalid and repeated action. | The later handler has a single explicit eligibility rule. |
| A source order can cease to be Approved between draft creation and a decision. | Draft creation does not reserve an order. | Require the caller's current Approved-order result at submission and final approval; permit rejection to release an existing Pending reservation. | Stale source approval blocks new reservation/posting without trapping a Pending delivery. |

The caller must obtain the source order status under the ordered SQL locks and persist the transition with SYS/INV. This slice has no database, endpoint, authorisation or stock effect. Focused tests were red before implementation and green after it; final offline verification and SQL limits are recorded in progress.md.
# WU-SD-004a2b2 — saved-row balance snapshot

- Review: the decimal balance guard required pre-aggregated Posted and other Pending totals. A caller could accidentally include the current Pending reservation twice or omit historical Posted consumption.
- Change: added a pure saved-row projector with explicit status/deletion handling, source validation and stable order-line ordering. Kept database selection and locking with the future SQL transaction owner.
- Evidence: snapshot tests failed to compile before implementation and passed 4/4 after it. Real SQL, concurrency, permissions and INV posting remain unverified while SQL Server is stopped.

## 2026-10-27T05:30:17+08:00 WU-SD-004a2b3 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A final approval snapshot could name an absent or inactive current delivery and silently omit its reservation. | The projector excluded rows by ID without proving that the ID represented an active saved Pending delivery. | Require at least one matching row and reject every matching deleted or non-Pending row; test missing, deleted and mixed states. | Malformed selected rows fail before balance or posting. |

The new test was red before the guard and passed afterward. SQL selection completeness, locks and transaction rollback remain unverified while SQL Server is stopped.

## WU-SD-004a2b4 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Final approval could check a caller-supplied quantity smaller than the saved current Pending delivery. | The snapshot builder returned only other reservations; the current request was a separate argument to the balance guard. | Add `CheckCurrentPending`, deriving the current request and other balances from one saved-row input, then call the existing six-place balance guard. | A final decision cannot pass an independent smaller request through this entry point; the 1000-line limit is checked on saved rows. |

Tests failed to compile before implementation and passed afterward. The later SQL handler must call this entry point after selecting every saved row under ordered locks; no transaction or HTTP behavior is claimed here.

## WU-SD-004a2b5 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Draft submit could check a caller-supplied quantity smaller than the saved Draft lines. | The existing balance guard accepts a separate requested array, and the final-decision helper only accepts Pending. | Add `CheckDraftSubmission`; require every current saved line to be active Draft, derive the requested array from those rows, and reuse the saved snapshot and six-place balance guard. | Missing, mixed-state, deleted, over-1000 and over-balance Draft submissions fail in the pure guard. |

The test failed to compile before implementation and passed afterward. SQL completeness, ordered locks, HTTP authority and transaction rollback still require WU-SD-004a2b.

## WU-SD-004a2b6 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A future submit or final-decision handler could lock duplicate order lines or use input order, causing inconsistent lock acquisition between concurrent deliveries. | Saved delivery lines may repeat one source order line and arrive in arbitrary query order. | Add `SalesDeliveryLockPlan.Create` with saved-row integrity checks, a 1000-row limit and sorted distinct IDs. | Both Draft submit and Pending final approval can use the same deterministic ascending plan before selecting the complete balance snapshot. |

The focused test first failed to compile and then passed 4/4. Review also added the 1000-row acceptance and invalid Posted-plan cases. This pure helper does not validate SQL row selection completeness or acquire database locks; WU-SD-004a2b retains those acceptance gates.

## 2026-10-27T05:53:31+08:00 — WU-SD-004a2b8 snapshot identity review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A complete first selection could be paired with a later balance snapshot that omitted or changed one current line. | The first-read guard checked count and IDs, while the balance projector had no persisted line ID to compare between reads. | Preserved optional line identity in the pure snapshot model and compared every current ID, source, quantity and active state against the selected set. | A mismatched second read fails before the balance decision; actual isolation and SQL locking remain WU-SD-004a2b. |

The new check does not use database or cross-module access. Focused tests failed at compile time before implementation, then passed. The optional snapshot LineId keeps existing pure balance callers compatible; the future SQL caller must supply positive persisted IDs for this check.

## 2026-10-27T05:57:46+08:00 — WU-SD-004a2b9 decision review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A future SQL handler could call the identity guard but omit the full quantity guard, or the reverse. | The two checks were separate public calls for both Draft submit and Pending approval. | Add one `CheckLockedDecision` entry that checks identity first, then the matching full balance rule; reject unsupported states. | A successful pure decision has passed both invariants on one supplied snapshot. |

The focused test failed at compile time before implementation, then passed 3/3. Review found no further code change; SQL isolation, actual ordered locks, SYS/INV atomic effects and HTTP acceptance remain WU-SD-004a2b.

## 2026-10-27T16:01:48+08:00 — WU-SD-003a manual persistence review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Replacing only order lines could keep the old header version and permit a stale subsequent command. | EF may see no changed header field when the commercial header is unchanged. | Explicitly mark the saved remark as modified during whole-line replacement. | The SQL regression observes version 1 to 2 and stale delete returns conflict. |
| A persistence call could save an order without SYS numbering in the same transaction. | The internal store otherwise owns no transaction boundary. | Require a caller-owned transaction for create, replace and delete. | The no-transaction call fails and a rolled-back create leaves no header or line. |

The store is registered for the later application service. Its prepared snapshot input is internal to SD; the authorised HTTP path must validate business input and MD/SYS contracts before invoking it.
# WU-SD-003a MD item identity checkpoint — 2026-10-27T16:10:46+08:00

Review: sales order lines require saved item code/name, but the existing MD selection API only exposes paged form choices and cannot safely resolve one requested ID. Added an ID-based current-company MD Contract with active and soft-delete checks; SD does not read MD DbSets. Its test covers the company and state boundary. Remaining risk: the future order transaction must hold reference stability through save, and must combine customer, item, currency, tax, amounts and SYS numbering atomically.
# WU-SD-003a commercial snapshot review — 2026-10-27T16:17:55+08:00

- Problem: the initial snapshot implementation used default currency decimals even though MD permits company-specific precision. This could save a different amount from the configured currency policy.
- Cause: `SalesOrderAmounts` delegated to the quotation calculator without passing MD's `CurrencyPrecision`.
- Change: pass the precision returned by `ICurrencyLookup.GetPrecisionAsync` through both calculators; retain their default for existing callers. Check original header totals against `DECIMAL(19,4)` before persistence.
- Evidence: three focused snapshot tests and 230 architecture tests pass; Debug solution builds with zero warnings/errors. Transactional persistence and HTTP are pending.

## 2026-10-27T16:26:28+08:00 — WU-SD-003a transaction review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Number allocation, MD snapshot resolution and store save had no shared command boundary. | Each piece was implemented as an independent service. | Add a CREATE-permission checked command that starts one database transaction, commits after store save and rolls back plus clears tracked entities on failure. | Real SQL test observes saved commercial amounts and a failed insert restoring the SYS sequence to its previous value. |

The test uses controlled MD contract replies; real MD lookup and concurrent changes require a separate SQL regression. HTTP, quotation conversion and full integration remain open.

## 2026-10-27T20:28:57+08:00 — WU-SD-003a customer reference review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The order transaction's customer lock had no direct real-MD regression test. | Earlier create tests supplied a fixed customer contract response. | Add an approved customer lookup with a second SQL connection that attempts a rename before order commit, then verify the persisted name after a later successful rename. Register the existing payment setup model in the focused test context because the real lookup projects default payment codes. | Rename blocks until commit; SalesOrder SQL 14/14 and Debug build pass. No product code change was needed. |

Currency and tax races, concurrent number allocation and authorised HTTP remain unverified.

## 2026-10-27T20:34:30+08:00 — WU-SD-003a currency and tax reference review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Currency precision, exchange rate and tax eligibility had no direct concurrent MD-write regression. | Earlier create tests used fixed MD contract replies. | Add one real MD transaction test with a second SQL connection attempting four updates after each lookup; assert the saved snapshot and a successful later rate update. Register the existing currency and tax models in the focused fixture. | Each in-transaction update times out with SQL 1222; the saved order retains its original rate and tax percentage. SalesOrder SQL 15/15 and Debug build pass. Product code required no change. |

The current Serializable boundary covers these reference reads for manual create. Quotation conversion, concurrent numbering, authorised HTTP and full integration remain open.

## 2026-10-27T20:40:31+08:00 — WU-SD-003a numbering race review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Manual create had rollback coverage but no two-connection allocation regression. | Prior tests used one connection and could not prove SYS `MERGE` locks survive through SD commit. | Add a second real SQL connection that attempts `SAL.ORDER` allocation while the first create transaction is open, then retries after commit; assert both saved numbers and sequence value. | SQL 1222 blocks the competing allocation, then `SO002` is saved; focused order suite 16/16. Existing product transaction and generator need no change. |
| First version of the probe waited 30 seconds and returned SQL -2. | EF closed the second connection after `SET LOCK_TIMEOUT`, resetting the session setting before `NextAsync`. | Hold the second EF connection open through the probe. | The focused loop now detects SQL 1222 in about one second; no product behavior changed. |

Quotation conversion, authorised HTTP/Swagger, company/scope and version races, and full integration remain open.

## 2026-10-28T14:59:53+08:00 — WU-SD-003a draft command review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A stale edit could resolve MD references before discovering the old order version. | The draft store's version check ran after snapshot assembly. | Check the saved scoped header version before rebuilding the snapshot; the store rechecks under its update lock. | Stale input returns a conflict before reference lookups. The real-SQL command test covers the saved version and delete conflict. |
| Edit and delete could expose an order outside their own action scope if callers used the store directly. | The low-level store only guarded company and version. | Add an application command service that resolves EDIT or DELETE scope and checks saved visibility inside its transaction. | A Branch-hidden ID returns 404; a visible current-version Draft can be changed. Authorised HTTP and simultaneous-write races remain to be tested. |

## 2026-10-28T15:30:04+08:00 — WU-SD-003a HTTP company isolation review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The earlier VIEW-only HTTP regression checked an empty company and could not prove saved IDs are hidden across two authorised companies. | No persisted row was used in that HTTP test. | Add a saved order and assign the same real SYS user to both companies; assert home detail 200, foreign detail 404 and foreign totalCount 0. | The VIEW query's company boundary is verified through authenticated HTTP with real SQL. Product code needed no change; action scope and write paths remain open. |

## 2026-10-28T15:37:40+08:00 — WU-SD-003a action-scope HTTP review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A company-wide VIEW could mask an accidental use of VIEW scope for EDIT or DELETE. | Existing authenticated HTTP tests checked missing permissions and cross-company reads, but not differing scopes for actions on the same saved ID. | Add a real SYS role combination and saved-row regression: VIEW Company plus EDIT/DELETE Self; check hidden writes and successful own Draft soft-delete against SQL. | HTTP now proves action-specific 404 and independent persisted-row isolation. The initial 409 was a fixture error: direct SQL seeding did not assign Version 1; no product change was required. |

## 2026-10-28T19:59:48+08:00 — WU-SD-003a SQL integration timeout diagnosis

The prior single SYS approval test failed during `TestDatabase.InitializeAsync` migration, before the test body. Today the same exact test passed 1/1 in 28 seconds, and the SalesOrder real SQL/HTTP group passed 29/29 in 44 seconds. This rules out a deterministic migration or SalesOrder failure. No product code or migration changed.

The full integration attempt with `--max-threads 2` was sampled by `Invoke-IntegrationDiagnostics.ps1`. Multiple `myerp_test` requests waited on `WAIT_SLEEP_LOWMEM_GUARD`; one request was blocked by another request that itself waited on the low-memory guard. SQL reported `process_physical_memory_low=1`; Windows available physical memory fell from about 1.1 GB to 0.5 GB of 32 GB. The sampler's SQL login then timed out during pre-login. This is direct evidence of host memory pressure during the full run; it does not establish which process consumed the memory or prove that all earlier timeouts share one cause. The full run produced no test summary after about seven minutes and was stopped under 0.5 GB free memory. Debug solution build passed with zero warnings/errors. Keep full integration acceptance open; next run should first recover host memory and use the existing low-thread diagnostic command, then record an actual suite summary.

## 2026-10-28T20:19:54+08:00 — WU-SD-003a order-line lock checkpoint

The `system_health` 20:08:16 graph identifies a serializable order edit holding a `RangeS-S` lock on the `sal_order_line` clustered key while a separate line insert waits for `RangeI-N`; the insert transaction holds an exclusive `sal_order` key that the edit's line-state trigger needs. The edit's joined `Include(Lines)` is the likely source of the broad line-key range. `SalesOrderDraftStore.LoadAsync` now loads the locked header first, checks the version, then explicitly loads only its lines through the company/order relationship. This keeps the application lock and snapshot checks while narrowing the line read. The existing authorised two-PUT and two-DELETE regression still checks 200/409, 204/404 and persisted state. Focused SalesOrder integration passes 29/29 in two consecutive two-thread runs; Debug solution build has 0 warnings and errors. The pre-change run in this round also passed 29/29, so the timing-sensitive deadlock is not proved eliminated. Host free physical memory is about 1.35 GB; full integration remains unverified. Next verify the actual line query plan and repeat the focused race under load, then run the full integration suite when memory recovers. Quotation conversion still depends on blocked WU-SD-002a.

## 2026-10-28T20:10:52+08:00 — WU-SD-003a low-memory and deadlock checkpoint

The full integration diagnostic run started with roughly 1.5 GB available physical memory and `process_physical_memory_low=1`. After about two minutes it had no result summary; memory fell below 0.8 GB, so the run was stopped. The SQL sampler now records available physical memory, SQL process memory and both SQL low-memory flags beside each wait sample. Its query was executed against the real instance and Debug build passed with zero warnings/errors.

The focused SalesOrder group exposed a separate repeatable failure at two test threads: 28/29 twice, with `AuthorizedManualDraftUsesRealReferencesAndPersistsHttpChanges` receiving HTTP 500 instead of 409. Its server log identifies SQL error 1205. The SQL `system_health` deadlock graph at 20:08:16 shows one transaction's `tr_sal_order_line_state_guard` reading `sal_order` while another inserts `sal_order_line`; their header and line key locks form a cycle. The same test alone passed 1/1, and the group at one test thread passed 29/29. Cross-test concurrency is a plausible trigger, but the graph does not yet identify both test methods. No test or product behavior was weakened. Next: identify both transaction callers and fix lock ordering or test isolation at the correct seam, then rerun at two threads and obtain a full integration summary after host memory recovers. Quotation conversion still requires the blocked real quotation service in WU-SD-002a.
## 2026-10-29T11:51:05+08:00 — WU-SD-002a VOID and detail review

Reviewed the distinct `SAL.QUOTATION.VOID` scope, serialized header lock, permanent source-order check using `IgnoreQueryFilters`, and VIEW-scoped SYS history lookup. The source-order query includes soft-deleted rows so a converted quote cannot become voidable after deleting its order. `isExpired` is derived by `IClock` and does not mutate Approved status. No product change arose from this review.

The focused quotation group was red in this run: detail HTTP returned 500 after 40 seconds, and a separate case timed out while creating a SYS company after 35 seconds. These failures occurred with about 0.3 GB free physical memory; the latter is outside the Sales quotation path. This supports a host resource contribution but does not prove the detail failure has the same cause. The run was stopped; full SQL regression was not started. Debug build passed with zero warnings/errors and SalesModule architecture tests passed 8/8. Recover memory, capture the detail server exception if it repeats, then rerun quotation and full integration gates. Draft/Pending/Rejected and cross-company VOID coverage remains to be added.

## 2026-10-29T12:06:25+08:00 — WU-SD-002a status coverage review

The VOID service checks the current version before the state transition; a new Draft test initially supplied version 2 despite a saved version of 1 and received 409. The fixture now uses the saved version for each state. This distinguishes status refusal from stale-version refusal, and SQL asserts the status, version and void timestamp are unchanged. The foreign-company case confirms the same document ID is hidden as 404. No product code optimization was needed. Focused quotation SQL/HTTP tests pass 29/29 at one thread. A full backend run was stopped before the integration summary when memory fell to 0.85 GB, so no full-regression claim is made.
## 2026-10-29T12:20:14+08:00 — WU-SD-002a full regression resource review

The complete one-thread backend test run again lacked a successful integration summary after about six minutes. Idle .NET build servers were shut down before the run, raising free physical memory from 1.43 GB to 2.35 GB; the run reduced it to 0.83 GB. Shared, Architecture and DocGen passed. The stopped aggregate reported 1301 passed and zero failed, but the integration test host exited -1, so the gate remains open. The largest remaining memory users were interactive IDE/browser processes, which were left untouched. No product code or tests were changed in response to this environmental result. Re-run the full suite when at least 2.5 GB is stable and retain the complete success summary.

## 2026-10-29T17:05:32+08:00 — WU-SD-002b0 read-client review

The first focused client test went red because the service file was absent. After implementation, an exact query-string assertion failed because it assumed parameter order; Angular sent the same supported keys in a different order. Changed the test to assert the URL and each key/value, so it verifies the HTTP contract without coupling to parameter order. Focused tests pass 2/2, full frontend tests pass 716/716, and the production build succeeds. The service has only GET methods, does not send company/owner fields, and never recalculates stored money. `web-design-guidelines` found no rendered UI surface in this service-only slice; the page review remains with WU-SD-002b1. The SQL/backend full gate is still DEFER under the memory guard, and this unit makes no product UI or SQL acceptance claim.

## 2026-10-29T21:03:25+08:00 — WU-SD-002b1 read-only quotation page review

The prior interrupted tree contained the routed GET-only quotation list/detail, three-language strings, simulated E2E cases and screenshots. The memory gate stayed DEFER at roughly 0.53 GiB, so the existing 29/29 SQL/HTTP GET evidence was used as the backend boundary and no SQL/full backend run was launched. Review of every captured light/dark state found that SYS approval-round and step statuses were displayed as raw English values. A new E2E assertion went red on ` · Approved`; the page now translates the known SYS states and formats saved approval timestamps as `yyyy-MM-dd HH:mm`. The new `sd` scope is registered in the three-language fixture, and a missing-detail 404 has a specific translated explanation.

`myerp-ux-review` inspected normal, detail, filtered-empty, unfiltered-empty, validation, 500/traceId, missing-detail, network and forbidden screenshots in both themes where relevant, plus normal English and Simplified Chinese. The 500 and network cases retain a retry and show no false success; a missing VIEW grant sends no quotation GET. `web-design-guidelines` review prompted `autocomplete=off` for the search input and long-content wrapping in detail. The first expanded E2E run failed because the intentional 500 console line was not marked expected and because the generic permission route is `/forbidden`; those fixture expectations were corrected without weakening the global guard. Focused E2E then passed 6/6, frontend suite passed 721/721, DocGen tests passed 22/22, and production build ran through Playwright. This slice exposes no mutation control and does not claim completion of the later Create/Edit page, real SQL browser linkage, or the blocked full backend gate.

2026-10-29T21:07:47+08:00 follow-up review: the first list had a sort state in its URL/client model but no sortable header or page-size picker. Added server-backed document-number and business-date headers, standard page-size choices and URL restoration of page size. The first Playwright build caught ng-zorro's mutable `number[]` input versus the shared readonly tuple, so the page supplies a copied array. The first interaction test used a nonexistent accessible combobox label; the browser snapshot showed ng-zorro's visible `20 條/頁` selector, and the test now locates that rendered control. Focused sorting/page-size E2E is green 1/1. The full page E2E and frontend suite are rerun after this change.
# WU-SD-003a6 review (2026-11-05T02:03:58+08:00)

The initial saved-money validator used `params decimal[]`, allocating an array for each quotation line. Replaced it with six scalar parameters and a shared scalar predicate; validation remains linear over saved lines with no per-line allocation. The SQL money limit and four-decimal precision are checked before arithmetic and copying. The 18 defect cases were red before the guard and the 46-case snapshot suite passed after the guard; wider offline verification is recorded in progress. Real SQL conversion was deferred by the memory gate.

## WU-SD-003a conversion review (2026-11-05T05:55:11+08:00)

The conversion path now locks the quote header before reading its status, source usage or lines, and targets the company/quote line index for both an independent count and complete load. Review found that the first integration fixture omitted `SalesQuotationDbModel`; it is now included. The test fixture did not reach this code path: a migration command timed out and EF later failed to release its session application lock while host free memory was below 1.3 GiB. Ranked causes are SQL low-memory delay, session replacement after timeout, and an independent migration defect; the memory gate now defers further SQL probes. The solution builds with zero warnings/errors; Architecture 297/297 and Shared 381/381 pass. The real SQL test remains red at fixture setup, so the conversion cannot yet be accepted. No temporary instrumentation was added.

## 2026-11-06 WU-SD-006b0 offline credit decision rules

- Review: `SalesCreditRules.Evaluate` validates each commitment and stops when the running sum passes decimal(19,4) max, so `decimal` arithmetic can never overflow even with a very long commitment list; headroom is computed once from the validated total. No I/O, clock or client figure is used.
- Kept the override check order identical to `SalesPricingRules.Decide` (grant 403 before reason 400), so the order form can show one consistent sequence for both overrides.
- No optimisation change was needed after the review; 21 rule cases + 5 translation cases passed, ArchitectureTests 1577/1577, Debug solution 0 warnings／0 errors, frontend `i18n-keys.spec.ts` 38/38.
- Not verified: FI credit lock, SQL commitment query, SYS round/version binding, audit row and HTTP (WU-SD-006b, needs SQL memory gate PASS).

### 2026-11-11T14:51:31+08:00 — WU-SD-005a1 read-only dated price quote

- Review: `Quote` delegates to existing `Select`, so customer precedence, date boundaries and duplicate integrity conflicts have one decision path. Missing/conflicting results carry no rule identity or price; response reads the request business date, not the system clock. No mutation, SQL, HTTP or UI in this slice.
- Test-first evidence: new U15–U18 produced CS0117 (missing `Quote`); after implementation targeted 7/7 and full Architecture 1581/1581. Sales Debug build had 0 warnings/errors.
- Build environment: initial project-reference build hit MSB3027 because existing MyERP.Api.exe (PID 27560) and Visual Studio locked API output. Sales module built independently; test project built with `BuildProjectReferences=false`. No user process was stopped. SQL memory gate was DEFER (1.076/1.065/1.025 GiB), so no SQL or HTTP completion claim.
- Remaining: WU-SD-005a must wire transactional rule loading and read-only HTTP quote, plus rule persistence, permissions, stale quote recheck and SQL tests. It must reuse `Select`/`Quote`/`Decide`.

### 2026-11-11T14:58:09+08:00 — WU-SD-005a2 quoted rule hint shape

- Review: `Decide` checked whether hints came as a pair but accepted zero or negative saved IDs/versions. Those entered stale-quote 409 or duplicate conflict instead of identifying the invalid field. The test first reproduced `STALE_QUOTE`.
- Change: validate both positive values in the existing field-error collection before duplicate, stale or override checks. Both bad fields are reported together; no new branch after conflict handling.
- Result: pricing rules 22/22 and Architecture 1582/1582; Sales Debug build 0 warnings/errors. Existing API/SQL remains WU-SD-005a; SQL gate DEFER 1.372/1.282/1.232 GiB.

### 2026-11-11T15:04:09+08:00 — WU-SD-005a3 save-context pricing decision

- Review: separate `Select` and `Decide` calls let a future save service accidentally pass a selection made for a different business date, customer or currency. Added one thin composition entry point so save uses its own lookup with the same candidate snapshot for both steps. `Quote` keeps its advisory read path.
- Test-first evidence: U21/U22 failed with CS0117 before the helper existed, then pricing rules passed 24/24 and Architecture 1584/1584. Sales Debug build had 0 warnings/errors. The helper has no I/O, allocation beyond existing selection, or second rule-resolution algorithm.
- Remaining: WU-SD-005a must load candidate rules and save in one SQL transaction, use this helper per line, enforce locking and scope, and verify HTTP plus saved monetary snapshots. SQL gate DEFER 1.902/1.879/1.526 GiB.

### 2026-11-11T15:11:10+08:00 — WU-SD-005a4 save-entry field precedence

- Review: `DecideForSave` selected and checked the loaded snapshot before validating client line fields. A duplicate row ID in that snapshot masked a bad unit price, discount or quote hint with a generic `rules` error.
- Change: extract the existing line validator and call it at the save entry before `Select`; `Decide` still invokes the same validator for direct callers. This keeps one set of field rules and preserves the conflict/override sequence.
- Test-first evidence: U23 failed because `rules` masked `lines[2].unitPrice`. After the change, Architecture 1585/1585 passed and Sales Debug built with 0 warnings/errors. U23 also checks a valid line still rejects the malformed snapshot and the ordinary saved decision is unchanged.
- SQL memory gate DEFER 1.583/1.556/1.549 GiB; actual SQL/HTTP pricing and persistence remain WU-SD-005a. API output was locked by an existing user process, so the test project used `BuildProjectReferences=false`; no process was stopped.
### 2026-11-11T15:17:16+08:00 — WU-SD-005a5 loaded pricing snapshot integrity

- Review: `Select` checked loaded rule IDs but allowed invalid version, scope, validity dates and numeric fields. `Quote` could expose a price that a rule write would reject; a save could use it under an override grant.
- Change: validate every loaded snapshot with the existing price, percentage and currency bounds before tier selection. This also checks inactive and other-scope rows so a malformed loaded result cannot hide behind a valid winner. The save entry still checks client line fields first.
- Verification: U25 first failed on version 0, then pricing rules 28/28 and Architecture 1588/1588 passed; Sales Debug build 0 warnings/errors. No additional query or database dependency was introduced.
- Remaining: WU-SD-005a must load snapshots under a real SQL transaction and verify HTTP, locking, data scope and persisted monetary values. SQL memory gate DEFER 1.136/1.126/1.146 GiB.

## WU-SD-003a retry: order line guard trigger deadlock (2026-11-16, ADR-137)

- Problem: with the SalesOrder integration classes at two test threads, `AuthorizedManualDraftUsesRealReferencesAndPersistsHttpChanges` failed: both concurrent `PUT /api/v1/sal/orders/{id}` returned 500 (SQL 1205), not 200/409. The test passed when run alone 3/3. This is the open item from the 2026-10-27 entry above.
- Cause: the `system_health` deadlock graph (04:07:37) showed the wait inside `tr_sal_order_line_state_guard` with RangeS-S on `pk_sal_order`, against X / RangeX-X held by an order transaction in another company. Both line guard triggers joined `sal_order` without a seek, so the optimizer scanned the small header table. Under the Serializable command transactions that scan locked every other order's key.
- Change: migration `Sales_OrderLineGuardSeek` alters `tr_sal_order_line_state_guard` and `tr_sal_order_line_source_consistency` to join `dbo.sal_order WITH (FORCESEEK)`. Rules and error numbers do not change, and `Down` restores the old text. Up/Down/Up was checked on `myerp`, and there are no pending model changes.
- Regression: `LineGuardTriggersDoNotLockOtherOrderHeaders` (SD-ORDER-032). Another transaction holds X on an unrelated order header, and a Serializable line replacement must finish under a 2 s lock timeout. Proof: with the migration's hint removed the test went red (1222), and with the hint restored it went green. A first, stricter version that also replaced the held order's lines was dropped, because the probe showed that adjacent range locks in `ix_sal_order_line_company_id_order_id_line_no` are ordinary one-way Serializable blocking, not this deadlock.
- Effect: the SalesOrder integration group passed 33/33 twice at two threads.
- Lesson: an unhinted trigger join on a small table can scan, and under Serializable that is a cross-company lock. Trigger joins on the Serializable write path should seek, and should have a lock-timeout probe test.

## 2026-11-16 WU-SD-004a2b10 delivery Draft create — isolation review

- Problem: the first version read the order and Posted/Pending totals in a Serializable transaction with a LINQ join. A two-connection test showed a create in another company or for another order waited 2 s and timed out (1222) on the first create's uncommitted insert; two such creates can deadlock (the ADR-137 pattern).
- Cause: the join scanned `sal_delivery`, so Serializable range locks and plain shared reads both hit other orders' rows.
- Change: Read Committed; primary-key `UPDLOCK, HOLDLOCK, FORCESEEK` on the source order (same-order creates serialize); raw SQL totals with `FORCESEEK` on `(company_id, order_id, status)`. The create balance is advisory because a Draft reserves nothing; submit rechecks it.
- Effect: the race test is red with the old design and green now; the other 4 create tests stay green. Diagnosis also found that SYS numbering blocks across companies (backlog P2 WU-SYS-NUMSEQ-LOCK).

## 2026-11-16 WU-SD-004a2b11 delivery Draft edit/delete — shared line rules

- Problem: edit needs the same order lock, order-line ownership, advisory balance and MD/tracking guard as create; copying them would let the two paths drift (for example, one forgetting FORCESEEK).
- Change: moved them from `SalesDeliveryCreateService` into the scoped `SalesDeliveryDraftLines` (`LockOrderAsync`, `BuildAsync` with an optional current delivery to exclude, `GuardAsync`). Create and the new `SalesDeliveryDraftCommandService` both use it. Edit and delete read the delivery's order ID with a primary-key seek, lock the order, then the delivery row, and load lines with an index seek.
- Effect: the 5 create tests stay green unchanged; the 5 new edit/delete tests pass, including the race test where another order's and another company's edits are not blocked.

## 2026-11-16 WU-SD-004a2b12 delivery HTTP — review notes

- Problem 1: the first `GetAsync` draft called `scope.Apply(...)` inside the `Where` lambda; EF cannot translate a method call on a captured object there and would fail at runtime.
- Change: build the scoped order query first (`visibleOrders`) and use `visibleOrders.Any(...)` in the predicate, which EF inlines as an `EXISTS` in the same SQL.
- Problem 2: returning the saved document after POST/PUT through `GetAsync` would require `SAL.DELIVERY.VIEW`, breaking "forms and writers must not need VIEW".
- Change: `DescribeAsync` maps an already-authorised entity (order header snapshot plus order-line numbers/labels) without a second permission check. Effect: SD-DEL-HTTP-002 proves CREATE/EDIT-only actors get 201/200 bodies while GET stays 403 for them.
- List counts: line count and total quantity are correlated subqueries in the paged projection (one round trip, only for the page), not N+1 queries.
- Observation (no change): list/detail are ordinary Read Committed reads; while a create on another order holds uncommitted inserts, a list scan may wait for that commit. Commands are short, so this is a brief wait, not a deadlock. Revisit with an index on `(company_id, business_date)` if the list becomes slow (P3, not opened as a backlog item).

## 2026-11-16 WU-SD-004a2b13 delivery submit/withdraw — review notes

- Problem 1: the first implementation set `Pending` before calling SYS. `IApprovalWorkflowService.SubmitAsync` saves the shared DbContext, so the header was saved twice (version 1 → 3). Cause: SYS persists caller changes (documented contract). Change: call SYS first, then set `Pending` and `ApprovalInstanceId` in one save; withdraw likewise changes status after `CancelAsync`. Effect: one command = one version step (SD-DEL-003, SD-DEL-HTTP-005 assert version 2 / 3).
- Problem 2: `SalesDeliverySelectionStore` demanded Serializable, which ADR-138 had shown to lock other orders' ranges. Change: any caller transaction; header/line reads use `FORCESEEK` with explicit company ID. Effect: the race test proves other-order and other-company submits finish within a 2 s lock timeout while the winner is uncommitted.
- Problem 3: no `SAL.DELIVERY` result handler meant SYS could approve silently and strand a Pending delivery. Change: `SalesDeliveryApprovalResultHandler` (Rejected → Rejected; Approved → 422 until 004b). Effect: tested with the live SYS engine.
- Observation (no change): order lines are locked with one statement per ID to keep ascending order deterministic; at the 1000-line limit that is 1000 short round trips under the already-held order lock. Acceptable for now; a single `ORDER BY` statement does not guarantee lock order.
# WU-SD-003c1 review (2026-11-18T15:16:53+08:00)

| Finding | Cause | Change | Result |
|---|---|---|---|
| Order API exposed numeric lifecycle state, making the future UI depend on enum order. | `SalesOrderStatus` had no JSON converter. | ADR-140 fixed named states and rejected numeric JSON; unit and DTO tests cover all five states and both read shapes. | The wire response has explicit names while EF persistence and lifecycle transitions stay unchanged. Architecture 2163/2163 and Debug build 0 warnings/errors; SQL suite deferred by the existing memory gate. |
WU-SD-003c2 browser-state review (2026-11-18T15:42:40+08:00): Added empty, invalid-date, detail-404 and offline/retry E2E coverage. The first test run exposed that an impossible deep-link date remains in the form until cleared; the test now clears it before a valid search. Eight new light/dark screenshots were inspected. Detail 404 showed an untranslated error key because the shared error catalogue lacked `SAL.ORDER.NOT_FOUND` in all three languages. Added the translations, asserted the visible reason and inspected the updated screenshots. Web Interface Guidelines review of `order-page.ts` found existing ISO date rendering and detail state outside the URL; these remain follow-up improvements, with no new UI control or layout change in this checkpoint. SQL-dependent checks remain deferred by the measured memory gate.
WU-SD-003c2 timeout review (2026-11-18T15:52:18+08:00): A stalled list GET had no deadline and held the loading state indefinitely. Added a six-second timeout to list and detail reads, translated the timeout reason in all three languages, and reused the existing retry action. The E2E was red before the change and green afterward; View 5/5 and frontend 909/909 passed. Both new light/dark screenshots show the reason, empty result state and retry without layout or contrast defects. Web Interface Guidelines review of the changed controls found labels, visible keyboard focus and semantic retry buttons; existing ISO date rendering and detail URL state remain follow-up work. SQL-dependent projection acceptance is still deferred by the measured memory gate.
## WU-SD-003c2 approval history checkpoint — 2026-11-18T20:04:31+08:00

- The order detail API previously omitted the existing SYS approval history reader. After the scoped order lookup, it now requests `SAL.ORDER` rounds; this order prevents history disclosure for a hidden order.
- The page presents saved rounds and an explicit empty state. The new E2E failed before the section existed, then passed. Light/dark screenshots were inspected; the new section has no visual issue. The web interface review found no new accessibility issue in this section. Existing hard-coded date display in the order page remains a later UI refinement.
- SQL memory gate sampled 2.265/2.238/2.235 GiB, so real SQL isolation and full backend validation remain deferred. A separate solution build failed because an existing `MyERP.Api.exe`/Visual Studio process holds API output DLLs; the Sales project builds with zero warnings/errors.
# WU-SD-003c3 receipt verification (2026-11-19T01:09:59+08:00)

- Reviewed manual Create receipt coverage and found quotation conversion had no matching CREATE-only read assertion. The conversion test now checks the saved receipt and the normal detail denial under the same token.
- SQL gate passed 2.883/2.874/2.916 GiB; Release authorised HTTP tests passed 5/5. Debug rebuild could not copy DLLs held by a running `MyERP.Api.exe` and Visual Studio, so the test was built and run in Release without changing the running process.
- No product code changed. Create route, pickers, saved-result UI and browser QA remain in this work unit.
# WU-SD-003c3 Create checkpoint (2026-11-19T01:18:31+08:00)

- Review: a 201 response does not prove that the screen can show saved data to a CREATE-only user. The form reads the saved receipt through the scoped endpoint before showing success; an unconfirmed result blocks another POST.
- Verification: frontend production build, 915/915 frontend tests, manual Create Playwright 1/1 and `git diff --check`. The remaining conversion and error states require further screen QA before this work unit is complete.
## WU-SD-003c3 Create rejection review — 2026-11-19T01:36:24+08:00

- Problem: a 422 business refusal displayed the generic unexpected-error text beside a concrete field reason. The red E2E asserted the mismatch.
- Change: show a translated business-rejection prompt for 422 while retaining field details; add E2E for 422, network loss and timeout, including no second POST after an uncertain result.
- Evidence: Create E2E 7/7; zh-Hant 422 light/dark screenshots inspected. Field details supplied as English by the server remain a P2 localisation issue for this active work unit. Review of the Create template against the current Web Interface Guidelines also found disabled controls without an explicit reason and incomplete field focus/labels; both remain in the next checkpoint.

## WU-SD-003c4 Edit locale error screens — 2026-11-20T18:20:59+08:00

- Problem 1 (P2): server field details on the order form were translated once when the error arrived, so switching to English or Simplified Chinese left Traditional Chinese reasons under a translated banner. The new zh-Hans/en E2E captures failed on 400 and 422.
- Change 1: the order page keeps the last validation failure and rebuilds its details on `onLangChange`, matching the quotation form; the field/reason separator is `：` for Chinese and `: ` for English (P3 seen in screenshots).
- Problem 2 (P2): the real backend refuses an inactive or unapproved customer with `422 MD.CUSTOMER.NOT_APPROVED` and no `errors`; Edit and Create showed "check the reason below" with nothing below. The red E2E used the real response shape.
- Change 2: both save paths recognise `MD.CUSTOMER.NOT_APPROVED` and show its translated message.
- Evidence: Edit 32/32, Create 24/24, View 7/7, frontend 922/922; backend unit Architecture 2173/2173 (two new tests: rejected customer stops further MD lookups; zero-line Edit rejected before scope/DB), Shared 386/386, DocGen 22/22; Debug build 0 warnings/errors. 26 new screenshots (Edit total 92) inspected one by one; no open P1/P2. SQL memory gate 1.767/1.775/1.767 GiB DEFER, so the SQL-backed full backend suite was not run.

## WU-SD-003c5c Order View/Create/Edit QA — 2026-11-20

- Problem (P2, D4/A5): the order detail printed raw JavaScript numbers (`1312.5`, `12.5`). The list printed the same saved amount as `1,312.50`, so one value appeared in two formats on adjacent panels.
- Change: `SalesOrderPage.amount()` is now the only amount formatter for the list cell, the detail header and the line amount. `decimal()` uses the shared `formatDecimalText` for quantities and unit price, which adds grouping without rounding or float arithmetic.
- Evidence: new E2E SD-ORDER-QA-001. A stateful simulated server receives Create (POST) and then Edit (PUT v1). The test asserts the list, detail and Edit form show the stored 1,312.50 → 1,575.00, quantity 100 → 120, remark and version, and that exactly one POST and one PUT were sent. View E2E 7/7 is unchanged. 18 new screenshots inspected.
- Deferred P3 (WU-SD-003c7): half-width colon in Chinese detail labels; Edit shows `5%` without the tax code shown on Create; pale light-mode success banner on the order form.

## WU-SD-003b1 order submit credit gate (2026-11-20T20:59:51+08:00)

- Review: the credit gate is shared by submit and the SYS handler so the two paths cannot drift; the FI lock is taken before the order row in both, matching BR-SD-033 lock order and avoiding a deadlock with credit-limit edits.
- Problem found while testing: seeding a FI limit without its `ar_credit_key` row violates the FI foreign key, and the order state trigger (51005) forbids Draft → Rejected/Cancelled; test seeds now follow the real transitions instead of weakening the trigger.
- Decision: the customer ID for the FI key is read without locks and rechecked after the order lock (409 on change) rather than locking the order first, which would invert the lock order.
- Not changed: commitments query uses the existing customer index with FORCESEEK; no new index needed.
- Problem (P1, found by the full backend run): the first full integration run stalled with no SQL activity after ~120 tests. `ApprovalApiTests` alone reproduced it (timeout). Cause: `SalesOrderApprovalResultHandler` → `SalesOrderCreditCheck` → `ICustomerLookup` (MD `CustomerService`, factory-registered) → `IApprovalWorkflowService` → all `IApprovalResultHandler`s — a DI cycle that the container cannot detect through the factory and that hung resolution. Change: the handler takes `Func<SalesOrderCreditCheck>` (DI factory registration) and resolves it inside `HandleAsync`. Effect: `ApprovalApiTests` + order submission 8/8 in 30 s; architecture guard test added (fails if any Sales handler injects the credit gate or an MD Contract).

## WU-SD-003b2 (2026-11-20T22:26:11+08:00)

- Review: cancel reuses `SalesOrderLocks.LockOrderAsync` so it takes the same order-row lock as delivery submission; no new lock order is introduced. The delivery existence check uses the existing `ix_sal_delivery_company_id_order_id_status` through EF with company and soft-delete filters.
- Decision: the submit response wraps the detail and a name-based credit DTO instead of exposing the internal `SalesCreditAssessment` (numeric enum, error code field) to keep the JSON contract stable.
- Not changed: no `cancelled_at` migration (ADR-143); audit columns and version already record the cancel.
- Verification gap: full backend run deferred by the SQL memory guard (DEFER); WU-SD-003b3 must run it.

## WU-SD-003b3 order HTTP races and resubmission (2026-11-20)

- Review: submit (FI credit key → order row), cancel (order row) and delivery submit (order row → delivery row → order lines) all take `UPDLOCK, HOLDLOCK` on the same `sal_order` row first, so no lock-order inversion exists between them; the new tests hold that row from a second connection and confirm every request waits behind it before any read decides the outcome.
- Finding (not changed): `SalesOrderCancellationService` relies on its order-row lock to make the Pending/Posted delivery check stable. Without it the check would read under Read Committed before the UPDATE blocks, and a delivery submitted in between could be left Pending on a Cancelled order. SD-ORDER-HTTP-007 guards this outcome (never Cancelled + Pending); a mutation run was not used because the unlocked variant only fails when the delivery wins the race, so it would be a flaky signal rather than a deterministic red.
- Finding (not changed): concurrent SYS approve/reject of one instance serialize inside SYS; the loser gets 409/422 and the SD handler sees the winner's order state, so no extra SD lock is needed.
- Test helper: `SalesOrderCommandHttpTests` gained a disposable `RowLock` (commit on release, rollback on dispose) so a failed assertion cannot leave the order row locked for later classes.

## WU-SD-004b1 delivery posting through INV (2026-11-20)

- Refactor: the order-line lock loop, the saved balance snapshot query and the line loader moved from `SalesDeliverySubmissionService` into `SalesDeliveryPosting`, so the submit path and the SYS final-approval handler share the same lock order and the same `CheckLockedDecision` call. Before, a second copy in the handler would have been needed. The submission service lost two constructor dependencies (selection store, order store).
- Review: the zero-step path posts inside the MD reference guard callback, so the MD shared locks on item/warehouse are still held when INV records its own references in the same transaction. No new lock order is introduced for that path.
- Finding (documented, not changed): SYS final approval (SYS instance → order row) and withdrawal (order row → SYS instance) of the same delivery lock in opposite order. A deadlock would make SQL Server fail one transaction with no partial effect. The order handler (ADR-142) has the same shape. Recorded in ADR-144, and WU-SD-004b2 adds an HTTP race test.
- Guard: an INV replay, or a movement count different from the line count, is a 409 conflict rather than a silent second success (SD-DEL-010).

## 2026-11-20T23:58:44+08:00 — WU-SD-004a2b14 merge review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| After WU-SD-004b1, the branch's private lock, snapshot and line-loading methods in `SalesDeliverySubmissionService` duplicated the new shared `SalesDeliveryPosting`, and the branch would have posted without lots or serials. | The branch was written before ADR-144 moved posting into a shared class. | Kept main's shared class only. Moved the tracking load into `SalesDeliveryPosting.LoadLinesAsync` (index seek on this delivery's lines only). `PostAsync` maps the selections to `InventoryTrackingLine`. | One loading path serves zero-step submission and SYS final approval. INV now checks lot and serial availability at posting. There is no duplicate SQL. |
| The branch ADR-140 collided with main's ADR-140 (order status JSON). | Numbers were allocated on separate lines of work. | Renumbered the branch ADR to ADR-145 in code comments, docs and the test plan. Kept REQ_DS43.203. | Every ADR has one meaning. |

## 2026-11-21 — WU-SD-004b2 movement-ID storage review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A Posted delivery had no SD-side link to its INV movements (SD-DEL-020 open). | ADR-144 deferred storage until the tracking migration merged. | Nullable `inv_movement_id` on each line, filled by `SalesDeliveryPosting` from the INV result ordered by line number; filtered unique index per company (ADR-146). | Detail shows the movement per line; a movement cannot be claimed twice. A new table was rejected as a duplicate one-to-one mapping. |
| The approve-versus-withdraw opposite lock order (ADR-144) could deadlock. | SYS locks its instance before calling the SD handler; SD withdraw locks order → delivery → SYS. | No code change. Added SD-DEL-HTTP-006 race over real HTTP; four runs gave one 200 and one 409/422, never a 1205 victim. | Risk documented; retry deferred until a victim is observed (ADR-146). |

## WU-SD-004c1 (2026-11-21T00:37:54+08:00)

- Problem: delivery status was returned as an integer, unlike order and quotation, and the VIEW detail had no SYS history. Change: ADR-147 named-state converter plus `approvalHistory` on the VIEW read only. Effect: the client uses one translation key per state, and the history matches the order detail. The HTTP tests went red 3/6 before the change and 6/6 after it, and the delivery tests pass 28/28.
- Review: `myerp-ux-review` covered 20 screenshots of 2.7 (normal in three languages, posted/pending detail, empty, no-result, invalid date, 404, 500), viewed in both themes with no P1 or P2 findings. P3: the warehouse and location columns show IDs because the delivery detail has no MD names; this is left to WU-SD-004c2, whose form already loads MD warehouse options. `web-design-guidelines`: labels, focus-visible, tabular numbers, native select colours, `autocomplete=off` and live alerts pass. Document numbers are now `translate="no"`. The detail panel is not deep-linked, as on the order page.

## WU-SD-004c2a (2026-11-21T01:04:45+08:00)

- Review: `myerp-ux-review` checked 13 screenshots of 2.8 (empty, normal in three languages, validation, line 422, balance reload, 500, success, light and dark). There were no P1 or P2 findings. P3 fixed: in the validation screenshot, a quantity input without its own error stretched to the height of a neighbouring field's error text. Change: `.line{align-items:start}`. Effect: inputs keep one height, confirmed in the regenerated screenshots.
- `web-design-guidelines`: labels wrap their controls; focus-visible outline; `autocomplete=off`; required marks; inline errors with focus moved to the first error; leave guard; tabular numbers; `translate="no"` on item codes; every disabled picker has a reason. Fixed: the quantity input now has `spellcheck="false"`. Not changed: the page is not deep-linked (it is a create form).
- Decision: no follow-up GET after POST, unlike the order create page. The 201 body is compared line by line instead, so CREATE-only users get a confirmed success (ADR-148).

## WU-SD-004c2b1 (2026-11-21T04:28:32+08:00)

- Found by test: `trimDecimal` lost the escape in `/\.$/` when written through a shell template, so '2.500000' became '2.'. SD-DEL-ED-002 caught it and it was fixed. The route-only spec was moved to its own `describe` because the shared `afterEach` HTTP verify created an unconfigured TestBed and broke every following test.
- `myerp-ux-review` (15 screenshots of 2.9: Rejected, conflict, validation, line 422, 500, delete confirm, deleted, blocked, success in three languages; light and dark). P2 fixed: in the stale state, save and delete were disabled with the reason only in the alert at the top. A hint is now shown next to the buttons (unit and E2E assertions). P3 fixed: the "Reload latest balance" button stretched to full width and is now wrapped in an action row. P3 open: saved warehouse/location show IDs until WU-SD-004c2b2 adds names.
- `web-design-guidelines`: added `role="status"` live regions to the outcome alerts. Labels wrap their controls, focus-visible outline, autocomplete off, inline errors with focus on the first one, leave guard, danger confirmation for delete, tabular numbers, `translate="no"` on document numbers. Title Case does not apply (ui-conventions, Chinese-first).

## WU-SD-004c2b2a (2026-11-21T04:54:37+08:00)

- Design: actions were put in the list detail rather than the edit page, because the edit route needs EDIT and a Pending outbound has no form. This also gives DELETE-only users a delete entry. No new component; the detail panel reuses the edit page's outcome rules (newer-version receipt, stale pause, line marker).
- `myerp-ux-review` (16 screenshots `2.7-action-*`: Draft, submit confirm, submitted in three languages, withdrawn, line 422, stock 422, 500, posted, delete confirm, deleted; light and dark). P2 fixed: an empty lot/serial cell said "Not tracked" (不追蹤) next to a TRACKING_REQUIRED refusal for the same line. It now says "None selected" (未選取). Unit and E2E assertions were updated or added. No P1. P3 open: warehouse/location still show IDs (WU-SD-004c2b2c). INV stock refusals cannot name a line (WU-SD-004c2b2d).
- `web-design-guidelines`: in the paused state the focused action button became disabled and keyboard focus fell to the page. Refresh is now focused after render (`afterNextRender`; SD-DEL-ACT-016 asserts it). Outcome alerts use `role="status"`/`role="alert"`, delete is a danger confirmation, buttons show loading and are locked during a request. Title Case does not apply (ui-conventions, Chinese-first).

## WU-SD-004c2b2b1 (2026-11-21T05:17:17+08:00, ADR-151)

- Design: the tracking mode comes from a batch MD contract method (`ISalesItemLookup.TrackingModesAsync`) inside `DescribeAsync`, so every receipt and the detail share one query and need no MD.ITEM.VIEW. We did not use the INV `IInventorySelectionOptions.SavedAsync`, because its contract is limited to INV documents. The INV lot list was not used as a picker, because its balance is company-wide rather than per warehouse/location (left to 004c2b2b2).
- Test first: SD-DEL-HTTP-008 failed (`[null]`) with the mapping removed and passed with it restored; backend full suite 3703/3703 (--max-threads 2).
- Found by E2E: focusing the new lot row with `queueMicrotask` ran before the zoneless render, so the row did not exist yet. Changed to `afterNextRender` with the component injector (SD-DEL-ED-034 asserts focus).
- `myerp-ux-review` (screenshots `2.9-tracking-*`, light/dark, three languages). P2: after a refused save, a corrected lot row still showed "each row needs a lot number" and "1 field needs correcting" next to "complete". Pick edits now re-run validation once a save has been attempted (E2E asserts the issue and the error count disappear). No P1. P3: none.
- `web-design-guidelines`, checked by hand against the rule set: inputs have aria-label, autocomplete off, spellcheck false, inputmode decimal and focus-visible; errors are inline with role=alert; progress uses tabular numbers. No findings.

## WU-SD-004c2b2b2a — tracking options endpoint (2026-11-21, ADR-152)

- Review: the item ID is never accepted from the client; it is read from the order line after the CREATE/EDIT scope check, so the endpoint cannot be used to probe other items' stock. The scope check tries each granted permission's filter (CREATE then EDIT) and stops at the first that sees the order: a user with CREATE in Self scope and EDIT in Company scope still gets results, matching what either action could save.
- Query plan: the INV query reuses the per-lot/per-serial correlated sum already used by `TrackingQueryService.LotsAsync` (covered by the `(company_id, lot_id)` / `(company_id, serial_id)` indexes including `movement_id, quantity`), filtered by the movement's warehouse/location, and stops at `limit + 1`. No N+1: one SQL statement for options, one MD batch call for the mode.
- Edge: a 51–100 character keyword on a Lot line is rejected by INV as a 400 `keyword` field error rather than silently truncated; documented in api.md.
- Mutation check: removing the warehouse/location predicate made SD-DEL-HTTP-009 fail (collections differ), restored → pass.

## WU-SD-004c2b2b2b — pick lots/serials from stock (2026-11-21T09:17:35+08:00, ADR-152)

- Problem: opening the picker cleared the line's last pick result before the dialog showed, so cancelling left no trace of what the previous pick added. Cause: the reset was placed at dialog open instead of on confirm. Change: the result is replaced only when a pick is confirmed. Effect: SD-DEL-ED-035 (result still readable after reopening, cancelling and switching language) passes.
- Test ID collision: the new create-receipt unit test reused `SD-DEL-CR-021` (already the E2E create test); renamed to `SD-DEL-CR-023`.
- Efficiency: one GET per keyword per dialog opening; pagination is sliced from the cached reply (≤ 100 rows), so paging sends no requests.
- Decimal safety: pick quantities use BigInt at six places (`addStockPicks`), so 13-digit quantities stay exact; no `Number` arithmetic on business values.
- E2E full run: 621 passed, 3 failed. SD-DEL-ED-035 was the bug above; FI-COLL-UI-102/103 and SD-ORDER-QA-001 failed waiting for the header theme/language menu to close under full-suite load and passed when rerun alone (unrelated to this unit; no change).

## WU-SD-004c2b2c — line warehouse/location labels (2026-11-21T09:50:04+08:00, ADR-153)

- Option chosen: backend labels via the existing MD contract `IInventorySelectionOptions.SavedAsync` instead of a front-end batch lookup, because VIEW users normally lack `MD.WAREHOUSE.VIEW` and locations would need one request per warehouse. Two reads per detail, IDs only from the saved delivery.
- Added `warehouseActive`/`locationActive` (beyond the backlog text) so an inactive saved warehouse is explained before save/submit is refused with `MD.WAREHOUSE.INACTIVE`.
- Review: the edit page's post-save label handling now merges (reply first, picked label as fallback) instead of restoring local labels over the reply. Removed the unused `sd.deliveryEdit.savedWarehouse/savedLocation` keys; list headers no longer say "ID".
- UX review (screenshots `tests/qa/SD/screenshots/2.7-references-*`): labels readable in light/dark; the inactive mark uses secondary text colour, not colour alone (the word is shown). No P1/P2 found.
- web-design-guidelines (rules from memory, latest list not fetched this round): label cells wrap, the inactive state is a word not only colour, no new controls; no findings.

## WU-SD-004c2b2d (2026-11-21T10:27:50+08:00, ADR-154)

- Option chosen: INV adds `lineNo`/`lineNos` extensions where it refuses (it already knows the caller's line numbers), instead of SD re-checking stock per line before calling INV (duplicate logic, extra reads under locks) or mapping INV messages back to lines (fragile). SD code is unchanged; both the submit and SYS approval routes return the extensions.
- `lineNos` (beyond the backlog text): INV checks a balance key total, so a shortage across two lines of the same item/warehouse/location names both rather than an arbitrary one.
- UI: one ARIA alert (first marked line) instead of one per line, to avoid repeated announcements; the line list separator is translated ("、" in Chinese).
- UX review (screenshots `2.7-action-stock-lines-*`, `2.7-action-serial-line-*`): marks and reasons readable in light/dark, three languages complete; no P1/P2. The mocked fixture has no warehouse labels, so "Deleted warehouse (ID 4)" appears — fixture data, not a product issue.
- web-design-guidelines (rules from memory, latest list not fetched this round): marked lines carry the reason as text, not colour only; one live alert; translated list separator. No findings.
- Full mocked E2E: 625 passed, 1 failed (SD-ORDER-EDIT-010/011, language menu did not close within 10 s, untouched code), 27 skipped; the spec alone with `--repeat-each 3` passed 96/96. Recurring, so it is now backlog WU-P0-E2E-FLAKE-MENU, to be diagnosed with diagnosing-bugs.

## WU-SD-004c4 — delivery form options by action (2026-11-21, ADR-155)

- Duplication: the order-line delivered/pending balance query lived inside `SalesOrderQueryService.GetScopedAsync`. Extracted to `DeliveryBalancesAsync` and reused by `SalesDeliveryFormOptionsService.OrderAsync`, so the order VIEW detail and the delivery form can never show different balances. Effect: one query definition; SD-DEL-HTTP-012 and the existing order detail tests both pass on it.
- Data exposure: the delivery form used to read the full order detail (prices, amounts, approval history). The new source DTO carries identity and quantities only (asserted: no `unitPrice`, no `originalGross`).
- Active-only warehouses: the MD warehouse list returned inactive warehouses, which the server then refused on save (`MD.WAREHOUSE.INACTIVE`). The delivery option uses the INV selection contract (active only), removing that trap.
- E2E mocks: generic `**/sal/deliveries**` handlers would swallow the new option routes (later routes win in Playwright); they now `fallback()` for `order-options`/`warehouse-options`.
- E2E flake: a trailing theme switch back to light in the new SD-DEL-CR-025 hit the known menu-close timeout (WU-P0-E2E-FLAKE-MENU); the step was unnecessary and was removed.
- UX review (`2.8-create-only-*`, light/dark zh-Hant): CREATE-only user fills order, balance, warehouse and location; no P1/P2. P3: no sidebar entry without VIEW → WU-SD-004c4a.

## WU-SD-004c3 Sales outbound 2.7–2.9 error QA (2026-11-21)

- Shared fix instead of page patches: the 2.7 list and detail showed "unexpected error" for a server 403 because they did not list `COMMON.PERMISSION.DENIED`. Cause: `apiErrorKey` only maps listed codes. Change: any unlisted 403 maps to the shared "no permission" key (ADR-156). Effect: every page that forgot the code now explains a 403. Frontend 1034/1034 tests pass.
- Test cost: timeouts are simulated by holding the request 6.8 s, longer than the service's 6 s `timeout`. Holds are limited to one per state, and the timeout-only create case runs as its own short test so it runs in parallel. The whole QA spec takes about 2 minutes on 4 workers.
- No product code change on the SD screens themselves; P3 wording items → WU-SD-004c3a.

## WU-SD-005a8 Price rule table and create/list/detail (2026-11-21)

- Wrong test expectation fixed (not weakened): SD-PRICE-HTTP-002 expected `sort=id` ascending to give [default, USD, customer], but the rules were created as default → customer → USD. Corrected the order of the expected IDs; the product sort was already right.
- Migration analyzer: EF generates constant column arrays (CA1861 error under warnings-as-errors). Used the same `#pragma warning disable CA1861` as earlier migrations.
- Index review: the selection index `(company,item,currency,customer,valid_from,id)` starts with company/item, so 005d's planned separate company/item index was not added. The overlap query is one seek per key under the applock, with no N+1.
- Known gap left for 005a9: if the lock is not granted within 15 s, SQL error 51020 comes back as a 500 with traceId. A named 409/503 would be clearer once the edit paths share the lock helper; it is recorded there instead of patched here.

## WU-SD-005a9 Price rule edit and deactivate (2026-11-21)

- Gap from 005a8 closed: lock-not-granted (SQL 51020) used to surface as 500. Cause: the T-SQL `THROW` was not mapped. Change: one `LockKeysAsync` helper for create, edit and deactivate maps it to 409 `SAL.PRICING.RULE_LOCK_TIMEOUT`. Effect: SD-PRICE-HTTP-009 gets a retryable named error in about 300 ms with a short test timeout.
- Deadlock avoidance: an edit that changes key needs two locks. The helper removes duplicates and sorts keys ordinally before locking, so two crossing edits always ask in the same order. SD-PRICE-HTTP-008 swaps keys in opposite directions four rounds without a deadlock.
- Duplicate code removed: MD checks for create and edit share `RequireMasterDataAsync`; the inline lock method became the shared helper.
- Write path review: the rule is read once before the transaction (fail fast on 404/409/422 without taking locks) and again under the locks. The second read plus the version as EF concurrency token covers a writer that moved the rule to another key in between. The overlap query excludes the rule's own ID in SQL, so an edit never conflicts with itself.

## WU-SD-005a10 Price quote endpoint (2026-11-21)

- Placement: the quote got its own `SalesPriceQuoteService` instead of a method on `SalesPriceRuleService`. Reason: the writer's constructor is rebuilt by the lock-timeout test (SD-PRICE-HTTP-009); adding `ICurrentAuthorization` there would spread an unused dependency to every writer. Effect: no change to the writer or its tests (ADR-159).
- Query review: one read loads both tiers (`customer_id = @c OR customer_id IS NULL`) for the item, currency and date, which seeks on the existing `(company_id, item_id, currency_code, customer_id, valid_from, id)` index; no new index is needed. Selection stays in `SalesPricingRules.Quote`, the same code document save will use, so quote and save cannot disagree on tier precedence or duplicates.
- Fail-closed checks: parameters (one 400 with every field) → permission → MD item/customer (404/422) → selection. A duplicate active tier returns 409 with `conflictingRuleIds` and no price; the test proves no rule row (version, status, price, updated time) changes after many quotes. Nothing else to optimise in this unit.

## WU-SD-005a11a quotation line pricing (2026-11-21T15:15:49+08:00, ADR-160)

- Problem: the first `ck_sal_quotation_line_pricing` accepted a `Rule` line with null rule ID/version. Cause: `[pricing_rule_id] > 0` on NULL is UNKNOWN, and a CHECK that evaluates to UNKNOWN passes. Change: every comparison on a nullable column is preceded by `IS NOT NULL` in the same conjunct. Effect: SD-QPRICE-HTTP-005 red → green; the migration was regenerated before it was applied to `myerp`.
- Query review: one candidate query per save loads both rule tiers for all line items (`item_id IN (...)`, currency, active, date), seeking on the existing `(company_id, item_id, currency_code, customer_id, valid_from, id)` index; lines are decided in memory, so a 1000-line quotation still issues one rule read. The FK index `(company_id, pricing_rule_id)` covers rule-usage lookups.
- Fail-closed: the pricing service refuses to run outside the caller's transaction; override actor/time come from the authorization context and `IClock`, never from the request; refusals roll back the number as well (SD-QPRICE-HTTP-002 asserts number 001 after refusals).

## WU-SD-005a11b manual order line pricing (2026-11-21T15:46:14+08:00, ADR-160)

- Reuse: the hint-pair/reason shape checks were duplicated between quotation and order input; extracted to `SalesPricingHints.Validate` used by both (quotation tests unchanged). `SalesOrderPricing.ApplyAsync` mirrors the quotation helper and calls the same `SalesDocumentPricingService` (one rule query per save).
- Error order: the draft store already refuses converted and non-editable orders, but only after pricing would have run; the edit service now checks `SourceQuotationId` and the lifecycle before pricing so such edits still return 422 instead of an unrelated 403/409 pricing error. The store keeps its guard.

## WU-SD-005b1a price-rule view (2026-11-21T19:20:19+08:00, ADR-161)

- Labels: instead of the page calling MD per row (needs MD permissions, N requests), `SalesPriceRuleLabels` adds current item/customer labels in one batch read per kind per page through existing MD Contracts; effect: the page needs only `SAL.PRICING.VIEW`, SD still never reads MD tables.
- Layout: date and amount cells wrapped at default widths (UX finding P2); `nowrap` added, guarded by a line-box assertion in SD-PRULE-UI-101.
- Recovery: the previous run stopped (quota) before docs and commit; this run re-verified build, SD-PRICE-HTTP 12/12, frontend 1048/1048, price-rule E2E 4/4 and Sales menu/home E2E 22/22 before committing.

## WU-SD-005b1b1 pricing option endpoints (2026-11-21T19:37:09+08:00, ADR-162)

- Reuse: the three routes delegate to the existing MD Contract `ISalesSelectionOptions` (active items, active approved customers, active currencies) instead of adding a new MD contract or copying its paging/keyword code; effect: one thin `SalesPricingOptionsService`, same 400 rules (`sort` only `code`) as the other sales forms.
- Known cost: `currency-options` uses `CommercialAsync`, which also reads output tax rates for today (one extra small query per call). Accepted: the form loads currencies once; a currency-only MD method is not worth a contract change now.
- UX finding P3 (screenshot `2.11-item-picker-light-zh-Hant`): the picker silently listed only active records, so a user looking for an inactive item's rules would think it missing. Fixed with a dialog description in three languages, asserted in SD-PRULE-UI-012 and SD-PRULE-UI-105.
- Frontend: the list page no longer injects MD `ItemService`/`CustomerService` (no MD request from a pricing-only user, asserted by SD-PRULE-UI-105).

## WU-SD-005b1b2 price-rule create/edit (2026-11-21T19:54:22+08:00, ADR-163)

- Structure: validation, JSON assembly and failure classification are pure functions in `price-rule-form.ts` (unit-tested without TestBed); the page only maps outcomes to alerts. Effect: the 14 server outcomes are tested in one table-like spec.
- Bug found by test: the currency option load shared the page epoch, so on the edit route `loadSaved()` invalidated the currency reply and the dropdown stayed empty; fixed with a separate currency epoch (SD-PRULE-FORM-UI-007/011).
- Safety: a 422 with an unknown code was first classified as "unknown outcome" (locking the form needlessly); now any other 4xx is "refused, nothing saved" and only 0/5xx/timeout are unknown.
- UX review (screenshots `2.13-inactive-light-zh-Hant`, `2.13-stale-light-zh-Hant`): P2 disabled text/date inputs looked editable → dimmed with not-allowed cursor; P3 the unknown-result message asked to check the list without a button → Back to list (and Reload rule on edit). Both asserted in SD-PRULE-UI-108. P3 not fixed here: English shell header overflows about 22 px at 768 px (layout, not this page) → WU-P0-HEADER-EN-768.
- web-design-guidelines: date inputs gained `autocomplete="off"`; Title Case rule not applied (ui-conventions uses sentence case).

## WU-SD-005b2a quotation pricing controls (2026-11-21T20:12:34+08:00, ADR-164)

- Reuse: line pricing logic is in pure `document-pricing.ts` (no TestBed) so the manual order form (005b2b) shares it; the page only wires lookups and messages.
- Correctness: line state is keyed by the line object (not index), so removing a line cannot move another line's rule or error; quote replies are guarded by a per-line token and the company epoch.
- Design change during the unit: first version required a reason on legacy saved lines; that would block saves the server accepts (a current rule may match the saved price). Legacy now shows an optional reason and the server decides (unit test U02 and edit E2E protect it).
- E2E: existing create/edit specs had no quote mock; the unmocked lookup produced a 404 console error. Mocks added with the same price as typed/saved so the original assertions keep their meaning.
- UX review (screenshots `2.2-pricing-*`, light/dark, 3 languages): no P1/P2. P3: the reason label does not say "required" when it is (the error appears after Save) — left as is, recorded here; the line message and alert both name the line.

## WU-SD-005b2b manual order pricing controls (2026-11-21T20:34:38+08:00, ADR-165)

- Reuse: `pricingTokens` moved into `document-pricing.ts` (quotation service re-exports it) so both draft JSON writers share one implementation; the order page reuses every pricing helper.
- Correctness: pricing 403/409 are classified before the edit page's generic 409 conflict and 5xx/unknown handling; otherwise STALE_QUOTE would lock the edit form as a version conflict. The conversion path skips pricing classification (no lines are sent).
- Bug in a new test, not in the product: the edit mock did not store the remark from PUT, so the page correctly reported an uncertain result (saved receipt did not match); mock fixed.
- UX review (screenshots `2.5-pricing-*`, `2.6-pricing-stale-requoted-*`, `2.4-pricing-detail-source-*`): P2 the shared alert `errors.SAL.PRICING.OVERRIDE_REQUIRED` said "此報價…" on the order page → generic wording in zh-Hant/zh-Hans, asserted in SD-OPRICE-UI-102. P3 (same as 005b2a): the reason label does not say "required" before Save. The detail column wraps long reasons without overflow.
- web-design-guidelines (checked against the known rule set; guidelines not re-fetched this round): reason textarea has label, name, autocomplete off and aria-invalid; pricing block is aria-live polite; errors use role=alert; buttons are type=button; focus-visible outline kept. No change needed.

## WU-SD-005b3a pricing real-API E2E (2026-11-22, ADR-166)

- Problem (found only against the real API): every quotation/order save on a seeded Development database returned 422 `SYS.NUMBER_RULE.NOT_CONFIGURED`. Cause: no SD number-rule seed (FI had one since ADR-135). Change: `SalesNumberRuleSeeds` (SQ/SO/DN, year-month + 4, monthly). Effect: `--seed` added 6 rules (2 companies × 3); SD-PRICE-NRSEED-001 green.
- Problem: quotation list tag / detail crashed with `toLowerCase is not a function` on real data. Cause: `SalesQuotationStatus` serialized as an integer; mocked E2E always returned names, so no mocked test could see it. Change: names-only `JsonStringEnumConverter`, two HTTP assertions moved from integer to name (SalesQuotationHttpTests red 1/4 → green). Effect: SD-PRICE-REAL-002 detail step passes.
- Test bug (not product): `line_no AS lineNo` in the sqlcmd read-back failed because `LINENO` is a T-SQL reserved word; alias bracketed.
- Seed gap: SALES role lacked `SAL.PRICING.CREATE/EDIT/DEACTIVATE/OVERRIDE` until `--seed` re-ran (the seeder back-fills; 4 grants added). The spec now asserts the five grants first.
- Open: SALES role cannot use quotation/order pickers (MD view) → WU-SD-FORM-OPTIONS (P1); misleading number-rule message → 005b3b (P2).

## WU-SD-FORM-OPTIONS SD document-form options (2026-11-22, ADR-167)

- Problem: the `sales` user could not pick a customer, item or tax rate on the quotation/order forms against the real API (MD view required), and the MD lists offered records the save rejects. Change: `GET /sal/document-options/{customers,items,output-tax-rates}` via `ISalesSelectionOptions` (writer permission, no MD); both forms switched; tax picker is one dialog of rates effective on the document date. Effect: real-API pricing E2E 4/4 runs every screen as `sales`.
- Reuse: no new MD contract — `CommercialAsync(date)` already returned the dated output tax rates; SD pages them in memory (few tax codes).
- UX review (screenshots `2.2-docopt-*`, `2.5-docopt-*`, zh-Hant light/dark, en and zh-Hans): no P1/P2. The disabled tax button carries a visible reason (aria-describedby). P3: on a 500 the shared select dialog shows the "no data" row under the error and trace ID (shared component, pre-existing); a rate chosen before the date changes stays selected and is rejected at save if not effective (server message) — both left as is.
- Test note: theme menu cannot be clicked while a modal is open; screenshots open the dialog once per theme.
- web-design-guidelines (known rule set; guidelines not re-fetched): tax button type=button, focus-visible kept, disabled state explained by a visible hint linked with aria-describedby; no change.

## WU-SD-005b3b pricing error-state UX QA (2026-11-22T01:31:26+08:00, ADR-168)

- Problem: a line quote answered with 5xx showed only "unexpected error" without the trace ID (MASTER_PROMPT 20.1). Cause: `LinePricing.failed` kept only the message key. Change: the state carries `apiErrorTraceId(error)` (5xx only) and both forms render `Trace ID: …` after the message. Effect: support can match the line failure to the server log; SD-PRICE-QA-U01, SD-PRICE-QA-001/002.
- Problem: 422 `SYS.NUMBER_RULE.NOT_CONFIGURED` on quotation save / order create looked like a reference or generic rejection. Cause: the code was not in the pages' known-code lists, so the 422 fallback text won. Change: added to both lists; the existing three-language errors text is used. Effect: the user learns nothing was saved and whom to ask.
- web-design-guidelines (known rule set; guidelines not re-fetched): the trace span lives inside the line's `aria-live="polite"` pricing box, plain selectable text, no new interactive element; no finding.
- P3 left in backlog WU-SD-005b3c (order detail header wrap, generic 403 text on quote line, Retry on 403 list).

## WU-SD-006b1 credit override on submit (2026-11-22, ADR-169)

- Review: `SalesOrderCreditCheck.RequireWithinLimitAsync` (SYS final decision) now delegates to the new `DecideAsync(..., reason: null, grant: false)`. Both paths keep a single evaluation and decision order (ADR-104), so the submit override can't drift from the final-decision refusal rules.
- Review: the override grant is resolved only when a reason is sent (`SalesActionAccess.AllowsAsync`), so a plain submit makes no extra authorization lookup. The grant must cover the order's data scope (Self/Branch/Department), the same rule as `SAL.PRICING.OVERRIDE`.
- Review: the evidence row is added before the single `SaveChangesAsync`, inside the submit transaction, after SYS returned the round. A rollback test proves that a SYS failure leaves no row.
- Schema safeguard: the CHECK `excess = open AR + other + candidate − limit AND excess > 0` stops a stored row from claiming an override for a within-limit order or with inconsistent figures. The immutable trigger (51031) and the unique round key prevent edits and duplicates.
- Considered, not done: reusing the submit-round override at the SYS final decision. That needs a rule for "exposure unchanged" and the approver's identity, so it is deferred to WU-SD-006b2 with its own ADR.

## WU-SD-006b3 read-only credit check (2026-11-22, ADR-170)

- Review: `SalesOrderCreditCheck.DecideAsync` now calls the new `AssessAsync`, and so does the read-only check. Submit, final decision and preview share one snapshot/commitment query and one evaluation order (ADR-104), so the preview can't disagree with the submit decision.
- Review: the FI snapshot needs the credit key lock, and the first lock inserts `ar_credit_key`. The check runs in its own transaction and rolls back in `finally` (also on 404/422), then clears the change tracker. SD-CREDIT-CHECK-HTTP-003 proves no FI key survives.
- Review: permission and scope are checked before the transaction opens, so a denied or hidden request never takes the FI lock.
- Considered, not done: a lock-free FI snapshot read. It would change the FI contract and allow a snapshot rule that differs from submit, so ADR-170 rejects it.

## WU-SD-006b2 (2026-11-23)

- **Issue:** after 006b2, `SalesOrderCreditCheck.RequireWithinLimitAsync` had no caller left. **Cause:** the final decision now uses `DecideAsync`, which takes the approver's reason. **Change:** the method was removed. **Effect:** one entry point for credit decisions.
- **Issue:** submit and the final decision would each build the evidence row. **Change:** `SalesOrderSubmissionService.Evidence` is now internal and takes the stage. **Effect:** both stages write the same columns.
- **Issue:** the 400 field name was fixed to `creditOverrideReason`. **Change:** `Decide` takes the field name (`overrideReason` for SYS). **Effect:** the client sees the error on the field it sent.
- Considered, not done: reusing the submit override at the final decision. It was rejected in ADR-171 because of segregation of duties and the actor identity.

## WU-SD-006c1 order submit UI (2026-11-23T05:09:30+08:00)

- **Issue:** after a reload with the same version (uncertain outcome) the submit area kept its paused state, because the detail block was reused. **Change:** the panel resets on every new `order` input. **Effect:** Reload always clears the pause (SD-ORDER-SUBMIT-UI-009).
- **Issue (UX P1):** a 5xx showed "cannot confirm the result" and "nothing was saved" together. **Change:** the "nothing saved" line only shows for definite refusals. **Effect:** no contradictory message (UI-011, E2E-002).
- **Issue (UX P3):** credit figures spread across the full width. **Change:** the figure list is limited to 480px.
- Kept as a separate component instead of growing `order-page.ts` (now ~270 lines); the delivery page action pattern (pause until reload) is reused.
- web-design-guidelines: added autocomplete=off to the reason textarea; Title Case rule not applied (ui-conventions uses sentence case).

## WU-SD-006c2 Approval Inbox credit override reason (2026-11-23T05:25:28+08:00, ADR-173)

- **Issue (UX P1):** after a 5xx on the override retry, the warning still said "your approval was not saved" while the outcome was uncertain. **Cause:** the excess warning text came from the earlier 422. **Change:** a `decisionUncertain` flag (5xx, timeout, network) switches to a "could not be confirmed, refresh your tasks first" text. **Effect:** unit and E2E assertions (SYS-APPROVAL-OVR-UI-003, SD-APPROVAL-OVR-E2E-002).
- **Issue (UX P3):** the shared error display had no gap above the decision summary. **Change:** `erp-api-error-display` gets the same 12px bottom margin as `nz-alert` in the inbox.
- **Test capture:** dark screenshots were taken before the dark stylesheet loaded (the dialog still looked light). This was not a product defect. The E2E now uses `emulateMedia` plus a wait for the `erp-theme-dark` stylesheet, because the open modal covers the header theme menu.
- web-design-guidelines: the reason field gets focus when it appears and on a validation error, and has `autocomplete="off"`. Placeholders keep the ui-conventions style (no trailing ellipsis). The reason for a disabled Approve is in the warning text.
- The overridable codes are a map (`OVERRIDABLE_REFUSALS`, code → permission) in `approval.service.ts`, so the dialog has no SD-specific branches.

## WU-SD-007a2 invoice ISSUE service (2026-11-23T09:46:03+08:00)

- **Problem:** SD-INVOICE-ISSUE-005 went red — an issuer of another order in the same company timed out (1222) while the first issuer's transaction was open.
- **Cause (diagnosing-bugs, hypothesis H5 confirmed by stack trace):** the delivery-line lock used one `JOIN OPENJSON` statement under `UPDLOCK, HOLDLOCK, FORCESEEK`; its plan seeked a company-prefixed range and held a key-range lock across other orders' delivery lines. Read-side seeks (balance, prior billing, request-key replay) were not involved.
- **Change:** one parameterised primary-key equality seek per delivery line in ascending ID order (ADR-177 §7).
- **Effect:** SD-INVOICE-ISSUE-005 green; same-line issuers still serialize, other orders proceed. Cost: one round trip per line (≤ 1000).
- Process note: allocation/input unit tests and the SQL tests were written in this round before the first run, but the implementation was drafted before that first run, so there was no separate red run for those tests; the concurrency test was the one observed red.

## WU-SD-007a3a invoice ISSUE command (2026-11-23)

- **Review:** the command adds one FI round trip set per ISSUE; the credit lock is taken once by SD's hook and re-entered by FI (`sp_getapplock` with transaction owner is re-entrant), so no second wait. The derived commitment adds an `OUTER APPLY` seek per Approved order of the customer on `ix_sal_invoice_company_id_order_id_id`; acceptable for one customer's open orders and avoids a stored counter that could drift (ADR-178 §4).
- **No change needed:** replay returns before FI and rolls back the read-only transaction; failure paths clear the change tracker so the scoped DbContext cannot leak the Issuing entity into a later save.
- Process note: SD-INVOICE-POST-001 was proven red by temporarily reverting the commitment query (commitment stayed 3,743 instead of 2,658); the other POST tests and U09/U10 were written with the implementation and passed on first run.

## WU-SD-007a3b1 invoice ISSUE/detail HTTP (2026-11-23T11:29:27+08:00)

- **Found by test (SD-INVOICE-HTTP-002):** the POST body was mapped from the in-memory entity, whose `PostedAt` has 100 ns ticks, while SQL keeps microseconds, so POST and GET differed. Cause: describing before a read-back. Change: `DescribeCommittedAsync` reads the committed row (AsNoTracking) for both new and replayed invoices. Effect: one extra indexed read per ISSUE; the client sees exactly the stored state and version.
- **Review:** the 500 mapping is generic (`InternalErrorException`) instead of an SD-only filter, so the Shared handler stays the single place that writes ProblemDetails; detail is suppressed for internal errors so FI messages never leak. Error-level logging keeps the stack trace for the trace ID.
- **Test robustness:** the HTTP test order owner is 987654, never a generated test user ID, so Self-scope assertions cannot collide with a user whose ID happens to equal the owner.

## WU-SD-007a3b2 invoice list (2026-11-23T11:47:58+08:00)

- **Review (N+1 / paging):** the scope is a join on the source order before `COUNT`, so the total never includes hidden invoices; `lineCount` is a correlated count inside the paged SELECT (one round trip for the page, ≤ 200 rows), not a per-row query. Existing indexes (`company, status, business_date, id` and `company, customer_id, business_date, id`; lines `company, invoice_id, line_no`) cover the default sort, the customer filter and the line count; no migration needed.
- **Review (duplication):** the list mirrors `SalesDeliveryQueryService.ListAsync` (same validation, sort switch and tie-break). Extracting a shared helper for two anonymous-type queries would need expression plumbing that costs more clarity than the ~20 duplicated lines save; left as is.
- **Process note:** the implementation was written before SD-INVOICE-HTTP-005/006 in the same round; mutation check — removing the scope join and flipping the descending tie-break — turned both tests red, restoring the code turned them green.

## WU-SD-007b0 invoice source reads (2026-11-23T12:20:29+08:00)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| First run: both new endpoints 500 (`could not be translated`) | The shared open-line query projected into a positional record, and EF cannot filter (`OrderId == …`) or sort after a constructor projection | `OpenLine` became a member-initialised private class | SD-INVOICE-HTTP-007/008 green; one query shape serves list count, `openLineCount` and the line grid |
| Review: index support | Open-line predicate joins delivery (by order + status), delivery line (by delivery) and sums invoice lines (by delivery line) | Checked existing indexes `sal_delivery(company_id, order_id, status)`, `sal_delivery_line(company_id, delivery_id, line_no)`, `sal_invoice_line(company_id, delivery_line_id, invoice_id)` | Every step is a seek; no migration needed |
| Review: scope parity with ISSUE | A form that offers a source POST would hide gives a confusing 404 after the user fills the form | Both reads require `SAL.INVOICE.ISSUE` and apply its scope to the order, returning ISSUE's own codes (404 SOURCE_NOT_FOUND, 422 ORDER_NOT_APPROVED) | Mutation (scope removed) turned HTTP-007 red; no new i18n keys |

## WU-SD-007b2 invoice issue form (2026-11-23T14:25:34+08:00)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| First run: after an unconfirmed attempt, a coded FI 500 left the warning shown and the form locked | The definite-refusal path renewed the request key but never cleared `uncertain` | `refused()` clears `uncertain` before handling a definite refusal | SD-INV-ISSUE-UI-005 green; the user can correct and retry with a new key |
| web-design-guidelines: the `[role=alert]` error colour also painted the warning alert ("result not confirmed") red | Selector too broad (nz-alert carries `role=alert`) | Narrowed to `small, p[role=alert]` | Warning keeps the alert theme colours in light and dark |
| web-design-guidelines (007b1 finding): opening a list detail left focus on the row button | No focus move after render | 2.14 focuses the detail title (`tabindex=-1`) via `afterNextRender`; also used by `?open=<id>` | SD-INV-ISSUE-UI-014 checks focus |
| Review: duplicate issuing after an unknown outcome | Timeout/network/uncoded 5xx may hide a committed invoice (the POST reads back after commit) | Form locks; "Resend safely" repeats the exact body and key (server replays 200); only coded 4xx/5xx refusals renew the key | SD-INV-ISSUE-UI-004/005 |
| Review: precision | Quantities up to 13+6 digits exceed a double | Quantities stay text end to end; POST body built with exact number literals; non-decimal text throws before sending | SD-INV-ISSUE-UI-002/015 |
| Review: open lines (≤ 1000 rows) rendered without virtualisation | Plain table | Accepted (P3): orders rarely have hundreds of open outbound lines; revisit if UX QA (007b3) shows lag | — |

## WU-SD-007b3a invoice Playwright with mocked API (2026-11-23T14:38:49+08:00)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| SD-INV-E2E-004: after 422 BALANCE_EXCEEDED the refused quantity was not focused (focus stayed on the submit button) | `focus()` was queued as a microtask right after the reply; the input was still disabled because ngModel applies `[disabled]` in a microtask after the next render | `afterNextRender` then `queueMicrotask` before focusing (also covers DATE_BEFORE_DELIVERY → date) | E2E green; keyboard users land on the field to fix |
| Same `queueMicrotask(... focus())` pattern exists in `order-create-page`, `delivery-create-page`, `delivery-edit-page` | Shared idiom | Not changed in this unit; backlog P3 WU-SD-FOCUS-AFTER-REFUSAL to check each with a browser test | — |
| E2E expected `?open=5` to remain in the URL | `?open` is one-shot by design (ADR-181/182) | Test asserts the opened, titled detail and a URL without `open` | — |

## WU-SD-007b3b invoice real-API E2E (2026-11-23, ADR-183)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Issue form 2.15 prefilled the quantity with `6.000000` while the balance column showed `6` (SD-INV-REAL-003) | The form copied the server's decimal text verbatim; the mocked E2E used `10.000001`, so trailing zeros never appeared | New shared `trimDecimalText` (no grouping, no floating point); `draftFromSource` / `mergeDraft` use it; unit tests in `decimal-text.spec.ts` and SD-INV-ISSUE-UI-013 | Input reads like the balance column; submitted text still exact |
| `delivery-draft-form.ts` trims the same way with its own inline regex and a local `trimDecimal` | Written before the shared helper | Not changed (scope); can switch to `trimDecimalText` later | — |
| sqlcmd read-back failed on `line_no AS lineNo` | `LINENO` is a T-SQL reserved keyword | Alias bracketed `[lineNo]` | Real spec reads invoice lines |

## 2026-11-23T15:11:11+08:00 — WU-SD-007b3c invoice UX review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| 2.15 line headers and dates wrapped per character with long item names (real data). | Table min-width 980px leaves the item column to take free width; short headers had no nowrap. | `.lines th, td.nowrap, td.number { white-space: nowrap }` on headers, outbound number, date and numbers. | Item name wraps instead; SD-INV-E2E-007 guards it. |
| Locked 2.15 inputs looked editable. | Page input style set background/colour for all states. | `input:disabled` opacity .55 and not-allowed cursor. | Uncertain state visibly read-only; E2E-005 asserts opacity. |
| 2.14 date validation not styled as an error. | `p[role=alert]` had no colour; inputs had no aria-invalid. | Error colour and aria-invalid + error border. | E2E-002 asserts both. |

## WU-SD-008a1 return schema review (2026-11-23T15:38:25+08:00, ADR-184)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Posting in one EF SaveChanges (header Posted + line inbound stamp) is refused by 51035/51036. | EF orders a single SaveChanges by table name, so `sal_return` is updated before `sal_return_line`; the header check then sees lines without inbound and the line trigger sees a Posted header. | Fixed the posting order as a contract (ADR-184 §3): stamp lines while Pending, then set Posted; test helper `MarkPostedAsync` uses two saves. | 008a5 knows the required order; the database still refuses any incomplete Posted return. |
| `state.json` listed 51034 as the next THROW number, but FI `PostingServiceSupport` already raises 51034 at runtime. | The counter tracked migrations only, not runtime SQL in services. | Used 51035～51037; next is 51038 (ADR-184 §7). | No ambiguous SQL error numbers between FI lock refusal and SD return triggers. |
| Test seeding a second source in the same company collided on `sal_delivery_line.inv_movement_id` / `sal_invoice.fi_sales_source_id` uniqueness. | Seed used `companyId * 10` / `companyId` as scalar IDs. | Seed takes an outbound movement ID and reuses it as the FI source ID. | Tests can seed several sources per company; product code unchanged. |

## WU-SD-008a2a Draft return creation review (2026-11-23T19:11:35+08:00, ADR-185)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Order-line consumption query failed with SQL Server error 130 once a Posted return with billed portions existed. | `SUM(rl.x - ISNULL(a.x,0))` over an `OUTER APPLY` that itself aggregates allocations; SQL Server forbids an aggregate over an expression containing an aggregate/subquery. | Pre-aggregate allocations per return line in a derived table and `LEFT JOIN` it (same fix in the test's verification query). | Unbilled snapshots after a Posted return compute correctly (SD-RETURN-DRAFT-002 green). |
| Tests seeding a second delivery in the same company collided on `ix_sal_delivery_line_company_id_inv_movement_id`. | Seed used fixed outbound movement IDs 501/502 per delivery. | Movement base = 501 + 10 × existing deliveries of the company (first delivery keeps 501/502 for the assertions). | Sibling-order and lock tests seed independently; product code unchanged. |
| Number-sequence assertion was an obfuscated always-true pattern. | Leftover `CurrentValue - 0 is var n ? n : 0` compared to 11. | Assert the sequence equals the 6 saved Drafts. | Proves refused requests consume no number. |

## WU-SD-008a2b1 Draft return edit/delete review (2026-11-23T19:30:20+08:00, ADR-186)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Edit needed the whole creation check/lock/snapshot path; copying it would let create and edit drift. | All logic lived as private members of `SalesReturnCreateService`. | Moved source resolution, line locks, eligibility/balance checks, snapshot, totals and the MD guard into `SalesReturnDraftBuilder`; create and edit both call it. | One implementation; SD-RETURN-DRAFT-001～006 unchanged and green after the move. |
| Posting an unbilled-only return in the test helper threw "no store type mapping for DBNull". | `ExecuteSqlRawAsync` cannot map a `DBNull.Value` argument. | Helper passes `0` and the SQL uses `NULLIF({0}, 0)`. | Tests can post returns without an FI credit; product code unaffected. |
| Replay and edit both had to order lines and allocations for output. | Sorting code was inline in the replay path. | Shared `SalesReturnCreateService.SortForOutput`. | Edit and replay responses list lines/allocations identically. |
| Return detail needed invoice numbers only for billed lines; an unbilled-only return would still query `sal_invoice`. | Lookup always ran over the allocation IDs. | Skip the invoice query when the return has no allocation; fixed number of lookups (order, order lines, deliveries, invoices) independent of line count. | No N+1; one query fewer for unbilled-only returns. (WU-SD-008a2b2) |
| First list test failed with "commercial header is immutable" when renaming an Approved order's customer. | SQL trigger freezes the approved order's snapshot; the test mutated it after approval. | Seed helper takes the customer name before approval. | Test follows the product invariant instead of bypassing it. (WU-SD-008a2b2) |

## WU-SD-008a4 FI return credit source review (2026-11-23T20:51:02+08:00, ADR-189)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Backlog asked for a new FI `PostSalesReturnCreditAsync`, but FI already shipped the credit service (WU-FI-002a3b). | Backlog text predates FI 002a3b. | Implemented only the missing SD adapter of FI's port; no duplicate FI entry point. | One FI credit path; FI tests unchanged. |
| Reporting the `sal_return` header totals to FI would credit unbilled amounts too. | Header/lines include billed + unbilled. | Snapshot header = sums of allocations; lines = allocations with invoice-line caps from one joined query. | FI header/line reconciliation holds; one query for all lines (no N+1). |
| Soft-deleted Drafts would vanish behind the soft-delete filter and look like "not found". | Global soft-delete query filter. | Header read ignores filters with explicit company/ID predicates and maps to `Deleted`. | FI answers 422 not-postable instead of 404 for a deleted return. |

## WU-SD-008a5a return SUBMIT/posting review (2026-11-23T21:14:47+08:00, ADR-190)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A Draft's amounts could be stale at submission (another return or invoice posted meanwhile) and Pending lines cannot be corrected. | Drafts reserve nothing (BR-SD-044); trigger 51036 freezes Pending lines. | `RecheckAsync` rebuilds the snapshot from the saved lines under the locks and refuses a difference with 409 `SNAPSHOT_STALE`. | No credit or release of amounts the user never saw; caps stay exact. |
| A string comparison of the snapshot would flag `1.500000` vs `1.5` as different. | SQL decimal scale differs from computed decimals. | Compare value tuples (decimal equality by value). | No false stale refusals. |
| The commitment SQL was inline in `AssessAsync`, so the return release could only be tested through a full FI credit-limit setup. | Query embedded in the assessment. | Extracted `CommitmentsAsync` (same SQL plus the Posted-return unbilled release); tests call the production query. | Release proven directly; red check without the release term failed (1085 vs 542). |
| An INV result could cover other lines or be a replay. | INV is idempotent per (return, line). | Require non-replay, one movement per saved (line no, original movement), positive IDs; else 409 `INV_POSTING_CONFLICT`. | Never stamps foreign movements or counts an old inbound as new. |

## WU-SD-008a5b return SYS decision review (2026-11-23T21:34:07+08:00, ADR-191)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Constructor-injecting posting, MD builder or FI credit into the handler risks the SYS workflow DI cycle. | SYS enumerates handlers when resolved; INV/MD/FI depend on SYS through MD (ADR-142). | Handler takes `Func<>` factories, resolved only on Approved. | Workflow resolution stays cheap; rejection never builds INV/FI. |
| Revising a Rejected return in one save could hit trigger 51036 (Rejected lines frozen) depending on EF command order. | EF does not guarantee header update before line deletes. | Save Rejected → Draft first, then replace lines. | Deterministic; version +2 documented. |
| Approver may have no SD order scope. | SUBMIT reads the order through the SD action scope. | Handler reads the order unscoped (SYS authority already checked the approver). | Approval never 404s for valid approvers. |
| Full suite caught SD-RETURN-EDIT-003 still expecting edit of a Rejected return to be 422. | Rule changed by ADR-191. | Test now asserts Pending/Posted edit+delete and Rejected delete refused; Rejected edit covered by SD-RETURN-APPROVE-002 (verified 2026-11-24T00:16:42+08:00). | Backend 3901/3901. |

## WU-SD-008a6 return HTTP review (2026-11-24, ADR-192)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| CREATE 201 body showed `33.33` while replay and GET showed `33.3300`, so clients comparing responses saw different JSON. | The 201 body was mapped from the in-memory entity; replay/GET read SQL `decimal(19,4)`. | `SalesReturnQueryService.DescribeCommittedAsync` reads the committed return back (as invoice ISSUE does). | SD-RETURN-HTTP-002 asserts 201, replay and GET bodies are identical. |
| Roles could not be granted return edit/delete/submit. | Catalogue only had VIEW/CREATE; service tests mocked authorization. | Added `SAL.RETURN.EDIT/DELETE/SUBMIT` to `SalesPermissions`. | Real-role HTTP tests exercise every action. |
| PUT without `draft` would throw a null reference (500). | Body record allows a missing nested object. | Controller validates a missing draft as an empty one. | 400 field errors instead of 500. |

## WU-SD-008b1 return source read review (2026-11-24, ADR-193)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| First test draft expected a fully credited invoice line to stay in `invoiceLines` with remaining 0. | Ambiguous contract: creditable vs all invoice lines. | Kept the service rule (only `remainingQuantity` > 0, like the invoice source read) and fixed the test; ADR-193 §5. | Form never offers an invoice line CREATE would refuse with BILLED_EXCEEDED. |
| Test tried to move an Approved order's date to vary sorting; trigger refused (commercial header is immutable). | Approved orders are frozen by design. | Sort assertions rely on the ID tie-break with equal dates. | No test-only bypass of a product trigger. |
| Tracked lines would vanish silently, leaving users unsure why a delivered line cannot be returned. | CREATE refuses lot/serial returns (ADR-184 §6). | Detail keeps them with `trackingUnsupported`; list does not count them. | 008b3 can show a disabled reason (MASTER_PROMPT 20.1). |
| `unbilledRemainingQuantity` could go negative on inconsistent history. | Derived difference of three sums. | Clamped at 0 (CREATE reports the same 0 via `remaining`). | Form never shows a negative balance. |


## WU-SD-008b2 (2026-11-24T01:32:20+08:00)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Return detail could only show destination warehouse/location IDs. | `SalesReturnQueryService` had no MD label lookup (delivery detail has had one since ADR-153). | Inject `IInventorySelectionOptions`; optional label fields on `SalesReturnLineDetail`. | Users see "WH1 · Main", "(inactive)" or "Deleted warehouse (ID n)" instead of a bare number. |
| First `?open` test reused `open=5` for a second component. | The ActivatedRoute test factory is evaluated once per TestBed. | Split into SD-RETURN-UI-015 / 015b. | No product change. |
| web-design-guidelines: long remark or customer names in the facts list could overflow. | `dd` had no wrap rule. | `overflow-wrap:anywhere` with `pre-wrap`. | Long text wraps. Other checks pass (labels, focus-visible, select colours, tabular numbers, URL state, loading "…"); dates use saved ISO text per ui-conventions instead of Intl; long line lists are not virtualized (same P3 as 2.14). |

## WU-SD-008b3a Sales Return Create Interface 2.17 (2026-11-24T02:01:56+08:00)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| The create form could not name the default destination. | Source read (ADR-193) returned only warehouse/location IDs. | Same MD label lookup as the return detail on `SalesReturnSourceQueryService`; optional DTO fields. | Line shows "WH51 · Main / L9 · Bin (inactive)" and warns that saving will be refused. |
| Decimal sum helper duplicated risk. | `sumDecimalText` was private to `delivery-draft-form.ts`. | Exported and reused by `return-form.ts` (`lineTotal`). | One exact BigInt adder; no binary rounding in the line total. |
| Route test broke the following spec file in the same worker. | `afterEach` injected `HttpTestingController` without a configured module, instantiating TestBed. | Moved SD-RETURN-CREATE-UI-012 to its own describe. | Service specs no longer fail with "test module already instantiated". |
| web-design-guidelines: destination warning used `role="alert"` on first render; remark lacked `autocomplete`. | Static warning is not an async update. | Plain warning text; `autocomplete="off"` on the remark. | Screen readers are not interrupted on load. Other checks pass (labels, focus-visible, first-error focus, tabular numbers, loading "…", leave warning); sentence-case labels follow ui-conventions. |

## WU-SD-008b3b1 return detail submit / delete (2026-11-24T05:12:52+08:00, ADR-195)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A coded rollback 5xx (FI_POSTING_FAILED etc.) would have been shown as an uncertain outcome. | The delivery pattern treats every 5xx as unknown. | `ROLLED_BACK_CODES` 5xx are definite refusals with trace ID and "still Draft". | Users are not told to refresh-and-check when the server has confirmed nothing was posted. |
| SNAPSHOT_STALE is a 409 but not a version conflict. | Generic 409 handling pauses actions. | 409 SNAPSHOT_STALE shows its own reason and keeps actions available. | The user knows to edit and resubmit instead of being told someone else changed it. |
| A 422 lineNo for a line not on the saved return could mark nothing yet replace the reason. | Line number taken without checking the saved lines. | `shownLine()` only marks a saved line; otherwise the named reason is shown. | No silent loss of the refusal text. |
| web-design-guidelines: "We could not confirm…" used first person. | Copied from the delivery wording. | English text rephrased to "This page could not confirm the result…". | Other checks pass (native buttons, danger confirmation, spinner, role=status/alert live regions, focus-visible, focus moved to Refresh when actions pause). |

## WU-SD-008b3b2a return edit page (2026-11-24T05:23:17+08:00, ADR-196)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Resending the same PUT after an unknown outcome would answer a misleading 409 once the first attempt had saved. | PUT carries the loaded version, unlike the idempotent POST. | Edit mode replaces "Resend" with "Check saved return", which compares the re-read return with the sent content and version. | The user learns saved / not saved / changed elsewhere instead of a false conflict. |
| A saved return whose balance was consumed by a Posted return would silently lose quantities. | Source lines only list open balances. | `draftFromSaved` reports dropped lines / credits; the page lists them before saving and disables the "no change" shortcut. | Removal is visible before it happens. |
| web-design-guidelines: identity line marked translatable "Version" as `translate="no"`. | Whole paragraph flagged. | Only the document number is `translate="no"`. | Other checks pass (link for navigation, loading text ends with …, leave warning, inline errors, focus-visible). |
| The identity line rendered "SR001 ·Version 1". | Angular template whitespace collapsing removed the space between "·" and the interpolation (SD-RETURN-EDIT-UI-001 caught it). | The separator is part of one interpolated string. | Correct spacing in all three languages. |

## WU-SD-008b3b2b return destination options (2026-11-24, ADR-197)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| The union-of-scopes check for "CREATE or EDIT" would be copied a third time (delivery options, tracking options, returns). | Each service resolved `ICurrentAuthorization` itself. | `SalesActionAccess.RequireAnyScopeAsync(permissions)` returns every held scope or 403; the return source read uses it. | One place decides the 403 and the union; delivery services can move to it later. |
| The edit page labelled a saved destination that differs from the delivery's with the delivery default. | The destination cell read only the source line. | A per-line `places` map is filled from the previous label, the saved line or the source line, whichever has the line's IDs; picks overwrite it. | Saved, default and picked destinations show their own code / name / inactive state. |
| `unavailable` name clash with the edit-page signal broke the build. | Same member name on the component. | Method renamed `placeUnavailable`. | Build and 80 return specs pass. |
| web-design-guidelines on the destination cell. | — | No change: real buttons, aria-labels naming the delivery line, existing focus-visible outline; "Clear location" is reversible by re-picking. | Pass. |

## WU-SD-008b3c1 sales return mocked Playwright (2026-11-24, ADR-198)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| First run asserted `?open=71` after save, but the list drops the parameter once the detail opens. | Test assumption, not a product bug (the list rewrites the URL with `replaceUrl`). | The spec asserts the opened detail title and the bare `/sal/returns` address. | 3 / 3 pass. |
| A refused destination (`MD.WAREHOUSE.INACTIVE`) shows its line message under the quantity split, while focus goes to the warehouse picker in another column. | `line-error` is rendered in the split cell for every line refusal. | No change here; recorded for the WU-SD-008b3c2 myerp-ux-review. | Behaviour documented by SD-RETURN-E2E-003 screenshots. |
| Spec repeats the option routes in each test. | Three flows share the same source / warehouse reads. | One `answerOptions` helper and a `detail()` builder that echoes the request body. | Shorter tests; receipt matching exercised with real echoes. |

## WU-SD-008b3c2a Sales return real-API Playwright (2026-11-24, ADR-199)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| The 008b3c1 mock answered a zero-step SUBMIT with version + 1; the real API answers + 2. | SUBMIT saves Pending (approval instance) and then Posted in the same request, each bumping the version (SD-RETURN-POST-001 already asserted 3). | Mock `postedFrom` now adds 2; the three-language notices expect version 3; test-plan row corrected. | Mocked and real flows agree; the notice the user sees in tests is the one the server produces. |
| First real run failed reading credit allocations with `credit_source_id = undefined`. | The FI source helper did not select `id`. | Allocation query keyed by the return's saved `fi_credit_note_id`. | 3/3 real. |
| Real spec needs a replay body identical to what the page sent. | The fingerprint covers the normalised request; re-typing the body risks a false 409. | The spec captures the 2.17 POST body from the page request and resends it. | Replay asserts the true idempotency contract of the screen. |

## WU-SD-008b3c2b Sales return UX review (2026-11-24)

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Destination refusal shown two columns away from the focused picker; stale after re-pick. | One `line-error` slot in the split cell for every line refusal. | `lineError.place` ('warehouse' / 'location' / null) chooses the cell; pickers get `aria-describedby`; `setPlace` clears a destination refusal for that line. | Reason, focus and fix sit together; quantity refusals unchanged (SD-RETURN-CREATE-UI-016). |
| Return qty column hidden behind horizontal scroll at 1280 px. | `table.lines` min-width 1080 px exceeded the content width (~1,012 px with the side menu open). | min-width 960 px. | All eight columns fit; SD-RETURN-E2E-005 guards against overflow. |
| Failed-row highlight invisible. | `--erp-color-error-bg` is not a defined token. | Inset error-colour bar on the first cell using `--erp-color-error-text`. | Refused line visibly marked in light and dark. |

## WU-SD-PRICE-REAL-CREDIT422 Pricing real-API spec vs. credit preview 422 (2026-11-24)

Diagnosis: reproduced from the backlog note and code reading. After save/convert, the 2.5 receipt mounts `erp-sales-order-credit-panel`, which calls `GET /api/v1/sal/credit-checks/{customerId}?orderId=…`. The spec's fresh customer deliberately has no FI credit limit, so the API correctly answers 422 `SAL.CREDIT.LIMIT_NOT_SET` (documented in the controller remarks) and the panel shows the translated refusal plus "訂單本身已保存". The product is right; Chromium logs every 4xx resource load as a console error, which the spec's "no console error" guard counted. SD-PRICE-REAL-004 was never reached in the full run because the serial describe stopped at 003.

| Problem | Cause | Change | Effect |
|---|---|---|---|
| SD-PRICE-REAL-003 (and 004) fail in afterEach with one console error. | Browser network log for the expected 422 credit preview of a customer without a credit limit. | Spec tolerates only a "status of 422" console error whose location URL is `/sal/credit-checks/{this customer}?`; a response listener collects every such 422 body and afterEach requires `errorCode` = `SAL.CREDIT.LIMIT_NOT_SET`; 003/004 assert the panel message, the saved-order hint and no over-limit alert. | 4/4 pass; the no-limit state on 2.5 is now asserted against the real API instead of being hidden. Creating a credit limit was rejected: it would remove the only real-API coverage of this refusal and add an FI dependency to a pricing spec. |
