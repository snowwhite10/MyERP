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

## WU-FI-003d purchase payable source scenarios (design; all product tests TODO)

The `myerp-test-scenarios` checklist covers normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file cases. U=unit, I=real SQL/authorised HTTP integration, E=Playwright. Every E test must assert both the visible result and a fresh read of persisted PUR/FI/GL data. WU-FI-003a/b/c first write failing product tests; this design unit adds scenarios only.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-PUR-001 | Normal | Matched saved invoice posts one FI source, payable and balanced purchase/input-tax/AP journal; PUR becomes Posted with identical gross. | I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-002 | Normal | Several receipt lines, accepted price/tax differences and zero-tax line preserve each saved component; zero tax emits no zero GL leg. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-003 | Normal | One receipt is invoiced in portions; exact final remainder uses saved original/base values without a balancing plug or over-invoicing. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-004 | Boundary | Empty/zero source, zero lines, negative/zero quantity, seventh quantity decimal, fifth money decimal, maximum valid decimal and overflow give field/line-specific failure and zero writes. | U,I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-005 | Boundary | Month end, year end and leap day retain saved date/rate; later MD exchange or tax change cannot restate historical posting. | U,I | REQ_F4116.9.1,9.3 | TODO |
| FI-PUR-006 | Boundary | Missing active account, disabled mapping, nonrecoverable tax without cost mapping, net+tax mismatch or base journal residual gives named 422, no suspense/rounding plug. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-007 | Duplicate/conflict | Same company/PUR/invoice key and identical fingerprint returns original payable/journal with changed=false and zero additional rows/audit. | I,E | REQ_F4116.9.2 | TODO |
| FI-PUR-008 | Duplicate/conflict | Same key with changed fingerprint or stale source version returns 409; soft-deleted historical key remains reserved. | I | REQ_F4116.9.2 | TODO |
| FI-PUR-009 | Duplicate/conflict | Two SQL connections post same key; one commits, other replays or conflicts, never two payables or a deadlock. | I | REQ_F4116.9.1–2 | TODO |
| FI-PUR-010 | Duplicate/conflict | Two invoices race for the final receipt quantity; PUR locks permit only one, FI has no orphan payable. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-011 | Partial result | Inject failure after source, journal, payable, PUR quantity/state or audit: all effects and number allocation roll back, original version remains retryable. | I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-012 | Partial result | One invalid line rejects the entire invoice; zero changed rows, no success toast. Partial success, all skipped and all failed batch outcomes are inapplicable because one source is atomic. | I,E | REQ_F4116.9.1 | TODO |
| FI-PUR-013 | Permission | Anonymous read 401, absent `AP.PURCHASE_SOURCE.VIEW` 403; posting requires authorised PUR POST and authenticated internal caller. | I,E | REQ_F4116.9.1,9.3 | TODO |
| FI-PUR-014 | Permission | Cross-company or SYS out-of-scope supplier/source is 404; forged company, supplier, actor, time, source amount or account is rejected without leakage. | I,E | REQ_F4116.9.1,9.3 | TODO |
| FI-PUR-015 | State | Draft, Disputed, Rejected, deleted and unsaved invoice cannot post; only Matched with all disputes accepted may call FI, then PUR marks Posted in one transaction. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-016 | State | Posted source, payable origin and journal cannot be edited/deleted or posted again; later correction requires a new specified compensating source. | I,E | REQ_F4116.9.1–3 | TODO |
| FI-PUR-017 | Relation | Wrong supplier/company/currency, non-Posted receipt, cancelled order, expired dispute decision or returned receipt quantity fails before FI posting. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-018 | Relation | Invoiced receipt cannot be returned without separately specified FI credit/debit note flow; no negative payable or silent journal rewrite. | I | REQ_F4116.9.1 | TODO |
| FI-PUR-019 | Error | FI/GL/MD/PUR contract unavailable, lock timeout and mismatched FI result yield named error and leave PUR not Posted. | U,I | REQ_F4116.9.1 | TODO |
| FI-PUR-020 | Error | 400/409/422/500 with traceId, timeout and network break show translated reason; committed source lookup distinguishes success from safe retry. | I,E | REQ_F4116.9.1–3 | TODO |
| FI-PUR-021 | Read/UI | Paged source read is stable and company/SYS scoped; list/detail show saved original/open payable and journal, never uncommitted data. | I,E | REQ_F4116.9.3 | TODO |
| FI-PUR-022 | UI | Three-language light/dark normal, empty/search-empty, denied, validation, success, unchanged replay and server-error states match fresh PUR/FI/GL reads. | E | REQ_F4116.9.1–3 | TODO |
| FI-PUR-023 | Audit/database | Source uniqueness survives soft deletion; FI FKs/indexes, immutable history, rollback audit and reversible migration are proved against SQL. | I | REQ_F4116.9.1–3 | TODO |
| FI-PUR-024 | File | No upload/import/export exists in this FI source slice; empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable until specified. | — | REQ_F4116.9.3 | N/A |

