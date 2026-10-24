# CO — Controlling business rules

## WU-CO-000d baseline

| Rule | Design invariant |
|---|---|
| BR-CO-001 | CO stores company-scoped cost/profit center identities and effective hierarchy. Codes remain reserved after soft deletion; saved historical assignments are never reparented. |
| BR-CO-002 | A dimension may be assigned only through a versioned, company-scoped policy with effective date. Missing or ambiguous mapping is incomplete, not an unassigned zero. FI journal identity and account remain FI-owned. |
| BR-CO-003 | FI exposes authorised immutable Posted journal/source lines, period status, base currency and a committed source manifest through an Application contract. CO never reads `gl_` tables or writes FI journal/period data. Reversal is a new FI source and is included according to cutoff. |
| BR-CO-004 | INV exposes confirmed Posted movement IDs and valuation status via its Application contract. Provisional valuation is labelled provisional; `Unpriced` cannot become zero cost. CO cannot change stock movements or inventory cost pools. |
| BR-CO-005 | PP exposes saved work order, actual time, accepted output and confirmed INV links. Requested or approved quantities are not treated as Posted consumption/output. Missing PP implementation is a named unavailable source. |
| BR-CO-006 | Product cost run freezes owner manifests, costing policy/version, allocation bases and decimal component values. A source key is counted once, and material, labour and overhead totals reconcile to the run total after documented four-place line rounding and final remainder. |
| BR-CO-007 | Standard cost is a management baseline. Its approval records SYS decision and CO version only; FI variance journals, if needed, require a later explicit FI source contract. No implicit GL or INV write occurs. |
| BR-CO-008 | Budget key is company, fiscal period, center type/ID, account or category, and version. Only one approved effective version per key; amendments create a new version and preserve prior forecasts. |
| BR-CO-009 | Budget actuals are FI Posted base-currency amounts assigned by the effective CO mapping. A source line contributes once to a center, with debit/credit sign policy fixed by account type; unmapped lines are reported separately and block a complete total. |
| BR-CO-010 | Comparison uses a frozen approved budget version and FI committed source set. A new FI posting, backdate, reversal or mapping revision after the cutoff does not change existing pages or export; a refresh creates a new result/version. |
| BR-CO-011 | Company, branch and data-scope access are checked on every read, drill, export and approval. Revocation invalidates later page/download access; cross-company IDs are hidden as 404 and missing permission is 403. |
| BR-CO-012 | SYS owns workflow and document numbering. CO owns draft/approved/rejected states, saved result provenance, optimistic version and audit. Replayed permanent source keys return unchanged; changed payload or stale version is 409. |
| BR-CO-013 | Zero change, all skipped, partial success, all failure and unknown timeout are separately reported with per-row reasons. An unavailable owner or incomplete manifest never produces a credible successful total. |

Implementation slices must decide allocation base granularity, labor/overhead source policy and fiscal calendar with ADRs where they affect shared contracts. CO shall not invent FI or PP values while those owners are unavailable.
