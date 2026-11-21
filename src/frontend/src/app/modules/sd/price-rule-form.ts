import { HttpErrorResponse } from '@angular/common/http';
import { SalesPriceRule } from './price-rule.service';

/** Editable price rule (interfaces 2.12/2.13, WU-SD-005b1b2). Prices stay exact decimal text, never a double. */
export interface PriceRuleDraft {
  itemId: number | null; scope: 'default' | 'customer'; customerId: number | null; currencyCode: string;
  validFrom: string; validTo: string; unitPrice: string; discountCeiling: string;
}
export type PriceRuleField = 'itemId' | 'customerId' | 'currencyCode' | 'validFrom' | 'validTo' | 'unitPrice' | 'discountCeiling';
export const PRICE_RULE_FIELDS: readonly PriceRuleField[] =
  ['itemId', 'customerId', 'currencyCode', 'validFrom', 'validTo', 'unitPrice', 'discountCeiling'];
/** Issue code per field; the page shows `sd.priceRuleForm.validation.<field>.<code>`. */
export type PriceRuleIssues = Partial<Record<PriceRuleField, 'required' | 'format' | 'range' | 'scale' | 'beforeFrom'>>;

export function emptyPriceRuleDraft(): PriceRuleDraft {
  return { itemId: null, scope: 'default', customerId: null, currencyCode: '', validFrom: '', validTo: '',
    unitPrice: '', discountCeiling: '0' };
}

export function priceRuleDraftFromRule(rule: SalesPriceRule): PriceRuleDraft {
  return { itemId: rule.itemId, scope: rule.customerId === null ? 'default' : 'customer', customerId: rule.customerId,
    currencyCode: rule.currencyCode, validFrom: rule.validFrom, validTo: rule.validTo,
    unitPrice: String(rule.unitPrice), discountCeiling: String(rule.discountCeiling) };
}

export function validCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
  const [year, month, day] = value.split('-').map(Number);
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 && day <= new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** Plain digits; integer part without leading zeros (a JSON number may not start with 0). */
function decimalParts(text: string): { integer: string; fraction: string } | null {
  const match = /^(\d+)(?:\.(\d+))?$/.exec(text.trim());
  return match ? { integer: match[1].replace(/^0+(?=\d)/, ''), fraction: match[2] ?? '' } : null;
}

function priceIssue(text: string): PriceRuleIssues['unitPrice'] {
  if (!text.trim()) return 'required';
  const parts = decimalParts(text);
  if (!parts) return 'format';
  if (parts.integer.length > 13) return 'range';
  return parts.fraction.length > 6 ? 'scale' : undefined;
}

function ceilingIssue(text: string): PriceRuleIssues['discountCeiling'] {
  if (!text.trim()) return 'required';
  const parts = decimalParts(text);
  if (!parts) return 'format';
  const over = parts.integer.length > 3 || Number(parts.integer) > 100 ||
    (Number(parts.integer) === 100 && /[1-9]/.test(parts.fraction));
  if (over) return 'range';
  return parts.fraction.length > 4 ? 'scale' : undefined;
}

/** Same checks and order as `SalesPricingRules.ValidateRule`, all fields reported at once. */
export function validatePriceRuleDraft(draft: PriceRuleDraft): PriceRuleIssues {
  const issues: PriceRuleIssues = {};
  if (draft.itemId === null || draft.itemId <= 0) issues.itemId = 'required';
  if (draft.scope === 'customer' && (draft.customerId === null || draft.customerId <= 0)) issues.customerId = 'required';
  if (!/^[A-Z]{3}$/.test(draft.currencyCode)) issues.currencyCode = 'required';
  if (!draft.validFrom) issues.validFrom = 'required';
  else if (!validCalendarDate(draft.validFrom)) issues.validFrom = 'format';
  if (!draft.validTo) issues.validTo = 'required';
  else if (!validCalendarDate(draft.validTo)) issues.validTo = 'format';
  else if (validCalendarDate(draft.validFrom) && draft.validTo < draft.validFrom) issues.validTo = 'beforeFrom';
  const price = priceIssue(draft.unitPrice);
  if (price) issues.unitPrice = price;
  const ceiling = ceilingIssue(draft.discountCeiling);
  if (ceiling) issues.discountCeiling = ceiling;
  return issues;
}

function decimalToken(text: string): string {
  const parts = decimalParts(text)!;
  return parts.fraction ? `${parts.integer}.${parts.fraction}` : parts.integer;
}

