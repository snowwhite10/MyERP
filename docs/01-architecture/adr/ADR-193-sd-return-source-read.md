# ADR-193: Sales return source reads and frontend service (WU-SD-008b1)

Date: 2026-11-24. Status: Accepted (WU-SD-008b1).

## Context

WU-SD-008b (return pages) is too large for one round. The return form needs to know which orders and delivery lines
can still be returned, how much of each is unbilled and which invoice lines can still be credited, before CREATE
rechecks them under lock. api.md (008d design) sketched `GET /api/v1/sal/deliveries/{id}/returnable-lines`, but the
invoice form already picks a source *order* (ADR-181) and a return is restricted to one order (SOURCE_MIXED).

## Decision

1. Split 008b: **008b1** source reads + frontend `return.service`; **008b2** list/detail pages; **008b3**
   create/edit/submit pages, Playwright (mock + real API) and UX QA.
2. Source reads follow the invoice pattern under the return route, both requiring `SAL.RETURN.CREATE` and applying
   its data scope to the order exactly like CREATE:
   - `GET /api/v1/sal/returns/source-orders` — Approved orders with at least one *untracked* Posted delivery line whose
     quantity exceeds Posted returns; keyword (order no., customer code/name), customerId, sort documentNo/businessDate
     (default `-businessDate`, ID tie-break); `openLineCount`.
   - `GET /api/v1/sal/returns/source-orders/{orderId}` — header (customer, currency, base currency, rate, version)
     and each returnable line: delivery no./date/line, order line, item, delivery warehouse/location (default
     destination), `trackingUnsupported`, posted / returned / returnable / invoiced / unbilledRemaining quantities and
     the Posted invoice lines with an uncredited quantity (`quantity`, `creditedQuantity`, `remainingQuantity`).
   - Missing / foreign / deleted / out-of-scope order → 404 `SAL.RETURN.SOURCE_NOT_FOUND`; not Approved → 422
     `SAL.RETURN.ORDER_NOT_APPROVED`.
3. Only **Posted** returns consume balances (BR-SD-044): Drafts, Pending and deleted Drafts reserve nothing — the same
   rule as CREATE, so the form never offers less than CREATE accepts. Balances are advisory.
4. Tracked (lot/serial) lines stay visible in the detail with `trackingUnsupported = true` so the form can explain
   why they cannot be selected (MASTER_PROMPT 20.1); they are not counted in `openLineCount`, and an order whose only
   returnable lines are tracked is not listed.
5. A fully credited invoice line is omitted from `invoiceLines`; the remaining part of the line is unbilled-only.
6. The sketched `deliveries/{id}/returnable-lines` route is not built.
7. Frontend `SalesReturnService` (`modules/sd/return.service.ts`): list/get/sourceOrders/sourceOrder/create/replace/
   delete/submit; money, rates and quantities kept as decimal text; draft JSON built with exact number literals
   (`returnDraftJson`); create distinguishes 201 from replay 200; writes allow 20 s.

## Consequences

- No migration. Tests SD-RETURN-SRC-001～004 (real SQL), SD-RETURN-HTTP-006, SD-RETURN-SVC-001～006 (Vitest).
- Pages and i18n texts for the source picker come in 008b2/008b3.
