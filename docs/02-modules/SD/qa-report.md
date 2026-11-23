# SD QA report

> This report grows by interface group. The module-level `WU-SD-QA` round (all SD interfaces, all roles) is still required before the SD tag.

## Sales Order View / Create / Edit (2026-11-20, WU-SD-003c5c)

### Scope

| Interface | ui-spec | States reviewed |
|---|---|---|
| Sales Order View Interface (list, detail, CSV export) | 4.4.1.2.4 | normal, empty, filtered empty, invalid date, 404 detail, list/detail timeout, offline, 500, no permission, approval history, export ready / disabled / disabled-filtered / success / empty / mismatch / 400 / 403 / 422 / 500 / timeout / view-only |
| Sales Order Create Interface | 4.4.1.2.5 | normal, validation (client and 400), 409 conflict, 422 rejected / expired / hidden / empty quotation, 500, network, timeout, company picker, create-only receipt, status changed, no permission, success |
| Sales Order Edit Interface | 4.4.1.2.6 | normal, blocked (Pending / Cancelled / converted / delivered), 409 conflict, 400, 422 business / reference item / tax / currency / customer not approved / date changed / status changed, 404 hidden / cross company / edit scope, no view, no permission, read and write network loss, read and write timeout, decimal boundary, incomplete line, empty currency, commercial and picker receipts, success |

Screenshots: 214 files in `tests/qa/SD/screenshots/2.4-*`, `2.5-*`, `2.6-*`. Every state has light and dark versions. zh-Hant covers every state. zh-Hans and en cover normal and success on all three interfaces (light and dark), plus the export and Edit error states.

This round adds a cross-screen data-consistency test, `SD-ORDER-QA-001`. The browser saves a draft, then edits it, against a stateful simulated server. The test then checks that the list row, the detail header, the detail line and the Edit form all show the values the server stored: amount 1,312.50 after the first save and 1,575.00 after the second, plus quantity, unit price, remark and version. It also checks that only one POST and one PUT were sent. It adds 18 screenshots (`2.4-qa-detail-*`, `2.4-normal-dark-{en,zh-Hans}`, `2.5-normal-dark-*`, `2.5-success-*-{en,zh-Hans}`, `2.6-normal-dark-*`, `2.6-success-*-{en,zh-Hans}`). All 18 were inspected.

### Findings

| # | Screenshot | Checklist | Issue | Severity | Handling |
|---|---|---|---|---|---|
| 1 | `2.4-qa-detail-*` | D4 / A5 | The detail header and line amounts were raw numbers (`1312.5`) while the list showed `1,312.50`. Quantity and unit price had no digit grouping. | P2 | Fixed in this unit. The list and detail now use one amount format: grouping and 2–4 decimals. Quantities and prices show exact grouped digits with trailing zeros removed. Asserted by SD-ORDER-QA-001. |
| 2 | `2.4-qa-detail-*-zh-Hans` | D2 | Chinese detail labels use a half-width colon (`保存状态: 草稿`). | P3 | Backlog WU-SD-003c7 |
| 3 | `2.6-normal-*`, `2.6-success-*` | D3 | The Edit form shows the saved tax rate as `5%`, while Create shows `OUT · 5%` for the same rate. | P3 | Backlog WU-SD-003c7 |
| 4 | `2.5-success-light-*`, `2.6-success-light-*` | E1 | The light-mode success banner on the order form is a greyish green. It is readable but paler than other success banners. | P3 | Backlog WU-SD-003c7 |

### Not reviewed and why

- Print / PDF: no shared print foundation exists yet (ADR-141). It is covered by a later work unit.
- Roles: admin, CREATE-only, EDIT-without-VIEW, VIEW-only and no-permission paths were simulated in E2E. A real multi-role walk with seeded accounts belongs to the module-level `WU-SD-QA`.
- Partial success, all skipped and all failed: order Create, Edit and View have no batch operations, so these states do not apply.

### Conclusion

P1: 0, P2: 0 (one P2 found and fixed). Three P3 items are in the backlog.

## Sales Outbound Edit — Pick from Stock (2026-11-21T09:17:35+08:00, WU-SD-004c2b2b2b)

### Scope

