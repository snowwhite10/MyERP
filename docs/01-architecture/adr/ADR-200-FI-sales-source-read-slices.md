# ADR-200: Split sales source trace delivery

Date: 2026-11-26T01:14:11+08:00

Status: Accepted

## Context

WU-FI-002c combines a company-scoped API, real SQL and authorised HTTP acceptance, a new three-language screen, light/dark Playwright states and UX review. This exceeds one 30–90 minute work unit.

## Decision

Split the parent into WU-FI-002c1 (read-only API and SQL/HTTP acceptance) and WU-FI-002c2 (screen and Playwright QA). Both are required before the parent is DONE. The read uses `AR.SALES_SOURCE.VIEW`; the source has no branch or owner dimension, so any granted SYS data scope sees the current company's source, like GL journals. The stable key route returns 404 until committed. No write route is exposed.

## Consequences

The screen can consume a stable, tested source read contract next round. The first slice alone does not claim UI acceptance or completion of REQ_F4116.8.3.
