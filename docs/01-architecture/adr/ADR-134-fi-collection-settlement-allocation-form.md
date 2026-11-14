# ADR-134: FI collection settlement allocation form

Date: 2026-11-15. Status: Accepted (WU-FI-005c2b2).

## Context

WU-FI-005c2b2 adds the create / edit form for collection settlements (interface 6.30): choose a posted collection, choose the same customer's open receivables (ADR-132 API), type an exact amount per line. The server already checks everything (BR-FI-157) and returns 400 `lines[i].*` or 422 with `lineErrors`. Choices were needed on how the screen picks, totals and reports.

## Decision

- **Collection first.** The collection picker lists `GET /api/v1/ar/sales-collections?status=Posted` with the current unapplied cash. The chosen collection decides the customer; choosing a collection of another customer drops the chosen lines (they cannot belong to it). The customer is not sent; the server derives it.
- **Date.** Defaults to today; if earlier than the chosen collection's date it is moved to that date. The screen refuses a date before the collection date when it knows it (only after choosing the collection on this screen; on edit the server check is used).
- **Receivables as of the date.** "Add receivables" opens a multi-select of the customer's open receivables with `postedOnOrBefore` = settlement date, so receivables that the date cannot settle are not offered. Already chosen receivables are skipped.
- **Prefill.** A new line starts with its open amount, capped by the collection's unapplied cash left after the lines above it (0 → empty). The user edits the exact text.
- **Exact totals.** Amounts are text; the total, the collection's unapplied cash and "unapplied left after posting" are computed in 1/10000 units with BigInt, never through a JavaScript double. Amounts are sent as JSON numbers from the validated digits (same as receipts, ADR-131).
- **Screen checks** mirror BR-FI-157 where the numbers are known: 1–1000 lines, amount format (≤15 integer digits, ≤4 decimals), > 0, not above the line's open amount, total not above the unapplied cash. The server stays authoritative.
- **Server errors on lines.** 400 `lines[i].*` maps by request index (the screen sends lines in screen order). 422 `lineErrors` maps by `receivableId`, because the server orders lines by receivable ID. Collection-level 422 codes (`RECEIPT_NOT_POSTED / MISMATCH / INSUFFICIENT`) are also shown under the collection field.
- **Refresh after a line refusal (UX review).** When the server refuses lines, the form re-reads the customer's open receivables (all dates, 200 per page, at most 5 pages) and updates "Open now"; a receivable no longer listed shows 0. Amounts typed by the user are never changed.
- **Messages follow edits (UX review).** Changing a line amount removes that line's reason and re-checks the total message, so no message describes a value no longer on screen.
- **Saved notice.** Saving returns to the list with "Draft settlement … was saved: amount, line count. Nothing is applied until it is submitted, approved and posted." The success style is used because a draft was really saved; the text says nothing was applied.
- **Receivable label.** Lines chosen on this screen show `#id` plus "Invoice #sourceId" and the posting date from the open-receivable API. Lines loaded for edit show `#id` and the current open amount only (the detail API has no source fields). A source label on edit is left as P3.

## Consequences

- Settlements can be created and edited on screen; WU-FI-005c3 (number-rule seed and real-API E2E) is next.
- The refresh reads at most 1000 open receivables; a customer with more keeps the rest at their earlier amounts until posting re-checks them.