Interface 2.9 Lot/Serial lines with "Pick from stock" (REQ_IO_A2.9.11–13) and the 2.8 receipt hint. Screenshots: `tests/qa/SD/screenshots/2.9-stock-light-zh-Hant.png`, `2.9-stock-dark-zh-Hant.png`, `2.9-stock-lot-dialog-{light,dark}-zh-Hant.png`, `2.9-stock-error-light-zh-Hant.png`, `2.9-stock-light-{en,zh-Hans}.png`, `2.9-stock-relocated-light-zh-Hans.png`; existing `2.9-tracking-*` screenshots were refreshed because the button and hint now appear on every tracked line. Three languages were exercised by SD-DEL-ED-035; the E2E guard fails on untranslated keys.

### Checks (myerp-ux-review, web-design-guidelines)

- Result truthfulness (20.1): the pick summary uses success style only when every chosen number was added; duplicates and "line already full" show warning style with counts.
- Disabled button: the reason ("choose a warehouse first", "enter a valid quantity first") is shown as visible text linked by `aria-describedby`.
- Data meaning: the dialog says the list is on-hand stock, not reserved, and rechecked at posting; truncation (> 100) asks for a keyword.
- Errors: 500 shows the message and trace ID inside the dialog; timeout has its own message; 403/404 use shared error codes.
- Dark mode: notice and alerts keep contrast; no overlap in the line card.

### Findings

One P2 found and fixed: reopening the picker and cancelling erased the previous pick result, so the user lost the record of what had been added (found by SD-DEL-ED-035 after the language switch). The result now stays until a new pick is confirmed. Observation (P3, not logged): the full-page dialog screenshot shows the modal over a partly scrolled page; this is a screenshot artefact, not a layout problem.

### Not reviewed and why

- Real-API run: the picker needs seeded lot/serial stock; the endpoint is covered by SD-DEL-HTTP-009 and the module-level `WU-SD-QA` will walk it with real data.

## Sales Outbound View / Create / Edit error and three-language round (2026-11-21, WU-SD-004c3)

### Scope

| Interface | ui-spec | States reviewed in this round |
|---|---|---|
| Sales Outbound View Interface 2.7 (list, detail, detail actions) | 4.4.1.2.7 | list 403 / offline / timeout; detail offline / timeout / 403; submit 403 / 409 / offline / timeout; no delivery permission (route refused); stored-data consistency after create, edit and submit |
| Sales Outbound Create Interface 2.8 | 4.4.1.2.8 | order option 403 / timeout; save 403 / offline / timeout (unconfirmed, locked); success |
| Sales Outbound Edit Interface 2.9 | 4.4.1.2.9 | load 403 / timeout; save 403 / offline / timeout (unconfirmed, needs refresh); success |

Earlier units already covered and captured normal, empty, filtered no-result, invalid date, 404, 422 (line / INV stock / tracking), 409 on edit, and 500 with trace ID (`2.7-*`, `2.8-*`, `2.9-*`). This round adds `tests/e2e/specs/sales-delivery-qa.spec.ts` (SD-DEL-QA-001…007) and 87 screenshots in `tests/qa/SD/screenshots/2.{7,8,9}-qa-*`. The key states (success, detail, submitted, list 403/offline, action 403/offline, order 403, save offline, load 403, save offline) were captured in zh-Hant, zh-Hans and en, each in light and dark. Every capture also asserts that no untranslated `sd.*` or `errors.*` key and no `undefined` appears. The remaining states were captured in zh-Hant (light, plus dark for list and save timeouts).

SD-DEL-QA-001 is the data-consistency test. A stateful simulated server receives the create (5.25 + 2), the edit (line 1 → 6, PUT v1) and the submit (v2). The list row (7.25, then 8), the detail lines, the edit form and the Pending status / version 3 all match what the server stored. Exactly POST, PUT v1 and SUBMIT v2 were sent.

### Findings

