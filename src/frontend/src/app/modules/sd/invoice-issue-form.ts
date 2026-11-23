import { HttpErrorResponse } from '@angular/common/http';
import { compareDecimalText, validDeliveryDate, validDeliveryQuantity } from './delivery-draft-form';
import { SalesInvoiceDetail, SalesInvoiceIssueRequest, SalesInvoiceSourceLine, SalesInvoiceSourceOrder } from './invoice.service';

export const MAX_INVOICE_LINES = 1000;

/** One open source line on the form; unselected lines are not sent. */
export interface InvoiceIssueLineForm { deliveryLineId: number; selected: boolean; quantity: string; }
export interface InvoiceIssueDraft { orderId: number | null; businessDate: string; lines: InvoiceIssueLineForm[]; }
export type InvoiceIssueProblem = 'required' | 'invalidDate' | 'beforeDelivery' | 'decimal' | 'aboveRemaining'
  | 'noLines' | 'tooMany';
/** Keys: `orderId`, `businessDate`, `lines` and `line-<deliveryLineId>` (that line's quantity). */
export type InvoiceIssueProblems = Record<string, InvoiceIssueProblem>;

export const lineKey = (deliveryLineId: number) => `line-${deliveryLineId}`;

/** Every open line selected with its whole remaining quantity (the usual case: bill what was shipped). */
export function draftFromSource(source: SalesInvoiceSourceOrder, businessDate: string): InvoiceIssueDraft {
  return { orderId: source.id, businessDate,
    lines: source.lines.map(line => ({ deliveryLineId: line.deliveryLineId, selected: true, quantity: line.remainingQuantity })) };
}

/**
 * After re-reading the balance: keep the user's selection and quantities for lines still open; lines that are no longer
 * open disappear and new open lines are added unselected.
 */
export function mergeDraft(draft: InvoiceIssueDraft, source: SalesInvoiceSourceOrder): InvoiceIssueDraft {
  const kept = new Map(draft.lines.map(line => [line.deliveryLineId, line]));
  return { ...draft, lines: source.lines.map(line => kept.get(line.deliveryLineId)
    ?? { deliveryLineId: line.deliveryLineId, selected: false, quantity: line.remainingQuantity }) };
}

/** The latest delivery date of the selected lines: the invoice date may not be earlier (BR-SD-055). */
export function latestDeliveryDate(draft: InvoiceIssueDraft, source: SalesInvoiceSourceOrder | null): string | null {
  const dates = draft.lines.filter(line => line.selected)
    .map(line => source?.lines.find(candidate => candidate.deliveryLineId === line.deliveryLineId)?.deliveryDate)
    .filter((date): date is string => !!date).sort();
  return dates.length ? dates[dates.length - 1] : null;
}

/** Checks before POST; the server rechecks balance and dates under its locks. */
export function validateInvoiceDraft(draft: InvoiceIssueDraft, source: SalesInvoiceSourceOrder | null): InvoiceIssueProblems {
  const problems: InvoiceIssueProblems = {};
  if (!Number.isSafeInteger(draft.orderId) || (draft.orderId ?? 0) <= 0 || !source) { problems['orderId'] = 'required'; return problems; }
  // Insertion order follows the page (date, then lines) so the first problem gets focus.
  if (!validDeliveryDate(draft.businessDate)) problems['businessDate'] = 'invalidDate';
  else {
    const latest = latestDeliveryDate(draft, source);
    if (latest && draft.businessDate < latest) problems['businessDate'] = 'beforeDelivery';
  }
  const selected = draft.lines.filter(line => line.selected);
  if (!selected.length) problems['lines'] = 'noLines';
  else if (selected.length > MAX_INVOICE_LINES) problems['lines'] = 'tooMany';
  for (const line of selected) {
    const quantity = line.quantity.trim();
    const remaining = sourceLine(source, line.deliveryLineId)?.remainingQuantity;
    if (!validDeliveryQuantity(quantity)) problems[lineKey(line.deliveryLineId)] = 'decimal';
    else if (remaining != null && /^\d+(\.\d+)?$/.test(remaining) && compareDecimalText(quantity, remaining) > 0)
      problems[lineKey(line.deliveryLineId)] = 'aboveRemaining';
  }
  return problems;
}

export function sourceLine(source: SalesInvoiceSourceOrder | null, deliveryLineId: number): SalesInvoiceSourceLine | undefined {
  return source?.lines.find(line => line.deliveryLineId === deliveryLineId);
}

/** Selected lines numbered 1..n in display order; trimmed quantity text, sent exactly. */
export function issueRequest(draft: InvoiceIssueDraft, requestKey: string): SalesInvoiceIssueRequest {
  return { requestKey, businessDate: draft.businessDate,
    lines: draft.lines.filter(line => line.selected)
      .map((line, index) => ({ lineNo: index + 1, deliveryLineId: line.deliveryLineId, quantity: line.quantity.trim() })) };
}

/** The saved invoice must be the one requested: same key, date, source lines and quantities. */
export function matchesIssueReceipt(saved: SalesInvoiceDetail | null | undefined, request: SalesInvoiceIssueRequest): boolean {
  if (!saved || !Number.isSafeInteger(saved.id) || saved.id <= 0 || saved.requestKey !== request.requestKey ||
      saved.businessDate !== request.businessDate || !Array.isArray(saved.lines) || saved.lines.length !== request.lines.length)
    return false;
  return request.lines.every(line => saved.lines.some(candidate => candidate.deliveryLineId === line.deliveryLineId &&
    /^\d+(\.\d+)?$/.test(String(candidate.quantity)) && compareDecimalText(String(candidate.quantity), line.quantity) === 0));
}

/** 422 `lineNo` (and `remaining` for BALANCE_EXCEEDED) as sent by ISSUE; invalid values are ignored. */
export function invoiceRefusal(failure: unknown): { lineNo: number | null; remaining: string | null } {
  if (!(failure instanceof HttpErrorResponse) || failure.status !== 422) return { lineNo: null, remaining: null };
  const body = failure.error as { lineNo?: unknown; remaining?: unknown } | null;
  const lineNo = typeof body?.lineNo === 'number' && Number.isSafeInteger(body.lineNo) && body.lineNo > 0 ? body.lineNo : null;
  const remaining = typeof body?.remaining === 'number' && Number.isFinite(body.remaining) && body.remaining >= 0
    ? String(body.remaining) : typeof body?.remaining === 'string' && /^\d+(\.\d+)?$/.test(body.remaining) ? body.remaining : null;
  return { lineNo, remaining };
}
