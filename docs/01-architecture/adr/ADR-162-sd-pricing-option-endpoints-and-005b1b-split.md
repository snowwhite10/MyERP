# ADR-162: Pricing-scoped item/customer/currency option endpoints; WU-SD-005b1b is split

Date: 2026-11-21. Status: Accepted (WU-SD-005b1b1).

## Context

WU-SD-005b1b (price-rule Create/Edit pages, interfaces 2.12/2.13) needs item, customer and currency choices. The
2.11 list filters (ADR-161) still used the MD lists, so a pricing user without `MD.ITEM.VIEW` / `MD.CUSTOMER.VIEW`
could not filter by item or customer, and the Create form would have the same gap. The whole unit (endpoints, form,
error mapping for overlap/stale/lock/inactive, three languages, light/dark, E2E, screenshot QA) is above 90 minutes.

## Decision

- **Split.** 005b1b → 005b1b1 (option endpoints; list filters use them) and 005b1b2 (Create/Edit pages and list
  buttons). The parent is not counted.
- **Endpoints** under the price-rule controller, each `RequireAnyPermission(SAL.PRICING.VIEW, CREATE, EDIT)`:
  `GET item-options` and `GET customer-options` (paged `SalesSelectionOption {id, code, name}`, keyword on code or
  name, only `sort=code`, code then ID order) and `GET currency-options` (`[{code, name, decimalPlaces}]`, code order).
  They delegate to the existing MD Contract `ISalesSelectionOptions` (active items; active **and approved** customers;
  active currencies) through `SalesPricingOptionsService`; SD reads no MD table and no new MD contract is needed.
- **Active only.** The options are exactly what a rule save accepts. The list filter therefore cannot pick an inactive
  item; its rules stay visible unfiltered and through `?itemId=` in the URL. The picker dialog says so
  ("Only active items are listed …") instead of silently hiding them.
- Quotation/order writers without a pricing permission get 403: their forms already use their own option lists.

## Consequences

- No migration; three read-only routes. The list page no longer injects MD `ItemService`/`CustomerService`.
- 005b1b2 builds the form on these endpoints.
