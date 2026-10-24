# FI test plan

## WU-FI-000d credit scenarios (design; all product tests TODO)

The `myerp-test-scenarios` checklist covers normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file cases. U=unit, I=real SQL/authorised HTTP integration, E=Playwright. E cases must assert both the displayed result and a fresh read of saved data. WU-FI-001a/b/c first write failing tests, then implement. WU-FI-000d adds scenarios only.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-CREDIT-001 | Normal | Create a dated TWD limit; snapshot returns same company/customer, base currency, limit, zero posted AR, date and version. | I,E | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-002 | Normal | Post and allocate AR; snapshot reports committed open balance only, and SD can add its own commitment without FI counting it. | I | REQ_F4116.7.2 | TODO |
| FI-CREDIT-003 | Normal | Limit edit and two order approvals for different customers can proceed independently; same customer serializes. | I | REQ_F4116.7.3 | TODO |
| FI-CREDIT-004 | Boundary | Empty customer/currency, zero/negative ID, negative amount, over DECIMAL(19,4), fifth fractional digit and maximum valid amount return field-specific results; zero limit is valid. | U,I,E | REQ_F4116.7.1 | TODO |
| FI-CREDIT-005 | Boundary | Inclusive dates at month end, year end and leap day select exactly one limit; invalid/reversed date interval returns 400. | U,I | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-006 | Boundary | Evaluation of backdated SD order uses server `IClock.Today`, never order or client date; expired limit fails closed. | I | REQ_F4116.7.2 | TODO |
| FI-CREDIT-007 | Duplicate/conflict | Overlapping approved periods, including touching endpoints, reject with 409; adjacent nonoverlapping periods work. | I | REQ_F4116.7.1 | TODO |
| FI-CREDIT-008 | Duplicate/conflict | Soft-deleted/deactivated history and same customer key cannot silently resurrect or reuse a conflicting interval. | I | REQ_F4116.7.1 | TODO |
| FI-CREDIT-009 | Duplicate/conflict | Stale edit/deactivate version returns 409 and preserves the saved limit and audit. | I,E | REQ_F4116.7.1 | TODO |
| FI-CREDIT-010 | Duplicate/conflict | Two SQL connections contend on one credit key: at most one approval consumes final headroom; lock timeout is named and retryable. | I | REQ_F4116.7.2–3 | TODO |
| FI-CREDIT-011 | Partial result | One invalid field/interval causes the single limit write to roll back fully; zero changed rows and no success message. Batch and partial-success modes are not offered. | I,E | REQ_F4116.7.1 | TODO |
| FI-CREDIT-012 | Permission | Anonymous 401; VIEW/CREATE/EDIT/DEACTIVATE are separate permissions, and denied action returns 403 without mutation. | I,E | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-013 | Permission | Cross-company and SYS out-of-scope customer/limit IDs return 404; forged company ID or base currency is ignored or rejected. | I,E | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-014 | State | Draft/Pending/failed AR has no exposure; Posted AR does; deactivated limit is unavailable, not a zero limit. | I | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-015 | Relation | Disabled/deleted MD customer or missing company base currency blocks new limit and snapshot; existing historical AR remains traceable. | I | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-016 | Error | Missing/ambiguous limit, mismatched identity/currency, negative/overflowed AR, stale snapshot and unavailable FI all fail closed with distinct codes. | U,I | REQ_F4116.7.2 | TODO |
| FI-CREDIT-017 | Error | HTTP 400/409/422/500, timeout and network interruption show localized reason or traceId; reread saved data before claiming success. | I,E | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-018 | Transaction | Inject failure after FI lock/limit or AR update: FI, SD, SYS and audit changes all roll back; replay does not double count. | I | REQ_F4116.7.2–3 | TODO |
| FI-CREDIT-019 | UI | Normal, empty/search-empty, no permission, validation, server error and success in three languages and light/dark modes; message agrees with saved limit. | E | REQ_F4116.7.1–2 | TODO |
| FI-CREDIT-020 | File | No upload/export is in this credit slice; empty file, format, size, row count and encoding cases are not applicable until a separately specified file feature exists. | — | REQ_F4116.7.1 | N/A |

Traceability: REQ_F4116.7.1 → BR-FI-002/004 → FI-CREDIT-001,004–009,011–15,17,19; REQ_F4116.7.2 → BR-FI-001/002/005/006 → FI-CREDIT-001–002,006,010,012–19; REQ_F4116.7.3 → BR-FI-003/006 → FI-CREDIT-003,010,018. No automated product test passed in this design unit.

## WU-FI-002d sales source scenarios (design; all product tests TODO)