| # | Screenshot | Checklist | Issue | Severity | Handling |
|---|---|---|---|---|---|
| 1 | `2.7-qa-forbidden-*` (first run) | 20.1 errors | A server 403 on the 2.7 list (and detail) showed "發生未預期的錯誤，請稍後再試" ("unexpected error, try again later"). This is misleading: retrying cannot help. 2.8/2.9 already said "no permission". | P2 | Fixed in the shared `apiErrorKey`: any 403 whose code the page did not list maps to `errors.COMMON.PERMISSION.DENIED` (ADR-156). Asserted by the apiErrorKey unit test and by E2E SD-DEL-QA-002 (list and detail). Screenshots re-captured. |
| 2 | `2.7-qa-action-offline-*`, `2.7-qa-action-timeout-*` | 20.1 errors | The action outcome for offline/timeout says "could not confirm the result, refresh first", which is correct and not misleading, but it does not say the cause was the network or a timeout. 2.8 and 2.9 name "timeout, network or server error". | P3 | Backlog WU-SD-004c3a |
| 3 | `2.8-qa-save-forbidden-*`, `2.9-qa-save-forbidden-*` | 20.1 result | A save refused with 403 shows "no permission" but not "nothing was saved". The 422 path shows that sentence. | P3 | Backlog WU-SD-004c3a |

No P1. Layout, dark-mode contrast and the three languages showed no overlap, truncation or missing translation.

### Not reviewed and why

- 422 / 500 / 409 on 2.7–2.9 are not repeated here. They were captured and reviewed in WU-SD-004c2a…004c2b2d (`2.7-action-*`, `2.8-line-422`, `2.8-server-error`, `2.9-conflict`, `2.9-line-422`, `2.9-server-error`).
- Read-only user and real API data: the module-level `WU-SD-QA` round walks all SD screens with seeded data and roles.
- Print / export: 2.7 has no export or print in the current scope.

## Sales Price Rule View Interface 2.11 (2026-11-21T19:20:19+08:00, WU-SD-005b1a)

### Scope

`/sal/price-rules` (ui-spec 4.4.1.2.11): normal (zh-Hant/zh-Hans/en light, zh-Hant dark), tablet 768 px (zh-Hans), empty, filtered no-result (dark), validation (dark), list 500 with trace ID, deactivate confirmation, deactivated success, stale 409 (dark). Screenshots: `tests/qa/SD/screenshots/2.11-*.png` (12), produced by `tests/e2e/specs/sales-price-rule-view.spec.ts`.

### Findings

| Severity | Finding | Action |
|---|---|---|
| P2 | Dates and amounts wrapped inside narrow cells ("2026-" / "06-01"), hard to read. | Fixed: `white-space: nowrap` for date/number cells; SD-PRULE-UI-101 asserts a single line box. |
| P3 | Item/customer filter pickers use MD lists, so a pricing user without `MD.ITEM.VIEW` / `MD.CUSTOMER.VIEW` sees a permission error in the picker (the list itself stays readable). | Deferred to WU-SD-005b1b (pricing-scoped options shared with the Create form), ADR-161. |
| P3 | Trace ID label prefix "Trace ID:" is not translated (same as the shared `ApiErrorDisplay`). | Left consistent with existing pages; candidate for the module QA round. |

Checked and fine: success appears only after the server reply; the 409 message says "not deactivated" and the list is reloaded with the newer version; deactivated rows lose the button; empty and no-result text differ; no untranslated keys; dark mode has no contrast or alignment faults; no horizontal overflow at 768 px.

## Pricing error states 2.2 / 2.5 / 2.11 / 2.12 (2026-11-22T01:31:26+08:00, WU-SD-005b3b, ADR-168)

### Scope

| Interface | States reviewed |
|---|---|
| Quotation create 2.2 (edit 2.3 shares the page) | line quote 500 with trace ID, network loss, 409 duplicate active rules, 403, timeout (zh-Hant light/dark, timeout also en and zh-Hans); save 422 number rule not configured (light/dark, en) |
| Manual order create 2.5 (edit 2.6 shares the page) | the same five quote states; save 422 number rule not configured (light/dark, zh-Hans) |
| Price rule list 2.11 | timeout, network, 403 (light/dark, 403 also en) |
| Price rule create 2.12 (edit 2.13 shares the page) | save 403, network (outcome unknown), timeout (outcome unknown) |

