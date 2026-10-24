# FI Finance requirements

## WU-FI-000d customer credit contract (design only)

BlueWhale Accounting sub-functions 6.1–6.6 retain their existing IDs for collections, payments and write-offs. New sub-function 6.7 is Customer Credit. This design unit defines the FI-owned contract required by REQ_F4112.6.1; it adds no database table, service, API or screen. WU-FI-001a/b must implement and verify it before SD order approval can use it.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.7.1 | 1.0 | System should allow user to maintain a customer credit limit in the company base currency. | Developer | One effective, approved limit per company/customer/date is returned; overlapping active periods, disabled or deleted limits, currency mismatch, negative amount and stale version are rejected. Zero is valid. Every mutation is authorised, scoped, audited and protected by the shared credit lock. |
| REQ_F4116.7.2 | 1.0 | System should enable user to obtain the current customer credit exposure. | Developer | FI returns the effective limit and committed, posted open AR for one company/customer with base currency, evaluation date and version. Missing or ambiguous data is an explicit unavailable result. SD receives no FI document details and cannot substitute zero for missing AR. |
| REQ_F4116.7.3 | 1.0 | System should enable user to reserve a transaction-scoped customer credit decision lock. | Developer | A caller joins the existing SQL transaction and locks the FI-owned company/customer key before reading exposure and SD commitments. Limit edits, AR changes and SD order decisions follow the same lock order. Rollback releases the lock without a saved credit decision. |

Acceptance is planned. FI never counts SD unbilled commitments; SD supplies that component when evaluating an order. A backdated order is evaluated at server `IClock.Today`, not its document date. See BR-FI-001–006 and FI-CREDIT-001–020.

## WU-FI-002d sales receivable sources (design only)

This slice defines FI's side of REQ_F4112.5.1 and REQ_F4112.7.1. It creates no journal, receivable, credit note, endpoint or database table. FI owns the posted receivable and credit note; SD owns the commercial invoice, return and unbilled commitment. A source may claim Posted only after FI and SD effects commit together under ADR-071.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.8.1 | 1.0 | System should enable user to post a receivable from an issued sales invoice. | Developer | One immutable FI source per company and saved SD invoice posts a balanced GL journal, tax liability and gross AR in the caller's SQL transaction. FI verifies source identity, company/customer, base and document currencies, saved component totals and account eligibility through public contracts. Same source and payload replays the original result; changed payload conflicts. FI and SD move equal base gross from approved unbilled commitment to open AR under the credit key lock before commit. |
| REQ_F4116.8.2 | 1.0 | System should enable user to issue a sales return credit note against posted receivables. | Developer | One immutable FI credit source per company and Posted SD return can link multiple original invoice postings, with invoice-line allocations. FI validates remaining creditable original/base net and tax, posts a balanced reversal journal and applies the credit only to those original receivables. Billed credit reduces open AR; unbilled return release remains SD-owned. Duplicate or failed calls create no second credit, allocation, journal or exposure change. |
| REQ_F4116.8.3 | 1.0 | System should enable user to inspect sales receivable source and credit allocations. | Developer | A company-scoped, authorised read reports immutable source IDs, original and remaining amounts, currency, posting state, journal ID and linked credit allocations. It never reports uncommitted or other-company details. Source replay and timeout recovery can resolve the committed result by the stable source key; a missing source is explicit. |

Change record: 2026-10-24T12:28:04+08:00 WU-FI-002d adds the planned sales receivable and credit source contract. Implementation, real SQL tests and any read screen are assigned to later slices; none is claimed here.

## WU-FI-003d purchase payable source (design only)

This slice defines FI's side of REQ_F4114.8.5. PUR owns the saved invoice, matching decision and receipt quantity; FI owns the payable, tax ledger and GL journal. No table, endpoint, screen or executable product test is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.9.1 | 1.0 | System should enable user to post a payable from a matched purchase invoice. | Developer | FI verifies the saved PUR invoice and accepted match through a PUR public contract in the caller's SQL transaction. One company/PUR/invoice source creates one payable and balanced GL journal using saved original/base net, input tax and gross; active account mapping is checked through MD's public contract. FI and PUR commit the source, invoiced quantity, Posted state and audit together. An unavailable FI or GL service leaves all unchanged. |
| REQ_F4116.9.2 | 1.0 | System should enable user to recover a purchase payable posting by its source key. | Developer | Identical committed source and canonical payload return the original payable and journal IDs with changed=false. Changed payload or stale source version conflicts; an unknown timeout is resolved by a fresh committed source read before retry. Soft deletion cannot release the source key or alter Posted history. |
| REQ_F4116.9.3 | 1.0 | System should enable user to inspect purchase payable sources. | Developer | A company-scoped authorised read shows saved PUR source identity, supplier, original/base net, tax and gross, journal, original/open payable and posting state; no uncommitted or other-company source is exposed. FI read does not recalculate historical amounts with current MD rates. |

