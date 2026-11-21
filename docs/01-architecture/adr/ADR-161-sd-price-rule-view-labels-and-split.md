# ADR-161: Price-rule responses carry current MD labels; WU-SD-005b is split into page, document controls and QA slices

Date: 2026-11-21. Status: Accepted (WU-SD-005b1a).

## Context

WU-SD-005b (price-rule View/Create/Edit pages plus quotation/order pricing controls, three languages, light/dark, E2E
and QA) is estimated well over 90 minutes. The price-rule list needs item and customer names, but the rule API only
returned IDs; the page would otherwise need `MD.ITEM.VIEW` and `MD.CUSTOMER.VIEW` and one MD read per row, and SD
must not read MD tables directly.

## Decision

- **Split.** 005b → 005b1a price-rule View (interface 2.11: list, filters, versioned deactivation), 005b1b price-rule
  Create/Edit (interfaces 2.12/2.13), 005b2 quotation/order pricing controls (quote call, `pricingRuleId/Version`,
  override reason, 403 `OVERRIDE_REQUIRED` / 409 `STALE_QUOTE` by `lineNo`, provenance in detail), 005b3 E2E/QA round
  and real-API check. The parent is not counted.
- **Labels.** Every rule response (list, detail, create, edit, deactivate) adds `itemCode`, `itemName`,
  `customerCode`, `customerName`, filled by `SalesPriceRuleLabels` through the existing MD Contracts
  (`IInventorySelectionOptions.SavedAsync`, `ICustomerLabels.GetLabelsAsync`), one batch read per kind per page.
  Labels are **current** values (not snapshots): inactive records keep their label; a soft-deleted item has none and
  the page shows "Deleted item (ID n)". A company default rule has null customer fields. No MD permission is needed.
- **Filter pickers** on the page still use the MD selection lists (`MD.ITEM.VIEW` / `MD.CUSTOMER.VIEW`); a user
  without them can still read the full list unfiltered. A pricing-scoped option endpoint is left to 005b1b, where the
  Create form needs the same choice.
- **Deactivation** sends the version shown in the row after a danger confirmation; 409 explains "changed by someone
  else, not deactivated" and reloads the list; 422 `RULE_INACTIVE` says it was already inactive.

## Consequences

- No migration; the response contract only gains nullable fields (backward compatible).
- Until 005b2, a user without `SAL.PRICING.OVERRIDE` still cannot save a quotation/manual order when no rule matches.
