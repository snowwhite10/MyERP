# ADR-204: Real-API invoice E2E arranges PUR order / receipt sources with sqlcmd

Date: 2026-11-26T23:49:18+08:00

Status: Accepted

## Context

WU-PUR-008c3c3 must drive screen 4.19 MATCH / RESOLVE / CSV export against the running API and SQL Server. `POST /api/v1/pur/invoices` exists, but a Draft needs an Approved purchase order line and a Posted receipt line. Purchase orders (006) and receipts (007) have no HTTP API or screen yet (006a is BLOCKED); the backend integration tests already insert these source rows with raw SQL. A Development demo seeder would not help: MATCH reserves receipt quantity and invoices are never deleted, so fixed seeded sources would be used up after one run.

## Decision

- `tests/e2e/support/real-api-sql.ts` gains `sqlArrange(statements, select)`: one transaction (`XACT_ABORT`) through sqlcmd with Windows authentication, returning ASCII JSON rows like `sql()`. It is for test-only source rows that no API can create; everything after the source (Draft creation, MATCH, RESOLVE, export) goes through HTTP or the screen.
- `real-api-purchase-invoice.spec.ts` creates fresh order / receipt rows per run for the seeded TW01 supplier and item and prefixes every supplier invoice number with a per-run token, so runs never collide and the list / export can be filtered to the run.
- When PUR 006/007 deliver order approval and receipt posting APIs, the spec must switch to them and `sqlArrange` must lose its last caller (follow-up recorded in backlog with 006/007).

## Consequences

The real-API invoice flow is verified end to end today without adding production code. The Development database accumulates test orders, receipts and invoices with the `RE2E-` prefix; they are ordinary data and do not affect other specs (which filter by their own records).
