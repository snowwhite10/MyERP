import { HttpErrorResponse } from '@angular/common/http';
import { compareDecimalText, linePricingNeedsOverride, pricingFailureOf, quoteFailureState, trimDecimalText, pricingSourceOf, LinePricing } from './document-pricing';

const rule = (extra: Partial<Extract<LinePricing, { kind: 'rule' }>> = {}): LinePricing => ({
  kind: 'rule', ruleId: 5, ruleVersion: 3, unitPrice: '100.500000', discountCeiling: '10.0000',
  validTo: '2028-12-31', fromSaved: false, ...extra });

describe('SD-QPRICE-UI-U01 exact decimal comparison', () => {
  it('compares decimal text without Number, ignoring trailing zeros', () => {
    expect(compareDecimalText('100.5', '100.500000')).toBe(0);
    expect(compareDecimalText('1234567890123.123457', '1234567890123.123456')).toBe(1);
    expect(compareDecimalText('9', '10')).toBe(-1);
    expect(compareDecimalText('0', '0.000001')).toBe(-1);
    expect(compareDecimalText('007', '7')).toBe(0);
  });
});

describe('SD-QPRICE-UI-U02 override need mirrors SalesPricingRules.Decide', () => {
  it('needs no override at the rule price with a discount at or below the ceiling', () => {
    expect(linePricingNeedsOverride(rule(), '100.5', '10')).toBe(false);
    expect(linePricingNeedsOverride(rule(), '100.50', '0')).toBe(false);
  });

  it('needs an override for a changed price or a discount above the ceiling', () => {
    expect(linePricingNeedsOverride(rule(), '100.4', '0')).toBe(true);
    expect(linePricingNeedsOverride(rule(), '100.5', '10.0001')).toBe(true);
  });

  it('needs an override when no rule applies', () => {
    expect(linePricingNeedsOverride({ kind: 'notFound' }, '1', '0')).toBe(true);
  });

  it('leaves the decision to the server while unknown, loading, failed, stale or duplicated', () => {
    for (const state of [{ kind: 'none' }, { kind: 'loading' }, { kind: 'legacy' }, { kind: 'failed', messageKey: 'x' },
      { kind: 'duplicate', ruleIds: [1, 2] }, { kind: 'stale' }] as LinePricing[])
      expect(linePricingNeedsOverride(state, '1', '0')).toBe(false);
  });

  it('does not decide on invalid number text', () => {
    expect(linePricingNeedsOverride(rule(), 'abc', '0')).toBe(false);
  });
});

describe('SD-QPRICE-UI-U03 pricing failure of a document save', () => {
  const fail = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

  it('reads the named pricing code and line number', () => {
    expect(pricingFailureOf(fail(403, { errorCode: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: 2 })))
      .toEqual({ code: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: 2, ruleIds: [] });
    expect(pricingFailureOf(fail(409, { errorCode: 'SAL.PRICING.STALE_QUOTE', lineNo: 1 })))
      .toEqual({ code: 'SAL.PRICING.STALE_QUOTE', lineNo: 1, ruleIds: [] });
    expect(pricingFailureOf(fail(409, { errorCode: 'SAL.PRICING.DUPLICATE_ACTIVE_RULE', lineNo: 3,
      conflictingRuleIds: [8, 9] }))).toEqual({ code: 'SAL.PRICING.DUPLICATE_ACTIVE_RULE', lineNo: 3, ruleIds: [8, 9] });
  });

  it('ignores other codes, a mismatched status and a missing line number', () => {
    expect(pricingFailureOf(fail(409, { errorCode: 'COMMON.CONCURRENCY.CONFLICT' }))).toBeNull();
    expect(pricingFailureOf(fail(400, { errorCode: 'SAL.PRICING.STALE_QUOTE', lineNo: 1 }))).toBeNull();
    expect(pricingFailureOf(fail(403, { errorCode: 'SAL.PRICING.OVERRIDE_REQUIRED' }))).toEqual(
      { code: 'SAL.PRICING.OVERRIDE_REQUIRED', lineNo: null, ruleIds: [] });
    expect(pricingFailureOf(new Error('x'))).toBeNull();
  });
});

describe('SD-QPRICE-UI-U04 quote failure state', () => {
  const fail = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

  it('maps a missing rule, duplicate rules, timeout and other errors', () => {
    expect(quoteFailureState(fail(422, { errorCode: 'SAL.PRICING.RULE_NOT_FOUND' }))).toEqual({ kind: 'notFound' });
    expect(quoteFailureState(fail(409, { errorCode: 'SAL.PRICING.DUPLICATE_ACTIVE_RULE', conflictingRuleIds: [4, 6] })))
      .toEqual({ kind: 'duplicate', ruleIds: [4, 6] });
    expect(quoteFailureState({ name: 'TimeoutError' })).toEqual({ kind: 'failed', messageKey: 'sd.pricing.timeout' });
    expect(quoteFailureState(fail(422, { errorCode: 'MD.CUSTOMER.NOT_APPROVED' })))
      .toEqual({ kind: 'failed', messageKey: 'errors.MD.CUSTOMER.NOT_APPROVED' });
    expect(quoteFailureState(fail(0, null))).toEqual({ kind: 'failed', messageKey: 'errors.http.network' });
  });
});

describe('SD-QPRICE-UI-U05 trimmed decimal text', () => {
  it('drops trailing fraction zeros only', () => {
    expect(trimDecimalText('100.500000')).toBe('100.5');
    expect(trimDecimalText('12.0000')).toBe('12');
    expect(trimDecimalText('1234567890123.123456')).toBe('1234567890123.123456');
    expect(trimDecimalText('abc')).toBe('abc');
  });
});

describe('SD-QPRICE-UI-U06 saved pricing source', () => {
  it('names rule, override with and without rule, and legacy lines', () => {
    expect(pricingSourceOf({ pricingMode: 'Rule', pricingRuleId: 5, pricingRuleVersion: 3 }))
      .toEqual({ key: 'sd.pricing.sourceRule', params: { id: 5, version: 3 } });
    expect(pricingSourceOf({ pricingMode: 'Override', pricingRuleId: 5, pricingRuleVersion: 3, overrideReason: 'Deal' }))
      .toEqual({ key: 'sd.pricing.sourceOverrideRule', params: { id: 5, version: 3, reason: 'Deal' } });
    expect(pricingSourceOf({ pricingMode: 'Override', pricingRuleId: null, overrideReason: 'No rule' }))
      .toEqual({ key: 'sd.pricing.sourceOverride', params: { reason: 'No rule' } });
    expect(pricingSourceOf({})).toEqual({ key: 'sd.pricing.sourceLegacy', params: {} });
  });
});
