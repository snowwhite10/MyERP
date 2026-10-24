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

## WU-FI-008d design review

The first other-payment concept could have treated any supplier transfer as settled AP or expensed a supplier advance/prepayment immediately. BR-FI-059/063 constrain it to an incurred non-invoice expense, leaving open AP and PUR unchanged; advances and prepaid assets require separate contracts. BR-FI-060 rejects AP control, payment clearing and prepaid-asset mappings. BR-FI-061/062 fix same-transaction posting, permanent source/reference uniqueness and committed-source timeout recovery. The reference key is scoped to withdrawal direction so a deposit does not collide accidentally; 008a must prove its exact SQL constraint alongside the existing purchase-payment rule. No product code, migration, UI or executable financial test was added; FI-AP-OTHER-001–020 are planned acceptance cases for 008a/b/c.

## WU-FI-009d design review

Review found three risks: using today's open AR/AP as an old opening balance, treating an unapplied receipt/payment as an allocation, and paging by a maximum identity that could include a transaction committed later with a lower identity. BR-FI-068–070 use signed committed source movements, exclude unapplied cash and require a durable cutoff for the exact committed set. FI-STMT-008/009 require two-connection and token tests before BI exposes paging/export. BI retains ownership of BlueWhale statement reports and cannot read FI tables. No product code, migration, UI or executable financial test was added; SQL-dependent acceptance remains TODO.

## WU-FI-010d design review

Review found that selecting GL bank lines by the current MD account mapping could silently move old transactions to a renamed or remapped fund account. BR-FI-075/076 require a saved fund identity on each cash leg. A second risk was deriving historical opening from today's balance or paging with a maximum identity despite out-of-order commits; BR-FI-078/079 require signed posted history and a proven committed cutoff. BR-FI-082 keeps external statement differences separate from the book and forbids an automatic balancing journal. FI-CASH-009/016/018 cover these boundaries. No product code, migration, UI or executable financial test was added; WU-FI-010a/b and WU-FI-011d retain their distinct acceptance work.

## WU-FI-011d design review

Review found three risks: an import retry could duplicate bank evidence, an automatic match could consume the same FI cash leg twice, and a reviewer might mistake an approved difference for a posted journal. BR-FI-085 fixes durable row identity and per-row outcomes; BR-FI-088–089 require unique eligible one-to-one matches, SQL uniqueness and fixed lock order; BR-FI-090–091 keep approval and accounting adjustments separate. The CSV contract rejects split deposits, FX and unsafe formula-bearing text until explicit policy exists. No product code, migration, UI or executable financial test was added. FI-RECON-001–030 remain planned acceptance for 011a/b/c; real SQL is unavailable while the service is stopped.
## WU-FI-012d design review

Review found that storing an editable card book value would let it diverge from GL; BR-FI-097 derives it only from committed depreciation sources and requires journal reconciliation before period close. Full-month depreciation and last-month residual rounding are explicit in BR-FI-095–096 to avoid cumulative over-depreciation. Disposal can otherwise understate accumulated depreciation or post proceeds twice; BR-FI-099 requires scheduled depreciation through disposal month and a distinct, checked proceeds source. Permanent source keys and the asset/period lock guard retries and concurrent runs. No product code, migration, UI or executable financial test was added; FI-FA-001–029 are planned acceptance for 012a/b/c/e while SQL is unavailable.
