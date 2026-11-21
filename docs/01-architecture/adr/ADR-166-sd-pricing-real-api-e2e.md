# ADR-166: Pricing real-API E2E (rule → quotation → conversion → manual order) and WU-SD-005b3 split

Date: 2026-11-22. Status: Accepted (WU-SD-005b3a).

## Context

WU-SD-005b3 bundled two different jobs: a real-API E2E proving that a price rule saved on screen drives the quotation,
the converted order and the manual order with the same price and provenance in SQL Server, and a full UX QA round over
every pricing error state (timeout, 500, network, DUPLICATE 409, no permission) in three languages and both themes.
Together they exceed one 90-minute work unit.

While preparing the real-API run two facts about the seeded data came up:

1. The development database had only `SAL.PRICING.VIEW` for the SALES role: the four actions added by WU-SD-005a
   (`CREATE`, `EDIT`, `DEACTIVATE`, `OVERRIDE`) are granted only when `--seed` runs again (the seeder back-fills new
   catalog codes to existing roles). The real-API script always runs `--seed`, so the spec asserts the five grants first.
2. The quotation and manual order forms read customers, items and tax codes from the MD lists
   (`MD.CUSTOMER.VIEW`, `MD.ITEM.VIEW`, `MD.TAX_CODE.VIEW`). The seeded SALES role (SD, CRM, INV read-only) has none
   of them, so the `sales` user cannot pick anything on those forms against the real API. The price rule screen is
   not affected (ADR-162 option endpoints), nor deliveries (ADR-155).

## Decision

- Split WU-SD-005b3 into **005b3a** (this ADR: real-API pricing E2E) and **005b3b** (pricing error-state UX QA round,
  `myerp-ux-review`, three languages, light/dark). The parent is not counted.
- `tests/e2e/specs/real-api-sales-pricing.spec.ts` (SD-PRICE-REAL-001–004), run by `Invoke-RealApiE2E.ps1`:
  - Arrange through the MD API as `admin` (never SQL writes): a fresh unit, item, output tax code with a 5 % rate
    from 2020-01-01, and an approved customer. The rule key (item + customer + TWD) is therefore new on every run and
    cannot overlap rules of earlier runs.
  - 001 `sales` creates the customer rule on screen (2.12); SQL row matches every field; list shows it.
  - 002 `admin` creates a quotation on screen; the rule price is applied without a reason; the saved line is
    `Rule` with rule ID/version and rule price/ceiling; quotation detail shows the source.
  - 003 the `SAL.QUOTATION` flow is switched off for a zero-step submit (restored in `afterAll`), the quotation is
    converted on the order create screen; the order line copies the provenance; order detail shows it.
  - 004 `admin` creates a manual order below the rule price: Save without a reason is blocked on screen and nothing
    is saved; with a reason the saved line is `Override` with the rule snapshot, trimmed reason, actor and time.
  - Every test checks the shared E2E guard (no console error, no 5xx, no raw i18n key).
- The quotation/order forms run as `admin` until the forms stop requiring MD view permissions. New backlog item
  **WU-SD-FORM-OPTIONS** (P1): SD-owned customer/item/output-tax option endpoints authorised by
  `SAL.QUOTATION.CREATE/EDIT` or `SAL.ORDER.CREATE/EDIT` (same pattern as ADR-155 / ADR-162), used by both forms;
  afterwards the real-API spec switches those steps to `sales`.
- Granting MD view to the SALES seed role was rejected: the role description fixed by MASTER_PROMPT section 12 is
  "SD、CRM；庫存唯讀", and the project rule is that a document form must not need another resource's permissions.

## Findings fixed in this unit (found only by the real API)

- **Number rules**: the Development database had no `SAL.QUOTATION` / `SAL.ORDER` / `SAL.DELIVERY` number rule, so
  every quotation or order save returned 422 `SYS.NUMBER_RULE.NOT_CONFIGURED`. SD now registers `SalesNumberRuleSeeds`
  through the ADR-135 seed contract: `SQ` / `SO` / `DN` + year-month + 4 digits, monthly reset; existing rules are
  never changed (SD-PRICE-NRSEED-001).
- **Quotation status JSON**: `SalesQuotationStatus` had no string converter, so the API returned a number while the
  pages (and the frontend `SalesQuotationStatus` type) expect the name; the list tag and detail crashed with
  `toLowerCase is not a function`. It now uses `JsonStringEnumConverter` (names only, integers rejected) like
  `SalesOrderStatus`. The two HTTP assertions that pinned the integer were corrected to the frontend contract.

## Consequences

- No migration. Product changes: SD number-rule seed provider and the quotation status JSON shape (names).
- Follow-up P2 for 005b3b: the quotation form shows `SYS.NUMBER_RULE.NOT_CONFIGURED` as "server rejected customer,
  item, currency or tax references" — a misleading message; it needs its own text.
- P3: order detail line table headers (quantity, delivered, pending) wrap one character per line at 1280 px.
- Real `sales` user coverage of the quotation/order forms waits for WU-SD-FORM-OPTIONS.
