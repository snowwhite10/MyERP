# ADR-186: Split WU-SD-008a2b; Draft sales return edit and soft delete (WU-SD-008a2b1)

Date: 2026-11-23. Status: Accepted (WU-SD-008a2b1).

## Context

WU-SD-008a2b (ADR-185 §1) bundles Draft edit, Draft soft delete and the scoped detail/list query service. Edit has to
re-run every creation check under the same locks, so the creation logic must first be shared; together with the query
service this exceeds one work unit. The saved return's request key and fingerprint are frozen by trigger 51035.

## Decision

1. Split 008a2b into **008a2b1** (this unit: shared `SalesReturnDraftBuilder`, `SalesReturnDraftCommandService`
   `UpdateAsync`/`DeleteAsync`) and **008a2b2** (scoped detail/list query service, `SAL.RETURN.VIEW`).
2. `SalesReturnDraftBuilder` owns source resolution, the source line locks (delivery lines then invoice lines, ascending),
   the eligibility and BR-SD-044 balance checks, the BR-SD-046 amount snapshot and the MD destination guard. Create and
   edit call the same code, so both refuse and snapshot identically.
3. Edit (`SAL.RETURN.EDIT`) replaces the date, remark and every line of a **Draft** with the same request shape as
   create. Lock order: `sal_order` row, `sal_return` row, delivery lines, invoice lines (all `UPDLOCK, HOLDLOCK`).
   Checks in order: header exists in the company and is not deleted, the order is visible in the EDIT scope (else 404
   `SAL.RETURN.NOT_FOUND`), the row version matches (else 409 `COMMON.CONCURRENCY.CONFLICT`), the status is Draft (else
   422 `SAL.RETURN.INVALID_STATUS`), the request key equals the saved key (else 422 `SAL.RETURN.REQUEST_KEY_IMMUTABLE`),
   and every line comes from the saved order (else 422 `SAL.RETURN.SOURCE_MIXED` with `lineNo`). Then the full creation
   checks and snapshot run again against committed Posted returns, excluding nothing (a Draft never consumes balance).
4. The request key and fingerprint keep identifying the **creation** request (BR-SD-048): a create retry with the
   original payload still replays the saved, possibly edited, return. An edit has no idempotency key; a retried edit
   carries a stale version and gets 409, which the client resolves by reloading.
5. Delete (`SAL.RETURN.DELETE`) soft-deletes a Draft (`is_deleted`, `deleted_at = IClock.LocalNow`) under the order and
   return row locks; it does not require the order to be Approved. A deleted return is 404 to edit and delete, and its
   request key stays reserved (409 on create).
6. Rejected → Draft revision (BR-SD-043) needs the SYS approval rounds and is delivered with the submission work
   (008a5); until then Pending, Rejected and Posted returns are refused with `SAL.RETURN.INVALID_STATUS`.

## Consequences

- No schema change. 008a6 maps `NOT_FOUND`/`INVALID_STATUS`/`REQUEST_KEY_IMMUTABLE` to HTTP 404/422.
- 008a5 adds Rejected revision to `SalesReturnDraftCommandService` and reuses `SalesReturnDraftBuilder` for the
  submission recheck.
