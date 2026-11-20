import { HttpErrorResponse } from '@angular/common/http';

/** Sales Outbound Create Interface 2.8 (REQ_F4112.3.2): quantities stay decimal text until the JSON write boundary. */
export interface DeliveryDraftLineForm {
  lineNo: number; orderLineId: number; warehouseId: number | null; locationId: number | null; quantity: string;
}
export interface DeliveryDraftForm { orderId: number | null; businessDate: string; lines: DeliveryDraftLineForm[]; }
export type DeliveryDraftIssue = 'required' | 'invalidDate' | 'count' | 'duplicate' | 'decimal' | 'aboveRemaining';
export type DeliveryDraftIssues = Record<string, DeliveryDraftIssue>;

/** Read-only order line facts shown next to the editable line (exact text from the order GET). */
export interface DeliverySourceLine {
  id: number; lineNo: number; itemCode: string; itemName: string; quantity: string; deliveredQuantity: string;
  pendingQuantity: string; remainingQuantity: string;
}
export interface DeliverySourceOrder {
  id: number; documentNo: string; status: string; customerCode: string; customerName: string; lines: DeliverySourceLine[];
}

export const MAX_DELIVERY_LINES = 1000;
const DECIMAL = /^(0|[1-9]\d*)(?:\.(\d+))?$/;

export function validDeliveryDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return false;
  const year = Number(match[1]), month = Number(match[2]), day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12) return false;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  return day >= 1 && day <= [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
}

/** Positive DECIMAL(19,6): at most 13 integer digits and 6 fractional digits. */
export function validDeliveryQuantity(value: string): boolean {
  const match = DECIMAL.exec(value);
  if (!match || match[1].length > 13 || (match[2]?.length ?? 0) > 6) return false;
  return match[1] !== '0' || !!match[2]?.replace(/0/g, '');
}

/** Exact comparison of two non-negative six-place decimal texts without converting to a binary number. */
export function compareDecimalText(left: string, right: string): number {
  const scaled = (value: string) => {
    const [whole, fraction = ''] = value.split('.');
    return BigInt(whole) * 1_000_000n + BigInt((fraction + '000000').slice(0, 6));
  };
  const a = scaled(left), b = scaled(right);
  return a < b ? -1 : a > b ? 1 : 0;
}

function sumDecimalText(values: string[]): string {
  const total = values.reduce((sum, value) => {
    const [whole, fraction = ''] = value.split('.');
    return sum + BigInt(whole) * 1_000_000n + BigInt((fraction + '000000').slice(0, 6));
  }, 0n);
  const fraction = (total % 1_000_000n).toString().padStart(6, '0').replace(/0+$/, '');
  return `${total / 1_000_000n}${fraction ? '.' + fraction : ''}`;
}

/**
 * Field checks before POST. `aboveRemaining` compares the total of all lines for one order line with the
 * advisory remaining quantity read with the order; the server rechecks the saved balance in its transaction.
 */
export function validateDeliveryDraft(draft: DeliveryDraftForm, source?: DeliverySourceOrder | null): DeliveryDraftIssues {
  const issues: DeliveryDraftIssues = {};
  if (!Number.isSafeInteger(draft.orderId) || (draft.orderId ?? 0) <= 0) issues['orderId'] = 'required';
  if (!validDeliveryDate(draft.businessDate)) issues['businessDate'] = 'invalidDate';
  if (draft.lines.length < 1 || draft.lines.length > MAX_DELIVERY_LINES) { issues['lines'] = 'count'; return issues; }
  const seen = new Set<number>();
  const byOrderLine = new Map<number, number[]>();
  draft.lines.forEach((line, index) => {
    const field = `lines[${index}]`;
    if (!Number.isSafeInteger(line.lineNo) || line.lineNo <= 0) issues[`${field}.lineNo`] = 'required';
    else if (seen.has(line.lineNo)) issues[`${field}.lineNo`] = 'duplicate';
    seen.add(line.lineNo);
    if (!Number.isSafeInteger(line.warehouseId) || (line.warehouseId ?? 0) <= 0) issues[`${field}.warehouseId`] = 'required';
    if (line.locationId != null && (!Number.isSafeInteger(line.locationId) || line.locationId <= 0))
      issues[`${field}.locationId`] = 'required';
    const quantity = line.quantity.trim();
    if (!validDeliveryQuantity(quantity)) issues[`${field}.quantity`] = 'decimal';
    else byOrderLine.set(line.orderLineId, [...(byOrderLine.get(line.orderLineId) ?? []), index]);
  });
  for (const [orderLineId, indexes] of byOrderLine) {
    const remaining = source?.lines.find(line => line.id === orderLineId)?.remainingQuantity;
    if (remaining == null || !DECIMAL.test(remaining)) continue;
    const total = sumDecimalText(indexes.map(i => draft.lines[i].quantity.trim()));
    if (compareDecimalText(total, remaining) > 0) for (const i of indexes) issues[`lines[${i}].quantity`] = 'aboveRemaining';
  }
  return issues;
}

