# ADR-195: Sales return detail submit / delete and split of WU-SD-008b3b

Date: 2026-11-24. Status: Accepted (WU-SD-008b3b1).

## Context

WU-SD-008b3b (ADR-194) bundles the edit page `/sal/returns/:id/edit`, the 2.16 detail Submit / Delete buttons and a
return-scoped destination warehouse / location choice. Together they are larger than one round. Submit and delete
only need the existing `POST /api/v1/sal/returns/{id}/submit` and `DELETE /api/v1/sal/returns/{id}?version=` (008a/b1)
and the saved detail already shown on 2.16, so they can ship independently.

Submit is riskier than a delivery submit: with no applicable SYS step the server posts at once, receiving goods into
INV and crediting invoiced quantity in FI in one transaction. A reply or failure the page cannot interpret must never be
shown as "nothing happened" or as success.

## Decision

1. Split 008b3b: **008b3b1** (this ADR) detail Submit / Delete; **008b3b2** edit page (Draft and Rejected revision,
   version 409, `REQUEST_KEY_IMMUTABLE`) with the "Edit" button and the destination choice (new return-scoped
   warehouse-options read). 008b3c (Playwright + UX QA) is unchanged.
2. The detail follows the delivery action pattern (ADR-150): buttons only for a Draft and the matching permission
   (`SAL.RETURN.SUBMIT`, `SAL.RETURN.DELETE`), a confirmation first (delete is a danger confirmation), the loaded version
   sent, success only when the reply is this return (same ID and number), a newer version and Pending or Posted.
   Pending says nothing was received or credited yet; Posted says goods were received and invoiced quantity credited.
   Delete reloads the list and says no stock or receivable was affected.
3. Version conflict (409 other than `SAL.RETURN.SNAPSHOT_STALE`), 404, `INVALID_STATUS`, timeout, network loss and an
   uncoded 5xx pause both actions until the user refreshes (focus moves to Refresh). `SNAPSHOT_STALE` is a definite
   refusal: the message tells the user to edit the return and submit again, and actions stay available.
4. A 5xx carrying one of the codes the server only raises after rolling the whole submission back
   (`SOURCE_INVALID`, `INV_POSTING_CONFLICT`, `APPROVAL_RESULT_INVALID`, `FI_RECEIPT_MISMATCH`, `FI_POSTING_FAILED`) is
   a certain outcome: the named reason, trace ID and "still Draft" are shown instead of the uncertain-outcome pause.
5. A 422 `lineNo` that matches a saved line marks that line and shows the reason under it; an unknown line number is
   not marked, the reason is still shown.
6. A Rejected return shows a hint (for users with SUBMIT or EDIT) that it must be revised and saved before submitting.

## Consequences

- No backend change. Until 008b3b2, a Draft can be submitted or deleted from the page but not edited, and a Rejected
  return can only be revised through the API.
