# BI optimization log

## 2026-10-24T17:36:23+08:00 WU-BI-000d design review

| Finding | Cause | Decision | Verification |
|---|---|---|---|
| A row ID cutoff could include an uncommitted earlier transaction only after the first page. | Row sequence is not commit order. | Require FI durable committed source-set cutoff and filter-bound continuation/export token. | BI-FIN-008–009 planned. |
| BI could silently restate historical balances from current AR/AP or MD data. | Report ownership and source ownership were mixed. | Restrict BI to authorised FI Posted read DTOs and saved values. | BI-FIN-002–5,014–15 planned. |
| A financial page could imply completeness when an FI source is absent. | UI might sum only available rows. | Expose incomplete reason and block unsupported export/totals. | BI-FIN-010–11 planned. |

Documentation-only design; no product code was optimized or tested against SQL.
