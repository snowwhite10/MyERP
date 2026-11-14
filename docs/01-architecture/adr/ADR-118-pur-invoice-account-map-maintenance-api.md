# ADR-118 — PUR purchase invoice account map maintenance API and demo row (WU-FI-003a2c)

Date: 2026-11-14. Status: Accepted (WU-FI-003a2c).

## Context

ADR-116 created `gl_purchase_invoice_account_map` (one row per company, row version = GL `PUR/PurchaseInvoice` mapping version, never deleted) and ADR-117 the posting service that reads it. Finance users need to maintain it over HTTP before the screen (003a2d), and the Development demo companies need a usable row so purchase posting can be exercised end to end once PUR-008a lands. The SD map already has the same need solved by ADR-110 (API) and ADR-112 (demo row).

## Decision

- **Same contract as ADR-110**, separate resource: `GET`/`PUT /api/v1/gl/purchase-invoice-account-map`, no POST/DELETE; GET returns `configured=false`, version 0 when absent; PUT version 0 creates, otherwise must equal the saved version (409 `FI.PURCHASE_MAP.VERSION_CONFLICT`, also for the loser of concurrent first saves); codes trimmed, required, ≤30; unchanged codes → `changed=false`, no new version; every changed code checked through `IChartOfAccounts` → one 422 `FI.PURCHASE_MAP.ACCOUNT_INVALID` with `accountErrors` per field (`payableAccountCode`, `purchaseAccountCode`, `inputTaxAccountCode`).
- **Separate permissions** `GL.PURCHASE_MAP.VIEW` / `GL.PURCHASE_MAP.EDIT` rather than reusing `GL.SALES_MAP.*`: the sales and purchase maps are owned by different finance roles in many companies (AR vs AP clerks), and the maps are separate rows precisely so that changing one never disturbs the other (ADR-116). Seed FINANCE gets both, MANAGER/VIEWER get VIEW through the existing `.VIEW` rule.
- **Separate service and DTOs, shared building blocks.** `PurchaseInvoiceAccountMapService` mirrors `SalesInvoiceAccountMapService`; MD postability (`AccountReason`), version matching and the account label record (`SalesInvoiceMappedAccount`) are reused instead of duplicated. A generic map service was considered and rejected: two three-field maps do not justify a generic abstraction, and the field names are part of the public API.
- **FinanceAccountUsage** also reports the three purchase map codes as in use to MD (no delete, direction change, child account; deactivation stays allowed and is shown by GET).
- **Demo data.** The MD demo chart gains `1151 進項稅額` (asset) and `5111 進貨` (cost); `GlDemoSeeder` creates map `2101 / 5111 / 1151` (version 1) for each seed company without a row, validates the codes with the same rule as the API and never touches an existing row (same rule as ADR-112). `GlDemoSeedResult` gains `PurchaseInvoiceMaps`. A purchase account (rather than inventory 1141) is used because the demo has no GR/IR clearing design yet; companies can switch to an inventory or clearing account through the API.

## Consequences

- AP setup is possible through the API; the screen is WU-FI-003a2d (reuse 6.23 layout with purchase labels and `FI.PURCHASE_MAP.*` translations).
- Demo seed adds 4 MD accounts and 2 FI rows; re-seeding adds nothing.
