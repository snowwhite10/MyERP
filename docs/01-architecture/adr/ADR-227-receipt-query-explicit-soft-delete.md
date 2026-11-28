# ADR-227 — Purchase receipt queries exclude soft-deleted rows explicitly

Date: 2026-11-28T21:48:07+08:00

Status: Accepted

## Context

ADR-226 found that EF Core's `IgnoreQueryFilters()` on any set in a query turns off the global filters of every entity in that query. WU-PUR-007a11 checked `PurchaseReceiptQueryService` with a real-SQL test (PUR-RECV-LST-006): a soft-deleted Draft receipt appeared in the LIST page items — not only with a purchase order number keyword, but on every LIST call, because the summary projection reads the PO number through `PurchaseOrderSource.IgnoreQueryFilters()`. `TotalCount` (computed without the projection) stayed correct, so the page showed more rows than the count. The CSV leaked under the keyword filter.

Other PUR query services were checked: `PurchaseReturnQueryService` already filters explicitly (ADR-226); `RfqQueryService` source query filters `!IsDeleted` explicitly and the award lookup only lifts the named soft-delete filter on the order itself; `PurchaseOrderQueryService` only lifts filters on requisition／RFQ sets joined to non-soft-deletable lines or read in separate queries; `PurchaseInvoiceQueryService` does not use `IgnoreQueryFilters`.

## Decision

`PurchaseReceiptQueryService` base query states `!IsDeleted` explicitly (with the company condition). Any PUR read query that joins or subqueries a set with `IgnoreQueryFilters()` must state the soft-delete condition of its main entity explicitly.

## Consequences

LIST items, count and CSV agree and never include soft-deleted receipts. Removing the condition turns PUR-RECV-LST-006 red. The same pattern in other modules (e.g. SD queries using `IgnoreQueryFilters([SoftDelete])` on joined sets) is not reviewed here.
