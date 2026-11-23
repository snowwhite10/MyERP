import { HttpErrorResponse } from '@angular/common/http';
import { compareDecimalText, sumDecimalText, validDeliveryDate } from './delivery-draft-form';
import { SalesReturnDetail, SalesReturnDraftRequest, SalesReturnSourceLine, SalesReturnSourceOrder } from './return.service';

export const MAX_RETURN_LINES = 1000;
export const RETURN_REMARK_MAX = 500;
/** Zero or positive DECIMAL(19,6): at most 13 integer and 6 fractional digits (blank counts as zero). */
const QUANTITY = /^(0|[1-9]\d{0,12})(?:\.\d{1,6})?$/;

/** Billed portion credited against one Posted invoice line; blank or zero is not sent. */
export interface ReturnBilledForm { invoiceLineId: number; quantity: string; }
/**
 * One returnable delivery line on the form. The returned quantity is `unbilled + Σ billed`; the destination defaults to
 * the delivery's warehouse/location (WU-SD-008b3a). Tracked (lot/serial) lines are shown but cannot be selected.
 */
export interface ReturnLineForm {
  deliveryLineId: number; selected: boolean; warehouseId: number; locationId: number | null; unbilledQuantity: string;
  billed: ReturnBilledForm[];
}
export interface ReturnDraftForm { orderId: number | null; businessDate: string; remark: string; lines: ReturnLineForm[]; }
export type ReturnProblem = 'required' | 'invalidDate' | 'beforeDelivery' | 'beforeInvoice' | 'decimal' | 'aboveUnbilled'
  | 'aboveCredit' | 'aboveReturnable' | 'zero' | 'noLines' | 'tooMany' | 'remarkLength' | 'tracking';
/**
 * Keys: `orderId`, `businessDate`, `remark`, `lines`, `unbilled-<deliveryLineId>`,
 * `billed-<deliveryLineId>-<invoiceLineId>` and `line-<deliveryLineId>` (the line total).
 */
export type ReturnProblems = Record<string, ReturnProblem>;

export const unbilledKey = (deliveryLineId: number) => `unbilled-${deliveryLineId}`;
export const billedKey = (deliveryLineId: number, invoiceLineId: number) => `billed-${deliveryLineId}-${invoiceLineId}`;
export const totalKey = (deliveryLineId: number) => `line-${deliveryLineId}`;

/** Blank → `0`; otherwise the trimmed text (validity is checked separately). */
export const quantityText = (value: string) => value.trim() || '0';
const validQuantity = (value: string) => QUANTITY.test(quantityText(value));
const positive = (value: string) => validQuantity(value) && compareDecimalText(quantityText(value), '0') > 0;

/** Nothing selected and every quantity blank: returns are usually partial, so the user enters what came back. */
export function draftFromSource(source: SalesReturnSourceOrder, businessDate: string, remark = ''): ReturnDraftForm {
  return { orderId: source.id, businessDate, remark, lines: source.lines.map(line => ({
    deliveryLineId: line.deliveryLineId, selected: false, warehouseId: line.warehouseId, locationId: line.locationId,
    unbilledQuantity: '', billed: line.invoiceLines.map(invoice => ({ invoiceLineId: invoice.invoiceLineId, quantity: '' })) })) };
}

/**
 * After re-reading the balances: lines and invoice lines still offered keep the user's entries; new ones are added
 * blank and unselected; ones no longer offered disappear.
 */
export function mergeDraft(draft: ReturnDraftForm, source: SalesReturnSourceOrder): ReturnDraftForm {
  const kept = new Map(draft.lines.map(line => [line.deliveryLineId, line]));
  const fresh = draftFromSource(source, draft.businessDate, draft.remark);
  return { ...draft, lines: fresh.lines.map(line => {
    const old = kept.get(line.deliveryLineId);
    if (!old) return line;
    const billed = new Map(old.billed.map(portion => [portion.invoiceLineId, portion.quantity]));
    return { ...old, selected: old.selected && !sourceLine(source, line.deliveryLineId)?.trackingUnsupported,
      billed: line.billed.map(portion => ({ ...portion, quantity: billed.get(portion.invoiceLineId) ?? '' })) };
  }) };
}

export function sourceLine(source: SalesReturnSourceOrder | null, deliveryLineId: number): SalesReturnSourceLine | undefined {
  return source?.lines.find(line => line.deliveryLineId === deliveryLineId);
}

/** Exact `unbilled + Σ billed` of a line; null while any entry is not a valid quantity. */
export function lineTotal(line: ReturnLineForm): string | null {
  const values = [line.unbilledQuantity, ...line.billed.map(portion => portion.quantity)];
  return values.every(validQuantity) ? sumDecimalText(values.map(quantityText)) : null;
}

/**
 * The earliest return date the selected lines allow: not before any selected delivery, nor any invoice that is credited
 * (BR-SD-044; the server rechecks both).
 */
export function earliestReturnDate(draft: ReturnDraftForm, source: SalesReturnSourceOrder | null): string | null {
  const dates: string[] = [];
  for (const line of draft.lines.filter(candidate => candidate.selected)) {
    const facts = sourceLine(source, line.deliveryLineId);
    if (!facts) continue;
    dates.push(facts.deliveryDate);
    for (const portion of line.billed.filter(candidate => positive(candidate.quantity))) {
      const invoice = facts.invoiceLines.find(candidate => candidate.invoiceLineId === portion.invoiceLineId);
      if (invoice) dates.push(invoice.invoiceDate);
    }
  }
  return dates.sort().at(-1) ?? null;
}

