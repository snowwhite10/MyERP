# ADR-128: FI collection receipt submit, final-approval posting and production source port; split 005b3

Date: 2026-11-14. Status: Accepted (WU-FI-005b3a).

## Context

WU-FI-005b3 had two parts: submit and approval for both receipts and settlements, and the two real source ports. Together that is more than 90 minutes of work. The receipt document (ADR-126) has no `approval_instance_id` column. The posting service (ADR-121) reads the saved document through `IFinanceCollectionReceiptSource`, and it rolls back the caller's transaction when it returns a Fault.

## Decision

- **Split.** 005b3a covers receipts. 005b3b covers settlements: submit and withdraw, the `AR.SETTLEMENT` handler, and the real `IFinanceCollectionWriteOffSource` built the same way.
- **No instance column.** SYS allows only one pending round per document, and the receipt is Pending only while that round is open. So "status = Pending" identifies the round, and adding a migration is not needed. A decision for a receipt that is not Pending gets 409 `RECEIPT_APPROVAL_STATE_CHANGED`.
- **Approve, then post, then mark Posted, all in the caller's transaction.** `CollectionReceiptApprovalPosting` first saves Approved. The source port reads committed-in-transaction data, so it must see Approved, and the version it saves becomes the posting `SourceVersion`. Then it posts and sets Posted, which the caller saves. The SYS result handler and the zero-step path in submit share this class.
- **Posting failure fails the approval.** Rejected or Fault results are turned into ERP exceptions that keep the FI posting code, so SYS rolls back the step decision and the approver sees the reason. Fault has already rolled the transaction back. Submit checks `CurrentTransaction` before it rolls back.
- **Fingerprint.** `FI-COLLECTION-RECEIPT-v1:` followed by SHA-256 over the length-prefixed posting fields (company, ID, customer, date, currency, amount to 4 decimals, fund account, reference). Status, number, remark and audit fields are excluded, so an approval does not change it. This follows the PUR invoice fingerprint pattern.
- **Source port.** It reads on the scoped DbContext and requires that the supplied `DbTransaction` be the context's current one. Otherwise it throws InvalidOperation. It ignores only the soft-delete filter, and deleted rows are reported as Deleted. `BaseCurrency` is the current MD base currency, not the saved currency.
- **Permission.** `AR.COLLECTION.SUBMIT` covers both submit and withdraw. It is checked in the service now. The catalog entry and roles come in 005b4.
- **Tests.** The SYS engine is a test double at service level. The real SYS engine with this handler is covered end to end over HTTP in 005b4.
- **Test hosts.** `FinanceModule` now registers the real source. The shared test `Provider` drops it when a test registers a test double for the same port.

## Consequences

- 005b3b repeats the pattern for settlements (fingerprint over header and lines in receivable-ID order).
- The first real approval also needs the `AR.COLLECTION` number rule and the permission seed, which come in 005b4.
