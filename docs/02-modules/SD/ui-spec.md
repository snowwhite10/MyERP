# SD UI specification (WU-SD-000 proposed)

The first nine interface numbers preserve the reference mapping: 2.1–2.3 quotation View/Create/Edit, 2.4–2.6 order View/Create/Edit, 2.7–2.9 delivery View/Create/Edit; 2.10 is Select Client. SRS 4.4, SRS 4.5 and SDS 4.2 use this same View → Create → Edit sequence. The first deliverable page defines each field's `REQ_IO_A` ID and English Description/Purpose/Format/Range in its implementation unit; WU-SD-000 allocates no interface item IDs for unbuilt controls.

| Interface No. | Interface Name | Planned contents | Related REQ_F |
|---|---|---|---|
| 4.4.1.2.1 | Sales Quotation View Interface | Paged list, saved detail, approval history, print/export states | REQ_F4112.1.1, REQ_F4112.1.3 |
| 4.4.1.2.2 | Sales Quotation Create Interface | Customer, date, currency, lines, price and output tax | REQ_F4112.1.2 |
| 4.4.1.2.3 | Sales Quotation Edit Interface | Saved lines, version, submit/void actions | REQ_F4112.1.2, REQ_F4112.1.3 |
| 4.4.1.2.4 | Sales Order View Interface | Paged list, delivery balance, approval history, print/export | REQ_F4112.2.1, REQ_F4112.2.4 |
| 4.4.1.2.5 | Sales Order Create Interface | Customer, quotation source, lines, credit result | REQ_F4112.2.2, REQ_F4112.6.1 |
| 4.4.1.2.6 | Sales Order Edit Interface | Versioned draft editing and submit/cancel | REQ_F4112.2.3, REQ_F4112.2.6 |
| 4.4.1.2.7 | Sales Outbound View Interface | Paged deliveries, saved tracking, INV reference | REQ_F4112.3.1, REQ_F4112.3.4 |
| 4.4.1.2.8 | Sales Outbound Create Interface | Approved order balance, warehouse and tracking | REQ_F4112.3.2 |
| 4.4.1.2.9 | Sales Outbound Edit Interface | Versioned draft editing and submit | REQ_F4112.3.3, REQ_F4112.3.4 |
| 4.4.1.2.10 | Select Client Interface | Current-company approved customers with pagination | REQ_F4112.1.2, REQ_F4112.2.2 |

Additional price, invoice, credit and return interfaces append after 2.10 when specified. Every page needs zh-Hant/zh-Hans/en, light/dark, explicit empty/search-empty/no-permission, validation and server/network error states. A disabled action explains the reason; 5xx shows traceId. Success is displayed only after a persisted change. Implementation units use `myerp-ux-review` and `web-design-guidelines` with Playwright screenshots. WU-SD-001 adds no page or dialog; UI acceptance remains with the later page units.

WU-SD-002d interaction design only: View shows stored status and a separate expired indicator, source order link and SYS history. Create/Edit display server-calculated line net/tax/gross and header totals; save/submit state the exact reference or line that failed. Edit of Rejected explains that saving restarts Draft while retaining earlier approvals. Pending disables edit/delete/void with a reason; Approved enables conversion only while unexpired and unused, and void only while unused. No change toast appears for a conflict or zero persisted rows. WU-SD-002b allocates `REQ_IO_A2.1.*`, `A2.2.*` and `A2.3.*` against actual controls and English SRS item text, then supplies three-language, light/dark screenshots. No item IDs or screen completion are claimed here.

WU-SD-003d order interaction design only: View (2.4) shows saved status, source quote, approval rounds, each line's ordered/delivered/pending/remaining base-unit quantity, monetary snapshots and specific credit result. Create (2.5) offers manual entry or an eligible quote selector; selecting a quote fills all saved lines and marks the commercial snapshot read-only. An unavailable FI/AR credit contract shows a clear block on submit, without suggesting an approval succeeded. Edit (2.6) permits full-line replacement only in Draft/Rejected, displays the saved version and explains that editing Rejected restarts Draft while retaining SYS history. Pending has decision-only actions; Approved cancellation is disabled with a reason when any pending/posted delivery exists. Conflict, invalid reference, credit refusal, 500/traceId and timeout clear false success and keep user-entered draft where safe. WU-SD-003c allocates actual `REQ_IO_A2.4.*`–`A2.6.*` IDs and verifies three languages, light/dark and persisted-result consistency; this design adds no screen or item ID.
