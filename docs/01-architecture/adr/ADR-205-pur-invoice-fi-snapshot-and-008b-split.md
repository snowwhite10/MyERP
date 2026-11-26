# ADR-205: PUR invoice → FI payable snapshot mapping and WU-PUR-008b split

Date: 2026-11-27T00:04:47+08:00

Status: Accepted

## Context

WU-PUR-008b must post a Matched purchase invoice to FI AP/GL in the same SQL transaction. FI already ships the contract (`MyERP.Modules.Finance.Contracts/PurchaseInvoicePosting.cs`, WU-FI-003a2b, ADR-116/117): FI calls the PUR-implemented port `IFinancePurchaseInvoiceSource` and expects a `FinancePurchaseInvoiceSnapshot` with original **and base-currency** header/line amounts, base currency, exchange rate, source version and fingerprint. No PUR implementation of the port exists yet.

The saved PUR invoice (`pur_purchase_invoice`) has original-currency amounts only. BR-PUR-043 says the rate is the saved purchase-order snapshot, but the current `pur_purchase_order` source table (placeholder until WU-PUR-006a, still BLOCKED) has no exchange-rate column, and the company base currency lives outside PUR. 008b is therefore too large and partly blocked to do in one round.

## Decision

1. **Split 008b** into:
   - **008b1** (offline, this round): pure `PurchaseInvoiceFinanceSnapshot.Build` maps a saved invoice + PUR fingerprint + rate snapshot (base currency, rate) to `FinancePurchaseInvoiceSnapshot`. No SQL, no FI call.
   - **008b2**: rate/base-currency source — add `exchange_rate` to the purchase-order snapshot (with 006a, or a minimal migration on the existing source table if 006a stays blocked) and read the company base currency through an existing SYS/MD contract; real SQL.
   - **008b3**: PUR `IFinancePurchaseInvoiceSource` SQL implementation (read inside caller transaction, company check, null for missing/foreign) registered in PurchasingModule; real-SQL test with FI `PurchaseInvoicePostingService`.
   - **008b4**: `POST /api/v1/pur/invoices/{id}/post` transaction (lock order → receipt lines → invoice → FI source key; reservation → invoiced transfer; FI call; receipt compare; Posted), replay/rollback, HTTP/Swagger, permission `PUR.INVOICE.POST`.
   - **008b5**: purchase-return limit against posted invoiced quantity (BR-PUR) verification.
2. **Mapping rules (008b1)**:
   - State: `IsDeleted` → `Deleted` first; otherwise Draft/Matched/Disputed/Rejected/Posted map 1:1. PUR does not decide postability; FI rejects non-Matched.
   - Original header amounts are the saved `total_*`; lines are the saved line `net/tax/gross` ordered by line number. PUR never recalculates; FI checks reconciliation.
   - Base amounts per line: `Money.Round(net × rate, base)` and `Money.Round(tax × rate, base)`, base gross = base net + base tax (same order as SD quotation/order amounts, MASTER_PROMPT 6.3). Header base = sum of line base amounts.
   - Same currency as base: rate must be exactly 1 and base = original (no rounding to base precision applied; FI requires identical amounts).
   - Rate must be > 0, ≤ 8 decimals and ≤ FI max rate; base currency ISO uppercase; version > 0; fingerprint is passed through unchanged. Invalid inputs throw `PUR.INVOICE.INVALID_POSTING_SNAPSHOT` (programming/data error, mapped to 500 by the caller, never a partial snapshot).
   - The fingerprint (ADR for 008a10, `PUR-INVOICE-POST-v1`) is not extended with the rate: the rate comes from an approved, immutable order snapshot, and FI's receipt returns base gross which PUR compares in 008b4.
3. Purchasing may reference `MyERP.Modules.Finance.Contracts` (module boundary rule: only other modules' Contracts).

## Consequences

The FI field mapping is test-covered before SQL work; 008b2–b5 can proceed independently of the UI. Until 008b4 lands, no invoice is shown as Posted and no screen claims posting.
