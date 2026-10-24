# BI test plan

## WU-BI-000d financial read scenarios (design; product tests TODO)

The `myerp-test-scenarios` categories are covered below. U=unit, I=real SQL and authorised HTTP, E=Playwright. Every E test must assert translated text, severity, row count and cutoff against a fresh authorised API read of the same FI source. Implementation units write failing product tests first. No executable product test is claimed in this design unit.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| BI-FIN-001 | Normal | Cash book opening, Posted debits/credits and closing equal FI source at one cutoff. | I,E | REQ_F4117.5.1 | TODO |
| BI-FIN-002 | Normal | AP and AR details show Posted principal, committed allocations and exact open amount. | I,E | REQ_F4117.5.2–3 | TODO |
| BI-FIN-003 | Normal | Client and supplier statement pages preserve opening, ordered signed lines and closing. | I,E | REQ_F4117.5.4–5 | TODO |
| BI-FIN-004 | Normal | Other income/expense report distinguishes Posted receipt/payment and excludes AR/AP settlement. | I,E | REQ_F4117.5.6 | TODO |
| BI-FIN-005 | Normal | Frozen Excel export rows, totals, order, cutoff and filters equal the view; no mutation. | I,E | REQ_F4117.5.7 | TODO |
| BI-FIN-006 | Boundary | Empty period retains opening = closing; empty account has zero rows and clear empty state. | U,I,E | REQ_F4117.5.1–5 | TODO |
| BI-FIN-007 | Boundary | Invalid/empty account, partner or dates; 366/367-day and leap/year boundary; maximum page size and decimal overflow. | U,I,E | REQ_F4117.5.1–6 | TODO |
| BI-FIN-008 | Duplicate/conflict | Backdated post during paging/export stays out of frozen set, appears on fresh cutoff. | I,E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-009 | Duplicate/conflict | Tampered, expired or changed-filter token fails; repeated read/export has no duplicate row. | U,I,E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-010 | Partial result | Missing FI source, mixed eligible/invalid source and inconsistent GL totals show named incomplete reason, never a guessed balance. | I,E | REQ_F4117.5.1–6 | TODO |
| BI-FIN-011 | Partial result | All excluded, zero eligible or failed export shows accurate zero/incomplete message and no partial file. | I,E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-012 | Permission | Anonymous 401, missing per-report VIEW or EXPORT 403, missing FI source authority 403. | I,E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-013 | Permission | Forged company, foreign partner/account and SYS out-of-scope record are hidden as 404. | I,E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-014 | State | Draft, Pending, Rejected, rolled-back and unapplied money do not enter Posted accounting totals. | I,E | REQ_F4117.5.1–6 | TODO |
| BI-FIN-015 | Relation | Inactive or soft-deleted partner/account stays in saved historical source, but cannot be used to broaden scope or create a new posting. | I | REQ_F4117.5.1–6 | TODO |
| BI-FIN-016 | Error | 400/409/422/500 with traceId, timeout and network failure show translated specific outcome; retry never silently changes cutoff. | I,E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-017 | File | Empty result exports header plus zero totals; formula-leading text is escaped; oversized/failed export has no partial download. | I,E | REQ_F4117.5.7 | TODO |
| BI-FIN-018 | File | Import upload cases are not applicable because this report slice has no import route. | - | REQ_F4117.5.1–7 | N/A |
| BI-FIN-019 | Read/UI | Three languages, light/dark, loading, empty, search-empty, denied and server-error states show correct source, cutoff and disabled reason. | E | REQ_F4117.5.1–7 | TODO |
| BI-FIN-020 | Boundary | View, pagination, print and export change no FI/GL/business audit rows. | I,E | REQ_F4117.5.1–7 | TODO |

Traceability: REQ_F4117.5.1 → BR-BI-001–8, BR-FI-075–082 → BI-FIN-001,006–16,019–20; REQ_F4117.5.2–5 → BR-BI-001–8, BR-FI-067–074 → BI-FIN-002–3,006–16,019–20; REQ_F4117.5.6–7 → BR-BI-001–8, BR-FI-059–066 → BI-FIN-004–5,008–20. All product tests remain TODO.

