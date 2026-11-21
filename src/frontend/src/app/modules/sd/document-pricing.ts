import { HttpErrorResponse } from '@angular/common/http';
import { apiErrorKey } from '../../core/http/api-error';

/**
 * Pricing state of one quotation/order form line (WU-SD-005b2a, ADR-164). The server re-decides every line in its
 * save transaction (ADR-160); this state only explains the price to the user and supplies the quote hint.
 * - `rule`: a dated quote (or a saved Rule/Override line with rule data) — price and ceiling are known.
 * - `notFound`: no rule applies — saving needs SAL.PRICING.OVERRIDE and a reason.
 * - `legacy`: saved before pricing rules; a current rule may still match, so the reason is optional and the server decides.
 * - `duplicate`: several active rules apply — never overridable, fix the rules.
 * - `stale`: the server reported the shown quote is out of date — request a new quote.
 */
export type LinePricing =
  | { kind: 'none' }
  | { kind: 'loading' }
  | { kind: 'rule'; ruleId: number; ruleVersion: number; unitPrice: string; discountCeiling: string;
      validTo: string | null; fromSaved: boolean }
  | { kind: 'notFound' }
  | { kind: 'legacy' }
  | { kind: 'duplicate'; ruleIds: number[] }
  | { kind: 'stale' }
  | { kind: 'failed'; messageKey: string };

const DECIMAL = /^(\d+)(?:\.(\d+))?$/;

/** Exact comparison of two nonnegative decimal texts (no Number rounding); NaN when either is not a decimal. */
export function compareDecimalText(left: string, right: string): number {
  const a = DECIMAL.exec(left.trim());
  const b = DECIMAL.exec(right.trim());
  if (!a || !b) return Number.NaN;
  const wholeA = a[1].replace(/^0+(?=\d)/, '');
  const wholeB = b[1].replace(/^0+(?=\d)/, '');
  if (wholeA.length !== wholeB.length) return wholeA.length < wholeB.length ? -1 : 1;
  if (wholeA !== wholeB) return wholeA < wholeB ? -1 : 1;
  const fracA = (a[2] ?? '').replace(/0+$/, '');
  const fracB = (b[2] ?? '').replace(/0+$/, '');
  const width = Math.max(fracA.length, fracB.length);
  const padA = fracA.padEnd(width, '0');
  const padB = fracB.padEnd(width, '0');
  return padA === padB ? 0 : padA < padB ? -1 : 1;
}

/** Drops trailing fraction zeros of a plain decimal text (`100.500000` → `100.5`); other text is returned as is. */
export function trimDecimalText(value: string): string {
  const match = /^(-?\d+)(?:\.(\d+))?$/.exec(value);
  if (!match) return value;
  const fraction = (match[2] ?? '').replace(/0+$/, '');
  return fraction ? `${match[1]}.${fraction}` : match[1];
}

/**
 * Mirrors SalesPricingRules.Decide: an override is needed when no rule applies, the price differs from the rule
 * price, or the discount exceeds the ceiling. Unknown states (incl. a legacy saved line, which a current rule may
 * still match) return false — the server stays authoritative.
 */
export function linePricingNeedsOverride(pricing: LinePricing, unitPrice: string, discountRate: string): boolean {
  if (pricing.kind === 'notFound') return true;
  if (pricing.kind !== 'rule') return false;
  const price = compareDecimalText(unitPrice, pricing.unitPrice);
  const discount = compareDecimalText(discountRate, pricing.discountCeiling);
  if (Number.isNaN(price) || Number.isNaN(discount)) return false;
  return price !== 0 || discount > 0;
}

export type DocumentPricingCode = 'SAL.PRICING.OVERRIDE_REQUIRED' | 'SAL.PRICING.STALE_QUOTE' |
  'SAL.PRICING.DUPLICATE_ACTIVE_RULE';

export interface DocumentPricingFailure { code: DocumentPricingCode; lineNo: number | null; ruleIds: number[]; }