## WU-FI-004d GL posting foundation (design only)

| Requirement ID | Version | Description | Author | Acceptance criteria |
|---|---|---|---|---|
| REQ_F4116.10.1 | 1.0 | System should enable user to maintain company accounting periods for general ledger posting. | Developer | Company-scoped periods have inclusive start/end dates, Open/Closed state and version. Overlap is rejected under a company period lock, including a concurrent insert. A posting business date must match exactly one Open period; missing, overlapping or Closed periods block the entire posting with a named reason. A period containing a Posted journal cannot be deleted or moved to exclude that journal. |
| REQ_F4116.10.2 | 1.0 | System should enable user to post balanced general ledger journals from authorised module sources. | Developer | `IGlPostingService` joins the caller's SQL transaction, verifies one saved source and company/base currency, checks every MD account is currently postable, requires at least two nonzero lines and equal positive base-currency debit/credit totals using DECIMAL(19,4). It writes one immutable journal and its lines through FI only. A missing period, invalid account, unbalanced or overflowed journal produces no GL, source state or audit mutation. |
| REQ_F4116.10.3 | 1.0 | System should enable user to recover and inspect posted general ledger journals by their source key. | Developer | Permanent `(company, module, type, sourceId)` uniqueness survives soft deletion; identical committed fingerprint returns the same journal ID with `changed=false`, changed content returns 409. Unknown timeout is resolved through a fresh committed lookup. Authorised company/SYS-scoped reads show saved source, period, currency, balanced lines and audit reference without exposing uncommitted or other-company rows. |

These criteria are planned. WU-FI-004a/b/c will implement and prove them with real SQL, authorised HTTP and UI tests; WU-FI-004d allocates no table or interface IDs and claims no Posted GL journal. See BR-FI-023–030 and FI-GL-001–024.

Change record: 2026-10-24T12:31:22+08:00 WU-FI-003d adds the planned purchase payable source contract. WU-FI-003a/b/c own implementation and verification after the GL foundation and SQL service are available.

## WU-FI-005d sales collection and AR settlement (design only)

This slice specifies the existing BlueWhale Accounting sub-functions 6.1 Sales Collection and 6.5 Collection Write-off as receipt and settlement workflows. Here “write-off” means applying a posted receipt to an open invoice, not forgiving bad debt. No receipt, allocation, journal, endpoint or screen is implemented by this design unit.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.1.1 | 1.0 | System should enable user to view sales collection details. | Developer | Authorised company/customer scoped read shows status, saved currency and amounts, receipt source, journal, allocated and unapplied balances; uncommitted and foreign-company data are hidden. |
| REQ_F4116.1.2 | 1.0 | System should allow user to create a sales collection. | Developer | Draft records a positive receipt, company/customer, currency, business date and unique bank reference; it posts no GL or AR change. Required and precision errors identify fields. |
| REQ_F4116.1.3 | 1.0 | System should allow user to edit sales collection details. | Developer | Only Draft/Rejected may be edited with matching version and authority. Changing identity, bank reference or amount after posting is prohibited. |
| REQ_F4116.1.4 | 1.0 | System should allow user to approve sales collections. | Developer | SYS final approval under the customer credit lock posts one immutable receipt source and balanced bank/unapplied-cash GL journal in the same transaction. Replay returns unchanged; failure leaves receipt unposted. Receipt approval alone does not reduce open AR. |
| REQ_F4116.1.5 | 1.0 | System should allow user to reject sales collections. | Developer | An authorised Pending receipt may be rejected with reason, preserving version and history; no journal or AR change occurs. |
| REQ_F4116.1.6 | 1.0 | System should allow user to delete sales collection records. | Developer | Only an unposted Draft/Rejected receipt may be soft deleted; posted source keys and accounting history remain permanent. |
| REQ_F4116.5.1 | 1.0 | System should enable user to view collection write-off details. | Developer | The read shows immutable receipt-to-invoice allocations, saved original/base amounts and remaining receipt/AR balances, with company and permission scope. |
| REQ_F4116.5.2 | 1.0 | System should allow user to create collection write-offs. | Developer | A Draft allocation references one Posted receipt and 1–1000 distinct same-company/customer/currency Posted receivables. Positive allocated amounts cannot exceed either unapplied receipt or each open receivable; no GL or exposure changes in Draft. |
| REQ_F4116.5.3 | 1.0 | System should allow user to edit collection write-off details. | Developer | Only Draft/Rejected may change ordered invoice allocations with matching version; stale version and changed Posted allocation are rejected. |
| REQ_F4116.5.4 | 1.0 | System should allow user to approve collection write-offs. | Developer | SYS final approval locks customer, receipt and receivables in fixed order, rechecks committed balances, posts one balanced clearing-to-AR journal and immutable allocations atomically. Only committed allocations reduce FI open AR and therefore SD credit exposure. Exact replay changes nothing; over-allocation and concurrent approval fail. |
| REQ_F4116.5.5 | 1.0 | System should allow user to reject collection write-offs. | Developer | Pending allocation may be rejected with reason and audit; no receipt/AR balance or GL change occurs. |
| REQ_F4116.5.6 | 1.0 | System should allow user to delete collection write-off records. | Developer | Only unposted Draft/Rejected allocation may be soft deleted. Posted allocation is immutable; correction requires a separately specified reversal. |

