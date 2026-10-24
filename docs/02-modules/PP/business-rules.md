# PP — Production business rules

## WU-PP-000d baseline

| Rule | Design invariant |
|---|---|
| BR-PP-001 | PP stores company/site scope and saved MD item/unit identities. It calls MD Application contracts to validate active selections; historical snapshots survive later deactivation. |
| BR-PP-002 | BOM revisions are immutable after approval or reference. Effective dates cannot overlap for the same parent/site. Grouping is categorisation only. |
| BR-PP-003 | BOM graph traversal rejects direct and transitive cycles, including concurrent revision approval; explosion uses decimal base-unit quantities and explicit scrap/yield policy. |
| BR-PP-004 | Routing revision is selected by site and effective date, and frozen on release. Work-centre calendars and operation duration determine capacity; no floating-point business arithmetic. |
| BR-PP-005 | MPS reads authorised, committed SD demand and committed supply through owner Application contracts. Each owner cutoff and unavailable/incomplete status is retained. |
| BR-PP-006 | MRP uses a frozen demand/stock/supply manifest. A source line is counted once; pegging links proposed quantity to demand and BOM revision. Proposed purchase/work orders do not become committed supply until owner confirmation. |
| BR-PP-007 | SYS owns approval and numbering. PP records its state and immutable approval references. Approval is not a GL or INV posting. |
| BR-PP-008 | PP owns requisition and completion intent. INV owns all Posted issue/receipt/reversal movements, allocation, lot/serial and valuation. PP holds confirmed movement IDs and quantities via an INV Application contract, never updates INV tables. |
| BR-PP-009 | A permanent company/source-type/source-line key makes release, issue and receipt requests idempotent. Unknown outcome is reconciled with INV by key before retry; no optimistic successful stock count. |
| BR-PP-010 | Requested, approved, issued, reversed, completed, accepted and Posted quantities are separate measures. Shortage, rejection, overproduction and partial confirmation carry line reasons/counts. |
| BR-PP-011 | A released work order or confirmed stock link is corrected by controlled revision/reversal, not edited or deleted. Cancellation cannot erase Posted movements. |
| BR-PP-012 | PP report source exposes saved plans, work orders, requisitions and completions with confirmed INV references at a committed cutoff to BI; BI may not read PP or INV tables. |
| BR-PP-013 | Every operation checks current SYS permission, company and site scope. Missing permission is 403; out-of-scope identity is hidden as 404. No cached manifest overrides revocation. |
| BR-PP-014 | Result messages distinguish zero changes, all skipped, partial success, all failed and unknown outcome; every skipped/failed line has a reason. |

Open implementation decisions: choose capacity calendar granularity, scrap/yield rounding, MRP horizon and lot-size policies in the relevant implementation slices with ADRs if they affect shared contracts. This design creates no production records.

## WU-PP-001d Released plan demand snapshot

| Rule | Design invariant |
|---|---|
| BR-PP-015 | PP is the sole owner of a Released plan and its line revisions. SYS final approval freezes the Approved plan, while a separate authorised release transition makes its lines eligible for this reader. Approval alone is not Released, and neither transition posts INV stock or GL. Release must preserve a permanent plan ID, line ID, revision, source key and actor/time. |
| BR-PP-016 | A released line records company, site, item, saved business date/month, source UOM and positive decimal quantity at six-place precision. The reader returns the saved raw quantity and UOM; MD supplies separate conversion evidence. A missing or changed MD reference never rewrites a historical PP row. |
| BR-PP-017 | A query fixes one company, authorised site set, item filter and inclusive-from/exclusive-to closed monthly range. PP certifies every requested bucket as Complete, including explicit zero-row buckets. An unknown site or incomplete release history is Unavailable with a reason, never a Complete empty bucket. |
| BR-PP-018 | An opaque committed cursor is issued only after all included PP changes commit. It fixes membership, values, state and stable ordering by `(businessMonth, planId, lineId, revision)`; every page returns identical cursor, schema version, row count and canonical digest. Page tokens bind cursor and filters. Partial or corrupt replay is `ReplayUnavailable`, not a smaller Complete result. |
| BR-PP-019 | A cancellation, amendment or backdated release appends a committed correction revision with original lineage. A new cursor reflects the correction exactly once; old cursors remain unchanged. SCM uses Released PP demand as an optional comparison overlay and never adds it blindly to Approved SD demand. |
| BR-PP-020 | PP retains exact replay evidence and cursors at least 90 days after issuance. Expired, malformed or foreign cursors cannot silently refresh to head. PP rechecks the caller's action permission, company and site data scope before each page, including replay; narrower scope revokes access to the cursor without disclosing counts or digest. |
| BR-PP-021 | Availability is Complete, Partial or Unavailable with a machine reason. OwnerMissing is reported by the caller when no PP backend exists; it is never represented by a PP response. Timeout, failed page or missing conversion evidence is not zero demand. No batch mutation occurs in this read. |

These rules are design only. The production owner project and persistent cursor mechanism are prerequisites of WU-PP-001c.
