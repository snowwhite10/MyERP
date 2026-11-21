# ADR-163: Price rule Create/Edit pages (interfaces 2.12/2.13) — outcome handling

Date: 2026-11-21. Status: Accepted (WU-SD-005b1b2).

## Context

The price-rule write API (ADR-157/158) has several distinct refusals: 400 field errors, 404/422 MD references,
409 `SAL.PRICING.RULE_OVERLAP` with `conflictingRuleIds`, 409 `SAL.PRICING.RULE_LOCK_TIMEOUT`, 409
`COMMON.CONCURRENCY.CONFLICT`, 422 `SAL.PRICING.RULE_INACTIVE`. A rule is active the moment it is saved, so a
blind resubmit after an unknown result can create a second (overlapping or duplicate) rule. Choices must come from the
pricing option endpoints (ADR-162) so users without MD permission can maintain rules.

## Decision

- One standalone page `SalesPriceRuleFormPage` serves `/sal/price-rules/new` (guard `SAL.PRICING.CREATE`) and
  `/sal/price-rules/:id/edit` (guards `SAL.PRICING.VIEW` + `EDIT`, because the page reads `GET /{id}`), with an
  unsaved-changes guard. A non-numeric ID never reaches the API (shown as "not found").
- Pure helpers in `price-rule-form.ts`: `validatePriceRuleDraft` repeats `SalesPricingRules.ValidateRule` (all fields
  at once; price ≥ 0, < 10^13, ≤ 6 decimals; ceiling 0–100, ≤ 4 decimals; real dates; to ≥ from; customer required
  for the customer scope); `priceRuleJson` assembles decimal tokens from validated text (leading zeros stripped,
  never through `Number`); `priceRuleSaveFailure` classifies failures.
- Outcomes: overlap → warning naming the rule IDs plus a link to the list filtered to the same key (active); lock
  timeout → "busy, try again" with Save still enabled; stale version / became inactive / not found → warning, form
  locked until **Reload rule**; MD 404/422 → message on the item, customer or currency field; 400 → "server refused
  this value" on each named field (generic message if a field is unknown); 403 → permission message; other 4xx →
  "refused, nothing saved". **Network, timeout and 5xx are "unknown"**: warning with trace ID, Save locked with the
  reason shown, and a "Back to list" button, so the user checks before retrying.
- Currency is a dropdown of active currencies with a hint of that currency's amount decimals; the rule price keeps
  up to 6 decimals regardless (DECIMAL(19,6), it is a unit price, not an amount). A saved currency that is no longer
  active stays selectable as "(inactive or not available)" so the edit page shows the truth; saving then gets the MD
  refusal on the field.
- Success only after the server reply; the form adopts the returned rule (new version). Create offers "Create
  another"; Save stays disabled with an explanation after success.
- List 2.11 gains **New price rule** (`SAL.PRICING.CREATE`) and per-row **Edit** (`SAL.PRICING.EDIT`, active rows
  only).

## Consequences

- No backend change or migration. The English layout header overflows by about 22 px at 768 px (shell header, not
  this page); recorded as P3 backlog item WU-P0-HEADER-EN-768.
