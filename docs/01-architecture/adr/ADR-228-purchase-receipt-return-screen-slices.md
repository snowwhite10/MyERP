# ADR-228 — Purchase receipt and return screens are delivered in slices e1–e6

Date: 2026-11-28T21:57:23+08:00

Status: Accepted

## Context

WU-PUR-007e covers six interfaces (4.4–4.6 Purchase Inbound View／Create／Edit, 4.16–4.18 Purchase Return View／Create／Edit), each with three languages, light／dark themes, CSV, print, lifecycle buttons and per-state Playwright QA. That is well over the 90-minute work-unit limit. The back-end contracts already exist (WU-PUR-007a8～a10, 007b, 007c3～c5), except that the receipt create form has no dedicated, CREATE-only source option endpoint yet (the purchase order screens use `/purchase-orders/*-options`, ADR-216).

## Decision

Split 007e (parent not counted) into:

| Slice | Scope |
|---|---|
| 007e1 | 介面 4.4 list (VIEW, filters in URL, server paging), saved read-only detail (lines, warehouse, saved base unit cost, lot／serial), CSV (EXPORT), print re-read; menu entry; front-end client; unit tests and mock Playwright screenshots. |
| 007e2 | 介面 4.4 detail SUBMIT／DELETE buttons with confirm, disabled reasons, 409／422 re-read, quantity-exceeded and tracking-required reasons. |
| 007e3 | 介面 4.5／4.6 receipt create／edit form; first verify or add a CREATE／EDIT-scoped approved purchase order line option endpoint (no MD.VIEW／ORDER.VIEW requirement), then the form with lot／serial capture. May split again (e3a options API, e3b form). |
| 007e4 | 介面 4.16 return list／detail／CSV／print plus SUBMIT／DELETE, showing `returnAmount` with the estimated-cost marker. |
| 007e5 | 介面 4.17／4.18 return create／edit form using the existing return source options. |
| 007e6 | Per-state Playwright QA for 4.4–4.6 and 4.16–4.18 (three languages, light／dark), plus a real-API spec for receipt → approval → posted. |

Each slice runs `myerp-test-scenarios`, `myerp-ux-review` (screenshots of its own screen) and `web-design-guidelines`.

The 4.4 detail shows the purchase order number from the list summary (the saved detail carries only `purchaseOrderId`); when the row is not on the current page it falls back to `#<id>`. Status labels and the per-status stock explanation follow the CSV wording (zh-Hans "已拒绝" for Rejected).

## Consequences

Every slice is shippable on its own with front-end tests green. No back-end change is needed for e1／e2／e4. Item, supplier and warehouse are shown by ID on 4.4 like the 4.1 purchase order screen; showing names needs a cross-module read contract and is not part of 007e.
