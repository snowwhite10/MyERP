# ADR-164: Quotation form pricing controls (interfaces 2.2/2.3) and WU-SD-005b2 split

Date: 2026-11-21. Status: Accepted (WU-SD-005b2a).

## Context

Since WU-SD-005a11a/b (ADR-160) the server re-decides every quotation and manual-order line inside the save
transaction: the optional `pricingRuleId`/`pricingRuleVersion` pair is a concurrency hint (409
`SAL.PRICING.STALE_QUOTE` when it no longer matches), a missing rule, a price different from the rule or a discount
above the ceiling needs `SAL.PRICING.OVERRIDE` (403 `SAL.PRICING.OVERRIDE_REQUIRED`) and a 1–200 character reason
(400 `lines[i].overrideReason`); several applicable rules give 409 `SAL.PRICING.DUPLICATE_ACTIVE_RULE`. Pricing
403/409 carry `lineNo`. The quotation form sent none of these, so users without OVERRIDE could not save at all and
users with it got a 400 with no reason field. WU-SD-005b2 (quotation and manual order) was estimated above 90 minutes.

## Decision

- Split WU-SD-005b2 into **005b2a** (quotation create/edit 2.2/2.3 and the saved detail price source) and **005b2b**
  (manual order create/edit 2.5/2.6, same helpers). The parent is not counted.
- Frontend only; no API or migration change. `SalesPriceRuleService.quote()` calls `GET price-rules/quote` (ADR-159)
  with exact decimal text.
- Pure helpers in `sd/document-pricing.ts`, reusable by the order form: `LinePricing` line state (`none`, `loading`,
  `rule`, `notFound`, `legacy`, `duplicate`, `stale`, `failed`), `compareDecimalText` (exact, no `Number`),
  `linePricingNeedsOverride` (mirrors `SalesPricingRules.Decide`), `pricingFailureOf` (403/409 code + `lineNo` +
  `conflictingRuleIds`), `quoteFailureState`, `pricingSourceOf` (saved provenance label).
- When customer, item, currency and a valid document date are all present, a change of any of them looks up the
  line's price again; a found rule **replaces the unit price** and sets the hint pair; only the latest reply per line
  counts (per-line token + company epoch). Pricing state is keyed by the line object so removing a line never shifts
  another line's source. A new line copies the previous line's currency.
- The form shows the source under each line; when an override is needed it says so, offers **Use rule price**
  (restores price, caps discount at the ceiling) and a reason field that the client requires (≤ 200). Missing
  OVERRIDE permission is stated up front; the server is still authoritative.
- Edit: saved Rule/Override lines show the saved rule price/ceiling and resend the saved pair; a saved Override keeps
  its reason. **Legacy** lines (saved before rules) show an optional reason — a current rule might still match the
  price, so the client does not block; the server decides.
- Save failures: pricing 403/409 are line problems, never the edit page's version conflict. STALE_QUOTE re-quotes that
  line automatically and tells the user to check before saving again; DUPLICATE lists the rule IDs (not
  overridable); OVERRIDE_REQUIRED marks the line; a 400 `lines[i].overrideReason` opens and requires that reason.
- Saved quotation detail adds a **Price source** column: Rule #id (version), Override (rule …): reason,
  Override (no rule): reason, Legacy.

## Consequences

- Users without OVERRIDE can save quotations whose lines follow the current rules.
- Manual order form still lacks these controls until 005b2b; real-API price consistency (rule → quotation → order)
  is WU-SD-005b3.
- The order form must reuse `document-pricing.ts` rather than copy it.
