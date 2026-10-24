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
