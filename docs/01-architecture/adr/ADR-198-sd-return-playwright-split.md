# ADR-198: Split sales return Playwright and UX QA (WU-SD-008b3c)

Date: 2026-11-24. Status: Accepted (WU-SD-008b3c1).

## Context

WU-SD-008b3c bundles three jobs: mocked Playwright for the return create / edit / submit flow, a real-API Playwright run
that needs seed data with a returnable Posted delivery (and a Posted invoice to credit), and the myerp-ux-review pass over
interfaces 2.16 / 2.17 in light / dark and three languages. The real-API run needs SQL Server, Redis, migrations and a
seed change; together they do not fit one unit.

## Decision

1. **WU-SD-008b3c1** (this unit): `tests/e2e/specs/sales-return-flow.spec.ts` against mocked APIs — create → submit →
   Posted, Rejected → revise → Draft → Pending, `MD.WAREHOUSE.INACTIVE` refusal focusing the line's warehouse picker,
   unknown 5xx then resend with the identical request key (200 replay). Mocks echo the posted request key and split so
   the page's receipt check runs as against the server. Screenshots `tests/qa/SD/screenshots/2.17-flow-*`.
2. **WU-SD-008b3c2**: real-API Playwright (`specs/real-api-sales-return*.spec.ts`; seed an Approved order with a Posted
   delivery and Posted invoice) plus the myerp-ux-review report for 2.16 / 2.17 using the 008b2 / 008b3 screenshots.
3. The parent WU-SD-008b3c is not counted.

## Consequences

Mocked E2E guards the UI contract now; real SQL behaviour of the return flow stays covered by the integration tests
(SD-RETURN-HTTP / SVC) until 008b3c2. A UX finding seen in the screenshots — a refused destination's message is shown
under the quantity split, not beside the destination picker — is left for the 008b3c2 review.