/** Checks before POST; balances are advisory and the server rechecks them under lock. */
export function validateReturnDraft(draft: ReturnDraftForm, source: SalesReturnSourceOrder | null): ReturnProblems {
  const problems: ReturnProblems = {};
  if (!Number.isSafeInteger(draft.orderId) || (draft.orderId ?? 0) <= 0 || !source) { problems['orderId'] = 'required'; return problems; }
  // Insertion order follows the page (date, remark, then lines) so the first problem gets focus.
  if (!validDeliveryDate(draft.businessDate)) problems['businessDate'] = 'invalidDate';
  if (draft.remark.trim().length > RETURN_REMARK_MAX) problems['remark'] = 'remarkLength';
  const selected = draft.lines.filter(line => line.selected);
  if (!selected.length) problems['lines'] = 'noLines';
  else if (selected.length > MAX_RETURN_LINES) problems['lines'] = 'tooMany';
  let beforeInvoice = false, beforeDelivery = false;
  for (const line of selected) {
    const facts = sourceLine(source, line.deliveryLineId);
    if (facts?.trackingUnsupported) { problems[totalKey(line.deliveryLineId)] = 'tracking'; continue; }
    if (!validQuantity(line.unbilledQuantity)) problems[unbilledKey(line.deliveryLineId)] = 'decimal';
    else if (facts && compareDecimalText(quantityText(line.unbilledQuantity), facts.unbilledRemainingQuantity) > 0)
      problems[unbilledKey(line.deliveryLineId)] = 'aboveUnbilled';
    for (const portion of line.billed) {
      const invoice = facts?.invoiceLines.find(candidate => candidate.invoiceLineId === portion.invoiceLineId);
      if (!validQuantity(portion.quantity)) problems[billedKey(line.deliveryLineId, portion.invoiceLineId)] = 'decimal';
      else if (invoice && compareDecimalText(quantityText(portion.quantity), invoice.remainingQuantity) > 0)
        problems[billedKey(line.deliveryLineId, portion.invoiceLineId)] = 'aboveCredit';
      else if (invoice && positive(portion.quantity) && validDeliveryDate(draft.businessDate) && draft.businessDate < invoice.invoiceDate)
        beforeInvoice = true;
    }
    const total = lineTotal(line);
    if (total != null && compareDecimalText(total, '0') === 0) problems[totalKey(line.deliveryLineId)] = 'zero';
    else if (total != null && facts && compareDecimalText(total, facts.returnableQuantity) > 0)
      problems[totalKey(line.deliveryLineId)] = 'aboveReturnable';
    if (facts && validDeliveryDate(draft.businessDate) && draft.businessDate < facts.deliveryDate) beforeDelivery = true;
  }
  if (!problems['businessDate'] && (beforeDelivery || beforeInvoice))
    return { businessDate: beforeDelivery ? 'beforeDelivery' : 'beforeInvoice', ...problems };
  return problems;
}

/** Selected lines numbered 1..n in display order; blank → 0, zero billed portions are not sent. */
export function returnRequest(draft: ReturnDraftForm, requestKey: string): SalesReturnDraftRequest {
  const remark = draft.remark.trim();
  return { requestKey, businessDate: draft.businessDate, remark: remark || null,
    lines: draft.lines.filter(line => line.selected).map((line, index) => ({
      lineNo: index + 1, deliveryLineId: line.deliveryLineId, warehouseId: line.warehouseId, locationId: line.locationId,
      unbilledQuantity: quantityText(line.unbilledQuantity),
      billed: line.billed.filter(portion => positive(portion.quantity))
        .map(portion => ({ invoiceLineId: portion.invoiceLineId, quantity: quantityText(portion.quantity) })) })) };
}

const same = (left: unknown, right: string) =>
  /^\d+(\.\d+)?$/.test(String(left)) && compareDecimalText(String(left), right) === 0;

/** The saved return must be the one requested: same key, date and, per source line, the same unbilled and billed split. */
export function matchesReturnReceipt(saved: SalesReturnDetail | null | undefined, request: SalesReturnDraftRequest): boolean {
  if (!saved || !Number.isSafeInteger(saved.id) || saved.id <= 0 || saved.requestKey !== request.requestKey ||
      saved.businessDate !== request.businessDate || !Array.isArray(saved.lines) || saved.lines.length !== request.lines.length)
    return false;
  return request.lines.every(line => saved.lines.some(candidate => candidate.deliveryLineId === line.deliveryLineId &&
    same(candidate.unbilledQuantity, line.unbilledQuantity) && Array.isArray(candidate.allocations) &&
    candidate.allocations.length === line.billed.length &&
    line.billed.every(portion => candidate.allocations.some(allocation =>
      allocation.invoiceLineId === portion.invoiceLineId && same(allocation.quantity, portion.quantity)))));
}

/** 422 `lineNo`, `invoiceLineId` and `remaining` as sent by CREATE; invalid values are ignored. */
export function returnRefusal(failure: unknown): { lineNo: number | null; invoiceLineId: number | null; remaining: string | null } {
  if (!(failure instanceof HttpErrorResponse) || failure.status !== 422) return { lineNo: null, invoiceLineId: null, remaining: null };
  const body = failure.error as { lineNo?: unknown; invoiceLineId?: unknown; remaining?: unknown } | null;
  const positiveId = (value: unknown) => typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
  const remaining = typeof body?.remaining === 'number' && Number.isFinite(body.remaining) && body.remaining >= 0
    ? String(body.remaining) : typeof body?.remaining === 'string' && /^\d+(\.\d+)?$/.test(body.remaining) ? body.remaining : null;
  return { lineNo: positiveId(body?.lineNo), invoiceLineId: positiveId(body?.invoiceLineId), remaining };
}
