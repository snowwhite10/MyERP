# BI data structure

WU-BI-000d introduces no persistence and allocates no `REQ_DS43` ID. Financial facts remain in FI-owned future tables; BI receives immutable read DTOs through FI Contracts. A saved export job or schedule, if required later, needs a separate BI data requirement and migration.

```mermaid
flowchart LR
    BI[BI financial report read] --> FI[FI authorised read contracts]
    FI --> GL[FI Posted GL and subledgers]
    BI --> View[Page or frozen export]
```

No BI-to-FI database FK or EF navigation is permitted. The diagram represents contract flow, not a physical ER model; there is no BI table to show in an ER diagram yet.

WU-BI-003d adds no table or `REQ_DS43` ID. Future dashboard cards consume owner read DTOs through Application/Contracts, with FI, SD and INV retaining their own source tables and committed manifests. A BI materialized KPI cache would need a separate design, migration and invalidation proof before use.

WU-BI-002d also allocates no table or `REQ_DS43` ID. PUR/SD/PP/INV retain their source data and expose authorised immutable DTOs. A future durable report manifest may be owner-held or use an owner-provided committed snapshot mechanism; its persistence design and migration belong to the source implementation units, and BI cannot substitute a local copy. The contract flow extends to PUR, SD, PP and INV with no cross-module database FK.
