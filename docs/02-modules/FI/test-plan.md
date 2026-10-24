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