/** One draft line per order line that still has a positive advisory balance, quantity prefilled with that balance. */
export function draftFromOrder(order: DeliverySourceOrder, businessDate: string): DeliveryDraftForm {
  const open = order.lines.filter(line => DECIMAL.test(line.remainingQuantity) && compareDecimalText(line.remainingQuantity, '0') > 0);
  return { orderId: order.id, businessDate, lines: open.map((line, index) => ({ lineNo: index + 1, orderLineId: line.id,
    warehouseId: null, locationId: null, quantity: line.remainingQuantity.replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '') })) };
}

/** Emit validated decimal text as JSON number tokens; client balances, status and posting fields are never sent. */
export function deliveryDraftJson(draft: DeliveryDraftForm): string {
  if (Object.keys(validateDeliveryDraft(draft)).length) throw new Error('Invalid delivery draft');
  const lines = draft.lines.map(line => `{${[`"lineNo":${line.lineNo}`, `"orderLineId":${line.orderLineId}`,
    `"warehouseId":${line.warehouseId}`, `"locationId":${line.locationId ?? 'null'}`,
    `"quantity":${line.quantity.trim()}`].join(',')}}`).join(',');
  return `{"orderId":${draft.orderId},"businessDate":${JSON.stringify(draft.businessDate)},"lines":[${lines}]}`;
}

/** The ProblemDetails `lineNo` extension of a 422 line error, or null when the error is not tied to one line. */
export function deliveryErrorLineNo(failure: unknown): number | null {
  if (!(failure instanceof HttpErrorResponse) || failure.status !== 422) return null;
  const value = (failure.error as { lineNo?: unknown } | null)?.lineNo;
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : null;
}

export interface DeliveryReceipt {
  id: number; documentNo: string; orderId: number; businessDate: string; status: string;
  lines: { lineNo: number; orderLineId: number; warehouseId: number; locationId: number | null; quantity: string }[];
}

/** Success is shown only when the saved Draft returned by the server matches what was sent, line by line. */
export function matchesDeliveryReceipt(receipt: DeliveryReceipt | null | undefined, draft: DeliveryDraftForm): boolean {
  if (!receipt || !Number.isSafeInteger(receipt.id) || receipt.id <= 0 || receipt.status !== 'Draft' ||
    !receipt.documentNo?.trim() || receipt.orderId !== draft.orderId || receipt.businessDate !== draft.businessDate ||
    !Array.isArray(receipt.lines) || receipt.lines.length !== draft.lines.length) return false;
  return draft.lines.every(line => {
    const saved = receipt.lines.find(candidate => candidate.lineNo === line.lineNo);
    return !!saved && saved.orderLineId === line.orderLineId && saved.warehouseId === line.warehouseId &&
      (saved.locationId ?? null) === line.locationId && typeof saved.quantity === 'string' &&
      DECIMAL.test(saved.quantity) && compareDecimalText(saved.quantity, line.quantity.trim()) === 0;
  });
}