Screenshots: 41 files `tests/qa/SD/screenshots/2.2-qa-*`, `2.5-qa-*`, `2.11-qa-*`, `2.12-qa-*`, produced by `tests/e2e/specs/sales-pricing-qa.spec.ts` (SD-PRICE-QA-001～004). Normal, empty, 500 list, overlap 409, stale 409, validation and success states were captured in earlier units (`2.11-*`, `2.12-*`, `2.13-*`, `2.2-pricing-*`, `2.5-pricing-*`).

### Findings

| # | Screenshot | Checklist | Issue | Severity | Handling |
|---|---|---|---|---|---|
| 1 | `2.2-qa-quote-server-*`, `2.5-qa-quote-server-*` (first run) | 20.1 errors | A quote answered with 500 showed only "unexpected error, try again later" on the line, without the server trace ID. | P2 | Fixed: `LinePricing.failed` carries the 5xx trace ID; both forms show `Trace ID: …` after the line message. SD-PRICE-QA-U01 and SD-PRICE-QA-001/002. |
| 2 | `2.2-qa-number-rule-*`, `2.5-qa-number-rule-*` (from 005b3a) | 20.1 errors | 422 `SYS.NUMBER_RULE.NOT_CONFIGURED` was shown as "server rejected customer, item, currency or tax references" (quotation) or "server refused to create the order, check reasons below" (order, with no reasons below). | P2 | Fixed: both saves map the code to the existing three-language text "no number rule for this document type, nothing was saved, ask an administrator". SD-PRICE-QA-001/002 assert the text and one POST, no success. |
| 3 | `2.2-qa-quote-denied-*`, `2.5-qa-quote-denied-*` | D3 | A 403 on the line quote says "You do not have permission for this action" without naming the price lookup. It is inside the line's pricing box, so it is understandable. | P3 | Backlog WU-SD-005b3c |
| 4 | `2.11-qa-no-permission-*` | B3 | The list 403 offers "Retry", which cannot help. | P3 | Backlog WU-SD-005b3c |
| 5 | (005b3a real API) order detail | D5 | Order detail line headers (quantity, delivered) wrap one character per line at 1280 px. | P3 | Backlog WU-SD-005b3c |

Checked and fine: every failed quote clears the rule hint and keeps "look up price again" enabled; timeout and network on 2.12 lock Save with a stated reason and offer "back to list"; no success banner on any failure; dark mode readable; no untranslated keys; en and zh-Hans texts complete.

### Not reviewed and why

- 2.3 / 2.6 / 2.13: same page component and error mappers as 2.2 / 2.5 / 2.12 in edit mode; edit-only states (409 version, blocked status) were captured in WU-SD-002b5, 003c5c and 005b1b2.
- Partial success / all skipped / all failed: no batch operation on these screens.
- Real roles: the module-level `WU-SD-QA` round.

### Conclusion

P1: 0, P2: 0 (two P2 found and fixed). Three P3 items in backlog WU-SD-005b3c.

## Credit override end to end on real data 2.5 / 2.4 / 8.70 (2026-11-23T06:02:27+08:00, WU-SD-006c3b, ADR-175)

### Scope

Real API and SQL Server (no mocks): Sales Order Create 2.5 saved-credit panel (over limit), View 2.4 submit area (reason entered, submitted), Approval Inbox 8.70 (refused approve with reason field, approved). 9 screenshots `tests/qa/SD/screenshots/real-credit-*.png`, zh-Hant, light and dark. Error states, en / zh-Hans and no-permission states were reviewed with mocks in 006c1, 006c2 and 006c3a.

### Findings

| # | Screenshot | Checklist | Finding | Severity | Handling |
|---|---|---|---|---|---|
| 1 | all | 20.1 result matches data | Every figure and message (limit, this order, remaining, excess 180, saved version 2, reason, Pending/Approved) equals the SQL rows asserted in the same test. | OK | SD-CREDIT-REAL-001～003 |
| 2 | (setup) | Permission | Seeded `manager` could not final-approve an over-limit order (no SAL.CREDIT.OVERRIDE; 8.70 disabled approve). | P1 (demo flow impossible) | Fixed: ADR-175 seed grant, SystemSeedTests + SQL precondition in the spec |
| 3 | real-credit-2.4-submit-reason-light | Validation | After typing the reason, the earlier "請輸入覆核信用額度的理由" error and red border stay until the next submit click. | P3 | WU-SD-006c4a |
| 4 | real-credit-8.70-* | Wording / format | Excess "180 TWD" and amount "1,680" without two decimals (SD screens show 180.00); header shows the code "SAL.ORDER". | P3 | WU-SYS-APPROVAL-FMT |
| 5 | real-credit-2.4-submitted-* | Traceability | The saved override is shown only in the submit notice; reopening the order shows no override record. | P3 | WU-SD-006c4b |

