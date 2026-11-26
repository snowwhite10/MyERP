# ADR-206: Purchase-order exchange-rate snapshot columns before WU-PUR-006a

Date: 2026-11-27T00:29:00+08:00

Status: Accepted

## Context

WU-PUR-008b2 (ADR-205) must supply the base currency and exchange rate that FI needs to book a purchase invoice. BR-PUR-043 fixes the rate as the **saved purchase-order snapshot**, not the rate on the invoice or posting date. The full purchase-order schema belongs to WU-PUR-006a, which is BLOCKED (see `blocked.md`). The only existing order table is the invoice source placeholder `pur_purchase_order` (`Purchasing_InvoiceSources`, WU-PUR-008a46/50), which carries currency but no rate.

The company base currency is owned by MD. SD sales invoices obtain it from the order snapshot (`sal_order.base_currency_code` + `exchange_rate`), captured through MD `ICurrencyLookup.GetBaseCurrencyAsync` when the order is created; PUR already consumes the same contract (`QuotationComparisonService`, `RfqAwardService`). No SYS contract exposes a base currency.

## Decision

1. Add two **nullable** columns to the existing `pur_purchase_order`, mirroring SD: `base_currency_code char(3)` and `exchange_rate decimal(19,8)` (MASTER_PROMPT 6.3/6.6). Migration `Purchasing_OrderRateSnapshot`; no data backfill.
2. CHECK `ck_pur_order_rate_snapshot`: both null, or both present with `exchange_rate > 0` and rate 1 when order currency equals base currency. Every comparison is guarded by `IS NOT NULL`, because a CHECK that evaluates to UNKNOWN is accepted (the first real-SQL run proved a NULL rate slipped through).
3. The columns are nullable only because pre-006a placeholder rows exist. WU-PUR-006a must write both at order creation (from `ICurrencyLookup`, as SD does) and may tighten them to NOT NULL once no legacy rows remain; it keeps these names.
4. Posting never substitutes today's rate. `PurchaseInvoiceExchangeRules.Resolve` (pure) and `PurchaseInvoiceExchangeSource` (in-transaction reader, company-scoped, null when the order is missing/foreign) return the `PurchaseInvoiceExchangeSnapshot` consumed by ADR-205's mapper:
   - no snapshot → 422 `PUR.INVOICE.RATE_SNAPSHOT_MISSING`;
   - company has no base currency → 422 `PUR.INVOICE.BASE_CURRENCY_NOT_SET`;
   - snapshot base ≠ current company base (`ICurrencyLookup`) → 422 `PUR.INVOICE.BASE_CURRENCY_CHANGED`;
   - invoice/order currency mismatch, half snapshot, invalid rate, non-uppercase codes → `PUR.INVOICE.INVALID_POSTING_SNAPSHOT` (data error).
5. The base currency is read through MD Contracts only; PUR never reads `md_currency`.

## Consequences

008b3 can build the FI snapshot from PUR-owned data without waiting for 006a. Until 006a writes the snapshot, any invoice on a placeholder order without a rate is refused at posting with a clear reason rather than booked at a guessed rate. The real-API E2E that arranges sources by SQL (ADR-204) must set both columns when it starts posting (008b4).
