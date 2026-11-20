# ADR-147: Serialise delivery status by name and return SYS history on the delivery VIEW read

Date: 2026-11-21. Status: Accepted (WU-SD-004c1).

## Context

The Sales Outbound View Interface (2.7) must show the saved delivery state and its SYS approval rounds (WU-SD-004d interaction design). `SalesDeliveryStatus` had no JSON converter, so the API returned it as an integer (`0`–`3`), while sales orders and quotations return named states (`"Draft"`, `"Approved"`, ...) with integer input refused. `GET /api/v1/sal/deliveries/{id}` returned no approval history, unlike the order and quotation details.

No client consumed the integer form yet: the delivery API had only server-side HTTP tests, and the Angular client starts in this unit.

## Decision

1. `SalesDeliveryStatus` uses `JsonStringEnumConverter` with `allowIntegerValues: false`, the same pattern as `SalesOrderStatus`. Responses carry `Draft`, `Pending`, `Rejected` or `Posted`. The list `status` query filter already binds by name.
2. `SalesDeliveryDetail` gains an optional `approvalHistory` (SYS `ApprovalDocumentHistory`, newest round first, document type `SAL.DELIVERY`). Only the VIEW read (`GET deliveries/{id}`) fills it. Create, edit, submit and withdraw responses come from `DescribeAsync` and leave it null, because those writers may lack VIEW. This matches the order detail.
3. The HTTP acceptance tests assert named states rather than integers. This is a contract change, not a weaker test: each assertion still checks the exact state.

## Consequences

- The Angular delivery service uses the named states directly, and the status tag and filter share one translation key per state.
- Any future integration must send and read delivery status by name. Integer status values in a JSON body are rejected.
- The VIEW detail reads SYS history in one extra query. Lists do not read it.