Dark mode: all text readable, warning and success alerts keep contrast. No untranslated keys (also asserted by the spec).

### Conclusion

P1: 0 open (one found and fixed), P2: 0. P3 items in backlog.

## Sales Invoice View 2.14 / Issue 2.15 (2026-11-23T15:11:11+08:00, WU-SD-007b3c)

### Scope

Two interfaces. Reviewed 33 mocked screenshots from 007b3a (`tests/qa/SD/screenshots/2.14-*`, `2.15-*`: normal three languages light/dark, detail, empty, no results, invalid dates, missing invoice 404, 500 with trace and retry, 2.15 empty, client validation, 422 BALANCE_EXCEEDED, network failure uncertain/locked, success, ISSUE without VIEW receipt) and 5 real-API screenshots from 007b3b (`real-invoice-*`). Re-captured 2.14-invalid-dates, 2.15-uncertain-* and new `2.15-long-names-light-zh-Hant` after the fixes. web-design-guidelines run on `invoice-page.ts` and `invoice-issue-page.ts`.

### Findings

| # | Screenshot | Checklist | Finding | Severity | Handling |
|---|---|---|---|---|---|
| 1 | real-invoice-2.15-remaining-light-zh-Hant | E2 / E3 | With real-length item codes and names, the 2.15 line headers wrapped one character per line (稅/率, 已過/帳, 已開/票, 未開/票, 訂單/行) and the outbound date broke after the last hyphen (2026-11-/23). | P2 | Fixed: headers, outbound number, date and numeric cells `white-space:nowrap`; SD-INV-E2E-007 (header heights equal, cells single-line, screenshot 2.15-long-names) |
| 2 | 2.15-uncertain-light-zh-Hant | C5 / E1 | After a network failure the form is locked, but the disabled date and quantity inputs looked exactly like editable ones (the page style overrode the disabled look); only the missing date picker icon hinted otherwise. | P2 | Fixed: `input:disabled{opacity:.55;cursor:not-allowed}` (same as price-rule form); SD-INV-E2E-005 asserts opacity < 1 on the locked inputs |
| 3 | 2.14-invalid-dates-dark-zh-Hant | B4 | The refused date range message was body-coloured text and the two date inputs were not marked. | P3 (fixed) | Fixed: message uses `--erp-color-error-text`, both inputs `aria-invalid` with error border; SD-INV-E2E-002 asserts both |
| 4 | real-invoice-2.14-detail-*, 2.14-detail-* | E3 | When currency = base currency (TWD → TWD) the detail lines show two identical gross columns "含稅 (TWD)". | P3 | WU-SD-INV-SAME-CCY |
| 5 | 2.14-detail-404-dark-zh-Hant | C2 | The missing-invoice message offers "重試" although retrying a 404 cannot succeed; "重新載入列表" would match the message. | P3 | WU-SD-INV-SAME-CCY (same slice) |
| 6 | 2.15 lines | Performance | Up to 1,000 lines render without virtualization (web-design-guidelines: > 50 items). | P3 | Existing note in backlog 007b3c; not observed slow at 2 lines |

Checked without findings: success alert and toast only after a real 201/200 (A1); 422 names the line and the remaining 4.5 (A4); 500 shows trace ID and retry (B2); network failure explains the uncertain outcome and offers safe resend with the same key (B3); empty / no results / load failed have distinct texts (C2); three languages complete, no untranslated keys (also guarded by the fixture); dark mode readable; amounts and quantities exact text with grouping (D4).

### Conclusion

P1: 0, P2: 0 open (two found and fixed). P3 items in backlog.

## Sales Return View 2.16 / Create & Edit 2.17 (2026-11-24T06:22:22+08:00, WU-SD-008b3c2b)

