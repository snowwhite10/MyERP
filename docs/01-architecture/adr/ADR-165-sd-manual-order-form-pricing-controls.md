# ADR-165: Manual order form pricing controls (interfaces 2.5/2.6) and order detail price source

Date: 2026-11-21. Status: Accepted (WU-SD-005b2b).

## Context

ADR-160 Addendum 2 (WU-SD-005a11b) made the server re-decide every manual-order line in the save transaction, as for
quotations. ADR-164 added the quotation form controls and left the manual order form (2.5 create, 2.6 edit) without
them, so users without `SAL.PRICING.OVERRIDE` could not save any manual order and users with it received a 400 with no
reason field. Unlike the quotation, the order currency is a **header** field, and the order page validates only on
save and handles edit errors in its own way (409 = version conflict, 404 = edit unavailable, 5xx/timeout = unknown).

## Decision

- Frontend only; no API or migration change. Reuse `sd/document-pricing.ts` (ADR-164) without copying.
  `pricingTokens` (hint pair + trimmed reason JSON tokens) moved from `quotation.service.ts` into
  `document-pricing.ts` (re-exported for existing imports) so `order-draft-form.ts` uses the same code.
- Every line is quoted in the **header currency**. A valid date change, a valid (three capital letters) currency
  change and a customer pick re-quote every line; an item pick re-quotes that line. A found rule replaces the unit
  price and sets the hint pair; only the latest reply per line counts (per-line token + page epoch); state is keyed
  by the line object.
- Same line block as the quotation (source text, override notice and missing-permission notice, Use rule price, Look
  up price again, reason ≤ 200 required where the form knows an override is needed). Price, discount and reason
  edits re-validate only after the first Save attempt (existing page behaviour).
- Edit: `orderEditDraft` maps saved provenance — the pair only when both are saved, the reason only for Override
  lines; saved Rule/Override lines show the saved rule price, Legacy lines show an optional reason. The pair and
  reason are part of the compared draft, so an unchanged edit is still "No data changed".
- Save failures: a pricing 403/409 (code + `lineNo`, ADR-160) is checked **before** the page's 409 version conflict
  and 5xx/unknown handling, so STALE_QUOTE never locks the form as a conflict; it re-quotes that line. Line messages
  use order wording (`sd.pricing.orderLine*`), the alert detail names the line and is re-translated on a language
  change. A 400 `lines[i].overrideReason` opens and requires that reason and the detail reads "Line n: …".
- Quotation conversion never looks up prices (the server copies the quotation's provenance; ADR-160).
- Order detail (2.4) adds the **Price source** column (`pricingSourceOf`), which also shows converted lines' copied
  provenance.
- The shared error text `errors.SAL.PRICING.OVERRIDE_REQUIRED` in Chinese said "this quotation"; it is now generic
  ("a line needs a pricing override") because it is shown on both forms.

## Consequences

- Users without OVERRIDE can save manual orders whose lines follow the current rules.
- Existing order create/edit/QA E2E specs mock the quote lookup with the price they keep, so their original
  assertions keep their meaning; the create and QA specs now also assert the sent pair.
- Real-API consistency (rule → quotation → order prices) and the pricing QA round remain WU-SD-005b3.
