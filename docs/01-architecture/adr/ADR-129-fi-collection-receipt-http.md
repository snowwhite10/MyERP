# ADR-129: FI collection receipt HTTP routes, list query and permission catalogue; split 005b4

Date: 2026-11-15. Status: Accepted (WU-FI-005b4a).

## Context

WU-FI-005b4 covered HTTP for both receipts and settlements: routes, list queries, the `AR.COLLECTION.*` and `AR.SETTLEMENT.*` permission catalogue and seed roles, number-rule seeds, Swagger, BR-FI-039 status mapping (line errors) and real SYS end-to-end tests. Both halves together are more than 90 minutes of work. The services already exist (ADR-126/127/128). The planned route table in `api.md` (WU-FI-005d) listed FI `reject` routes, but approve and reject are SYS routes (`/api/v1/sys/approvals/{id}/approve|reject`) and the FI services offer submit and withdraw.

## Decision

- **Split.** 005b4a covers receipts (this unit). 005b4b covers settlements: routes, list, `AR.SETTLEMENT.*` catalogue, `lineErrors` in the 422 body (BR-FI-039) and SYS end-to-end tests.
- **Routes.** `/api/v1/ar/sales-collections`: GET list, GET `{id}`, POST, PUT `{id}` (body carries `version`), DELETE `{id}?version=`, POST `{id}/submit` and POST `{id}/withdraw` (body `{version}`). There is no FI approve or reject route; the final decision is SYS-owned and triggers the FI handler from ADR-128.
- **Permissions.** `AR.COLLECTION.VIEW/CREATE/EDIT/DELETE/SUBMIT` are declared in `FinancePermissions`. Seed roles follow the existing rules: FINANCE gets all, MANAGER and VIEWER get VIEW only. Approvers need `SYS.APPROVAL.PROCESS`, not an FI code.
- **List.** `CollectionReceiptQueryService`: company-wide scope like other FI documents (ADR-075/126), live rows only, filters customer, fund account, status (Deleted rejected with 400), inclusive business-date range and keyword (document number or bank reference, at most 50 characters). Sort is one of `businessDate` (default descending), `documentNo`, `amount`, each with ID as tie-break. Customer labels come from one MD batch per page; fund-account labels from `IFundAccountLookup.FindByIdAsync` once per distinct ID on the page (a page has few accounts, and MD has no batch method yet).
- **Posted balance.** List rows and the detail show `unappliedAmount` (and the detail `journalId`) from FI's own `ar_collection_receipt` row; null until Posted. No other module table is read.
- **Number rule seed: deferred.** ADR-023 leaves number rules to business-module seeds, but no module seeds them yet and FI must not write SYS tables directly. A missing rule returns 422 `SYS.NUMBER_RULE.NOT_CONFIGURED` (tested). The Development seed of `AR.COLLECTION`/`AR.SETTLEMENT` rules moves to WU-FI-005c, where the real-API E2E needs them; it will use the SYS API or a new SYS seeding contract.
- **Tests.** `SalesCollectionReceiptHttpTests` (FI-COLL-HTTP-001～009) use the real API host, SQL, SYS authorization, number generator and approval engine. Setup writes company, currencies, customer and number rule directly; accounts, fund accounts, GL period and collection map go through their APIs.

## Consequences

- 005b4b repeats the pattern for settlements and adds the BR-FI-039 line-error body.
- 005c builds the screens on these routes and seeds the number rules for the demo company.
