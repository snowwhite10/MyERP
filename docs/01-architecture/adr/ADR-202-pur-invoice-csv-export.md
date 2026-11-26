# ADR-202: Purchase invoice CSV export on the server and WU-PUR-008c3c split

Date: 2026-11-26T23:22:11+08:00

Status: Accepted

## Context

WU-PUR-008c3c bundled a language-following CSV export for 介面 4.19, a real-API Playwright flow (MATCH → Disputed → RESOLVE with screen/data assertions) and the 008a55i index evaluation. Together they exceed one 30–90 minute round. Earlier PUR exports (requisition, RFQ) build CSV in the browser from detail reads, but MASTER_PROMPT 9.1 requires exports to follow `Accept-Language` with backend resources; the newer SD order export (ADR-141) already streams CSV from the server.

## Decision

- Split 008c3c into **c3c1** backend export (this ADR), **c3c2** export button on 介面 4.19 (Vitest + mock Playwright, language switch asserts file content) and **c3c3** real-API Playwright MATCH→Disputed→RESOLVE plus screenshot QA. 008a55i stays its own P3 item.
- Route `GET /api/v1/pur/invoices/export` with the same query parameters and validation as the list; paging is ignored. One CSV row per saved invoice line with the header snapshot (supplier invoice number, date, status, supplier ID, purchase order ID, currency, line no., receipt line ID, quantity, unit price, tax rate, net, tax, gross).
- New permission `PUR.INVOICE.EXPORT`, company data scope only (invoices carry no branch/department/owner, same as VIEW). VIEW alone cannot export.
- Headers and status text follow `Accept-Language` (zh-Hant default / zh-Hans / en) with the 4.19 status-tag wording; ISO dates and invariant numbers at saved scale; UTF-8 BOM; RFC 4180 quoting and a leading apostrophe on formula-like text.
- Count and rows are read in one Snapshot transaction; over 10,000 lines → 422 `PUR.INVOICE.EXPORT_LIMIT` before any byte. `X-Export-Row-Count` and `Content-Language` headers report the result.
- Supplier and order are exported as IDs because PUR does not yet own supplier/order display snapshots on the invoice (cross-module table reads are forbidden); names follow when 008c2 sources expose them.

## Consequences

The CSV cell helper duplicates `SalesOrderCsv.Cell`; extracting a shared helper into `MyERP.Shared` is tracked as P3 WU-P0-CSV-CELL. The front end must download via the API (not build CSV locally) so the server language is used.