### Scope

Two interfaces. Reviewed the 21 mocked flow screenshots from 008b3c1 (`tests/qa/SD/screenshots/2.17-flow-*`: create empty / filled in three languages, draft detail, rejected edit, revised, pending, posted, inactive warehouse 422, uncertain 5xx; light and dark), the 6 real-API screenshots from 008b3c2a (`real-return-*`), and captured the missing states in new SD-RETURN-E2E-004 / 005: 2.16 empty, no results, invalid dates, 500 with trace, normal (zh-Hant light/dark, en, zh-Hans), detail 404 (`2.16-*`, 13 files) and 2.17 client validation (`2.17-flow-validation-lines-*`). web-design-guidelines run on `return-create-page.ts`.

### Findings

| # | Screenshot | Checklist | Finding | Severity | Handling |
|---|---|---|---|---|---|
| 1 | 2.17-flow-inactive-warehouse-light-zh-Hant | B4 / A4 | A refused destination (422 `MD.WAREHOUSE.*` / `MD.STORAGE_LOCATION.*`) put its line message under the quantity split, two columns away from the warehouse picker that received focus. After re-picking, the stale "warehouse inactive" text stayed next to the new warehouse. | P2 | Fixed: the reason renders in the destination cell under the pickers, which reference it via `aria-describedby`; picking another warehouse / location removes it. Quantity refusals stay under the split. SD-RETURN-CREATE-UI-015 / 016, SD-RETURN-E2E-003 (bounding boxes, cleared after re-pick) |
| 2 | 2.17-flow-validation-lines-dark-zh-Hant (before the fix) | E3 / B4 | At 1280 px the line table (min-width 1080 px) scrolled horizontally, so the "Return qty" column, including the "enter a quantity above 0" reason, was cut off at the right edge. | P2 | Fixed: table min-width 960 px; SD-RETURN-E2E-005 asserts no horizontal overflow and the reason fully in view |
| 3 | 2.17-flow-inactive-warehouse-* | A4 | The refused row was meant to be highlighted, but its style used an undefined token (`--erp-color-error-bg`), so nothing showed. | P3 (fixed) | Fixed: a 3 px error-colour bar on the row's first cell (re-captured) |
| 4 | real-return-2.16-posted-light-zh-Hant | E3 | When currency = base currency (TWD → TWD) the detail lines show two identical "含稅 (TWD)" columns (same as 2.14 finding #4). | P3 | WU-SD-RET-UX-P3 |
| 5 | 2.16-detail-404-dark-zh-Hant | C2 | The missing-return message says "reload the list" but the button offers "重試", which cannot succeed for a 404. | P3 | WU-SD-RET-UX-P3 |
| 6 | real-return-2.16-posted-* | D3 | The FI credit note shows only its internal ID ("財務信用票據 ID 10") instead of a document number or link. | P3 | WU-SD-RET-UX-P3 |
| 7 | 2.16-server-error-*, 2.16-detail-404-* | E2 | The retry button below the error stretches the full content width. | P3 | WU-SD-RET-UX-P3 |

Checked without findings: the save toast only after a real 201/200 and a replay says nothing new was created (A1/A5); posting with no approval step says it posted and what changed in stock and receivables (A1/A6); 500 shows the trace ID and retry (B2); an unknown outcome locks the form, explains it and offers a safe resend with the same key (B3); client validation sits beside each field with a count of problems and no request (B4); empty / no results / load failed have distinct texts (C2); Draft / Pending / Rejected / Posted are clearly tagged and Posted offers no edit (C5); three languages complete with no untranslated keys (also guarded by the fixture); dark mode readable (E1); amounts use grouping and two decimals with the currency code (D4).

### Not reviewed and why

- 409 on create: not applicable (a new document has no version); edit 409 was covered by the 008b3b2a unit tests, not screenshotted.
- Partial results / all skipped: not applicable — one document saves all-or-nothing.
- No-permission states: route guards and hidden buttons are covered by unit tests (SD-RETURN-UI-017, CREATE-UI permission cases); not screenshotted.

### Conclusion

P1: 0, P2: 0 open (two found and fixed). P3 items in backlog WU-SD-RET-UX-P3.