## WU-BI-001d focused source binding scenarios (design; product tests TODO)

FI-RPT-001–015 covers FI's owner-side normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories. BI tests below exercise its own binding and rendered outcome. For every E test, compare message/severity, visible rows and full-set totals with the saved FI-backed API response at the same cutoff; no UI-only count assertion suffices.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| BI-FIN-021 | Normal | AR/AP detail shows FI as-of saved original, credit, settled, open and source links; totals cover all pages, not the current page. | I,E | REQ_F4117.5.2–3 | TODO |
| BI-FIN-022 | Normal | Other-source view separates FI positive income/expense and signed net; Excel matches frozen set and order. | I,E | REQ_F4117.5.6–7 | TODO |
| BI-FIN-023 | Boundary | As-of midnight/leap/year boundary and 366-day other range are exact; empty and zero-open history have distinct labels and correct totals. | U,I,E | REQ_F4117.5.2–3,5.6 | TODO |
| BI-FIN-024 | Duplicate/conflict | Backdated/out-of-order commit and duplicate replay do not change existing pages or export; fresh cutoff changes once. | I,E | REQ_F4117.5.2–3,5.6–7 | TODO |
| BI-FIN-025 | Partial result | FI missing provenance, mixed eligible/incomplete or all-excluded result shows named reason/count; no guessed amount or partial download. | I,E | REQ_F4117.5.2–3,5.6–7 | TODO |
| BI-FIN-026 | Permission | BI VIEW/EXPORT and FI source permissions are separately required; revoked role between pages gives 403, foreign partner/company 404 and no cached row leak. | I,E | REQ_F4117.5.2–3,5.6–7 | TODO |
| BI-FIN-027 | State | Draft/Pending/Rejected and unapplied money are absent; Posted rows remain visible with historic saved labels after MD inactivation. | I,E | REQ_F4117.5.2–3,5.6 | TODO |
| BI-FIN-028 | Error | 400/409/422/500 with traceId, timeout and network failure show translated cause; retry retains or explicitly refreshes cutoff. | I,E | REQ_F4117.5.2–3,5.6–7 | TODO |
| BI-FIN-029 | File | Export escapes formula text, includes cutoff/currency/filters and no partial file; empty set has header/zero totals. Upload N/A: no import route. | I,E | REQ_F4117.5.7 | TODO |
| BI-FIN-030 | Read/UI | Three languages and both themes show loading, empty, denied, incomplete, expired and success states with correct source and disabled reason. | E | REQ_F4117.5.2–3,5.6–7 | TODO |
| BI-FIN-031 | Read/audit | Repeated view, page and export leave FI/GL/workflow/business audit counts and versions unchanged. | I,E | REQ_F4117.5.2–3,5.6–7 | TODO |

Traceability: BR-BI-009 → BI-FIN-021,023–28,030–31; BR-BI-010 → BI-FIN-022,024–31; BR-BI-011 → BI-FIN-024–31. All are planned product tests; the design unit has no runtime implementation.

## WU-BI-002d operational report scenarios (design; product tests TODO)

