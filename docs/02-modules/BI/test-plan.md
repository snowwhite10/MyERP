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
