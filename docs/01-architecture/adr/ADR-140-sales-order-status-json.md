# ADR-140: Sales order status JSON uses names

Date: 2026-11-18. Status: Accepted (WU-SD-003c1).

## Context

`SalesOrderStatus` was serialized as its numeric enum ordinal. An ordinal has no business meaning, can change when states are inserted, and makes the order View/Create/Edit client depend on C# declaration order. Other ERP document APIs expose named states.

## Decision

The saved order list, detail, create, conversion and replace responses serialize `SalesOrderStatus` as `Draft`, `Pending`, `Rejected`, `Approved` or `Cancelled`. JSON deserialization of this enum rejects integers. The enum names are the public contract; UI translates them for the current language. The database status mapping and enum ordering remain as before. The list `status` query parameter continues to use the existing ASP.NET Core enum model binder; its validation and OpenAPI representation need a separate HTTP contract check in the View slice.

## Consequences

Existing clients reading numeric `status` must switch to names. The change is applied before the order UI ships. Type and DTO serialization tests lock the response contract. No migration or saved document change is required.
