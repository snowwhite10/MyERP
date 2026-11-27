# FI QA report

Module QA results (MASTER_PROMPT section 20.4). Sections are added per sub-area; the module-level QA round (WU-FI-QA) adds the remaining screens before the FI tag.

## GL: Accounting Period View 6.20, Maintenance 6.21, Posted Journal View 6.22 (2026-11-13T23:28:55+08:00)

### Scope and method

| Item | Detail |
|---|---|
| Work units | WU-FI-004c1 (6.20), WU-FI-004c2 (6.21), WU-FI-004c3 (6.22), WU-FI-004c4a (demo seed), WU-FI-004c4b (real API) |
| Roles | finance (full GL rights), viewer (read only), a close-only role and a user without GL rights (mocked authorization) |
| Real data | Development seed (ADR-105): TW01 and HK01 each have 12 periods of the current year (Jan–Sep Closed) and monthly Posted demo journals; HK01 has the precision journal 123,456,789,012,345.67 HKD |
| Checks | Screen values against SQL Server read-back (`real-api-finance-gl.spec.ts`, FI-GL-REAL-001–006); states, three languages and light/dark with mocked API (`finance-gl-period.spec.ts`, `finance-gl-journal.spec.ts`) |
| Guards | Console errors, 5xx responses, unhandled rejections and untranslated i18n keys fail the test |

### Screenshots

All under `tests/qa/FI/screenshots/`.

| Screen | States | Files |
|---|---|---|
| 6.20 | normal (zh-Hant/en/zh-Hans × light/dark), empty, no results, invalid window, server error (traceId), network error, no permission | `6.20-*.png` (14) |
| 6.20 real | saved TW01 periods; viewer read only | `6.20-real-list-light-zh-Hant.png`, `6.20-real-viewer-light-zh-Hant.png` |
| 6.21 | validation, created, unchanged warning, close confirmation, deleted, edit (en), create (zh-Hans), overlap, excludes Posted, contains Posted, stale version (light/dark), close-only | `6.21-*.png` (13) |
| 6.21 real | created, overlap 409, deleted, contains Posted 422 | `6.21-real-*.png` (4) |
| 6.22 | normal and detail in three languages × light/dark, empty, no results, invalid filter, server error, network error, detail 404/500 | `6.22-*.png` (19) |
| 6.22 real | TW01 detail; HK01 precision detail | `6.22-real-tw01-detail-light-zh-Hant.png`, `6.22-real-hk01-precision-light-zh-Hant.png` |

### Checklist

| Check | Result |
|---|---|
| Success notice only when data changed; unchanged save uses the warning style | PASS (mocked and real: SQL version unchanged after the warning) |
| Rejections name the period and the reason (overlap, Posted dates, contains Posted, stale version, not found) | PASS (real 409/422 checked with SQL unchanged) |
| Amounts show every saved digit, grouped, without floating-point loss | PASS (real 123,456,789,012,345.67 HKD equals SQL) |
| Saved snapshots (account code/name, account and mapping versions, source key) shown as saved | PASS (real, per line) |
| Company isolation: switching company re-reads; another company's journal is not listed | PASS |
| Buttons follow permissions: viewer and close-only roles see no write control they cannot use | PASS |
| Empty, filtered-empty, validation, 5xx with traceId, network loss each have their own message | PASS (mocked) |
| Three languages complete, light/dark without layout faults | PASS (screenshots reviewed) |

### Findings

| # | Severity | Finding | Handling |
|---|---|---|---|
| 1 | P2 | Errors did not say which period they referred to | Fixed in WU-FI-004c2 (`fi-gl-period-action-target`) |
| 2 | P3 | Retry button width; dates wrapping at the hyphen | Fixed in WU-FI-004c1 and WU-FI-004c3 |
| 3 | P3 | Journal number is not a link that supports Ctrl+click; placeholder ellipsis style differs | Backlog WU-FI-004c3a |
| 4 | P3 | Journals posted by the seed show 「過帳者（用戶 ID） 0」: user 0 is the system, but the screen shows a bare 0 | Fixed 2026-11-14T06:17:24+08:00 WU-FI-004c4c: user 0 shows 「系統」/System/系统; screenshots 6.22-detail-system-{light,dark}-zh-Hant, 6.22-real-tw01-detail-light-zh-Hant |
| 5 | Note | Real-API specs together exceed the login limit (5 per IP per minute); they now share one login per user and wait for `Retry-After` | Fixed in WU-FI-004c4b (`support/real-api.ts`, one worker) |