const STATUS: Record<DocumentPricingCode, number> = {
  'SAL.PRICING.OVERRIDE_REQUIRED': 403,
  'SAL.PRICING.STALE_QUOTE': 409,
  'SAL.PRICING.DUPLICATE_ACTIVE_RULE': 409,
};

function ids(value: unknown): number[] {
  return Array.isArray(value) ? value.filter((id): id is number => Number.isSafeInteger(id) && id > 0).slice(0, 20) : [];
}

/** Named pricing rejection of a quotation/order save (ADR-160: 403/409 carry `lineNo`); null for anything else. */
export function pricingFailureOf(error: unknown): DocumentPricingFailure | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body = error.error as { errorCode?: unknown; lineNo?: unknown; conflictingRuleIds?: unknown } | null;
  const code = body?.errorCode;
  if (typeof code !== 'string' || !(code in STATUS) || STATUS[code as DocumentPricingCode] !== error.status) return null;
  const lineNo = Number.isSafeInteger(body?.lineNo) && (body!.lineNo as number) > 0 ? body!.lineNo as number : null;
  return { code: code as DocumentPricingCode, lineNo, ruleIds: ids(body?.conflictingRuleIds) };
}

/** Line state for a failed `GET price-rules/quote` (ADR-159). */
export function quoteFailureState(error: unknown): LinePricing {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as { errorCode?: unknown; conflictingRuleIds?: unknown } | null;
    if (error.status === 422 && body?.errorCode === 'SAL.PRICING.RULE_NOT_FOUND') return { kind: 'notFound' };
    if (error.status === 409 && body?.errorCode === 'SAL.PRICING.DUPLICATE_ACTIVE_RULE')
      return { kind: 'duplicate', ruleIds: ids(body.conflictingRuleIds) };
  }
  if ((error as { name?: unknown } | null)?.name === 'TimeoutError') return { kind: 'failed', messageKey: 'sd.pricing.timeout' };
  return { kind: 'failed', messageKey: apiErrorKey(error, ['MD.ITEM.NOT_FOUND', 'MD.CUSTOMER.NOT_FOUND',
    'MD.ITEM.INACTIVE', 'MD.CUSTOMER.NOT_APPROVED']) };
}

/** Saved line provenance (ADR-160) as an i18n key and parameters for detail views; absent mode = Legacy. */
export function pricingSourceOf(line: { pricingMode?: string; pricingRuleId?: number | null;
  pricingRuleVersion?: number | null; overrideReason?: string | null }): { key: string; params: Record<string, unknown> } {
  const rule = { id: line.pricingRuleId, version: line.pricingRuleVersion };
  if (line.pricingMode === 'Rule' && line.pricingRuleId != null) return { key: 'sd.pricing.sourceRule', params: rule };
  if (line.pricingMode === 'Override')
    return line.pricingRuleId != null
      ? { key: 'sd.pricing.sourceOverrideRule', params: { ...rule, reason: line.overrideReason ?? '' } }
      : { key: 'sd.pricing.sourceOverride', params: { reason: line.overrideReason ?? '' } };
  return { key: 'sd.pricing.sourceLegacy', params: {} };
}

/** ADR-160 line pricing hints: the quote pair only together, a trimmed override reason only when entered. */
export function pricingTokens(line: { pricingRuleId?: number | null; pricingRuleVersion?: number | null;
  overrideReason?: string }): string[] {
  const tokens: string[] = [];
  if (Number.isSafeInteger(line.pricingRuleId) && Number.isSafeInteger(line.pricingRuleVersion) &&
      line.pricingRuleId! > 0 && line.pricingRuleVersion! > 0)
    tokens.push(`"pricingRuleId":${line.pricingRuleId}`, `"pricingRuleVersion":${line.pricingRuleVersion}`);
  const reason = line.overrideReason?.trim();
  if (reason) tokens.push(`"overrideReason":${JSON.stringify(reason)}`);
  return tokens;
}
