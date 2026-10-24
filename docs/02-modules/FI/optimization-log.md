# FI optimization log

## WU-FI-000d design review

The existing architecture text permitted deferred FI posting after SD/PUR document completion, while current SD/PUR acceptance requires atomic FI effects. ADR-071 records the transaction rule for financial source posting. No production code was changed in this design unit. The planned contract separates FI open AR from SD unbilled commitments and requires a database credit-key lock, avoiding double counting and process-local race conditions. Real SQL tests remain necessary before this is considered working credit control.

## WU-FI-002d design review

Review found three risks in the initial source concept: trusting an SD-supplied snapshot without checking its saved issuing state, crediting an invoice already fully settled into negative AR, and treating an unknown timeout as permission to post a new FI source. BR-FI-007 requires an SD-owned saved-source verification contract; BR-FI-012 rejects credit below zero until a refund/unapplied-credit flow is designed; BR-FI-008 requires source-key lookup and immutable payload comparison. FI source uniqueness includes soft-deleted history, and FI returns `changed=false` for identical replay. No production code or executable financial test was added. DocGen unit tests passed 7/7; direct artifact generation was blocked by another process holding `MyERP_SRS.docx` open, so generated-document consistency was not claimed. Real SQL and authorised HTTP remain required for 002a/b/c.

## WU-FI-003d design review

The initial proposal risked deriving AP directly from caller totals, posting a stock purchase to an unspecified account and retrying an unknown timeout as a fresh source. BR-FI-015/017 require FI to verify persisted PUR source and saved components; BR-FI-018 rejects missing account policy instead of assuming receipt accrual or changing INV valuation; BR-FI-016 resolves committed source by permanent key. BR-FI-020 fixes lock order and transaction scope. No production code or payable product test exists in this design slice; real SQL and authorised HTTP are mandatory for WU-FI-003a/b/c.

## WU-FI-004d design review

The first GL model could have accepted arbitrary caller-provided lines, selected a period from processing time or treated an unbalanced base journal as a rounding difference. BR-FI-023–027 instead require a saved source date, FI-owned mapping and MD postable-account validation, plus an exact checked base balance. BR-FI-028–029 require permanent source uniqueness and same-transaction rollback with the upstream state. The design separates a reusable internal posting service from public read and period APIs, so an HTTP client cannot create a journal directly. No production code or executable GL test was added; WU-FI-004a/b/c must prove the invariants against real SQL, HTTP and UI before any Posted claim.

## WU-FI-005d design review

Review found that a receipt could be mistaken for settled AR, allowing SD credit to reopen before an invoice is actually paid. BR-FI-034/038 separate unapplied cash from committed receipt allocations and expose only the latter in FI open AR. The initial row-key idea also risked forbidding a legitimate second partial allocation of the same receipt to the same invoice; uniqueness is scoped to a settlement and receivable. BR-FI-036 fixes credit, receipt, receivable, settlement and GL lock order to guard two-connection over-allocation. No product code, migration, UI or executable financial test was added; FI-AR-COLL-001–028 are planned acceptance cases for 005a/b/c.

## WU-FI-006d design review

Review found that an approved payment could incorrectly clear AP before any payable was allocated. BR-FI-043 makes the payment an unapplied clearing balance and reduces open AP only for committed settlements. A per-payment lock alone would permit two payments to over-allocate one payable, so BR-FI-045 uses a common supplier lock before payment and payable keys, with reread under the SQL transaction. Separate partial settlements of the same payment and payable remain valid; row uniqueness is per settlement. BR-FI-047 resolves unknown timeout through a committed source lookup and never treats an in-flight row as success. The FI-owned payable ID prevents trusting a client-supplied PUR invoice number. No production code, migration, UI or executable financial test was added; FI-AP-PAY-001–028 are planned acceptance cases for 006a/b/c.

## WU-FI-007d design review

The first other-receipt concept could have treated a customer-named receipt as AR payment, or posted an unidentified advance directly to income. BR-FI-051/055 require a non-invoice earned-income category and keep AR, SD commitment and unapplied-sales-cash unchanged; an advance needs a separate liability contract. BR-FI-052/053 require FI-owned account mapping, MD validation and one saved-source GL transaction, avoiding client-selected accounts and partial SYS approval. BR-FI-054 preserves source/reference uniqueness through soft deletion and resolves unknown timeouts by committed lookup. No product code, migration, UI or executable financial test was added; FI-AR-OTHER-001–020 are planned acceptance cases for 007a/b/c.