Traceability: REQ_F4116.9.1 → BR-FI-015,017–022 → FI-PUR-001–006,009–019,022–023; REQ_F4116.9.2 → BR-FI-016,020 → FI-PUR-007–011,020,023; REQ_F4116.9.3 → BR-FI-019,021–022 → FI-PUR-005,013–014,016,020–024. No executable FI payable test or real SQL acceptance is claimed here.

## WU-FI-004d GL foundation scenarios (design; all product tests TODO)

U = unit, I = real SQL integration, E = Playwright with UI and persisted-data assertions. The later implementation units must write failing tests first. No scenario below is reported as passed by this design unit.

| ID | Category | Scenario and expected result | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-GL-001 | Normal | Create Open period; saved business date within inclusive interval posts one balanced immutable journal and lines through FI. | I,E | REQ_F4116.10.1–2 | TODO |
| FI-GL-002 | Normal | SD invoice and PUR matched invoice each use their FI source adapter and one GL journal; totals equal saved AR/AP components. | I | REQ_F4116.10.2 | TODO |
| FI-GL-003 | Normal | Multiple accounts and tax legs retain deterministic sequence and saved MD account/mapping snapshots; zero tax omits a zero line. | U,I | REQ_F4116.10.2 | TODO |
| FI-GL-004 | Boundary | First/last inclusive day, month/year boundary and leap day resolve exactly one Open period from saved business date. | U,I | REQ_F4116.10.1–2 | TODO |
| FI-GL-005 | Boundary | Empty source/lines, one line, zero/negative amount, fifth money decimal, both debit/credit on one line and overflow return field/line error with zero writes. | U,I,E | REQ_F4116.10.2 | TODO |
| FI-GL-006 | Boundary | Maximum valid DECIMAL(19,4) total works; checked sum overflow and one-unit base imbalance fail without a rounding/suspense plug. | U,I | REQ_F4116.10.2 | TODO |
| FI-GL-007 | Duplicate/conflict | Inclusive period overlap, including shared endpoint, returns 409; adjacent nonoverlapping periods work. | I | REQ_F4116.10.1 | TODO |
| FI-GL-008 | Duplicate/conflict | Two SQL connections create overlapping periods or post the same source; only one period/journal wins, second conflicts or replays. | I | REQ_F4116.10.1,10.3 | TODO |
| FI-GL-009 | Duplicate/conflict | Same permanent source/version/fingerprint replays committed ID with changed=false, zero added lines/number/audit and no success toast. | I,E | REQ_F4116.10.3 | TODO |
| FI-GL-010 | Duplicate/conflict | Changed fingerprint/stale version returns 409; soft-deleted history cannot free source key; period stale edit returns 409. | I | REQ_F4116.10.1,10.3 | TODO |
| FI-GL-011 | Partial result | One invalid line or fault after source/header/line/AR/AP/upstream state/audit rolls back all rows and number; no partial success. | I,E | REQ_F4116.10.2 | TODO |
| FI-GL-012 | Partial result | All-skipped/all-failed batch and zero-row batch are inapplicable: one posting is atomic; unchanged replay is explicitly reported as unchanged. | I,E | REQ_F4116.10.2–3 | TODO |
| FI-GL-013 | Permission | Anonymous read is 401; missing GL.JOURNAL.VIEW or period permission is 403; arbitrary client journal POST is unavailable. | I,E | REQ_F4116.10.1–3 | TODO |
| FI-GL-014 | Permission | Cross-company or SYS out-of-scope period/journal/source/account ID returns 404; forged company/actor/date/amount cannot alter posting. | I,E | REQ_F4116.10.1–3 | TODO |
| FI-GL-015 | State | Missing, overlapping or Closed period blocks posting; close/reopen uses version, and Posted journal remains readable and immutable. | I,E | REQ_F4116.10.1–2 | TODO |
| FI-GL-016 | State | Draft/Pending/Rejected/deleted/unsaved source cannot post; an issuing source may post only in its same transaction before final Posted flag. | I | REQ_F4116.10.2 | TODO |
| FI-GL-017 | Relation | Missing, inactive, deleted, non-leaf or wrong-company MD account and missing mapping fail before GL write; `IAccountUsage` protects Posted references. | I | REQ_F4116.10.2 | TODO |
| FI-GL-018 | Relation | Posted journal prevents period deletion/date move excluding its date; later MD changes cannot rewrite saved account and amount snapshots. | I | REQ_F4116.10.1–3 | TODO |
| FI-GL-019 | Error | Wrong base currency, source version, saved totals, balance or unavailable MD/FI contract returns named 409/422 and leaves upstream not Posted. | U,I | REQ_F4116.10.2 | TODO |
| FI-GL-020 | Error | HTTP 400/409/422/500 with traceId, timeout and network break show translated reason; fresh committed source lookup distinguishes success from safe retry. | I,E | REQ_F4116.10.1–3 | TODO |
| FI-GL-021 | Read/UI | Stable paged company/SYS-scope list/detail shows saved lines/totals, empty/search-empty/no-permission; no uncommitted or foreign-company data. | I,E | REQ_F4116.10.3 | TODO |
| FI-GL-022 | UI | Three languages and light/dark normal, validation, success, unchanged replay, permission and server error states match fresh GL/source reads. | E | REQ_F4116.10.1–3 | TODO |
| FI-GL-023 | Database/audit | Real SQL proves permanent source unique key, period lock, FI FKs/indexes/checks, immutable Posted history, audit rollback and reversible migration. | I | REQ_F4116.10.1–3 | TODO |
| FI-GL-024 | File | No import/export in this GL slice: empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable until a separate feature exists. | — | REQ_F4116.10.3 | N/A |

