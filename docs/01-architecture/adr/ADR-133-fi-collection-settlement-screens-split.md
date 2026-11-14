# ADR-133: FI collection settlement screens — list first, allocation form next

Date: 2026-11-15. Status: Accepted (WU-FI-005c2b1).

## Context

WU-FI-005c2b covered the whole settlement UI: the list, the detail with lines, the state actions, and the allocation form (choose a posted collection, choose open receivables through the ADR-132 API, type an amount per line, total equals header, `lineErrors` per line). Together these are about twice the size of WU-FI-005c1, which already took a full round. MASTER_PROMPT section 1 says a unit estimated above 90 minutes must be split first.

## Decision

- **Split.** WU-FI-005c2b is split into:
  - **005c2b1:** `/ar/collection-settlements` list, detail with lines, Submit / Withdraw / Delete, and the error and `lineErrors` display for refused actions (interface 6.29).
  - **005c2b2:** the create / edit allocation form (interface 6.30) and the list's New / Edit entry points.
  The parent does not count.
- **Menu and permissions.** Finance → Collection settlements, shown only with `AR.SETTLEMENT.VIEW`; each row action needs its own `AR.SETTLEMENT.*` permission. Receipt permissions grant nothing here, matching the API (ADR-130).
- **Line errors on the list.** A submit can be refused with 422 `FI.COLLECTION_WRITEOFF.ALLOCATION_INVALID` and `lineErrors` (the posting re-check, BR-FI-040). The list shows the banner and then one line per refused allocation: "Line n (receivable #id): reason". Reasons are i18n keys under `errors.FI.COLLECTION_WRITEOFF.LINE.*`. An unknown reason falls back to a generic line text, so the screen never shows a raw code.
- **Receivable label.** Lines show `#receivableId`. SD invoice numbers are not joined (ADR-132). If the 005c2b2 UX review finds this unclear, a source label can come from an SD contract later.
- **Failed list read.** When the list read fails, the table says the list could not be loaded instead of the empty-state text. This came from the UX review. The receipt page (005c1) has the same gap, which is recorded as backlog WU-FI-005c1-LOADFAIL (P2).

## Consequences

- Settlements created through the API can already be reviewed and moved through approval on screen. Creating them on screen arrives with 005c2b2.
- 005c3 (number-rule seed and real-API E2E) now depends on 005c2b2.
