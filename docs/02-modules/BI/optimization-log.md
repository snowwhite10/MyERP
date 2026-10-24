# BI optimization log

## 2026-10-24T17:36:23+08:00 WU-BI-000d design review

| Finding | Cause | Decision | Verification |
|---|---|---|---|
| A row ID cutoff could include an uncommitted earlier transaction only after the first page. | Row sequence is not commit order. | Require FI durable committed source-set cutoff and filter-bound continuation/export token. | BI-FIN-008–009 planned. |
| BI could silently restate historical balances from current AR/AP or MD data. | Report ownership and source ownership were mixed. | Restrict BI to authorised FI Posted read DTOs and saved values. | BI-FIN-002–5,014–15 planned. |
| A financial page could imply completeness when an FI source is absent. | UI might sum only available rows. | Expose incomplete reason and block unsupported export/totals. | BI-FIN-010–11 planned. |

Documentation-only design; no product code was optimized or tested against SQL.
## 2026-10-24T17:41:07+08:00 WU-BI-001d design review

Finding: WU-BI-000d named AR/AP detail and other-income sources without specifying the owner DTO, as-of allocation date, full-set totals or the FI authority check on continuation. Cause: the BI presentation boundary was defined before FI report source contracts. Change: REQ_F4116.19.1–3, BR-FI-124–132 and FI API design now fix saved provenance, durable committed manifest, actor-scope recheck and exact base-currency formulas; BI bindings and FI/BI scenario matrices trace the contract. Effect: implementation can write failing real-SQL tests against one source set and distinguish incomplete accounting from empty results. Runtime performance and correctness remain unverified until FI source tables and SQL service are available. No code optimisation or product test is claimed.

## 2026-10-24T17:48:32+08:00 WU-BI-002d design review

The BlueWhale operational list contains sixteen reports, including five PP reports despite PP having no implementation. An empty PP response would falsely imply no production activity; the design requires a named unavailable-source result until PP and its INV movement references exist. INV stock-flow currently offers a per-request snapshot, which is insufficient for a stable multi-page BI report; owner implementation must add a durable committed source manifest. PUR/SD tracking measures were separated from Posted stock and invoice/GL measures to avoid double counting. Planned BI-OPS-005,006,010–11,015 prove these decisions when code and SQL are available. No runtime optimisation or product test is claimed.

## 2026-10-24T21:58:49+08:00 WU-BI-003d design review

Finding: the existing home page has three static demonstration cards, so reusing their labels or values as live KPIs would mislead users. Cause: the BI owner sources and cutoffs have not been implemented. Decision: specify four owner-owned read metrics with distinct interval versus as-of semantics, committed manifests and per-card unavailable/incomplete states. A cross-owner dashboard never claims an atomic cutoff. Effect: BI-KPI-001–017 can later prove real values and failure states before replacing demonstration cards. No runtime optimisation or product test is claimed.
