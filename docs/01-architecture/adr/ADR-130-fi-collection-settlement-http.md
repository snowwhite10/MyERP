# ADR-130: FI collection settlement HTTP routes, list query and permission catalogue

Date: 2026-11-15. Status: Accepted (WU-FI-005b4b).

## Context

ADR-129 split WU-FI-005b4. The receipt half (005b4a) is done. This unit adds HTTP for settlement documents. The services already exist: draft create/replace/delete/read (ADR-127) and submit/withdraw plus the SYS `AR.SETTLEMENT` handler (ADR-128). BR-FI-039 requires line-level reasons when a settlement cannot be posted.

## Decision

- **Routes.** `/api/v1/ar/collection-settlements`: GET list, GET `{id}`, POST, PUT `{id}` (body carries `version`; lines are replaced as a whole), DELETE `{id}?version=`, POST `{id}/submit` and POST `{id}/withdraw` (body `{version}`). Like receipts, there is no FI approve or reject route; SYS owns the decision.
- **Permissions.** `AR.SETTLEMENT.VIEW/CREATE/EDIT/DELETE/SUBMIT` are declared in `FinancePermissions`. FINANCE gets all; MANAGER and VIEWER get VIEW only (existing seed rule). Receipt permissions do not grant settlement actions.
- **Line errors.** The services already throw `BusinessRuleException` with a `lineErrors` extension (`[{line, receivableId, reason}]`); the shared ProblemDetails factory copies extensions into the body. No new code was needed; the HTTP tests prove it for create and for a failed SYS approval.
- **List.** `CollectionSettlementQueryService`: company-wide scope, live rows only. Filters: customer, receipt (receipt document ID), status (Deleted is 400), inclusive business-date range and keyword (document number, at most 50 characters). Sort is one of `businessDate` (default descending), `documentNo`, `totalAmount`, with ID as tie-break. Rows carry the receipt document number and the line count from FI tables in the same query, and customer labels from one MD batch per page.
- **No `IgnoreQueryFilters` in the list projection.** EF Core applies `IgnoreQueryFilters` to the whole query, so using it in the receipt-number subquery removed the company filter from the settlement rows too (found by FI-COLL-SHTTP-004: another company's rows appeared). The subquery uses the normal filters. A settlement only references a posted receipt, and posted receipts cannot be deleted, so the number is always found.
- **Tests.** `CollectionSettlementHttpTests` (FI-COLL-SHTTP-001～009) use the real API host, SQL, SYS authorization, number generator and approval engine. Setup posts a receipt through the receipt HTTP routes (zero-step flow) and writes invoice receivables directly into FI's own `ar_posted_receivable` (invoice posting has its own tests).

## Consequences

- WU-FI-005b4 is complete. WU-FI-005c builds the receipt and settlement screens on these routes and seeds the `AR.COLLECTION`/`AR.SETTLEMENT` number rules for the demo company (ADR-129).
