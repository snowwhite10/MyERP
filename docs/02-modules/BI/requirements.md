# BI reporting requirements

## WU-BI-000d financial read boundary (design only)

This unit fixes the BI side of BlueWhale financial reports 4.1.1.7.5.1–7. BI owns presentation, filtering and export. FI owns Posted accounting facts and their authorised read contracts. No report endpoint, page, table or executable product behaviour is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4117.5.1 | 1.0 | System should enable user to view Cash & Bank Report. | Developer | An authorised report for one company, fund account and inclusive date range shows FI's saved book opening, ordered Posted cash movements and closing at one committed cutoff. Bank statement balance is separately labelled and never substituted for book balance. |
| REQ_F4117.5.2 | 1.0 | System should enable user to view Accounts Payable Details Report. | Developer | FI's authorised Posted payable and committed allocation source supplies supplier, original amount, allocated amount, open amount, business date and immutable source links at one cutoff; Draft, Pending and unapplied payment are excluded. |
| REQ_F4117.5.3 | 1.0 | System should enable user to view Accounts Receivable Details Report. | Developer | FI's authorised Posted receivable and committed allocation source supplies customer, original amount, allocated amount, open amount, business date and immutable source links at one cutoff; Draft, Pending and unapplied collection are excluded. |
| REQ_F4117.5.4 | 1.0 | System should enable user to view Client Statement. | Developer | FI's authorised customer statement supplies opening, ordered charges, credits and allocations, closing and stable cutoff. Every page and export uses the same source set and company base currency. |
| REQ_F4117.5.5 | 1.0 | System should enable user to view Supplier Statement. | Developer | FI's authorised supplier statement supplies opening, ordered payables and allocations, closing and stable cutoff. Every page and export uses the same source set and company base currency. |
| REQ_F4117.5.6 | 1.0 | System should enable user to view Other Income & Expenses Detail Report. | Developer | Only FI Posted other collections and other payments appear, signed and separately classified with saved category, business date, base amount and journal/source IDs. They do not affect AR/AP open balances. |
| REQ_F4117.5.7 | 1.0 | System should allow user to export Other Income & Expenses Detail Report as Excel. | Developer | Export contains exactly the authorised frozen filtered set and totals seen in the report, escapes formula-leading text, names its cutoff and changes no accounting data. An expired cutoff fails explicitly instead of silently refreshing. |

The BI read layer must not query another module's tables. FI source contracts for cash book and partner statements are specified in REQ_F4116.12.1–3 and REQ_F4116.11.1–3; AR/AP detail and other income/expense report sources still require implementation slices. UI field IDs are allocated when pages exist. The other 15 BlueWhale report requirements remain in `reference-notes.md` and EP-BI1 for later slices.

Change record: 2026-10-24T17:36:23+08:00 WU-BI-000d records the BI financial read boundary and acceptance criteria without claiming a working report.
