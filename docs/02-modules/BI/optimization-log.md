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