The `myerp-test-scenarios` checklist covers normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file cases. U=unit, I=two-connection real SQL and authorised HTTP integration, E=Playwright. Every E case asserts both visible message and fresh persisted FI/SD/GL data. WU-FI-002d adds scenarios only; WU-FI-002a/b/c write failing product tests before implementation.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-SALES-001 | Normal | Saved SD invoice posts one balanced AR/revenue/output-tax journal; source, original/open AR and FI result equal SD snapshots. | I,E | REQ_F4116.8.1,8.3 | TODO |
| FI-SALES-002 | Normal | Several invoice lines and tax rates aggregate by saved mapping; zero tax omits a zero leg, journal remains balanced. | U,I | REQ_F4116.8.1 | TODO |
| FI-SALES-003 | Normal | Under one credit key lock, invoice adds base gross to AR and releases the same approved SD commitment; total exposure is unchanged. | I | REQ_F4116.8.1 | TODO |
| FI-SALES-004 | Normal | Billed return referencing two invoice postings creates one balanced FI credit source and reduces only their credited open AR. | I,E | REQ_F4116.8.2 | TODO |
| FI-SALES-005 | Normal | Mixed billed/unbilled return credits billed AR and SD releases only unbilled commitment; FI creates no credit for unbilled portion. | I,E | REQ_F4116.8.2 | TODO |
| FI-SALES-006 | Boundary | Empty source/lines, zero or negative quantity, >6 quantity decimals, >4 amount decimals, maximum DECIMAL(19,4), overflow and net+tax mismatch fail by field with zero writes. | U,I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-007 | Boundary | Exact final partial invoice/credit uses saved remainder; repeated portions reconcile original/base net, tax and gross without rounding plug. | U,I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-008 | Boundary | Month/year/leap-day business date, later MD rate/account changes and disabled reference do not rewrite historical posted source; current ineligible account blocks new post. | U,I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-009 | Duplicate/conflict | Same source and fingerprint replays stable result with changed=false; new number/journal/AR/credit/audit count is zero. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-010 | Duplicate/conflict | Same source with different fingerprint or stale source version returns 409; soft-deleted history cannot free the source key. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-011 | Duplicate/conflict | Two SQL connections posting same source or final creditable portion serialize; one wins, other replays or conflicts, never double-counts. | I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-012 | Partial result | Failure after FI source, journal, AR, SD commitment, INV movement, SYS decision or audit rolls back whole command and number; zero-change result has no success toast. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-013 | Partial result | Partial success, all skipped and all failed batch outcomes are not offered for one atomic source; duplicate replay is explicitly unchanged. | I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-014 | Permission | Anonymous read 401, missing `AR.SALES_SOURCE.VIEW` 403; posting requires authorised SD ISSUE/final decision and approved internal caller. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-015 | Permission | Other-company and SYS out-of-scope source/customer/invoice IDs give 404 without financial leakage; forged company/actor/time/amount is rejected. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-016 | State | Draft/Pending/Rejected/deleted SD source cannot post; issuing source in the same transaction may post before final Posted flag, but rollback leaves neither Posted nor FI rows. | I | REQ_F4116.8.1–2 | TODO |
| FI-SALES-017 | State | Posted FI source and journal are immutable; later correction uses a new credit source and retains original trace/audit. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-018 | Relation | Credit rejects wrong invoice line, customer, currency, company, missing original FI posting and disabled/deleted related source, with zero allocations. | U,I | REQ_F4116.8.2 | TODO |
| FI-SALES-019 | Relation | Prior receipts and credits constrain open AR; over-credit or credit after full settlement is rejected pending separate refund design, never negative AR. | I | REQ_F4116.8.2 | TODO |
| FI-SALES-020 | Error | Missing MD account, journal mapping, FI/SD contract, lock timeout, mismatched FI result and balance residual return named 422/409, no Posted claim. | U,I,E | REQ_F4116.8.1–2 | TODO |
| FI-SALES-021 | Error | HTTP 400/409/422/500 with traceId, timeout and network break show translated cause; key lookup distinguishes committed result from absent safe retry. | I,E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-022 | Read/UI | Source read filters company/customer/type/date, pages deterministically, hides uncommitted and inaccessible IDs, and displays saved original/open amount and credit links. | I,E | REQ_F4116.8.3 | TODO |
| FI-SALES-023 | UI | Three-language light/dark normal, empty/search-empty, permission, validation, success, replay and server-error states match fresh persisted SD/FI/GL reads. | E | REQ_F4116.8.1–3 | TODO |
| FI-SALES-024 | File | No upload/import/export is specified for this source slice; empty, malformed, oversized, wrong-encoding and column cases are inapplicable until a separate file feature exists. | — | REQ_F4116.8.3 | N/A |

Traceability: REQ_F4116.8.1 → BR-FI-007–010,013–014 → FI-SALES-001–003,006–017,020–021,023; REQ_F4116.8.2 → BR-FI-007–008,011–014 → FI-SALES-004–013,015–021,023; REQ_F4116.8.3 → BR-FI-008,014 → FI-SALES-009,014–015,017,021–024. No executable product test or SQL acceptance is claimed by this design unit.
