# ADR-168: Pricing error-state UX QA round (WU-SD-005b3b)

Date: 2026-11-22. Status: Accepted (WU-SD-005b3b).

## Context

ADR-166 split the pricing QA into a real-API E2E (005b3a, done) and this error-state UX round over Interfaces
2.2/2.3 (quotation form), 2.5/2.6 (manual order form) and 2.11–2.13 (price rule list and form). 005b3a also found that
the quotation form explained `422 SYS.NUMBER_RULE.NOT_CONFIGURED` as "the server rejected customer, item, currency or
tax references" — wrong and misleading.

## Decision

- New simulated-server spec `tests/e2e/specs/sales-pricing-qa.spec.ts` (SD-PRICE-QA-001～004). Each state asserts the
  screen text and what the simulated server received (quote calls, POST count, saved body):
  - 2.2 and 2.5 line quote: 500 with trace ID, network loss, 409 `SAL.PRICING.DUPLICATE_ACTIVE_RULE`, 403, timeout;
    every state keeps the "look up price again" button; save refused with `SYS.NUMBER_RULE.NOT_CONFIGURED`.
  - 2.11 list: timeout, network, 403 (500 and empty were covered by SD-PRULE-UI-102/104).
  - 2.12 save: 403 (retry allowed), network and timeout (outcome unknown, save locked, no success).
- Fixes:
  - **Quote 5xx trace ID (P2)**: `LinePricing.failed` now carries `traceId` (`apiErrorTraceId`, 5xx only) and both
    forms show `Trace ID: …` after the line message (MASTER_PROMPT 20.1).
  - **Number rule gap (P2)**: quotation save and order create save list `SYS.NUMBER_RULE.NOT_CONFIGURED` as a known
    code, so the existing three-language `errors.SYS.NUMBER_RULE.NOT_CONFIGURED` text ("nothing was saved; ask an
    administrator to add a number rule") replaces the generic "rejected" messages. Order edit never allocates a number.
- 2.3 / 2.6 / 2.13 use the same components and error mappers as 2.2 / 2.5 / 2.12 (edit mode of the same page), so
  their error states were not captured again.

## Consequences

- Frontend only; no backend change, no migration.
- Remaining P3 items go to backlog WU-SD-005b3c (order detail header wrapping at 1280 px, generic "no permission"
  text on a quote line, Retry offered for a 403 on the rule list).
