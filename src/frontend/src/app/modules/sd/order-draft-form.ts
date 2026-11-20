/** Draft decimals remain text until the JSON write boundary. */
export interface OrderDraftLineForm {
  lineNo: number; itemId: number | null; quantity: string; unitPrice: string;
  discountRate: string; taxRateId: number | null;
}
export interface OrderDraftForm {
  customerId: number | null; businessDate: string; currencyCode: string;
  remark: string; lines: OrderDraftLineForm[];
}
export type OrderDraftIssue = 'required' | 'invalidDate' | 'currency' | 'tooLong' | 'count' | 'duplicate' | 'decimal';
export type OrderDraftIssues = Record<string, OrderDraftIssue>;

/** Saved GET input for a manual Draft or Rejected order; decimals must already be text. */
export interface OrderEditableSnapshot {
  id: number; version: number; status: string; sourceQuotationId: number | null;
  customerId: number; businessDate: string; currencyCode: string; remark: string | null;
  lines: { lineNo: number; itemId: number; quantity: string; unitPrice: string;
    discountRate: string; taxRateId: number; deliveredQuantity: number; pendingQuantity: number }[];
}

export function orderEditDraft(saved: OrderEditableSnapshot): { version: number; draft: OrderDraftForm } | null {
  if (!Number.isSafeInteger(saved.id) || saved.id <= 0 ||
    !Number.isSafeInteger(saved.version) || saved.version <= 0 ||
    (saved.status !== 'Draft' && saved.status !== 'Rejected') ||
    saved.sourceQuotationId != null || !Array.isArray(saved.lines) ||
    saved.lines.some(line => line.deliveredQuantity !== 0 || line.pendingQuantity !== 0)) return null;
  const draft: OrderDraftForm = { customerId: saved.customerId, businessDate: saved.businessDate,
    currencyCode: saved.currencyCode, remark: saved.remark ?? '', lines: saved.lines.map(line => ({
      lineNo: line.lineNo, itemId: line.itemId, quantity: line.quantity,
      unitPrice: line.unitPrice, discountRate: line.discountRate, taxRateId: line.taxRateId,
    })) };
  return Object.keys(validateOrderDraft(draft)).length ? null : { version: saved.version, draft };
}

export interface OrderReceiptIdentity {
  id: number; status: string; documentNo: string; customerId?: number;
  businessDate?: string; currencyCode?: string; sourceQuotationId?: number | null;
  lines: { lineNo?: number; itemId?: number }[];
}

/** A successful write must be confirmed by the saved document for the selected source. */
export function matchesOrderReceipt(
  receipt: OrderReceiptIdentity | null | undefined,
  source: { draft: OrderDraftForm } | { quotationId: number },
): boolean {
  if (!receipt || !Number.isSafeInteger(receipt.id) || receipt.id <= 0 ||
    receipt.status !== 'Draft' || !receipt.documentNo?.trim() ||
    !Array.isArray(receipt.lines) || receipt.lines.length === 0) return false;
  if ('quotationId' in source) return receipt.sourceQuotationId === source.quotationId;
  const draft = source.draft;
  return receipt.sourceQuotationId == null && receipt.customerId === draft.customerId &&
    receipt.businessDate === draft.businessDate && receipt.currencyCode === draft.currencyCode &&
    receipt.lines.length === draft.lines.length && draft.lines.every((line, index) =>
      receipt.lines[index].lineNo === line.lineNo && receipt.lines[index].itemId === line.itemId);
}

function validDate(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return false;
  const year = Number(match[1]), month = Number(match[2]), day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12) return false;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  return day >= 1 && day <= [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
}
function validDecimal(value: string, scale: number, positive: boolean): boolean {
  const match = /^(0|[1-9]\d*)(?:\.(\d+))?$/.exec(value);
  if (!match || (match[2]?.length ?? 0) > scale) return false;
  const whole = match[1];
  if (whole.length > 13) return false;
  return !positive || whole !== '0' || !!match[2]?.replace(/0/g, '');
}
function validDiscount(value: string): boolean {
  const match = /^(0|[1-9]\d*)(?:\.(\d+))?$/.exec(value);
  if (!match || (match[2]?.length ?? 0) > 4) return false;
  return match[1].length < 3 || (match[1] === '100' && !match[2]?.replace(/0/g, ''));
}
export function validateOrderDraft(draft: OrderDraftForm): OrderDraftIssues {
  const issues: OrderDraftIssues = {};
  if (!Number.isSafeInteger(draft.customerId) || (draft.customerId ?? 0) <= 0) issues['customerId'] = 'required';
  if (!validDate(draft.businessDate)) issues['businessDate'] = 'invalidDate';
  if (!/^[A-Z]{3}$/.test(draft.currencyCode)) issues['currencyCode'] = 'currency';
  if (draft.remark.trim().length > 500) issues['remark'] = 'tooLong';
  if (draft.lines.length < 1 || draft.lines.length > 1000) { issues['lines'] = 'count'; return issues; }
  const seen = new Set<number>();
  draft.lines.forEach((line, index) => {
    const field = `lines[${index}]`;
    if (!Number.isSafeInteger(line.lineNo) || line.lineNo <= 0) issues[`${field}.lineNo`] = 'required';
    else if (seen.has(line.lineNo)) issues[`${field}.lineNo`] = 'duplicate';
    seen.add(line.lineNo);
    if (!Number.isSafeInteger(line.itemId) || (line.itemId ?? 0) <= 0) issues[`${field}.itemId`] = 'required';
    if (!validDecimal(line.quantity, 6, true)) issues[`${field}.quantity`] = 'decimal';
    if (!validDecimal(line.unitPrice, 6, false)) issues[`${field}.unitPrice`] = 'decimal';
    if (!validDiscount(line.discountRate)) issues[`${field}.discountRate`] = 'decimal';
    if (!Number.isSafeInteger(line.taxRateId) || (line.taxRateId ?? 0) <= 0) issues[`${field}.taxRateId`] = 'required';
  });
  return issues;
}

/** Emit validated decimal text as JSON number tokens without a JavaScript Number conversion. */
export function orderDraftJson(draft: OrderDraftForm): string {
  if (Object.keys(validateOrderDraft(draft)).length) throw new Error('Invalid order draft');
  const lines = draft.lines.map(line => `{${[
    `"lineNo":${line.lineNo}`, `"itemId":${line.itemId}`, `"quantity":${line.quantity}`,
    `"unitPrice":${line.unitPrice}`, `"discountRate":${line.discountRate}`,
    `"taxRateId":${line.taxRateId}`,
  ].join(',')}}`).join(',');
  return `{${[
    `"customerId":${draft.customerId}`, `"businessDate":${JSON.stringify(draft.businessDate)}`,
    `"currencyCode":${JSON.stringify(draft.currencyCode)}`,
    `"remark":${JSON.stringify(draft.remark.trim())}`, `"lines":[${lines}]`,
  ].join(',')}}`;
}
