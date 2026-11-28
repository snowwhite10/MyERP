# ADR-218 — Split purchase receipt draft delivery at the database boundary

Date: 2026-11-28T13:01:04+08:00

Status: Accepted

## Context

WU-PUR-007a includes schema, authorized CRUD, source capacity reservation and a two-connection race. The existing receipt tables were created for invoice matching and contain posted source rows, so a replacement table would break that flow. This is larger than one 90-minute work unit.

## Decision

WU-PUR-007a7 extends the existing order and receipt tables with nullable draft fields for legacy rows, default-zero order receipt counters, same-company document number uniqueness and SQL capacity checks. It changes no API and does not reserve stock. WU-PUR-007a8 will implement authorized draft CREATE/EDIT/READ against these tables. WU-PUR-007a9 will implement SUBMIT reservation and concurrent capacity validation. Both remain part of parent 007a.

## Consequences

Existing posted receipt source rows remain readable. A draft has positive requested quantity and warehouse ID while `posted_received` remains zero. SQL prevents a purchase order line's posted plus pending receipt quantity from exceeding ordered quantity. Application writes must still lock and recheck sources; the constraint alone does not implement the workflow.