/** POST body, or PUT body when a version is given; decimal tokens come from validated text. */
export function priceRuleJson(draft: PriceRuleDraft, version?: number): string {
  if (Object.keys(validatePriceRuleDraft(draft)).length) throw new Error('Invalid price rule draft');
  if (version !== undefined && (!Number.isSafeInteger(version) || version <= 0)) throw new Error('Invalid price rule version');
  const fields = [`"itemId":${draft.itemId}`, `"customerId":${draft.scope === 'customer' ? draft.customerId : null}`,
    `"currencyCode":${JSON.stringify(draft.currencyCode)}`, `"validFrom":${JSON.stringify(draft.validFrom)}`,
    `"validTo":${JSON.stringify(draft.validTo)}`, `"unitPrice":${decimalToken(draft.unitPrice)}`,
    `"discountCeiling":${decimalToken(draft.discountCeiling)}`];
  if (version !== undefined) fields.push(`"version":${version}`);
  return `{${fields.join(',')}}`;
}

export type PriceRuleSaveFailure =
  | { kind: 'overlap'; conflictingRuleIds: number[] }
  | { kind: 'busy' | 'stale' | 'inactive' | 'missing' | 'denied' | 'rejected' | 'unknown' }
  | { kind: 'reference'; field: PriceRuleField; key: string }
  | { kind: 'validation'; fields: PriceRuleField[]; unknown: number };

const REFERENCE_CODES: Record<string, { field: PriceRuleField; key: string }> = {
  'MD.ITEM.NOT_FOUND': { field: 'itemId', key: 'itemMissing' },
  'MD.ITEM.INACTIVE': { field: 'itemId', key: 'itemInactive' },
  'MD.CUSTOMER.NOT_FOUND': { field: 'customerId', key: 'customerMissing' },
  'MD.CUSTOMER.NOT_APPROVED': { field: 'customerId', key: 'customerNotApproved' },
  'MD.CURRENCY.NOT_FOUND': { field: 'currencyCode', key: 'currencyMissing' },
  'MD.CURRENCY.INACTIVE': { field: 'currencyCode', key: 'currencyInactive' },
};

/**
 * Classifies a failed POST/PUT. `unknown` covers network, timeout and 5xx — the write may have happened, so the
 * page must not offer a blind resubmit. Field names of a 400 are kept so each field shows its own reason.
 */
export function priceRuleSaveFailure(error: unknown): PriceRuleSaveFailure {
  if (!(error instanceof HttpErrorResponse)) return { kind: 'unknown' };
  const body = (error.error ?? {}) as { errorCode?: unknown; conflictingRuleIds?: unknown; errors?: unknown };
  const code = typeof body.errorCode === 'string' ? body.errorCode : '';
  if (error.status === 409 && code === 'SAL.PRICING.RULE_OVERLAP') {
    const ids = Array.isArray(body.conflictingRuleIds)
      ? body.conflictingRuleIds.filter((id): id is number => Number.isSafeInteger(id) && id > 0).slice(0, 20) : [];
    return { kind: 'overlap', conflictingRuleIds: ids };
  }
  if (error.status === 409 && code === 'SAL.PRICING.RULE_LOCK_TIMEOUT') return { kind: 'busy' };
  if (error.status === 409) return { kind: 'stale' };
  if (error.status === 422 && code === 'SAL.PRICING.RULE_INACTIVE') return { kind: 'inactive' };
  const reference = REFERENCE_CODES[code];
  if ((error.status === 404 || error.status === 422) && reference)
    return { kind: 'reference', field: reference.field, key: `sd.priceRuleForm.refused.${reference.key}` };
  if (error.status === 404) return { kind: 'missing' };
  if (error.status === 403) return { kind: 'denied' };
  if (error.status === 400) {
    const errors = body.errors && typeof body.errors === 'object' && !Array.isArray(body.errors) ? Object.keys(body.errors) : [];
    const fields = errors.filter((name): name is PriceRuleField => (PRICE_RULE_FIELDS as readonly string[]).includes(name));
    return { kind: 'validation', fields, unknown: errors.length - fields.length };
  }
  // Any other 4xx is a definite refusal (nothing saved); 5xx/0 may have written.
  return error.status >= 400 && error.status < 500 ? { kind: 'rejected' } : { kind: 'unknown' };
}