Acceptance is planned for later implementation slices. This scope excludes foreign-currency matching, bank reconciliation, refunds, bad-debt forgiveness and receipt reversal; each needs a separate contract before use. See BR-FI-031–040 and FI-AR-COLL-001–028.

Change record: 2026-10-24T12:38:48+08:00 WU-FI-005d defines the existing 6.1/6.5 baseline without allocating new IDs or claiming product verification.

## WU-FI-006d purchase payment and AP settlement (design only)

This slice specifies BlueWhale Accounting 6.3 Purchase Payment and 6.6 Payment Write-off. “Write-off” here means applying a posted payment to an open supplier invoice, not forgiving debt. FI owns payment, settlement, payable and GL effects; the originating PUR invoice remains immutable. No table, endpoint, screen or executable product test is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.3.1 | 1.0 | System should enable user to view purchase payment details. | Developer | Authorised company/supplier scoped read shows state, saved currency, original and unapplied amounts, bank reference, source and journal; uncommitted and foreign-company data are hidden. |
| REQ_F4116.3.2 | 1.0 | System should allow user to create purchase payments. | Developer | Draft saves positive amount, company/supplier, base currency, business date and bank reference with version; it changes neither GL nor open AP. Required and precision errors name the field. |
| REQ_F4116.3.3 | 1.0 | System should allow user to edit purchase payment details. | Developer | Only Draft/Rejected may be edited with matching version and authority. A Posted amount, supplier or bank identity is immutable. |
| REQ_F4116.3.4 | 1.0 | System should allow user to approve purchase payments. | Developer | SYS final approval posts one permanent FI payment source and balanced unapplied-payment debit/bank credit journal in the caller transaction. It creates unapplied payment; open AP changes only when settlement commits. Replay is unchanged and any failure rolls back state, number and audit. |
| REQ_F4116.3.5 | 1.0 | System should allow user to reject purchase payments. | Developer | Authorised Pending payment may be rejected with reason and audit; no GL or AP change occurs. |
| REQ_F4116.3.6 | 1.0 | System should allow user to delete purchase payments. | Developer | Only unposted Draft/Rejected may be soft deleted; Posted source key and accounting history remain permanent. |
| REQ_F4116.6.1 | 1.0 | System should enable user to view payment write-off details. | Developer | Company/supplier scoped read shows immutable payment-to-payable allocations, original/base amounts and current unapplied/open balances with journal references. |
| REQ_F4116.6.2 | 1.0 | System should allow user to create payment write-offs. | Developer | Draft references one Posted payment and 1–1000 distinct Posted FI payables for the same company, supplier and base currency. Positive allocations cannot exceed unapplied payment or each open payable; Draft changes no balance or GL. |
| REQ_F4116.6.3 | 1.0 | System should allow user to edit payment write-off details. | Developer | Only Draft/Rejected may change ordered allocations with matching version; stale version and Posted changes are rejected. |
| REQ_F4116.6.4 | 1.0 | System should allow user to approve payment write-offs. | Developer | SYS final approval locks supplier, payment, payables and source in fixed order, rechecks committed balances, posts AP debit/unapplied-payment credit and immutable allocations atomically. Only committed allocations reduce open AP. Replay changes nothing; over-allocation or concurrent approval fails. |
| REQ_F4116.6.5 | 1.0 | System should allow user to reject payment write-offs. | Developer | Pending allocation may be rejected with reason and audit; payment and AP balances and GL remain unchanged. |
| REQ_F4116.6.6 | 1.0 | System should allow user to delete payment write-offs. | Developer | Only unposted Draft/Rejected may be soft deleted. Posted allocation requires a separately specified compensating transaction. |

