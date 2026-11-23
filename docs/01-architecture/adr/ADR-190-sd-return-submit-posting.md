# ADR-190: Split WU-SD-008a5; sales return SUBMIT and the shared posting step (WU-SD-008a5a)

Date: 2026-11-23. Status: Accepted (WU-SD-008a5a).

## Context

WU-SD-008a5 covers SUBMIT, zero-step SYS approval, the SYS final decision handler (approve → post, reject →
Rejected), Rejected → Draft revision and the INV/FI/commitment posting step. That is more than one work unit. The FI
credit service (ADR-113/189) and the INV `SALES_RETURN` inbound (ADR-188) exist. Drafts reserve nothing (BR-SD-044),
so a Draft's amount snapshot can be out of date at submission when another return or an invoice of the same source
lines was posted in between; once Pending, return lines are frozen by trigger 51036.

## Decision

1. Split 008a5 into **008a5a** (this unit: shared posting step + SUBMIT incl. zero-step) and **008a5b** (SYS
   `IApprovalResultHandler` for `SAL.RETURN`: final approval reruns the same recheck/post under the same locks,
   rejection → Rejected; Rejected → Draft revision keeping SYS rounds). Parent 008a5 is not counted.
2. `SalesReturnPosting` (SD Application), caller-owned Read Committed transaction:
   - `RecheckAsync`: rebuilds the request from the saved lines (destination, unbilled quantity, billed portions),
     takes the builder's delivery-line then invoice-line locks and reruns the Draft checks (order Approved, sources
     Posted, dates, balances against committed Posted returns) and the snapshot. A different snapshot is
     **409 `SAL.RETURN.SNAPSHOT_STALE`**: the Draft must be saved again (edit re-snapshots); SD never silently changes
     amounts the user saw. Quantity refusals keep their Draft codes (`RETURN_EXCEEDED` etc., 422 with `lineNo`).
   - `PostAsync` (header already saved Pending): INV `PostSalesReturnInboundAsync(returnId, date, lines by line no
     with original outbound movement)`; a replay, a missing/foreign line or non-positive movement is
     409 `SAL.RETURN.INV_POSTING_CONFLICT`; stamps every line's `inv_movement_id` and saves (header still Pending,
     ADR-184 §3); if any billed allocation exists calls FI `ISalesCreditPostingService` with the current header
     version and fingerprint, maps FI Rejected 404/409/422 to the same status keeping FI's code (422 adds
     `creditLineNo`), any other result to 500 (`SalesReturnPostingException`, FI code or
     `SAL.RETURN.FI_POSTING_FAILED`); verifies the receipt (company, return, customer, version, fingerprint, currencies,
     billed gross/base gross, every allocation) else 500 `SAL.RETURN.FI_RECEIPT_MISMATCH`; sets `fi_credit_note_id`
     = receipt FI source ID; then Posted with `IClock` time. A wholly unbilled return never calls FI.
3. `SalesReturnSubmissionService.SubmitAsync(id, version)`: `SAL.RETURN.SUBMIT`, exact action scope on the source
   order (hidden → 404 `SAL.RETURN.NOT_FOUND`). Lock order (BR-SD-047): FI customer credit key → order row → return
   row → delivery lines → invoice lines. Version 409; only Draft (else 422 `SAL.RETURN.INVALID_STATUS`). After the
   recheck, MD destinations are revalidated under their shared locks; SYS `SubmitAsync` (document type `SAL.RETURN`,
   amount = return base gross) saves its instance; the header becomes Pending with the instance ID; if SYS approved
   at once (no applicable step) the posting step runs in the same transaction (Draft → Pending → Posted). Any
   exception rolls back SYS, INV, FI and SD together (an FI fault has already rolled back).
4. SD commitment release is derived, like invoices (ADR-178): `SalesOrderCreditCheck.CommitmentsAsync` (extracted,
   used by `AssessAsync`) subtracts, per Approved order, the unbilled part of Posted returns
   (`return base_gross − Σ allocation base_gross`). Billed parts reduce FI open AR instead; nothing is released twice.

## Consequences

- No schema or permission-table change; new error codes `SAL.RETURN.SNAPSHOT_STALE`, `INV_POSTING_CONFLICT`,
  `FI_RECEIPT_MISMATCH`, `FI_POSTING_FAILED`, `APPROVAL_RESULT_INVALID` need i18n in 008a6/008b.
- Tests SD-RETURN-POST-001～005 use real SQL with real SD, SYS, FI (credit note, AR, GL) and commitments; INV is a
  recording double of its public contract (real inbound proven by INV-SRET-001～005).
- 008a5b must take the same lock order inside the SYS decision transaction and reuse `RecheckAsync`/`PostAsync`.
