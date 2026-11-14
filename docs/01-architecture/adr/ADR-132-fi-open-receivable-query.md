# ADR-132: FI open receivable read API for settlement lines

Date: 2026-11-15. Status: Accepted (WU-FI-005c2a).

## Context

WU-FI-005c2 builds the settlement form, where the user picks receivables of the receipt customer and types an amount per line. No FI route listed a customer's open receivables (`ar_posted_receivable`): the settlement detail only shows the open amount of receivables already on the settlement. Without a read API the form cannot offer choices.

## Decision

- **Split.** WU-FI-005c2 is split into 005c2a (this backend read slice) and 005c2b (screens). The parent does not count.
- **Route.** `GET /api/v1/ar/collection-settlements/open-receivables` under the settlement controller, permission `AR.SETTLEMENT.VIEW` (the list only exists to build settlements; receipt permissions grant nothing). `customerId` is required (400 when missing or not positive) so the route never lists all receivables of the company.
- **Data.** `OpenReceivableQueryService` reads FI's own `ar_posted_receivable` only (company query filter applies), rows with open base amount > 0. Output `{receivableId, sourceType, sourceId, postingDate, currency, originalAmount, openAmount}` in company base currency. Source document numbers (SD invoice numbers) are not joined: FI must not read SD tables; the screen shows source type and ID. A number lookup via an SD contract can be added later if the UX review asks for it.
- **Filters and sort.** `postedOnOrBefore` (inclusive) lets the form hide receivables posted after the settlement date. One sort field: `postingDate` (default ascending, oldest first, the usual settlement order) or `openAmount`; ID breaks ties. Standard paging (max 200).
- **No reservation.** Drafts and pending settlements reserve nothing (BR-FI-157), so the open amount is the committed balance; posting re-checks it and returns `lineErrors` (BR-FI-039).
- **Index.** The existing `ix_ar_posted_receivable_company_id_customer_id` serves the query; per-customer row counts are small. No migration.

## Consequences

- WU-FI-005c2b can build the settlement list and allocation form on ADR-130 routes plus this one.