Acceptance is planned for WU-FI-006a/b/c. This base-currency slice excludes foreign-currency settlement, refund, bank reconciliation, prepayment reclassification and posted reversal until separate contracts exist. See BR-FI-041–050 and FI-AP-PAY-001–028.

Change record: 2026-10-24T16:58:26+08:00 WU-FI-006d defines the existing 6.3/6.6 baseline without allocating table or interface IDs or claiming product verification.

## WU-FI-007d other collection (design only)

This slice specifies the existing BlueWhale Accounting 6.2 Other Collection. It records a receipt from a non-invoice income source, using an approved MD income category and a FI-owned GL mapping. It is distinct from Sales Collection (6.1): it cannot settle an SD invoice, create an AR receivable, release SD unbilled commitment or alter customer credit exposure. An unidentified advance or invoice payment belongs to a separately specified unapplied-cash flow. No table, endpoint, page or executable product test is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.2.1 | 1.0 | System should enable user to view other collection details. | Developer | An authorised company-scoped read shows saved state, income category, payer description, bank/reference, base amount, source and journal; it labels the receipt as non-AR income and hides uncommitted or foreign-company rows. |
| REQ_F4116.2.2 | 1.0 | System should allow user to create an other collection. | Developer | Draft saves a positive base-currency amount, business date, income category, payer description and bank/reference with version; MD category and bank are checked by public contracts. Draft creates no GL, cash or AR effect. Required, length and precision errors identify fields. |
| REQ_F4116.2.3 | 1.0 | System should allow user to edit other collection details. | Developer | Only Draft/Rejected can be version-edited by an authorised user. Pending/Posted identity, category, bank and amount are immutable; stale version returns 409. |
| REQ_F4116.2.4 | 1.0 | System should allow user to approve other collections. | Developer | SYS final approval verifies the saved Draft/Pending source, active category and bank, exactly one Open GL period and eligible FI-owned account mapping in the caller transaction. One permanent source posts balanced bank debit and mapped income credit; FI state, journal and audit commit together. Identical committed replay returns changed=false and cannot touch AR or SD. |
| REQ_F4116.2.5 | 1.0 | System should allow user to reject other collections. | Developer | Authorised Pending rejection saves reason and workflow history with no GL, AR or cash posting. |
| REQ_F4116.2.6 | 1.0 | System should allow user to delete other collection records. | Developer | Only unposted Draft/Rejected may be soft deleted. A Posted source key, journal and audit remain permanent; correction needs a separately specified compensating source. |

Acceptance is planned for WU-FI-007a/b/c. This base-currency scope excludes invoice settlement, customer advance, refund, FX, bank reconciliation, batch import and posted reversal. See BR-FI-051–058 and FI-AR-OTHER-001–020.

Change record: 2026-10-24T17:00:28+08:00 WU-FI-007d defines the 6.2 baseline without allocating table/interface IDs or claiming product verification.

## WU-FI-008d other payment (design only)

