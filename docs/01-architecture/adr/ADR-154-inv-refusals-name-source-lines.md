# ADR-154: INV stock refusals name the caller's source lines

Date: 2026-11-21. Status: Accepted (WU-SD-004c2b2d).

## Context

When a sales outbound posts (zero-step submit or SYS final approval), INV may refuse with `INV.STOCK.INSUFFICIENT`, `INV.LOT.INSUFFICIENT`, `INV.SERIAL.NOT_AVAILABLE` or `INV.TRACKING.REQUIRED`. These errors carried only a message with item/warehouse IDs, so the outbound detail (ADR-150) could only show a general hint, while SD's own refusals already carry a `lineNo` extension that marks the refused line. INV checks stock per balance key (item + warehouse + location), so one shortage can involve several outbound lines.

## Decision

1. `StockPostingService` attaches ProblemDetails extensions to these refusals: `lineNo` = smallest involved source line number and `lineNos` = all involved source line numbers, ascending and distinct. Line numbers are the caller's own `InventoryPostingLine.LineNo` values (for SD, the delivery line number), so no data of another document or company is exposed.
2. Involved lines: stock and lot shortages name the **Out** lines of the short balance key (lot + warehouse + location for lots); a lot or serial that does not exist names the line that asked for it; serial not at the place names the Out lines of that serial and place; `INV.SERIAL.IN_STOCK` (409) names the In lines of the serial; `INV.TRACKING.REQUIRED` names its line.
3. SD passes INV errors through unchanged (BR-SD-024), so the submit endpoint and the SYS approval endpoint return the same extensions. No contract or SD code change is needed; `lineNo` stays compatible with SD's single-line marker.
4. The outbound detail marks every named line that exists in the loaded detail with the reason under each, says "lines 1, 2" (language-specific list separator) and keeps the stock hint; only the first marked line is an ARIA alert. Unknown or invalid numbers are ignored; with none left the general message is shown as before.

## Consequences

- Other INV callers (PUR receipt, INV documents) also receive the extensions; they are additive and ignored by clients that do not read them.
- Line numbers are only as meaningful as the caller's; INV's own documents pass their own line numbers.
- No migration, no new permission.