Traceability: REQ_F4116.10.1 → BR-FI-023,029–030 → FI-GL-001,004,007–008,010,013–015,018,020,022–023; REQ_F4116.10.2 → BR-FI-024–027,029–030 → FI-GL-001–006,008,011–017,019–020,022–023; REQ_F4116.10.3 → BR-FI-028–030 → FI-GL-008–010,012–014,018,020–024. Real SQL and UI tests remain TODO.

## WU-FI-005d sales collection and settlement scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are all covered below. U=unit, I=real SQL/authorised HTTP, E=Playwright. Every E case must compare message/style/count/reason with a fresh API or list read of receipt, allocation, GL and AR exposure. WU-FI-005a/b/c first write failing tests, then implement; no executable financial product test is delivered here.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-AR-COLL-001 | Normal | Save a positive Draft receipt; no journal, clearing or AR change before final approval. | I,E | REQ_F4116.1.2 | TODO |
| FI-AR-COLL-002 | Normal | SYS final approval posts one bank debit/clearing credit; unapplied equals received and open AR is unchanged. | I,E | REQ_F4116.1.4 | TODO |
| FI-AR-COLL-003 | Normal | Apply one receipt to one invoice; clearing debit/AR credit and committed FI open AR decrease equally. | I,E | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-004 | Normal | Partial settlement of several invoices leaves exact unapplied receipt and open AR; subsequent settlement uses remainder. | I,E | REQ_F4116.5.1,5.4 | TODO |
| FI-AR-COLL-005 | Boundary | Empty customer/bank/reference/currency/date, zero/negative ID/amount, fifth decimal, max valid DECIMAL(19,4) and overflow identify fields and save nothing. | U,I,E | REQ_F4116.1.2,5.2 | TODO |
| FI-AR-COLL-006 | Boundary | Month/year/leap-day receipt and settlement business dates resolve an inclusive Open GL period; missing/Closed period posts nothing. | U,I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-007 | Boundary | Zero or >1000 allocation lines, duplicate invoice row, zero line and sum overflow fail all; exact final remainder succeeds. | U,I,E | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-008 | Duplicate/conflict | Same bank reference in one company/bank, including deleted Posted history, is 409; distinct bank/company references are independent. | I | REQ_F4116.1.2,1.4 | TODO |
| FI-AR-COLL-009 | Duplicate/conflict | Identical committed receipt or settlement source replay returns same IDs, changed=false, zero extra GL/audit/exposure and warning UI. | I,E | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-010 | Duplicate/conflict | Changed fingerprint or stale version is 409; deleted history cannot free source key. | I | REQ_F4116.1.3,5.3 | TODO |
| FI-AR-COLL-011 | Duplicate/conflict | Two SQL connections allocate the same receipt or invoice concurrently; locks serialize and reject the losing over-allocation. | I | REQ_F4116.5.4 | TODO |
| FI-AR-COLL-012 | Partial result | Invalid second allocation or fault after receipt, line, journal, SYS/audit save rolls back every row, number and exposure; row reason appears. | I,E | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-013 | Partial result | Batch partial/all-skipped/all-failed/zero-row outcomes are inapplicable: one receipt/settlement is atomic and zero lines are rejected. | U,I | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-014 | Permission | Anonymous 401, missing distinct receipt/settlement action permission 403; VIEW alone cannot submit or approve. | I,E | REQ_F4116.1.1–6,5.1–6 | TODO |
| FI-AR-COLL-015 | Permission | Cross-company and SYS out-of-scope receipt/invoice/bank IDs return 404; forged company/customer/actor/amount cannot change FI. | I,E | REQ_F4116.1.1–6,5.1–6 | TODO |
| FI-AR-COLL-016 | State | Draft/Rejected edit/delete succeeds by version; Pending, Posted and soft-deleted edit/delete fail with named reason. | I,E | REQ_F4116.1.3,1.6,5.3,5.6 | TODO |
| FI-AR-COLL-017 | State | Pending rejection preserves reason/history and changes no GL/AR; zero-step final approval still posts in same transaction. | I,E | REQ_F4116.1.4–5,5.4–5 | TODO |
| FI-AR-COLL-018 | State | Unsaved, Draft, Rejected or deleted receipt cannot fund a settlement; Posted allocation cannot be changed or deleted. | I | REQ_F4116.5.2,5.4,5.6 | TODO |
| FI-AR-COLL-019 | Relation | Missing, inactive/deleted, nonpostable or wrong-company MD bank/account fails before GL; posted reference protects history. | I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-020 | Relation | Wrong customer/company/currency, credited/fully settled invoice or amount above invoice open balance fails with specific row reason. | I,E | REQ_F4116.5.2,5.4 | TODO |
| FI-AR-COLL-021 | Relation | Receipt approval leaves SD unbilled commitments and FI open AR unchanged; settlement changes only committed FI AR and credit snapshot. | I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-022 | Error | HTTP 400/409/422/500 with traceId, timeout and network loss display translated reason; unknown commit uses fresh source lookup. | I,E | REQ_F4116.1.1–6,5.1–6 | TODO |
| FI-AR-COLL-023 | Error | FI, GL, SYS, MD or audit failure after an intermediate write leaves no Posted receipt/settlement, GL or AR change. | I | REQ_F4116.1.4,5.4 | TODO |
| FI-AR-COLL-024 | Read/UI | Stable paged reads show original, allocated/unapplied, open AR and journal; empty/search-empty/denied states and foreign-company hiding. | I,E | REQ_F4116.1.1,5.1 | TODO |
| FI-AR-COLL-025 | UI | Three languages and light/dark normal, validation, success, unchanged, error and timeout states; receipt toast says unapplied, settlement toast matches fresh saved balances. | E | REQ_F4116.1.1–5,5.1–5 | TODO |
| FI-AR-COLL-026 | Database/audit | SQL unique source/reference constraints, ordered locks, FI FKs/indexes, checked balances, immutable Posted history and reversible migration. | I | REQ_F4116.1.2,1.4,5.2,5.4 | TODO |
| FI-AR-COLL-027 | File | No import/export in this slice: empty, malformed, oversized, wrong-encoding, missing/extra-column files are inapplicable. | — | REQ_F4116.1.1,5.1 | N/A |
| FI-AR-COLL-028 | Scope | Cross-currency, refund, bank reconciliation, bad-debt forgiveness and posted reversal are rejected until separate authorised contracts exist. | I,E | REQ_F4116.1.4,5.4 | TODO |