## AR: Sales Collection View 6.26, bank reference column (2026-11-15T07:25:29+08:00, WU-FI-005c1-REFCOL)

- **Finding (P2, from real-API screenshot `6.26-real-posted-light-zh-Hant.png`, WU-FI-005c3b1).** The 「銀行參考號」 header and a real-length reference (`E2E-69806713969-A`) were stacked one character per line. Cause: the cell used `overflow-wrap:anywhere`, which lets the min-content width of an auto-layout table column shrink to one character, so the table gave the width to other columns. Mock data (`TT-001`) was too short to show it.
- **Fix.** Header and list cells get `min-width:10em` and `overflow-wrap:break-word` (long references still wrap, at the hyphen first; no change to the detail panel). Amount cells were left as they were: making them `nowrap` overflowed the page with 15-digit amounts in English (caught by the existing no-overflow guard), so their wrapping stays as reviewed in 005c1.
- **Guard.** FI-COLL-UI-101 now uses a real-length reference and checks in zh-Hant light and en dark that the header and cell are at least 120 px wide and their text takes at most two lines (red before the fix: header 103 px).
- **Screenshots reviewed.** `tests/qa/FI/screenshots/6.26-normal-{light,dark}-{zh-Hant,en,zh-Hans}.png`: header on one line in all three languages, the reference on at most two lines, no horizontal overflow. No P1/P2 left on this column.
- **Other findings (P3, not fixed here).** In English with 15-digit test amounts the amount cells still break inside the number and `1,200.5` / `TWD` sit on two lines (`6.26-posted-dark-en.png`); fund account names wrap mid-word. Listed as backlog WU-FI-005c1-AMTWRAP. web-design-guidelines: the change passes (long user content handled, no new horizontal scroll); the non-breaking space between amount and currency is part of AMTWRAP.
## WU-FI-002c2 single-page UX review (2026-11-26T01:44:03+08:00)

Interface 6.31: inspected 24 screenshots `tests/qa/FI/screenshots/6.31-*` covering normal (three languages, both themes), empty, no results, invalid ID, missing source, 500 with trace ID, network error, timeout, no permission, and real SQL/API invoice/return detail (both themes). Screen messages match the mock API state, and there is no success notice for a read. Dark controls, exact amounts and allocation links are readable. The initial code review found one P1 stale-detail risk on company/filter switch and one P2 numeric-key rounding risk; both fixed and covered in unit behavior. Screenshot set has no remaining P1/P2 finding. Permission/real SQL checks passed in FI-SRC-UI-005/FI-SRC-REAL-001 (2026-11-26T01:50:32+08:00).

P3 follow-up `WU-FI-SRC-URL`: filters, pagination and opened detail are not yet in the URL, so a shared link opens the default list. The current page remains read-only and shows correct committed data; this does not block interface 6.31 acceptance.

## AP: Other Payment 6.10 / 6.11 / 6.12 / 6.32 UX QA (2026-11-28T00:54:18+08:00, WU-FI-008c4)

