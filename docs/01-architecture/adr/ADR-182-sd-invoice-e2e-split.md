# ADR-182: Split the SD invoice Playwright and UX QA unit (WU-SD-007b3)

Date: 2026-11-23. Status: Accepted (WU-SD-007b3a).

## Context

WU-SD-007b3 bundles three different kinds of work: mocked-API Playwright for the invoice list/detail (Interface 2.14)
and issue form (Interface 2.15), a real-API run that issues an invoice against SQL Server and compares the screen with
`sal_invoice` and the FI receivable / GL journal, and the `myerp-ux-review` pass with the qa-report update. Each needs
its own build, environment (the real run needs SQL Server, Redis and seed data) and review time; together they exceed
one round.

## Decision

1. Split WU-SD-007b3 into:
   - **007b3a** mocked-API Playwright `tests/e2e/specs/sales-invoice.spec.ts` (SD-INV-E2E-001～006): list/detail in
     three languages and two themes, empty / no-result / invalid dates / 404 / 500 with trace and retry; issue from the
     list with exact quantity literals and the posted detail opened through `?open=<id>`; 422 BALANCE_EXCEEDED line
     marking, reload and retry with a new request key; network failure → locked form → "Resend safely" with the same
     key and body → 200 replay message; ISSUE without VIEW keeps the receipt on the page. Screenshots go to
     `tests/qa/SD/screenshots/2.14-*` and `2.15-*` for the later review.
   - **007b3b** real-API Playwright: seed an Approved order, post a delivery, issue one invoice through the form and
     compare the screen with SQL (`sal_invoice`, FI receivable, GL journal).
   - **007b3c** `myerp-ux-review` of 2.14 / 2.15 using the 007b3a screenshots (plus real ones from 007b3b) and the
     SD qa-report update.
   The parent 007b3 (and 007b) is complete when all three are.
2. `?open=<id>` stays one-shot: the list removes it from the URL with its first URL update after opening the detail,
   so the E2E asserts the opened detail and a URL without `open`.

## Consequences

- Each slice can be verified in one round; the real-API slice can be deferred if SQL Server is unavailable without
  blocking the mocked coverage.
