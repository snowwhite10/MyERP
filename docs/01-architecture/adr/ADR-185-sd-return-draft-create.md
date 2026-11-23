# ADR-185: Split WU-SD-008a2; Draft sales return creation service (WU-SD-008a2a)

Date: 2026-11-23. Status: Accepted (WU-SD-008a2a).

## Context

WU-SD-008a2 (ADR-184 §1) bundles Draft create, edit, delete and view. Creation alone needs source eligibility,
BR-SD-044 balances under locks, remaining-proportional money snapshots, MD destination guard and request-key replay,
which already fills one work unit. Edit/delete/view reuse the same checks and add version concurrency.

## Decision

1. Split 008a2 into:
   - **008a2a** (this unit): `SalesReturnCreateService.CreateAsync(SalesReturnDraftInput)` plus the pure
     `SalesReturnDraftInput` validation/fingerprint and `SalesReturnAmounts` allocation; number rule `SAL.RETURN`
     (prefix `SR`). Real SQL SD-RETURN-DRAFT-001～006, unit SD-RETURN-U01～U06.
   - **008a2b**: Draft update (row version 409, same request-key rules, rechecked balances), Draft soft delete and
     scoped detail/list query service. Real SQL first.
2. Request shape: `requestKey`, `businessDate`, `remark`, lines of `{lineNo, deliveryLineId, warehouseId, locationId,
   unbilledQuantity, billed[{invoiceLineId, quantity}]}`. The client never sends the line quantity, customer, order,
   currency, amount, cost, status or INV/FI identity; the line quantity is `unbilled + Σ billed`. The split between
   billed and unbilled is explicit (the user chooses which invoice lines are credited) rather than inferred.
3. Eligibility: all lines from Posted (not deleted) deliveries of one Approved order visible under the
   `SAL.RETURN.CREATE` data scope; each delivery line has its outbound movement; each billed portion is a Posted invoice
   line of the same delivery line; return date ≥ delivery date and ≥ invoice date; lot/serial tracked delivery lines or
   items are refused (`SAL.RETURN.TRACKING_UNSUPPORTED`, ADR-184 §6). Unknown, other-company or out-of-scope sources are
   404 `SAL.RETURN.SOURCE_NOT_FOUND`.
4. Locks and balances (BR-SD-044/047, Read Committed): `sal_order` row, then delivery lines, then invoice lines, each
   `UPDLOCK, HOLDLOCK` ascending by ID. Only **Posted** returns consume balances — Draft, Pending, Rejected and
   soft-deleted returns reserve nothing; the 008a5 submission rechecks under the same locks. Limits:
   returned ≤ delivered − Posted returned; unbilled ≤ delivered − invoiced − Posted unbilled; billed portion ≤
   invoice-line quantity − Posted billed portions. Errors carry `lineNo`, `invoiceLineId` and `remaining`.
5. Amount snapshots (BR-SD-046): billed portions are a share of what is left of the invoice line, unbilled portions a
   share of what is left of the order line (order line − invoice lines − Posted unbilled return parts). Each component
   is `Money.Round(remaining × qty ÷ remainingQty)` in its currency (base components in the base currency); a portion
   that takes the whole remaining quantity takes the exact remaining amounts. Gross = net + tax; header = line sums.
6. Replay (BR-SD-048): the request key is looked up including soft-deleted rows. Identical fingerprint on a live,
   visible return → the saved return, `Replayed = true`, nothing written. Different fingerprint, soft-deleted Draft or a
   return whose order the caller cannot see → 409 `SAL.RETURN.REQUEST_CONFLICT`. The lookup is repeated after the locks
   so a concurrent identical request replays; a unique-key race on insert is also 409.
7. The number is taken and the row inserted inside the innermost MD destination guard; any failure rolls back the
   number and all rows. The service never calls INV or FI.

## Consequences

- 008a2b, 008a5 and 008a6 build on this request shape and error codes; HTTP mapping is 008a6.
- Because Drafts reserve nothing, two Drafts may together exceed a balance; the later submission is refused.
- SQL Server error 130 forbids `SUM` over an applied aggregate; allocation sums are pre-aggregated in a derived table.
