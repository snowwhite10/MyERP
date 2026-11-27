import { validIsoDate } from '../../shared/date';
import { OtherPaymentDraft } from './other-payment.service';

export type OtherPaymentField = keyof OtherPaymentDraft;
export type OtherPaymentIssues = Partial<Record<OtherPaymentField, string>>;

/** Client preflight mirrors the backend's field limits; the server remains authoritative. */
export function otherPaymentIssues(draft: OtherPaymentDraft): OtherPaymentIssues {
  const issues: OtherPaymentIssues = {};
  const payee = draft.payeeDescription.trim();
  const reference = draft.bankReference.trim();
  if (!payee || payee.length > 200 || /[\x00-\x1f\x7f]/.test(payee)) issues.payeeDescription = 'payeeInvalid';
  if (!Number.isSafeInteger(draft.expenseCategoryId) || draft.expenseCategoryId <= 0) issues.expenseCategoryId = 'categoryRequired';
  if (!Number.isSafeInteger(draft.bankAccountId) || draft.bankAccountId <= 0) issues.bankAccountId = 'fundRequired';
  if (!reference || reference.length > 50 || /[\x00-\x1f\x7f]/.test(reference)) issues.bankReference = 'referenceInvalid';
  if (!validIsoDate(draft.businessDate)) issues.businessDate = 'dateInvalid';
  const amount = draft.amount.trim();
  if (!/^(?:0|[1-9]\d{0,14})(?:\.\d{1,4})?$/.test(amount)) issues.amount = 'amountInvalid';
  else if (!/[1-9]/.test(amount)) issues.amount = 'amountInvalid';
  if ((draft.remark ?? '').trim().length > 500) issues.remark = 'remarkInvalid';
  return issues;
}
