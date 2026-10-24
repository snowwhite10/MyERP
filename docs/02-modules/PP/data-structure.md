# PP — Planned data structure

No PP migration or `REQ_DS43` allocation in WU-PP-000d. Candidate PP-owned aggregates are BOM group/revision/component, routing revision/operation, MPS run and demand snapshot, MRP run/peg/proposal, production plan, work order, requisition/line, completion/line and work-centre calendar/progress. All company and site keys are scoped; MD, SYS, SD, PUR and INV references are IDs without cross-module foreign keys. Exact columns, indexes, consistency constraints and requirement IDs belong to each migration slice.

```mermaid
erDiagram
    PP_BOM_REVISION ||--o{ PP_BOM_COMPONENT : contains
    PP_ROUTING_REVISION ||--o{ PP_ROUTING_OPERATION : contains
    PP_PRODUCTION_PLAN ||--o{ PP_WORK_ORDER : releases
    PP_WORK_ORDER ||--o{ PP_REQUISITION : requests
    PP_WORK_ORDER ||--o{ PP_COMPLETION : completes
    PP_MPS_RUN ||--o{ PP_PRODUCTION_PLAN : proposes
    PP_MRP_RUN ||--o{ PP_MRP_PEG : explains
```

The diagram is conceptual, not an implemented schema. INV movement references are stored as confirmed external IDs; the stock movement remains INV-owned.

WU-PP-001d adds no table or `REQ_DS43` ID. WU-PP-001c must persist released plan/line revision lineage, committed cursor membership, bucket certificates, count/digest and expiry so a later edit or cancellation cannot alter an issued snapshot. The design must keep PP-owned company/site keys and source identifiers; MD and SYS references stay external IDs without cross-module foreign keys.
