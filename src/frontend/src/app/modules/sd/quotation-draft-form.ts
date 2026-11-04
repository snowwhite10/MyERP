/** Raw form values stay as decimal text until the write boundary; JS Number loses six-place precision. */
export interface QuotationDraftLineForm {
  lineNo: number;
  itemId: number | null;
  quantity: string;
  unitPrice: string;
  discountRate: string;
  currencyCode: string;
  taxRateId: number | null;
}

export interface QuotationDraftForm {
  customerId: number | null;
  businessDate: string;
  expiryDate: string;
  remark: string;
  lines: QuotationDraftLineForm[];
}

export type QuotationDraftIssue = 'required' | 'invalidDate' | 'tooLong' | 'count' |
  'duplicate' | 'decimal' | 'currency';
export type QuotationDraftIssues = Record<string, QuotationDraftIssue>;

const DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const DECIMAL = /^(0|[1-9]\d*)(?:\.(\d+))?$/;

function validDate(value: string): boolean {
  const match = DATE.exec(value);
  if (!match) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12) return false;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return day >= 1 && day <= days[month - 1];
}

function validDecimal(value: string, scale: number, upperExclusive: string, zeroAllowed: boolean): boolean {
  const match = DECIMAL.exec(value);
  if (!match || (match[2]?.length ?? 0) > scale) return false;
  const whole = match[1];
  if (whole.length > upperExclusive.length ||
      (whole.length === upperExclusive.length && whole >= upperExclusive)) return false;
  return zeroAllowed || whole !== '0' || !!match[2]?.replace(/0/g, '');
}

function validDiscount(value: string): boolean {
  const match = DECIMAL.exec(value);
  if (!match || (match[2]?.length ?? 0) > 4) return false;
  const whole = match[1];
  return whole.length < 3 || (whole === '100' && !match[2]?.replace(/0/g, ''));
}

/** Mirrors SalesQuotationInput.Validate at the form seam; the server remains authoritative. */
export function validateQuotationDraft(draft: QuotationDraftForm): QuotationDraftIssues {
  const issues: QuotationDraftIssues = {};
  if (!Number.isSafeInteger(draft.customerId) || (draft.customerId ?? 0) <= 0) issues['customerId'] = 'required';
  const businessValid = validDate(draft.businessDate);
  if (!businessValid) issues['businessDate'] = 'invalidDate';
  if (!validDate(draft.expiryDate) || (businessValid && draft.expiryDate < draft.businessDate))
    issues['expiryDate'] = 'invalidDate';
  if (draft.remark.trim().length > 500) issues['remark'] = 'tooLong';
  if (draft.lines.length < 1 || draft.lines.length > 1000) {
    issues['lines'] = 'count';
    return issues;
  }
  const seen = new Set<number>();
  draft.lines.forEach((line, index) => {
    const field = `lines[${index}]`;
    if (!Number.isSafeInteger(line.lineNo) || line.lineNo <= 0) issues[`${field}.lineNo`] = 'required';
    else if (seen.has(line.lineNo)) issues[`${field}.lineNo`] = 'duplicate';
    seen.add(line.lineNo);
    if (!Number.isSafeInteger(line.itemId) || (line.itemId ?? 0) <= 0) issues[`${field}.itemId`] = 'required';
    if (!validDecimal(line.quantity, 6, '10000000000000', false)) issues[`${field}.quantity`] = 'decimal';
    if (!validDecimal(line.unitPrice, 6, '10000000000000', true)) issues[`${field}.unitPrice`] = 'decimal';
    if (!validDiscount(line.discountRate)) issues[`${field}.discountRate`] = 'decimal';
    if (!/^[A-Z]{3}$/.test(line.currencyCode)) issues[`${field}.currencyCode`] = 'currency';
    if (!Number.isSafeInteger(line.taxRateId) || (line.taxRateId ?? 0) <= 0) issues[`${field}.taxRateId`] = 'required';
  });
  return issues;
}