### Scope
Four interfaces (list 6.10, create 6.11, edit/detail 6.12, GL expense account map 6.32). States: normal, empty, filtered empty, invalid date range, validation, duplicate reference 409, version 409, mapping 422, submit 500 with traceId, submitted (Pending), approved and posted (zero-step), withdrawn, deleted, map 500 and unchanged; light and dark in zh-Hant, normal/posted in zh-Hans and en. Real API/SQL screens from 008c3 (`real-api-finance-other-payment.spec.ts`, rerun 6/6). Screenshots: `tests/qa/FI/screenshots/6.10-*`, `6.11-*`, `6.12-*`, `6.32-*` (new this round: `6.12-{duplicate-reference,stale-version,submit-server-error,submitted-pending,withdrawn,submitted-posted,deleted}-{light,dark}-zh-Hant.png`, `6.12-submitted-posted-light-{en,zh-Hans}.png`). Every new or re-captured screenshot was opened and reviewed.

### Findings
| # | Screenshot | Check | Problem | Severity | Handling |
|---|---|---|---|---|---|
| 1 | 6.12-submitted-posted-* (real FI-OP-REAL-002) | A5 | Zero-step submit approved and posted in one request, but the notice said only "submitted, check the status below" | P1 | Fixed: notice follows the returned status — "付款 {no} 已核准並過帳，總帳傳票見下方" vs "已送審，正在等待審批，尚未過帳"; FI-OP-UI-018/019, real API REAL-002/003 assert the text |
| 2 | 6.12-deleted-* | A1/A6 | Deleting a draft closed the editor with no confirmation that anything was deleted | P2 | Fixed: list shows "草稿付款 {no} 已刪除，提款參考已釋出" and the refreshed list no longer has the row (FI-OP-UI-022, 108) |
| 3 | 6.11-validation-*, 6.12-duplicate-reference-* | B4 | Field errors were separate grid items, so they landed in the neighbouring column (the reference error appeared under 資金帳戶) | P2 | Fixed: each error sits inside its field container; FI-OP-UI-107/108 assert placement |
| 4 | 6.12-duplicate-reference-* | B4/B5 | Duplicate reference 409 offered "重新載入付款" (would discard the typed values) and did not point at the field | P2 | Fixed: no reload button for input errors; reason under the reference field, field focused and aria-invalid (FI-OP-UI-021) |
| 5 | 6.12-stale-version-* | E2 | "重新載入付款" stretched the full editor width | P3 | Fixed: normal-width button (FI-OP-UI-021, 108 width check) |
| 6 | 6.11-create-* | E2 | Single inputs were stretched to the height of the taller category/fund cell | P3 | Fixed: `align-content:start` on field containers |
| 7 | 6.11-saved-light, 6.12-*-light | E1 | Success alerts in light mode use a muted grey-green background (shared ng-zorro light theme, all modules); text remains readable | P3 | Backlog WU-P0-ALERT-LIGHT |
| 8 | 6.10 | — | Filters, page and sort not in the URL | P3 | Backlog WU-FI-008c5 (existing) |

### Not reviewed
Timeout/network loss on 6.11/6.12 only by unit tests of the shared error display (same component as 6.10, whose network state was captured); partial results and files not applicable (single-document writes).

### Conclusion
P1: 0, P2: 0 open for other payments.

## AP: Other Payment 6.10 URL state review (WU-FI-008c5)

Scope: 6.10 restored from a shared link (keyword, status, dates, page 2/3, 50 per page, amount sort) after reload and Back; 6 screenshots `tests/qa/FI/screenshots/6.10-url-restored-{light,dark}-{zh-Hant,en,zh-Hans}.png`, each opened and checked. Invalid hand-edited links are asserted in FI-OP-UI-103 (no error banner, URL normalised); no new visual state.

| # | Screenshot | Checklist | Finding | Severity | Handling |
|---|---|---|---|---|---|
| 1 | 6.10-url-restored-light-zh-Hant | Layout | At 1280px the pager sat at the right end of the 1300px scrolling table, so the restored page number was off screen. | P2 | Fixed: pager sticks to the left of the scroll area; E2E `toBeInViewport` (red before, green after). |
| 2 | all | Web Interface Guidelines | Document number is a button opening the inline editor; the open payment is not in the URL and cannot be opened in a new tab. | P3 | backlog WU-FI-008c6 |

Conclusion: P1 0, P2 0 open.
