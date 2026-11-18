# ADR-140 — Saved delivery lot and serial selections

Date: 2026-11-18
Status: Accepted

## Decision

Save each lot or serial choice in `sal_delivery_line_tracking`, owned by a delivery line with a same-company foreign key. Preserve entry order with `seq_no`. Each row has exactly one lot or serial number and a positive six-place base-unit quantity; a serial row represents one unit. The API trims identifiers and rejects duplicate numbers without regard to case. It checks the item's tracking mode through the MD reference contract while saving and submitting. Drafts may have incomplete choices; submit requires the selected quantities to equal each tracked line's quantity. INV remains responsible for stock and serial availability at posting in WU-SD-004b.

This keeps Draft editing possible before all numbers are known, while preventing a Pending document from carrying an incomplete selection. Replacing Draft lines cascades their old choices. The delivery header transaction lock protects choices during submission; the final posting path must reload and recheck them.
