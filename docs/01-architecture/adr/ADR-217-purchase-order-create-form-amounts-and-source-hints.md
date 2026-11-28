# ADR-217 — Purchase order create form: server amounts, context re-read and source hints

Date: 2026-11-28T11:14:15+08:00. Status: Accepted for WU-PUR-006f2b.

## Context

REQ_IO_A4.2.2 asks the 介面 4.2 lines to show remaining quantity, price, input tax and line amount. Line amounts are rounded by `Money.Round` with the currency's decimal places, and the exchange rate snapshot is chosen by the server on save. The create context (006f2a) returns currency codes and tax rates but not the currency decimal places or the rate. The tax rates valid on a date can change when the business date changes.

## Decision

- The create form does **not** compute a line or order amount before saving. It states that amounts and tax are calculated by the system for the currency when saved. The saved amounts are then shown on the 4.1 detail, which the form opens with `/pur/purchase-orders?id=<id>` after a 201. A client preview with a different rounding or rate would be misleading (MASTER_PROMPT 20.1).
- Currencies and input tax rates are re-read from `create-context` every time a valid business date is entered. A line whose tax rate is not in the new list is cleared and named in a warning. A currency that is no longer active is cleared and explained. When only one currency is active, it is chosen automatically.
- A requisition source can only be chosen after the line's item, and the chooser asks only for that item. If the item changes, a source for another item is removed with an explanation. A quantity above the source's remaining quantity shows a soft warning, compared exactly as decimal text without a JavaScript double, and does not block saving: a draft does not reserve demand (REQ_F4114.1.2), and SUBMIT refuses shortages per line (006f1).
- Users without `PUR.ORDER.VIEW` stay on the form after saving. The saved number and line count are shown, and a blank form is ready.

## Consequences

The form needs no new API. If a later unit adds currency decimal places and a rate preview to `create-context`, a preview can be added, labelled as an estimate.
