# ADR-123 — Sales collection account map maintenance API, SD receivable warning and demo row (WU-FI-005a2d)

Date: 2026-11-14. Status: Accepted (WU-FI-005a2d).

## Context

ADR-120 created `gl_sales_collection_account_map`. It holds one row per company with the unapplied-cash clearing and AR control codes, and the two codes must differ. The row version is the GL `FI/SalesCollection` and `FI/CollectionWriteOff` mapping version. ADR-121 and ADR-122 post receipts and write-offs against this map. No environment has a row yet, so collection posting always fails closed. ADR-120 also asked the API to warn when the AR control code differs from the receivable code in the SD invoice map.

## Decision

- **Same contract as ADR-110/118**, separate resource: `GET`/`PUT /api/v1/gl/sales-collection-account-map`, no POST or DELETE.
  - GET returns `configured=false` and version 0 when no map exists.
  - PUT with version 0 creates the map. Any other version must equal the saved version, otherwise 409 `FI.COLLECTION_MAP.VERSION_CONFLICT`. The loser of two concurrent first saves gets the same 409.
  - Codes are trimmed, required and at most 30 characters (400).
  - Unchanged codes return `changed=false` and do not create a new version.
- **Equal codes are a 422, not a 400.** This is a business rule, and SQL check `ck_gl_sales_collection_account_map_distinct` enforces it too (trimmed, case-insensitive). It is reported in the same 422 `FI.COLLECTION_MAP.ACCOUNT_INVALID` as the MD reasons, as `accountErrors.receivableAccountCode = FI.COLLECTION_MAP.SAME_ACCOUNT`. The screen can then show every field problem from one response. If the AR field also has an MD reason, the MD reason is shown, because it has to be fixed first anyway.
- **The SD receivable warning is computed on every read, not stored at save time.** The SD map can change after the collection map is saved, so GET and the PUT result recompute it each time:
  - `salesInvoiceReceivableAccountCode` is the SD map's code, or null when the SD map is not configured.
  - `warnings` contains `"FI.COLLECTION_MAP.RECEIVABLE_DIFFERS_FROM_SALES_MAP"` when the collection map is configured and names another code (case-insensitive).
  - The warning never blocks a save, because a clearing structure may use another AR account on purpose.
  - A user with only `GL.COLLECTION_MAP.VIEW` can see the SD code. This is acceptable: both maps are FI configuration of the same company, and the code alone is not sensitive.
- **Separate permissions** `GL.COLLECTION_MAP.VIEW` / `EDIT`, so AR cash clerks can maintain this map without access to the invoice map (same reasoning as ADR-118). Seed FINANCE gets both permissions; MANAGER and VIEWER get VIEW only.
- **FinanceAccountUsage** reports both codes to MD as in use.
- **Demo data.**
  - The MD demo chart gains `2181 暫收款` (liability).
  - `GlDemoSeeder` creates map `2181 / 1131` (v1) for each seed company that has no row, and never changes an existing row. It uses the same AR code as the demo SD map, so no warning appears.
  - `GlDemoSeedResult` gains `SalesCollectionMaps`.
  - The three map-seeding methods were merged into one generic `EnsureMapAsync<TMap>`.

## Consequences

- Collection posting can be tried in Development once 005b supplies documents.
- The screen is a separate unit. It reuses the 6.23 layout plus a warning banner, and adds `FI.COLLECTION_MAP.*` to the frontend error whitelist test.
- The demo seed adds 2 MD accounts and 2 FI rows; re-seeding adds nothing.