Traceability: REQ_F4116.1.1–6 → BR-FI-031–034,036,038–040 → FI-AR-COLL-001–002,005–006,008–010,012,014–017,019,021–028. REQ_F4116.5.1–6 → BR-FI-031–032,034–040 → FI-AR-COLL-003–007,009–018,020–028. All real SQL, HTTP and UI cases remain TODO.

## WU-FI-006d purchase payment and AP settlement scenarios (design; product tests TODO)

The `myerp-test-scenarios` normal, boundary, duplicate/conflict, partial, permission, state, relation, error and file categories are covered below. U=unit, I=real SQL/authorised HTTP, E=Playwright. Each E case asserts translated message, style, count and row reason against a fresh API/list read of payment, allocation, GL and open AP. WU-FI-006a/b/c first write failing tests, then implement; no executable payment product test is delivered here.

| ID | Class | Scenario and assertion | Level | Requirement | Status |
|---|---|---|---|---|---|
| FI-AP-PAY-001 | Normal | Save positive Draft payment; no journal, unapplied payment or open AP change before final approval. | I,E | REQ_F4116.3.2 | TODO |
| FI-AP-PAY-002 | Normal | SYS final approval posts one clearing debit/bank credit; unapplied equals posted amount while open AP stays unchanged. | I,E | REQ_F4116.3.4 | TODO |
| FI-AP-PAY-003 | Normal | Apply one payment to one payable; AP debit/clearing credit equals committed allocation and open AP decreases equally. | I,E | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-004 | Normal | Partial settlement of several payables leaves exact unapplied payment and open AP; a later settlement may use the remainder. | I,E | REQ_F4116.6.1,6.4 | TODO |
| FI-AP-PAY-005 | Boundary | Missing supplier/bank/reference/currency/date, zero/negative ID or amount, fifth money decimal, maximum DECIMAL(19,4) and overflow return field errors without writes. | U,I,E | REQ_F4116.3.2,6.2 | TODO |
| FI-AP-PAY-006 | Boundary | Month/year/leap-day saved dates resolve exactly one inclusive Open GL period; missing/Closed period leaves payment or settlement unposted. | U,I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-007 | Boundary | Zero or >1000 allocation lines, duplicate payable row, zero line and sum overflow reject all; exact final remainder succeeds. | U,I,E | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-008 | Duplicate/conflict | Same bank reference within company/bank, including deleted Posted history, returns 409; other bank or company is independent. | I | REQ_F4116.3.2,3.4 | TODO |
| FI-AP-PAY-009 | Duplicate/conflict | Identical committed payment/settlement replay returns stable IDs and changed=false with no extra journal/audit/AP effect; UI warns unchanged. | I,E | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-010 | Duplicate/conflict | Changed fingerprint, stale version or reuse of a deleted historical source key returns 409. | I | REQ_F4116.3.3,6.3 | TODO |
| FI-AP-PAY-011 | Duplicate/conflict | Two SQL connections allocate same payment or payable concurrently; loser rereads and cannot over-allocate. | I | REQ_F4116.6.4 | TODO |
| FI-AP-PAY-012 | Partial result | Invalid second row or injected failure after number, allocation, GL, SYS or audit write rolls back all changes; UI names the row and shows zero change. | I,E | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-013 | Partial result | Partial/all-skipped/all-failed/zero-row batch outcomes are inapplicable: one atomic settlement rejects zero lines and never silently skips. | U,I | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-014 | Permission | Anonymous 401, absent payment/settlement action permission 403; VIEW does not grant create/submit/final approval. | I,E | REQ_F4116.3.1–6,6.1–6 | TODO |
| FI-AP-PAY-015 | Permission | Cross-company and SYS out-of-scope payment/payable/bank IDs return 404; forged company/supplier/actor/amount cannot change FI. | I,E | REQ_F4116.3.1–6,6.1–6 | TODO |
| FI-AP-PAY-016 | State | Draft/Rejected edit/delete by version succeeds; Pending, Posted or deleted edits/deletes fail with state reason. | I,E | REQ_F4116.3.3,3.6,6.3,6.6 | TODO |
| FI-AP-PAY-017 | State | Pending rejection preserves reason/history without GL/AP effect; zero-step SYS final approval still posts in one transaction. | I,E | REQ_F4116.3.4–5,6.4–5 | TODO |
| FI-AP-PAY-018 | State | Unsaved, Draft, Rejected or deleted payment cannot fund settlement; Posted allocation remains immutable. | I | REQ_F4116.6.2,6.4,6.6 | TODO |
| FI-AP-PAY-019 | Relation | Missing/inactive/deleted/nonpostable or wrong-company MD bank/account/supplier fails before GL; Posted reference protects history. | I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-020 | Relation | Wrong supplier/company/currency, fully settled or credited payable, or row above open payable rejects whole settlement with specific row reason. | I,E | REQ_F4116.6.2,6.4 | TODO |
| FI-AP-PAY-021 | Relation | Payment approval leaves FI open AP unchanged; only committed allocations decrease it, without editing PUR source or original payable. | I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-022 | Error | HTTP 400/409/422/500 with traceId, timeout and network loss display translated reason; unknown commit requires fresh source lookup. | I,E | REQ_F4116.3.1–6,6.1–6 | TODO |
| FI-AP-PAY-023 | Error | FI, GL, SYS, MD or audit failure after intermediate write leaves no Posted payment/settlement, journal or AP change. | I | REQ_F4116.3.4,6.4 | TODO |
| FI-AP-PAY-024 | Read/UI | Stable paged scoped reads show saved original/allocated/unapplied/open AP and journal; empty/search-empty/denied states and foreign-company hiding. | I,E | REQ_F4116.3.1,6.1 | TODO |
| FI-AP-PAY-025 | UI | Three-language light/dark normal, validation, success, unchanged, server-error and timeout states; payment toast says unapplied, settlement toast matches fresh balances. | E | REQ_F4116.3.1–5,6.1–5 | TODO |
| FI-AP-PAY-026 | Database/audit | SQL unique permanent source/reference, supplier lock order, FI FKs/indexes, checked balances, immutable Posted rows, audit rollback and reversible migration. | I | REQ_F4116.3.2,3.4,6.2,6.4 | TODO |
| FI-AP-PAY-027 | File | No import/export exists in this slice; empty, malformed, oversized, wrong-encoding and missing/extra-column files are inapplicable. | — | REQ_F4116.3.1,6.1 | N/A |
| FI-AP-PAY-028 | Scope | FX settlement, refund, bank reconciliation, prepayment reclassification, debt forgiveness and reversal fail closed pending separate authorised contracts. | I,E | REQ_F4116.3.4,6.4 | TODO |

Traceability: REQ_F4116.3.1–6 → BR-FI-041–043,045,047–050 → FI-AP-PAY-001–002,005–006,008–010,012,014–017,019,021–028. REQ_F4116.6.1–6 → BR-FI-043–050 → FI-AP-PAY-003–007,009–018,020–028. All real SQL, HTTP and UI cases remain TODO.
