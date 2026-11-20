# ADR-145 — Saved delivery lot and serial selections

Date: 2026-11-18
Status: Accepted

## Decision

Save each lot or serial choice in `sal_delivery_line_tracking`, owned by a delivery line with a same-company foreign key. Preserve entry order with `seq_no`. Each row has exactly one lot or serial number and a positive six-place base-unit quantity; a serial row represents one unit. The API trims identifiers and rejects duplicate numbers without regard to case. It checks the item's tracking mode through the MD reference contract while saving and submitting. Drafts may have incomplete choices; submit requires the selected quantities to equal each tracked line's quantity. INV remains responsible for stock and serial availability at posting in WU-SD-004b.

This keeps Draft editing possible before all numbers are known, while preventing a Pending document from carrying an incomplete selection. Replacing Draft lines cascades their old choices. The delivery header transaction lock protects choices during submission; the final posting path must reload and recheck them.

## Renumbering and posting (2026-11-20, merge of WU-SD-004a2b14)

This decision was written on `feature/sd-delivery-line-tracking` as ADR-140. While the branch waited for a full regression, `main` used ADR-140 for the sales-order status JSON contract, so the branch decision was renumbered ADR-145 when merged. The table keeps REQ_DS43.203.

WU-SD-004b1 (ADR-144) moved the delivery locks, snapshot and line loading into the shared `SalesDeliveryPosting`. On merge, `SalesDeliveryPosting.LoadLinesAsync` also loads this delivery's selections by index seek, and `PostAsync` maps them to INV `InventoryTrackingLine` in `seq_no` order: a lot keeps its quantity, a serial is one unit, and no manufacture or expiry date is sent for an outbound. An untracked line sends no tracking. Both the SYS final-approval handler and zero-step submission use this path, so INV checks lot balance and serial availability inside the same transaction. An INV refusal rolls back SYS and SD together, as ADR-144 describes.
