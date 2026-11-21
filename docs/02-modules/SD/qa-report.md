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