`myerp-test-scenarios` categories are all considered. U=unit, I=real SQL plus authorised HTTP, E=Playwright. Each E test must assert translated message, severity, cutoff, row count and visible values against an independent authorised owner-backed API read of the same frozen source set; check that reads do not mutate owner rows. Implementation units write failing product tests first. PP source tests remain pending until its module exists.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| BI-OPS-001 | Normal | PUR order tracking shows ordered, Posted received, Posted returned and open separately; Pending receipt is labelled but not added. | I,E | REQ_F4117.1.1 | TODO |
| BI-OPS-002 | Normal | PUR detail links each saved order/receipt/return once; product and supplier summaries reconcile by currency/base unit across all pages. | I,E | REQ_F4117.1.2–4 | TODO |
| BI-OPS-003 | Normal | SD tracking separates order, Posted delivery, return and invoice; detail keeps delivery and invoice dates and saved prices distinct. | I,E | REQ_F4117.2.1–2 | TODO |
| BI-OPS-004 | Normal | SD product/client summaries reconcile with full detail, grouped by currency and base unit. | I,E | REQ_F4117.2.3–4 | TODO |
| BI-OPS-005 | Normal | PP planning, requisition and completion detail/summary match saved PP status and confirmed Posted INV references once implemented. | I,E | REQ_F4117.3.1–5 | TODO |
| BI-OPS-006 | Normal | INV opening plus interval in minus out equals closing; transfer legs remain separately visible and summary matches every detail page. | I,E | REQ_F4117.4.1–3 | TODO |
| BI-OPS-007 | Boundary | Empty range and zero eligible rows show zero counts; opening may be nonzero. Month/year/leap boundary and 366-day maximum include both end dates; 367 days fails. | U,I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-008 | Boundary | Empty/invalid item, partner, warehouse, location, page and maximum page size fail specifically; six-decimal quantity and maximum saved monetary scale are exact, overflow fails. | U,I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-009 | Duplicate/conflict | Same source line replay and both transfer legs do not duplicate totals; repeated page/export is read-only. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-010 | Duplicate/conflict | Concurrent backdated commit cannot enter an old page/summary; fresh manifest includes it once. Tampered, expired or changed-filter continuation gets named 400/409. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-011 | Partial result | One missing movement reference amid valid rows yields named incomplete source IDs/count, not a plausible partial total; all unavailable PP sources yield unavailable, not empty. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-012 | Partial result | All rows excluded by state/scope shows accurate zero eligible with exclusion reason where permitted; failed future export yields no partial file. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-013 | Permission | Anonymous 401, missing BI VIEW 403, missing owner source VIEW 403 and revoked authority between pages deny new reads. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-014 | Permission | Cross-company or out-of-scope partner/warehouse/source ID is hidden as 404; no cached prior page leaks after role/scope change. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-015 | State | Draft/Pending/Rejected/Void receipts, deliveries and completions cannot become Posted flow; cancelled orders stay traceable without implying open authority. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-016 | Relation | Soft-deleted/inactive item, partner or warehouse keeps saved historical identity but is not selectable for new work; owner source links remain intact. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-017 | Error | 400/409/422/500 with traceId, timeout and network failure show translated named outcomes; retry preserves or explicitly refreshes manifest. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-018 | File | Planned export equals frozen full set and totals, escapes formula-leading text, has safe filename/currency/cutoff, rejects oversize without truncation; empty file has header/zero totals. | I,E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-019 | File | Import cases (empty, malformed, oversize, wrong encoding) are N/A because these reports have no upload route. | - | REQ_F4117.1.1–4.3 | N/A |
| BI-OPS-020 | Read/UI | Three languages and both themes show loading, empty, denied, unavailable, incomplete, expired and success, with disabled reason and no false success toast. | E | REQ_F4117.1.1–4.3 | TODO |
| BI-OPS-021 | Read/audit | View, page, print and future export leave PUR/SD/PP/INV rows, GL, workflow and business audit versions/counts unchanged. | I,E | REQ_F4117.1.1–4.3 | TODO |

Traceability: 7.1.1–4 → BI-OPS-001–2,007–21; 7.2.1–4 → 003–4,007–21; 7.3.1–5 → 005,007–21; 7.4.1–3 → 006–21. Source-specific integration and E2E cases are split into later implementation work units; this design has no executable product test.

## WU-BI-003d dashboard scenarios (design; product tests TODO)

`myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are covered below. U=unit, I=real SQL plus authorised HTTP, E=Playwright. Each E test compares visible value, unit, severity and cutoff with an independently authorised owner read of the same manifest; reads must leave saved rows and business audit unchanged. Implementation writes failing product tests first.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| BI-KPI-001 | Normal | FI Posted sales invoice less credit revenue equals saved base-currency source lines; tax, receipt and non-sales journal excluded. | I,E | REQ_F4117.6.1 | TODO |
| BI-KPI-002 | Normal | SD counts distinct first approvals once; reapproval, second line and duplicate event do not increase count. | I,E | REQ_F4117.6.2 | TODO |
| BI-KPI-003 | Normal | FI open AR as-of date matches Posted invoices, credits and committed allocations; unapplied collection separate. | I,E | REQ_F4117.6.3 | TODO |
| BI-KPI-004 | Normal | INV low-stock count matches distinct item/warehouse keys below positive thresholds; transfer legs change correct warehouses. | I,E | REQ_F4117.6.4 | TODO |
| BI-KPI-005 | Boundary | Empty eligible set yields genuine zero; leap day, year boundary, one-day and 366-day inclusive interval work; zero-day or 367-day interval fails 400. | U,I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-006 | Boundary | Decimal money and six-decimal stock are exact, counts are integers, overflow is named 422; previous zero or unavailable produces no percentage. | U,I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-007 | Duplicate/conflict | Concurrent backdated commit is absent from old manifest and present once in new; tampered, changed-filter and expired manifest fail explicitly. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-008 | Partial result | One owner unavailable/incomplete leaves authorised other cards valid with separate cutoff and reason; none unavailable is shown as fake zero. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-009 | Partial result | All owners unavailable shows four named reasons and no apparent successful dashboard; zero eligible with available source remains distinguishable. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-010 | Permission | Anonymous 401, missing BI VIEW 403, missing/revoked owner VIEW denied; role or company switch invalidates old visible values. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-011 | Permission | Cross-company and out-of-scope partner/warehouse IDs never leak values or existence; every owner applies current SYS scope. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-012 | State | Draft, Pending, Rejected, Void and unposted documents cannot contribute Posted/approved metrics; reapproval does not shift first-approval date. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-013 | Relation | Soft-deleted historical item/customer keeps source provenance; absent or zero threshold is excluded with named explanation. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-014 | Error | 400, 409, 422, 500 with traceId, timeout and network loss show translated card or request errors and safe retry. | I,E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-015 | File | Upload and export cases are N/A: the dashboard KPI route has no file operation. | - | REQ_F4117.6.1–4 | N/A |
| BI-KPI-016 | UI/read | Three languages, light/dark, loading, empty, denied, unavailable, incomplete, expired and success show correct unit/date/cutoff and no false success toast. | E | REQ_F4117.6.1–4 | TODO |
| BI-KPI-017 | Read/audit | Repeat and comparison reads change no FI/SD/INV/GL/workflow/business audit rows, counts or versions. | I,E | REQ_F4117.6.1–4 | TODO |

Traceability: BR-BI-020–25 → BI-KPI-001–17; owner-specific assertions 001→6.1, 002→6.2, 003→6.3, 004→6.4; common cases 005–17→6.1–4. No executable BI product test exists in this design slice.

## WU-BI-004d comparison, drill and schedule scenarios (design; product tests TODO)

`myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are covered. U=unit, I=real SQL plus authorised HTTP and worker, E=Playwright. Every E case must compare translated message, severity, cutoffs, rows/counts and downloadable result with independently authorised owner-backed saved data from the same manifests. Implementation units write failing product tests first; none is executable in this design unit.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| BI-ADV-001 | Normal | Two eligible periods use same metric/group/unit with independent manifests; difference, signed totals and percentage reconcile to each owner set. | I,E | REQ_F4117.7.1 | TODO |
| BI-ADV-002 | Normal | Drill all pages for one group returns each immutable source ID once; full-set detail sum equals selected aggregate. | I,E | REQ_F4117.7.2 | TODO |
| BI-ADV-003 | Normal | Create daily/weekly schedule, claim due occurrence once, freeze complete owner set and download exact safe file with count, totals and cutoff. | I,E | REQ_F4117.7.3–4 | TODO |
| BI-ADV-004 | Normal | Comparison/drill export retains both or selected manifest, deterministic order and complete totals from view. | I,E | REQ_F4117.7.5 | TODO |
| BI-ADV-005 | Boundary | Empty/zero comparison, zero prior denominator and negative baseline show undefined or signed percentage as specified; no divide-by-zero. | U,I,E | REQ_F4117.7.1 | TODO |
| BI-ADV-006 | Boundary | One-day, leap/year boundary, 366-day maximum accepted; empty dates, reversed or 367-day range, maximum page/row/file limit rejected exactly. | U,I,E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-007 | Boundary | Schedule nonexistent local time is skipped; ambiguous local time selects earlier instant once; next run and occurrence key are auditable. | U,I,E | REQ_F4117.7.3–4 | TODO |
| BI-ADV-008 | Duplicate/conflict | Two workers and timeout retry claim one occurrence and yield at most one completed artifact; failed lease can be recovered without double delivery. | I | REQ_F4117.7.4 | TODO |
| BI-ADV-009 | Duplicate/conflict | Concurrent schedule edit/pause/delete returns 409 for stale version; pause/delete prevents future run but preserves prior history. | I,E | REQ_F4117.7.3–4 | TODO |
| BI-ADV-010 | Duplicate/conflict | Backdated source commit changes only fresh manifests; changed group/filter/tampered or expired token cannot enter old comparison/drill/export. | I,E | REQ_F4117.7.1–2,7.5 | TODO |
| BI-ADV-011 | Partial result | One comparison side or one drilled source incomplete blocks difference/export with named source IDs; no plausible partial total. | I,E | REQ_F4117.7.1–2,7.5 | TODO |
| BI-ADV-012 | Partial result | Due batch with completed, skipped and failed schedules shows each count/reason; all skipped or all failed shows warning/error, not success. | I,E | REQ_F4117.7.4 | TODO |
| BI-ADV-013 | Partial result | Complete zero-row run is explicitly empty with safe header/zero totals; unavailable/partial generation leaves no downloadable artifact. | I,E | REQ_F4117.7.4 | TODO |
| BI-ADV-014 | Permission | Anonymous 401, missing BI VIEW/CREATE/EDIT/DELETE/DOWNLOAD/EXPORT or owner VIEW 403; revoked authority at due time and download denies file. | I,E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-015 | Permission | Forged company/recipient, out-of-scope drill source and cross-company artifact hidden as 404; role/company switch clears cached rows and links. | I,E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-016 | State | Paused/deleted schedule creates no future run; pending/running/failed artifact cannot download; Draft/Pending owner facts stay out of Posted source set. | I,E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-017 | Relation | Historical soft-deleted item/partner remains in frozen source; removed recipient or owner permission stops future delivery without erasing prior audit. | I,E | REQ_F4117.7.2–4 | TODO |
| BI-ADV-018 | Error | 400/409/422/500 with traceId, worker timeout/network loss and unknown outcome show specific translated state; retry uses occurrence key. | I,E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-019 | File | Formula-leading text escaped, filename/MIME/digest safe; oversize/expired/failed file yields no partial download and no leaked rows. | I,E | REQ_F4117.7.4–5 | TODO |
| BI-ADV-020 | File | Import empty/malformed/encoding/column cases are N/A: no upload route in comparison, drill or scheduling. | - | REQ_F4117.7.1–5 | N/A |
| BI-ADV-021 | UI | Three languages and both themes show loading, empty, denied, unavailable, incomplete, expired and success with cutoff, unit and disabled reason; messages match saved rows. | E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-022 | Read/audit | Compare, drill, scheduled export and download leave owner business rows, GL, stock, workflow and their audit versions unchanged; BI run audit is recorded. | I,E | REQ_F4117.7.1–5 | TODO |
| BI-ADV-023 | Security | Schedule contains no credential or external address; artifact link/notification contains no report rows, and digest/retention check prevents stale or modified file delivery. | U,I,E | REQ_F4117.7.3–4 | TODO |

Traceability: 7.1 → BR-BI-026–27,029 and BI-ADV-001,005–6,010–11,014–16,018,021–22; 7.2 → BR-BI-028–29 and 002,010–11,014–18,021–22; 7.3 → BR-BI-030–31,033 and 003,007–9,012,014–18,021–23; 7.4 → BR-BI-031–34 and 003,007–9,012–19,021–23; 7.5 → BR-BI-026–29,032–33 and 004,010–11,014–15,018–19,021–22. All product tests remain TODO.
