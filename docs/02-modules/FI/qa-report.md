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
| 4 | P3 | Journals posted by the seed show 「過帳者（用戶 ID） 0」: user 0 is the system, but the screen shows a bare 0 | Backlog WU-FI-004c4c |
| 5 | Note | Real-API specs together exceed the login limit (5 per IP per minute); they now share one login per user and wait for `Retry-After` | Fixed in WU-FI-004c4b (`support/real-api.ts`, one worker) |