This slice specifies BlueWhale Accounting 6.4 Other Payment: a payment for an already incurred, non-invoice expense using an active MD expense category and FI-owned account mapping. It is separate from Purchase Payment (6.3): it cannot settle a PUR invoice, create an AP allocation or reduce open AP. A supplier advance belongs to a separate liability contract. No table, endpoint, page or executable financial product test is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.4.1 | 1.0 | System should enable user to view other payment details. | Developer | An authorised company-scoped read shows saved state, expense category, payee description, bank/reference, base amount, source and journal, clearly labelled non-AP expense; uncommitted and foreign-company rows are hidden. |
| REQ_F4116.4.2 | 1.0 | System should allow user to create other payments. | Developer | Draft saves positive company-base-currency amount, business date, active expense category, payee description and bank/reference with version. FI checks category and bank through MD public contracts. Draft changes no bank, GL or AP balance; required, length and precision errors name fields. |
| REQ_F4116.4.3 | 1.0 | System should allow user to edit other payment details. | Developer | Only Draft/Rejected can be edited by an authorised user with matching version. Pending/Posted identity, category, bank and amount are immutable; stale version returns 409. |
| REQ_F4116.4.4 | 1.0 | System should allow user to approve other payments. | Developer | SYS final approval verifies the persisted source, active category and bank, one Open GL period and FI-owned category-to-expense-account mapping in the caller transaction. One permanent source posts expense debit and bank credit for equal base amount. FI state, journal and audit commit atomically. Identical committed replay returns changed=false and cannot alter AP or PUR. |
| REQ_F4116.4.5 | 1.0 | System should allow user to reject other payments. | Developer | Authorised Pending rejection saves reason and workflow history without GL, bank or AP posting. |
| REQ_F4116.4.6 | 1.0 | System should allow user to delete other payment records. | Developer | Only unposted Draft/Rejected may be soft deleted. Posted source key, journal and audit remain permanent; correction needs a separately specified compensating source. |

Acceptance is planned for WU-FI-008a/b/c. The base-currency slice excludes supplier advance, purchase-invoice settlement, prepaid-asset recognition, FX, refund, bank reconciliation, batch import and posted reversal. See BR-FI-059–066 and FI-AP-OTHER-001–020.

Change record: 2026-10-24T17:07:19+08:00 WU-FI-008d defines the 6.4 baseline without allocating table/interface IDs or claiming product verification.

## WU-FI-009d statement source contract (design only)

BlueWhale Client/Supplier Statement requirements REQ_F4117.5.4–5 and interfaces 4.4.1.7.20–21 remain BI-owned. These FI requirements define only the authorised accounting source that BI will consume. The statement is a read of committed history, not a new posting, settlement or persisted invoice balance. No table, endpoint, page or executable financial product test is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.11.1 | 1.0 | System should enable user to retrieve a customer statement source from posted receivables and settlements. | Developer | One company/customer/base-currency/date-range request returns an as-of-consistent opening balance, ordered posted invoice or credit-source movements, committed collection allocations and closing balance. Opening includes movements before start; closing = opening + period charges - credits - allocations. Draft, Pending, Rejected, other income and unapplied receipts have no AR balance effect. Each line cites an immutable FI source and saved date/amount; FI never reads SD tables directly. |
| REQ_F4116.11.2 | 1.0 | System should enable user to retrieve a supplier statement source from posted payables and settlements. | Developer | One company/supplier/base-currency/date-range request returns an as-of-consistent opening balance, ordered posted purchase-payable and committed payment-allocation movements and closing balance. Opening includes movements before start; closing = opening + period payables - allocations. Draft/Pending documents, other expense and unapplied payments have no AP balance effect. Each line cites immutable FI source data; FI never reads PUR tables directly. |
| REQ_F4116.11.3 | 1.0 | System should enable user to verify and page statement balances without changing accounting data. | Developer | FI returns a stable statement cutoff/version and deterministic `(businessDate, postingTime, sourceType, sourceId, lineId)` order; all pages and BI export use the same committed cutoff. The range is inclusive and bounded, sums use checked DECIMAL(19,4), and invalid or inaccessible partner/company/currency/range fails with a named reason. Repeated reads change no source, GL, audit or balance; a later posting appears only in a new statement cutoff. |

Acceptance belongs to WU-FI-009a/b after the FI posting sources exist. A historical as-of balance follows the committed posting date and saved business date specified in BR-FI-067–074; it is not the current open balance backdated by subtracting current allocations. See FI-STMT-001–020.

## WU-FI-010d cash and bank book source (design only)

BlueWhale Cash & Bank Report REQ_F4117.5.1 and interface 4.4.1.7.17 remain BI-owned. MD owns fund-account definitions (`REQ_DS43.125`), while FI owns posted cash movements and the accounting balance. This slice defines a read-only FI source contract and the boundary of a later external bank reconciliation. No table, endpoint, page or executable financial product test is delivered here.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.12.1 | 1.0 | System should enable user to retrieve posted cash and bank book movements. | Developer | For one authorised company and MD fund account, FI returns only committed GL cash-side lines tagged with the saved fund-account identity, business date, signed base amount, immutable source and journal/line IDs. Draft, Pending, Rejected, rolled-back and non-cash GL lines contribute zero. FI uses an MD public lookup, not an MD table join. |
| REQ_F4116.12.2 | 1.0 | System should enable user to verify opening, movement and closing balances for a cash or bank account. | Developer | Inclusive bounded date range returns opening from all earlier committed FI cash lines, ordered period debit/credit movements and closing = opening + debits - credits in company base currency. Every page and BI export shares one consistent committed cutoff; later/backdated posts appear only with a fresh cutoff. Empty period returns opening = closing. |
| REQ_F4116.12.3 | 1.0 | System should enable user to distinguish book balance from external bank reconciliation. | Developer | FI exposes book balance and immutable source links only. External statement rows, matched/unmatched state, bank balance and difference are separately labelled and cannot mutate the book through this read contract. A discrepancy requires a later authorised accounting source; no automatic balancing entry, silent edit or posted deletion is permitted. |

