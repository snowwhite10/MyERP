# ADR-159: The price quote is a separate read-only service open to pricing viewers and quotation/order form writers

Date: 2026-11-21. Status: Accepted (WU-SD-005a10).

## Context

`GET /api/v1/sal/price-rules/quote` must let a quotation or order form show the rule price without the user having
`SAL.PRICING.VIEW`, must never write, and must use the same selection as document save (`SalesPricingRules.Quote`,
BR-SD-027). The API plan left open: the response shape, how "hidden" item/customer and an unknown currency are
reported, and where the code lives.

## Decision

- **Permission.** `[RequireAnyPermission]` with `SAL.PRICING.VIEW`, `SAL.QUOTATION.CREATE/EDIT`,
  `SAL.ORDER.CREATE/EDIT`; the service repeats the check (permission held and a data scope granted). Rules are
  company-wide, so the scope only proves the permission. `SAL.ORDER.VIEW`, `SAL.DELIVERY.*` and `SAL.PRICING.CREATE`
  alone are 403.
- **Parameters.** `itemId`, `customerId`, `currency`, `businessDate` are all required. Missing, non-positive,
  lowercase or wrong-length values are one 400 listing every field; an unparsable date (`2028-02-30`) or ID is 400 from
  model binding. The company comes only from `X-Company-Id`.
- **References.** The item must be an active current-company item and the customer an approved current-company
  customer, through the same MD Contracts as rule save: other-company or unknown IDs are 404 (`MD.ITEM.NOT_FOUND`,
  `MD.CUSTOMER.NOT_FOUND`), inactive item / unapproved customer 422. A form cannot save with those anyway, so quoting
  them would only show a price the user cannot use.
- **Currency.** Only the shape is checked. An unknown currency has no rule, so it is 422
  `SAL.PRICING.RULE_NOT_FOUND`; no MD currency lookup is needed for a read.
- **Outcome.** 200 `SalesPriceQuoteResult` (`asOf`, `ruleId`, `ruleVersion`, `itemId`, `customerId` — null when the
  default rule was chosen —, `currencyCode`, `unitPrice`, `discountCeiling`, `validFrom`, `validTo`). No rule: 422
  `SAL.PRICING.RULE_NOT_FOUND`. Two active rows on the deciding tier (only possible through damage outside the API,
  because writes are overlap-guarded): 409 `SAL.PRICING.DUPLICATE_ACTIVE_RULE` with `conflictingRuleIds`; no price is
  returned. A clean customer tier still wins over a damaged default tier (the default tier is not consulted).
- **Code.** A separate `SalesPriceQuoteService` instead of a method on `SalesPriceRuleService`, so the rule writer's
  constructor (and the lock-timeout test override) does not gain an authorization dependency it does not use. It
  loads both tiers' active candidates covering the date in one query and hands snapshots to `SalesPricingRules.Quote`.

## Consequences

- The WU-SD-005b form can call one endpoint for any quotation/order writer; "rule ID + version" from the answer is the
  concurrency hint that WU-SD-005a11 save checks again (409 `SAL.PRICING.STALE_QUOTE`).
- No transaction or lock: the quote is advisory and may be stale by the time the document is saved.
