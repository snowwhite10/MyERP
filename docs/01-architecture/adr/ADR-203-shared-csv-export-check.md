# ADR-203: Shared front-end check for server CSV exports

Date: 2026-11-26T23:37:15+08:00

Status: Accepted

## Context

WU-PUR-008c3c2 adds the 介面 4.19 export button for `GET /api/v1/pur/invoices/export` (ADR-202). The SD order export (ADR-141) already verifies a server CSV in the browser: `X-Export-Row-Count` must be a non-negative integer and equal the RFC 4180 data-row count of the file, otherwise nothing is saved; 0 rows warns without a file. Copying that function into PUR would create a second rule that can drift.

## Decision

- Move the check to `shared/export/csv-export.ts` as `checkCsvExport` / `CsvExportOutcome`, next to `countCsvDataRows` and `saveBlob`. `sd/order-export.ts` keeps `checkOrderExport` as an alias so the SD page and its tests are unchanged.
- Error-to-message mapping stays per module (each page names its own document and permission). PUR maps 422 `PUR.INVOICE.EXPORT_LIMIT` to the shared errors key `errors.PUR.INVOICE.EXPORT_LIMIT` so the same text is available to any screen that receives the code.
- The PUR page cancels a running export when new filters are applied, the sort changes or the company changes; paging does not change the file and keeps the result.

## Consequences

Future server-side CSV exports use `checkCsvExport` + `saveBlob` and only add a failure mapper and texts. Older PUR browser-built exports (requisition, RFQ) are unaffected.