Acceptance belongs to WU-FI-010a/b. External statement import, matching, approval and adjustment source require WU-FI-011d and later implementation slices. See BR-FI-075–082 and FI-CASH-001–022.

Change record: 2026-10-24T17:13:59+08:00 WU-FI-010d adds FI cash-book source requirements without allocating table/interface IDs or claiming product verification.

## WU-FI-011d external bank reconciliation (design only)

This FI/CM function compares an imported external bank statement with FI's committed cash-book source for one company and one MD bank fund account. An imported row is evidence, not a journal. Reconciliation decisions are separately saved and audited; only a separately approved accounting source may change the book. This unit defines requirements and acceptance cases, without a migration, route, page or executable financial product test.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.13.1 | 1.0 | System should allow user to import an external bank statement for reconciliation. | Developer | An authorised user uploads one UTF-8 CSV statement for one company, active MD bank fund account, account currency and inclusive date range. FI validates header, encoding, size, row count, dates, signed amounts and bank row identity; it saves valid rows as external evidence with file digest, row number and per-row outcome. Exact re-upload reports zero changes. Invalid rows have explicit reasons and cannot be matched. Import does not create a journal or alter book balance. |
| REQ_F4116.13.2 | 1.0 | System should enable user to view external bank rows beside posted book movements. | Developer | Company-scoped view shows bank opening/closing and imported rows separately from the FI cash-book opening/closing at one durable committed cutoff. Each side has its own currency, date and source identity. Missing bank opening, unsupported currency or incomplete FI opening history is labelled incomplete; no inferred equality or guessed difference is shown. |
| REQ_F4116.13.3 | 1.0 | System should allow user to match and unmatch bank rows to book movements. | Developer | A versioned decision links exactly one eligible bank row to one committed FI cash leg of the same company, fund account, currency and amount/sign, within a documented date tolerance. Automatic matching accepts only a unique candidate; ambiguous or amount-mismatched rows stay unmatched with reasons. An authorised manual match records actor, reason and evidence. No book or GL value changes. |
| REQ_F4116.13.4 | 1.0 | System should allow user to submit, approve or reject a reconciliation. | Developer | Submit freezes the imported row set, match decisions, FI committed cutoff and bank opening/closing evidence. A separate reviewer approves only when every row and book leg has an explicit disposition and arithmetic checks pass; rejection records reason and reopens a versioned draft. Approval retains unmatched differences with reasons and does not silently post adjustment journals. |
| REQ_F4116.13.5 | 1.0 | System should enable user to inspect unmatched differences and authorised adjustments. | Developer | Each difference identifies its bank row or book leg, sign, amount, reason, reviewer and related adjustment source if later posted. Bank fee, interest, correction and reversal require separate approved FI source requirements and balanced GL posting; reconciliation cannot directly edit a Posted journal, cash leg or imported evidence. A new cash-book cutoff shows any later adjustment. |
| REQ_F4116.13.6 | 1.0 | System should enable user to export and audit reconciliation results. | Developer | Export and paged detail use the approved frozen row set and FI cutoff, include per-row match/disposition, bank and book totals, difference, actor/time/version and immutable source links. Read/export creates no posting. Missing authority, company scope, expired draft token and server failure have named outcomes; zero-change replay uses a warning rather than a success claim. |

Acceptance is planned for WU-FI-011a/b/c after FI cash-book and GL sources exist. Base-currency bank accounts and one-to-one matching are the initial implementation scope; split/combined deposits, FX and accounting adjustments need separate requirements before implementation. See BR-FI-083–092 and FI-RECON-001–030.

Change record: 2026-10-24T17:18:15+08:00 WU-FI-011d defines the external reconciliation boundary and planned acceptance without claiming product verification.
